param([switch]$Bootstrap, [string]$PluginModule, [switch]$Interactive, [switch]$BuildRuntime)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$tools = Join-Path $repoRoot 'artifacts/plugin-overlay-tools'
$sources = Join-Path $repoRoot 'artifacts/plugin-overlay-sources'
$checkout = Join-Path $sources 'extism-1.13.0'
$env:CARGO_HOME = Join-Path $tools 'cargo'
$env:RUSTUP_HOME = Join-Path $tools 'rustup'
$cargo = Join-Path $env:CARGO_HOME 'bin/cargo.exe'

if (-not (Test-Path -LiteralPath $cargo)) {
    if (-not $Bootstrap) { throw 'Run again with -Bootstrap to download a portable Rust compiler into artifacts/.' }
    New-Item -ItemType Directory -Path $tools -Force | Out-Null
    $installer = Join-Path $tools 'rustup-init.exe'
    Invoke-WebRequest 'https://static.rust-lang.org/rustup/dist/x86_64-pc-windows-msvc/rustup-init.exe' -OutFile $installer
    & $installer -y --no-modify-path --profile minimal --default-toolchain 1.90.0
    if ($LASTEXITCODE) { throw 'Portable Rust installation failed.' }
}

if (-not (Test-Path -LiteralPath $checkout)) {
    New-Item -ItemType Directory -Path $sources -Force | Out-Null
    $archive = Join-Path $sources 'extism.tar.gz'
    Invoke-WebRequest 'https://codeload.github.com/extism/extism/tar.gz/refs/tags/v1.13.0' -OutFile $archive
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne '586D0F84E362E09EF671AFC92F3CF5D77BD4CD8084C8EA0B5D29D134CF7C318D') { throw 'Unexpected Extism source archive hash.' }
    tar -xzf $archive -C $sources
    if ($LASTEXITCODE) { throw 'Source extraction failed.' }
}

$native = Join-Path $checkout 'runtime/src'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'overlay_dir.rs') -Destination (Join-Path $native 'overlay_dir.rs')
$lib = Join-Path $native 'lib.rs'
$content = Get-Content -LiteralPath $lib -Raw
if (-not $content.Contains('mod overlay_dir;')) { Set-Content -LiteralPath $lib -Value $content.Replace('mod readonly_dir;', "mod readonly_dir;`nmod overlay_dir;") -NoNewline }
$current = Join-Path $native 'current_plugin.rs'
$content = Get-Content -LiteralPath $current -Raw
if (-not $content.Contains('ezpocket.prototype.overlay.pocket')) {
    $needle = 'let file: Box<dyn wasi_common::dir::WasiDir> = if readonly {'
    if (-not $content.Contains($needle)) { throw 'Unexpected Extism source; refusing to patch.' }
    $replacement = @'
let file: Box<dyn wasi_common::dir::WasiDir> = if readonly
                        && v == std::path::Path::new("pocket")
                        && manifest.config.contains_key("ezpocket.prototype.overlay.pocket") {
                        Box::new(crate::overlay_dir::OverlayDir::new(
                            std::path::Path::new(dir_path),
                            std::path::Path::new(&manifest.config["ezpocket.prototype.overlay.pocket"]),
                        )?)
                    } else if readonly {
'@
    Set-Content -LiteralPath $current -Value $content.Replace($needle, $replacement) -NoNewline
}

$examples = Join-Path $checkout 'runtime/examples'
New-Item -ItemType Directory -Path $examples -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'probe.rs') -Destination (Join-Path $examples 'overlay_probe.rs')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'probe.wat') -Destination (Join-Path $examples 'probe.wat')

$nativeExport = Join-Path $checkout 'libextism/src/lib.rs'
$content = Get-Content -LiteralPath $nativeExport -Raw
if (-not $content.Contains('ezpocket_overlay_version')) {
    Add-Content -LiteralPath $nativeExport -Value '`n#[no_mangle] pub extern "C" fn ezpocket_overlay_version() -> u32 { 1 }'.Replace('`n', "`n")
}

# The .NET SDK passes a null pointer for an empty call input. Preserve the C API
# contract without forming a Rust slice from a null pointer in debug builds.
$sdk = Join-Path $native 'sdk.rs'
$content = Get-Content -LiteralPath $sdk -Raw
$needle = 'let input = std::slice::from_raw_parts(data, data_len as usize);'
if ($content.Contains($needle)) {
    $replacement = @'
let input: &[u8] = if data_len == 0 { &[] } else if data.is_null() {
        plugin.error_msg = Some(make_error_msg("Null plugin input with a nonzero length".into()));
        return -1;
    } else { std::slice::from_raw_parts(data, data_len as usize) };
'@
    Set-Content -LiteralPath $sdk -Value $content.Replace($needle, $replacement) -NoNewline
}

# Keep a reviewed dependency resolution after the initial build.
$prototypeLock = Join-Path $PSScriptRoot 'Cargo.lock'
if (Test-Path -LiteralPath $prototypeLock) { Copy-Item -LiteralPath $prototypeLock -Destination (Join-Path $checkout 'Cargo.lock') }
$env:CARGO_RESOLVER_INCOMPATIBLE_RUST_VERSIONS = 'fallback'
if ($BuildRuntime) {
    & $cargo build --locked --manifest-path (Join-Path $checkout 'Cargo.toml') -p libextism
    if ($LASTEXITCODE) { throw 'Native overlay runtime build failed.' }
    $destination = Join-Path $repoRoot 'artifacts/plugin-overlay-native'
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $checkout 'target/debug/extism.dll') -Destination (Join-Path $destination 'ezpocket_extism.dll')
    Write-Output "Native overlay runtime: $destination/ezpocket_extism.dll"
    return
}
$output = Join-Path $repoRoot ('artifacts/plugin-overlay-results/' + [guid]::NewGuid().ToString('N'))
$arguments = @('run', '--locked', '--manifest-path', (Join-Path $checkout 'Cargo.toml'), '-p', 'extism', '--example', 'overlay_probe', '--', $output)
if ($PluginModule) { $arguments += (Resolve-Path -LiteralPath $PluginModule).Path }
if ($Interactive) {
    if (-not $PluginModule) { throw '-Interactive requires -PluginModule.' }
    $arguments += '--interactive'
}
& $cargo @arguments
if ($LASTEXITCODE) { throw "Prototype failed with exit code $LASTEXITCODE." }
Write-Output "Results: $output/results.json"
