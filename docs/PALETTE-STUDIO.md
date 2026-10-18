# Palette Studio

Palette Studio is a native visual browser and editor, available from the dashboard even without a connected Pocket. It keeps the existing external GB Palette Editor link as an alternative.

## Workflow

1. Browse starter swatches, palettes already installed on the selected target, or files opened locally. With a target selected, **BROWSE COMMUNITY PACK** downloads the existing community collection into temporary staging. Browsing does not install files.
2. Select a palette and edit its RGB hex colors, light to dark. APGB palettes expose background, sprite 0, sprite 1, and window groups. Edits preserve the remaining groups and LCD-off color. **RESET EDITS** restores the selected source palette.
3. Compare palettes immediately using the generated four-shade calibration pattern, or open a screenshot you own. **COMPARE WITH THIS** pins the current four colors as the reference while you browse or edit another palette. Processing happens locally. Images are limited to 16 million pixels and reduced to a maximum 640-pixel dimension for previews.
4. Favorite a palette to save a local copy of its colors. Other opened or downloaded palettes remain available only for the current Studio session. Switching palettes discards edits unless they were favorited or exported.
5. **EXPORT FILE** writes the palette's existing format to a chosen local folder and never overwrites an existing file. **REVIEW INSTALL** stages a palette for the existing asset review flow; only its separate Apply action writes the target. Selecting a palette for use still happens on Pocket.

## Preview and format boundaries

- Preview maps image luminance to four shades using the selected color group. It cannot recover hardware background/sprite/window assignments or faithfully recolor arbitrary color games. The comparison is illustrative, not an emulator.
- GBP: 16 bytes, four RGB colors from light to dark followed by four zero bytes.
- APGB: 56 bytes, four dark-to-light RGB groups, one LCD-off RGB color, and the `81 41 50 47 42` footer. The editor normalizes the groups to light-to-dark internally and reverses them on serialization. Export retains the original format; cross-format conversion is outside this first version.
- Invalid installed/community files are skipped and counted. Invalid locally opened files are reported without changing the source.
- The community pack requires a selected target in this first version. macOS and Linux UI/device behavior require further desktop testing; Linux compilation is checked locally.

## Sources

- [Analogue APGB format documentation](https://www.analogue.co/developer/docs/openfpga/custom-palettes)
- [Nortakales GB Palette Editor and format notes](https://www.nortakales.com/page/gbpEditor)
- [Community palette collection](https://github.com/davewongillies/openfpga-palettes)

Image decoding and preview PNG generation use SkiaSharp (MIT). No source code or game imagery from the web editor is bundled.
