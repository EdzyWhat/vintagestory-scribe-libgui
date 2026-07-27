## ADDED Requirements

### Requirement: A bundled TTF is registered through LibGUI's Skia font registry

The mod SHALL render its own GUI text in a bundled `.ttf` typeface by loading that file through
LibGUI's Skia asset loader (`Gui.Rendering.SkiaAssetLoader.LoadFont(domain, path)`, which returns an
`SKTypeface` from the asset bytes) and registering the returned typeface under a family name via
`Gui.Rendering.Text.FontRegistry.RegisterCustomFont(familyName, weight, typeface)`. The mod SHALL
NOT install the font at the OS level, modify `clientsettings.json defaultFontName`, or otherwise rely
on OS/fontconfig name resolution to find the bundled file.

#### Scenario: The bundled face registers without an OS font install

- **WHEN** the client initializes with the bundled Caudex `.ttf` present in the mod's assets and the
  font not installed at the OS level
- **THEN** `SkiaAssetLoader.LoadFont` returns a usable `SKTypeface` for that asset
- **AND** the mod registers it via `FontRegistry.RegisterCustomFont` under a family name

#### Scenario: No global font configuration is touched

- **WHEN** the bundled-font path is active
- **THEN** the mod SHALL NOT modify `clientsettings.json defaultFontName` or require any OS font
  installation
- **AND** the mechanism relies only on the already-depended-on `gui` (LibGUI) mod and its bundled
  SkiaSharp, adding no new package or mod dependency

### Requirement: The bundled face applies to only the mod's own text

The bundled face SHALL be applied only to Scribe's own text by setting the `TextStyle.FontFamily` of
the lectern row text to the registered family name, so that `Gui.Rendering.Text.TextLayoutHelper`
resolves the registered typeface for that text and no other GUI text in the game is affected. The
same family name SHALL be used for both the read-view row text and the editor field so the two
resolve the same typeface.

#### Scenario: Only the lectern row text changes typeface

- **WHEN** the lectern dialog is open with the bundled-font path active
- **THEN** the lectern's row text renders in the bundled Caudex face
- **AND** all other in-game GUI text (menus, tooltips, other dialogs) renders in its normal font,
  unchanged

#### Scenario: Read and editor views resolve the same face

- **WHEN** the read view measures its row text and the editor field measures its text
- **THEN** both resolve their typeface through the same registered family name
- **AND** their measured line height and drawn glyphs stay in lockstep

### Requirement: The registered face is resolved by the layout path automatically

The mod SHALL rely on `TextLayoutHelper` consulting `FontRegistry.GetCustomTypeface` for a resolved
family before any system-font fallback, so that naming the registered family in a `TextStyle` is
sufficient for both text measurement and drawing; the mod SHALL NOT add a per-surface or per-draw
font override to route the face.

#### Scenario: Naming the family is sufficient

- **WHEN** the row text's `TextStyle.FontFamily` names the registered family
- **THEN** both `TextLayoutHelper` measurement and the text draw resolve the registered `SKTypeface`
- **AND** no per-row or per-surface font-override call is required

### Requirement: The face is registered once at client init

The mod SHALL load and register the bundled face exactly once per client session, at client
initialization (mirroring the existing icon-registration precedent), and SHALL NOT load or register
it per row or per frame. The mod SHALL NOT add its own dispose hook for the registered typeface, as
the shared LibGUI registry owns its lifetime for the client session.

#### Scenario: The face is not re-registered per draw

- **WHEN** the lectern dialog recomposes its rows repeatedly
- **THEN** the same registered typeface is reused for every row draw
- **AND** no new `LoadFont`/`RegisterCustomFont` call occurs per row or per frame

### Requirement: The bundled face renders on Apple Silicon

The spike SHALL be validated by running it on the author's Apple Silicon (arm64) macOS machine,
confirming the bundled TTF registers and renders correctly on that hardware through the SkiaSharp
path.

#### Scenario: The bundled face renders on arm64 macOS

- **WHEN** the spike build runs on the author's Apple Silicon Mac and the lectern is opened
- **THEN** the row text renders in the bundled face without crash, error, or garbled glyphs

### Requirement: The bundled font's license is honored

The mod SHALL ship the bundled font's license file (`OFL.txt`) alongside the `.ttf` and SHALL credit
the font (Caudex, SIL OFL 1.1) in a `CREDITS` file. If the font files were modified they SHALL NOT be
redistributed under the original reserved font name; for this spike the files are unmodified.

#### Scenario: License artifacts ship with the font

- **WHEN** the mod is packaged with the bundled Caudex `.ttf`
- **THEN** Caudex's `OFL.txt` is included in the package
- **AND** a `CREDITS` file names Caudex and its SIL OFL 1.1 license
