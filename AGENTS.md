# ez-pocket contributor notes

## Product direction

- ez-pocket is a modern .NET MAUI desktop app for managing Analogue Pocket cores, firmware, assets, saves, and syncing.
- Prioritize Windows and macOS. Linux is not a first-release target.
- Prefer friendly core names. Show technical identifiers as secondary diagnostic text.
- Scan and preview before writing to a Pocket. Never make a device change without an explicit user action.

## UI conventions

- Use MAUI/WinUI native controls and theme colors as the baseline.
- Follow a restrained, retro-technical desktop visual language: near-black and white are the primary surfaces, with cool grays for hierarchy and a single restrained signal color only for state such as verification, selection, or focus.
- Keep geometry crisp. Prefer 0–4px corner radii, 1px rules, and flat surfaces; avoid soft shadows, large rounded cards, gradients, and multi-color "SaaS dashboard" treatments.
- Establish hierarchy with generous whitespace, contrast, weight, and compact all-caps labels such as `TARGET`, `CORE INVENTORY`, and `FIRMWARE`. Use friendly names as the primary content and technical identifiers/versions as quieter diagnostic text.
- Design inventory and detail views like an organized hardware catalog or specification sheet: clear columns, hairline dividers, concise metadata, and only the controls needed for the task.
- Use rectangular, action-led button labels (`SCAN TARGET`, `MANAGE CORES`, `REVIEW CHANGES`). Primary actions are near-black; secondary actions are high-contrast outlined or neutral controls.
- Successful, verified, and staged states should be quiet inline indicators or small status markers. Do not make green a secondary brand color or use large celebratory success cards.
- The visual direction may take broad inspiration from premium retro-hardware presentation, but ez-pocket must always look and read as an independent application. Do not copy Analogue wordmarks, product imagery, distinctive typography, product naming conventions, or other source-identifying trade dress.
- Do not imply endorsement or affiliation. Use factual compatibility language, reserve `official` for firmware or material actually obtained from its official source, and keep an independent-project notice available in relevant product-facing surfaces.
- MAUI `PointerOver` visual states are unreliable or visually ineffective on Windows in this app. For important desktop buttons, use MAUI `PointerGestureRecognizer` hover feedback plus `Pressed`/`Released` button events.
- Interactive controls must have clear hover, pressed, focused, and disabled feedback.
- Detail pages need an obvious in-page breadcrumb and a clickable Back action; do not rely only on the Windows title-bar back affordance.
- Keep successful operations in the page UI. Use alerts for errors or genuinely blocking confirmations, not to duplicate information already displayed.

## Engineering workflow

- Keep platform-specific filesystem/device code behind services so macOS support remains possible.
- Build the Windows target after changes: `dotnet build EzPocket.sln -f net9.0-windows10.0.19041.0 --no-restore`.
- If the WinUI XAML compiler cannot write `obj/.../input.json`, first run `dotnet build-server shutdown`, then rerun the normal build. This releases stale MSBuild/C# compiler hosts without closing Visual Studio.
- Do not create temporary `verify-bin` or `verify-obj` folders inside the project directory: MAUI's default globbing can compile their generated `.cs` files. Put any isolated build outputs outside the repository instead.
- Make focused local commits as stages are completed. Do not configure or use a remote unless explicitly requested.
