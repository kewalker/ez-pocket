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

Windows is the current supported development target. An experimental Linux desktop head in `Linux/` reuses the same MAUI pages and services through the GTK4 backend. The project also contains .NET MAUI targets for macOS (Mac Catalyst), iOS, and Android. Linux builds run an automated GTK4 smoke check; tagged builds remain technical previews for desktop testing.

The application is currently version `0.1.10` and remains under active development. Open an issue to report a bug, discuss an improvement, or ask about planned work.

## Requirements

- Windows 10 version 1809 or later for the Windows target.
- .NET 10 SDK.
- .NET MAUI Windows and Tizen workloads. The .NET 10 MAUI restore currently checks for `maui-tizen` even when building only Windows.

For the experimental Linux target, use a Linux desktop with the .NET 10 SDK and GTK 4.12 or later. The GTK4 backend is experimental and is not officially supported by Microsoft. On Debian or Ubuntu, install `libgtk-4-dev`, `gobject-introspection`, `libgirepository1.0-dev`, `gir1.2-gtk-4.0`, and `pkg-config`. WebKitGTK is only needed for Blazor content, which this app does not use.

## Build and run

From a PowerShell prompt in the repository:

```powershell
dotnet workload install maui-windows maui-tizen
dotnet restore EzPocket.sln -p:TargetFramework=net10.0-windows10.0.19041.0
dotnet build EzPocket.sln -f net10.0-windows10.0.19041.0 --no-restore
dotnet run --project EzPocket.csproj -f net10.0-windows10.0.19041.0
```

On a Linux desktop, from the repository root:

```sh
bash scripts/build-linux.sh
dotnet run --project Linux/EzPocket.Linux.csproj --no-build
```

The Linux head uses the same .NET 10 generation as the main app and pins the `Microsoft.Maui.Platforms.Linux.Gtk4` preview packages. **SCAN TARGET** looks for Pocket folders mounted under `/media/$USER`, `/run/media/$USER`, and `/mnt`. **CHOOSE FOLDER** accepts an absolute path to another mounted target. Selecting a path only scans it; writes still require the normal review and explicit apply action.

If a stale WinUI/MSBuild process prevents the XAML compiler from writing an `obj/.../input.json` file, run the following once and then repeat the build:

```powershell
dotnet build-server shutdown
```

## Test

After restoring and building the Windows target:

```powershell
dotnet test Tests/EzPocket.Tests.csproj -f net10.0-windows10.0.19041.0 --no-build --no-restore
```

GitHub Actions builds and tests the Windows target and compiles the experimental Linux GTK4 head for pull requests and changes to `main`.

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

Tagged builds produce unsigned Windows preview artifacts and an experimental self-contained Linux x64 archive. They are for technical preview testing, not normal public downloads. Windows may show SmartScreen warnings or organization policies may block unsigned executables; the Linux archive requires GTK 4.12+. CI checks Linux startup under a virtual display, while desktop and device testing remain in progress.

MSIX packaging and trusted signing are planned before a normal public download is offered. See [docs/RELEASING.md](docs/RELEASING.md) for artifact and signing details.

## Project layout

| Path | Purpose |
| --- | --- |
| `Services/` | Target discovery, inventory, staging, sync, firmware, selection, and diagnostics services. |
| `Models/` | Pocket, core, firmware, and change-preview models. |
| `Platforms/Windows/` | Windows-specific folder selection and app configuration. |
| `Linux/` | Experimental GTK4 app entry point and Linux target selection. |
| `Tests/` | Fast service/domain tests. |
| `docs/` | Maintainer and release documentation. |

## Contributing

Read [AGENTS.md](AGENTS.md) for product, UI, and engineering conventions. In particular, preserve the scan → preview → explicit-write model, keep platform-specific filesystem code behind services, and build the Windows target after changes.
