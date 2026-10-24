# Releasing ez-pocket

## Current desktop release status

The separate **macOS unsigned preview** workflow builds an Apple Silicon Mac
Catalyst app on pull requests, main, or manual dispatch. It uploads a tester ZIP
and checksum as Actions artifacts, without Apple developer credentials or
notarization. On version tags, Desktop CI calls the same workflow and attaches
the versioned Mac ZIP, SHA-256 checksum, and README to the same prerelease as
Windows and Linux, after the Mac build and Windows release job succeed. The ZIP
includes first-launch instructions. See
[macOS preview instructions](MACOS-PREVIEW.md) for the current validation limits.

GitHub Actions builds and tests the Windows target on pull requests and `main`.
Pushing a version tag such as `v0.1.13` produces two **unsigned** Windows x64 artifacts, retained for 14 days, and an experimental Linux x64 archive. The workflow creates or updates a clearly labelled GitHub **prerelease** with the preview ZIP and Linux archive:

- An MSIX package for eventual trusted signing.
- A self-contained, unpackaged Windows publish folder compressed as a ZIP. Extract into a new folder and launch `EzPocket.exe`; no separate runtime installer is required.
- A self-contained Linux GTK4 publish folder compressed as a tarball. Extract on a Linux desktop with GTK 4.12+ and launch `EzPocket.Linux`. CI checks startup under a virtual display; desktop and device testing remain in progress.

MSIX and ZIP builds use separate jobs and fresh checkouts. Reusing MSIX intermediate files for the ZIP can omit the WinUI theme resources from `resources.pri`, causing an immediate startup crash. CI extracts and launches the ZIP before publishing it, requiring a window to be created and the process to remain running. This is a startup check, not a full clean-machine compatibility test.

Public downloads remain clearly labelled technical previews. The unsigned MSIX remains an Actions artifact and is not attached to the prerelease; the unsigned ZIP is the Windows tester download. Windows signing is deferred at the maintainer's request and does not block these preview releases.

## Why an unsigned package is not a public release

Windows does not trust an unsigned MSIX package. A self-signed package is suitable only for machines where the recipient has manually installed the publisher certificate. Neither option is appropriate for ordinary GitHub Release downloads.

The preview ZIP does not need certificate installation, but its executable is unsigned. Users can encounter SmartScreen warnings or a policy block. It is a temporary technical-preview delivery option, not a substitute for trusted signing.

If trusted MSIX distribution is pursued later, SignPath Foundation is a possible route, subject to its eligibility and policy requirements. Signing is currently deferred. Do not attach an unsigned MSIX or a self-signed certificate as the normal installer; continue using accurately labelled preview ZIPs.

## Pre-signing release checklist

1. Update `ApplicationDisplayVersion` and `ApplicationVersion` in `EzPocket.csproj` and `Linux/EzPocket.Linux.csproj`.
2. Update the MSIX four-part version in `Platforms/Windows/Package.appxmanifest`.
3. Build and test the Windows target locally.
4. Add user-facing notes in `docs/releases/v<display-version>.md`. When present, the workflow uses that file for the new prerelease's description.
5. Commit the version changes and release notes, then tag that commit as `v<display-version>`. The workflow rejects a mismatched tag.
6. Confirm the tag workflow uploaded the unsigned MSIX artifact and created or updated the technical-preview prerelease with its Windows ZIP and Linux archive.

## After SignPath is configured

1. Send only the CI-produced MSIX package through the approved SignPath workflow.
2. Verify the signed result on a clean Windows machine using `Get-AuthenticodeSignature`.
3. Promote or replace the technical-preview prerelease with the signed MSIX, a SHA-256 checksum, release notes, and an install/update guide.
4. Never present an unsigned artifact or a self-signed certificate as the primary release download.

## Package identity

The Windows package identity is `Bitbird.EzPocket`, with publisher `CN=Bitbird`. The package publisher must remain aligned with the eventual trusted signing identity for upgrades to work correctly.
