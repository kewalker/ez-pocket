# Plugin overlay prototype

**Historical research:** the current app uses **RUN PLUGIN** with direct target
access on every supported desktop platform. It does not use this native overlay
or make a full-card copy. The earlier Windows-only integration described below
has been removed from normal build packaging. Keep this harness for future
filesystem research; see `docs/POCKET-PLUGINS.md` for the current user workflow.

This is an isolated, executable proof that existing Pocket Plugins can use a
copy-on-write `pocket/` mount instead of copying the whole card. The Windows
Debug app can now use this runtime through its normal **RUN AND REVIEW** page.
The regular Extism DLL remains available for builds without the extension.

The runner always creates a fresh synthetic source directory. It has no argument
for a real Pocket target. An optional plugin module is read from its installation;
its private `host/` cache is replaced with a fresh synthetic folder for this test.

## Use in the app

```powershell
./prototypes/plugin-overlay/Run-Prototype.ps1 -Bootstrap -BuildRuntime
& "$env:USERPROFILE/.dotnet/dotnet.exe" build EzPocket.sln -f net10.0-windows10.0.19041.0 --no-restore
```

The Debug build copies `artifacts/plugin-overlay-native/ezpocket_extism.dll` beside
the executable. The plugin page displays **no full-card copy is made** when it is
loaded. The service indexes target file names, lengths and last-write times,
then reviews staged output and deletion markers. Files being replaced or deleted
are content-hashed, backed up, and checked before applying. A temporary file
created and renamed by a plugin is filtered out of the final change review.
The native extension is excluded from Release publishing while macOS packaging
and the remaining compatibility work below are pending.

## Run on Windows

From the repository root, in PowerShell:

```powershell
./prototypes/plugin-overlay/Run-Prototype.ps1 -Bootstrap
```

To additionally smoke-test an already installed PlayStation plugin:

```powershell
./prototypes/plugin-overlay/Run-Prototype.ps1 -PluginModule 'C:/path/to/installed/plugin.wasm'
```

Add `-Interactive` to answer the actual plugin's choice prompts in the console.
Choose **Generate game files** to generate the synthetic game's manifest. The
runner displays staged output and waits before discarding it. It still uses only
the synthetic card; this is not the app's `RUN AND REVIEW` page.

`-Bootstrap` downloads Rust 1.90.0 into the ignored `artifacts/` folder. It does
not install Rust globally or change the system PATH. A Visual C++ build toolchain
and Windows SDK must already be available. The first run downloads and builds
dependencies; subsequent runs reuse them. The upstream source is Extism v1.13.0,
with its archive SHA-256 verified, and dependencies are pinned by `Cargo.lock`.

The runner copies `overlay_dir.rs` into an ignored upstream checkout and adds a
single experimental mount branch. Only a read-only mount named `pocket` with the
explicit `ezpocket.prototype.overlay.pocket` configuration uses the new provider.
Other mounts retain their upstream behavior. The harness uses Extism's Rust API
and ordinary WASI calls; the real plugin module is not modified.

Build products, synthetic fixtures, and `results.json` are stored under
`artifacts/plugin-overlay-*`. The large input is a **100 GiB sparse file**, with
only a small header written. It does not allocate 100 GiB of data. Each test run
discards its staged files and retains its synthetic source and small report.

## Verified on Windows x64, 2026-10-30

- Read a small range of a 107,374,182,400-byte logical file with **zero staged
  bytes**. The measured read call was 1 ms after module construction; this is not
  a benchmark for scanning or reading an entire card.
- Truncating an existing file staged only the new contents without copying the
  original. A partial write copied up the affected 17-byte file and preserved
  its untouched suffix.
- Create, file rename, deletion markers, nested directory creation, and merged
  directory listings worked through ordinary WASI calls.
- Parent-path escape was rejected. Discard and interruption of an executing
  WebAssembly call left the synthetic source unchanged.
- The installed `GuardianSoul/openfpga-PlayStation-plugin` v0.1.8 generated a
  valid `APF_VER_1` instance from a synthetic CUE and four empty BIN sectors.
  It staged **244 bytes**, copied **zero input bytes**, and completed its start
  and answer calls in 178 ms after module construction. This smoke test covers
  game-manifest generation, not image conversion or save migration.

The provider copies an existing file only when it needs preserving for a partial
write or rename. Reads and full replacement writes do not trigger copy-up.
Large conversions will still read and generate substantial data when that is the
plugin's actual job.

## Remaining compatibility and release work

The Windows Debug integration supports file-based operations. Unsupported WASI
operations fail closed. Review/apply still requires an explicit user action.

- The app now filters transient operations into a final diff, validates the
  directory/file metadata inventory during a run and before apply, and uses the
  existing backup/rollback, protected workflow, and cancellation logic. Content
  changes to an unrelated input that preserve its size and last-write timestamp
  are not detected. Per-read content tracking would strengthen that guarantee.
- Add enforced runtime disk quotas, more detailed progress, and crash cleanup.
- Complete file/directory replacement semantics, removed-and-recreated
  directories, open-handle behavior across rename/unlink, case handling, and
  concurrent access. Directory renames, links, and timestamp changes currently
  fail as unsupported.
- Harden path handling against filesystem races, links/junctions at roots, and
  hostile staged trees. Current path checks are sufficient for the synthetic
  fixtures, not a completed security boundary. Test all mutating WASI operations,
  not just open/write/unlink.
- Build and test the native extension on macOS, then package the native runtime
  for the supported architectures with notices and a reproducible release build.
  Only Windows x64 has been exercised here.

The Windows solution build and 33 focused plugin tests pass using the user-local
.NET 10 SDK. The system-wide `dotnet` is .NET 9, so use the user-local executable
shown above on this machine. The standalone native prototype also passes.
