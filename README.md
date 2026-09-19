# ez-pocket

`ez-pocket` is a .NET MAUI desktop app for inspecting and managing an Analogue Pocket SD-card target. It helps make core and PocketOS firmware maintenance understandable: scan a target, review the planned changes, then explicitly choose whether to write them.

It is an independent project and is not affiliated with, endorsed by, or sponsored by Analogue.

> **Early development software.** Review every change preview and keep your own backup of important SD-card content and saves. Do not remove the SD card or close the app while a write is underway.

## What it does

- Finds Pocket-like removable drives by recognizing the standard `Assets`, `Cores`, `Platforms`, and `System` folders, or lets you choose a folder explicitly.
- Scans installed core metadata and combines it with the live [openFPGA cores inventory](https://openfpga-cores-inventory.github.io/analogue-pocket/).
- Displays friendly core names alongside identifiers, versions, categories, and install/update status.
- Lets you search, filter, select a desired core set, and inspect a full reconciliation preview before applying core changes.
- Downloads and stages selected core packages, reports package conflicts or blockers, backs up replaced/removed core content, and restores changed files if a core sync fails.
- Provides curated featured core sets as a starting point for the normal review workflow.
- Checks the official Pocket firmware source, verifies the published MD5 checksum, and stages firmware separately from core updates.
- Exports local diagnostics that deliberately exclude target paths and exception messages.

Core updates and PocketOS firmware are intentionally separate workflows. Neither operation includes saves, games, or device firmware changes unless you explicitly use that workflow.

## Platforms and status

Windows is the current supported development target. The project also contains .NET MAUI targets for macOS (Mac Catalyst), iOS, and Android, but Windows is the first-release focus; folder picking and target management are currently implemented for Windows.

The application is currently version `0.1.0` and remains under active development. See [PLAN.md](PLAN.md) for the roadmap and known hardening work.

## Requirements

- Windows 10 version 1809 or later for the Windows target.
- .NET 9 SDK.
- .NET MAUI Windows workload.

## Build and run

From a PowerShell prompt in the repository:

```powershell
dotnet workload install maui-windows
dotnet restore EzPocket.sln -p:TargetFramework=net9.0-windows10.0.19041.0
dotnet build EzPocket.sln -f net9.0-windows10.0.19041.0 --no-restore
dotnet run --project EzPocket.csproj -f net9.0-windows10.0.19041.0
```

If a stale WinUI/MSBuild process prevents the XAML compiler from writing an `obj/.../input.json` file, run the following once and then repeat the build:

```powershell
dotnet build-server shutdown
```

## Test

After restoring and building the Windows target:

```powershell
dotnet test Tests/EzPocket.Tests.csproj -f net9.0-windows10.0.19041.0 --no-build --no-restore
```

GitHub Actions builds and tests this same Windows target for pull requests and changes to `main`.

## Typical workflow

1. Connect the Pocket SD card, then use **SCAN TARGET** or choose its folder.
2. Open **MANAGE CORES** to refresh the installed and live inventories.
3. Choose the cores to keep; select **REVIEW CHANGES** to inspect additions, replacements, removals, package conflicts, and blockers.
4. Apply the reviewed core changes only when the target and preview are correct.
5. For PocketOS, use the separate firmware page: check the official release, review the verified staged file, then explicitly stage it at the selected target's root.
6. Safely eject the SD card before using it in the Pocket.

Selecting a folder never writes to it by itself. Core and firmware writes require their own review and confirmation.

## Data and network behavior

- Core metadata is read from the selected target. The available-core inventory is fetched live; if unavailable, the app retains the installed-core view and offers retry.
- Core packages are downloaded only for a reviewed sync. Staging and backups live under the current user's local application-data directory (`EzPocket`).
- Firmware metadata and the firmware download come from Analogue's Pocket firmware support pages. The downloaded file must match the MD5 published with the release before it can be staged.
- Diagnostics remain local until you explicitly export them, and intentionally omit target paths and exception messages.

## Releases

Version tags build an unsigned x64 MSIX artifact in CI for maintainer validation and signing. Unsigned artifacts are **not** normal public downloads. The intended public release path uses trusted signing; see [docs/RELEASING.md](docs/RELEASING.md) for the release process and signing requirements.

## Project layout

| Path | Purpose |
| --- | --- |
| `Services/` | Target discovery, inventory, staging, sync, firmware, selection, and diagnostics services. |
| `Models/` | Pocket, core, firmware, and change-preview models. |
| `Platforms/Windows/` | Windows-specific folder selection and app configuration. |
| `Tests/` | Fast service/domain tests. |
| `docs/` | Maintainer and release documentation. |

## Contributing

Read [AGENTS.md](AGENTS.md) for product, UI, and engineering conventions. In particular, preserve the scan → preview → explicit-write model, keep platform-specific filesystem code behind services, and build the Windows target after changes.
