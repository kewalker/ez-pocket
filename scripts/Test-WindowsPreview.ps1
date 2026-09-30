param([Parameter(Mandatory = $true)][string]$Directory)
$ErrorActionPreference = 'Stop'
$previewDirectory = (Resolve-Path -LiteralPath $Directory).Path
$previousLog = $env:EZPOCKET_STARTUP_LOG
$env:EZPOCKET_STARTUP_LOG = Join-Path $previewDirectory 'startup-exception.txt'
$process = $null
try {
    $process = Start-Process -FilePath (Join-Path $previewDirectory 'EzPocket.exe') -WorkingDirectory $previewDirectory -WindowStyle Hidden -PassThru
    $deadline = (Get-Date).AddSeconds(45)
    do {
        Start-Sleep -Milliseconds 500
        $process.Refresh()
        if ($process.HasExited) { throw "Preview exited during startup: $($process.ExitCode)" }
        if (Test-Path -LiteralPath $env:EZPOCKET_STARTUP_LOG) {
            throw (Get-Content -LiteralPath $env:EZPOCKET_STARTUP_LOG -Raw)
        }
    } until ($process.MainWindowHandle -ne 0 -or (Get-Date) -ge $deadline)
    if ($process.MainWindowHandle -eq 0) { throw 'Preview did not create a window within 45 seconds.' }
    Start-Sleep -Seconds 5
    $process.Refresh()
    if ($process.HasExited -or (Test-Path -LiteralPath $env:EZPOCKET_STARTUP_LOG)) { throw 'Preview failed after creating its window.' }
    Write-Output 'PASS: Extracted preview created a window and remained running.'
} finally {
    if ($process -and -not $process.HasExited) {
        $null = $process.CloseMainWindow()
        if (-not $process.WaitForExit(5000)) { $process.Kill() }
    }
    $env:EZPOCKET_STARTUP_LOG = $previousLog
}
