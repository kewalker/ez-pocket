# macOS technical preview

This experimental build is for Apple Silicon Macs. It is not an App Store build,
has no Apple Developer ID signature, and is not notarized. The build tools may
apply an ad-hoc signature required to run native code; this does not identify a
developer or require an Apple account or signing certificate.

## Download and open

1. Open the repository's Actions tab and select **macOS unsigned preview**.
2. Select a successful run and download the `ez-pocket-macos-arm64-unsigned` artifact.
3. Extract the artifact, then extract `ez-pocket-macos-arm64-unsigned.zip`.
4. Move `EzPocket.app` into Applications and open it.
5. If macOS blocks it, attempt to open it once, then open System Settings →
   Privacy & Security → Open Anyway and confirm. Managed Macs may prohibit this.

See [Apple's instructions](https://support.apple.com/en-us/102445).
Do not disable Gatekeeper system-wide. No separate .NET installation is needed.

## First test on a Mac

Build success does not establish that the app works on a Mac. Record the macOS
version, Mac model, workflow run, and any error when reporting results.

- Launch and resize the window; check navigation, Back actions, and readability.
- Choose a local sample Pocket folder. Cancel the picker, then reopen and select
  a folder. Confirm that selection and scanning do not change any files.
- Insert a backed-up SD card. Try SCAN TARGET, then CHOOSE FOLDER if discovery
  does not find it. On macOS, mounted cards normally appear under `/Volumes`.
- Browse inventory, Game Shelf, palettes, and core change previews without applying.
- On a spare or fully backed-up card, separately test a reviewed core update,
  Save Vault backup/restore, asset installation, and firmware staging. Each write
  must still require the normal explicit apply action. Verify the resulting files.
- Test card removal and permission failures; confirm that errors remain readable.
- Test Pocket Plugins separately: its native Extism runtime is not yet validated
  on Mac Catalyst. Palette/image rendering also needs real-device validation.

The preview runs without App Sandbox so it can manage selected mounted cards.
macOS privacy permissions and filesystem permissions still apply. Selecting a
folder opens it in place; it does not import or copy the card.

## Build and release scope

The workflow builds on GitHub's macOS runner; maintainers do not need a local Mac
or Apple developer credentials. Run it manually with **Run workflow**, or let it
run for pull requests and pushes to main. It uploads a ZIP and SHA-256 checksum
for 14 days; it does not publish a GitHub Release. Intel Macs are not covered by
this first preview.

The workflow pins .NET SDK/workload set `10.0.401` and Xcode `26.6` together.
The SDK selection is generated in CI so it does not change local Windows builds.
The runner's Xcode must match the installed .NET Mac Catalyst workload. If the
build reports an Xcode mismatch, align those versions before treating the build
as a code failure. CI currently checks packaging, not interactive startup or SD
card behavior.

ez-pocket is an independent project, not affiliated with, endorsed by, or
sponsored by Analogue.
