# ez-pocket contributor notes

## Product direction

- ez-pocket is a modern .NET MAUI desktop app for managing Analogue Pocket cores, firmware, assets, saves, and syncing.
- Prioritize Windows and macOS. Linux is not a first-release target.
- Prefer friendly core names. Show technical identifiers as secondary diagnostic text.
- Scan and preview before writing to a Pocket. Never make a device change without an explicit user action.

## UI conventions

- Use MAUI/WinUI native controls and theme colors as the baseline.
- MAUI `PointerOver` visual states are unreliable or visually ineffective on Windows in this app. For important desktop buttons, use MAUI `PointerGestureRecognizer` hover feedback plus `Pressed`/`Released` button events.
- Interactive controls must have clear hover, pressed, focused, and disabled feedback.
- Detail pages need an obvious in-page breadcrumb and a clickable Back action; do not rely only on the Windows title-bar back affordance.
- Keep successful operations in the page UI. Use alerts for errors or genuinely blocking confirmations, not to duplicate information already displayed.

## Engineering workflow

- Keep platform-specific filesystem/device code behind services so macOS support remains possible.
- Build the Windows target after changes: `dotnet build EzPocket.sln -f net9.0-windows10.0.19041.0 --no-restore`.
- Make focused local commits as stages are completed. Do not configure or use a remote unless explicitly requested.
