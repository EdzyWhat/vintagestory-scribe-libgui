# bundled-font-rendering Specification

## Purpose
TBD - created by archiving change prove-bundled-font-seam. Update Purpose after archive.
## Requirements
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
the lectern dialog's TITLE text to the registered family name, so that
`Gui.Rendering.Text.TextLayoutHelper` resolves the registered typeface for the title and no other GUI
text in the game is affected. The lectern's task-row text (read view and editor field) SHALL remain on
its default family and SHALL NOT be switched to the bundled face.

#### Scenario: Only the lectern title changes typeface

- **WHEN** the lectern dialog is open with the bundled-font path active
- **THEN** the dialog's title text renders in the bundled Caudex face
- **AND** the task-row text (read and editor) renders in its default family, unchanged
- **AND** all other in-game GUI text (menus, tooltips, other dialogs) renders in its normal font,
  unchanged

### Requirement: The registered face is resolved by the layout path automatically

The mod SHALL rely on `TextLayoutHelper` consulting `FontRegistry.GetCustomTypeface` for a resolved
family before any system-font fallback, so that naming the registered family in a `TextStyle` is
sufficient for both text measurement and drawing; the mod SHALL NOT add a per-surface or per-draw
font override to route the face.

#### Scenario: Naming the family is sufficient

- **WHEN** the title text's `TextStyle.FontFamily` names the registered family
- **THEN** both `TextLayoutHelper` measurement and the text draw resolve the registered `SKTypeface`
- **AND** no per-surface font-override call is required

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
- **THEN** the title text renders in the bundled face without crash, error, or garbled glyphs

### Requirement: The bundled font's license is honored

The mod SHALL ship the bundled font's license file (`OFL.txt`) alongside the `.ttf` and SHALL credit
the font (Caudex, SIL OFL 1.1) in a `CREDITS` file. If the font files were modified they SHALL NOT be
redistributed under the original reserved font name; for this spike the files are unmodified.

#### Scenario: License artifacts ship with the font

- **WHEN** the mod is packaged with the bundled Caudex `.ttf`
- **THEN** Caudex's `OFL.txt` is included in the package
- **AND** a `CREDITS` file names Caudex and its SIL OFL 1.1 license

### Requirement: The literal default text family never resolves via OS/fontconfig lookup

The mod SHALL make its own default text family (used wherever Scribe's code names the empty/default
family — the task-font default, HUD chrome, Settings chrome, History/Timer/Guestbook metadata)
resolve to one of Scribe's already-registered bundled typefaces, selected from an ordered fallback
chain of its bundled faces (preferring a general-purpose sans face), so that face never resolves via
`SKTypeface.FromFamilyName` or any other live OS/fontconfig name lookup. The mod SHALL do this by
naming its own bundled family directly wherever it means "my default family," and SHALL NOT achieve
this by calling `Gui.Rendering.Text.FontRegistry.RegisterFontAlias("sans-serif", ...)` or otherwise
changing what the shared `"sans-serif"` family name resolves to. `"sans-serif"` is LibGUI's own
framework-wide default `TextStyle.FontFamily`, consulted by every `gui`-dependent mod's unstyled
text, not a name Scribe owns; the mod SHALL leave its resolution exactly as LibGUI (or the OS) would
resolve it absent Scribe.

#### Scenario: The default family resolves to a bundled face without an OS lookup

- **WHEN** the client initializes and Scribe's bundled task fonts register successfully
- **THEN** Scribe's own default-family text (task-font default, HUD chrome, Settings chrome,
  History/Timer/Guestbook metadata) resolves to a bundled `SKTypeface` via
  `FontRegistry.GetCustomTypeface`, never falling through to `SKTypeface.FromFamilyName`

#### Scenario: The alias degrades to another bundled face if the preferred one failed to load

- **WHEN** the preferred default-family target (the general-purpose sans face) failed to load
- **THEN** the mod selects the next successfully-loaded face in its fallback chain as its own default
  family
- **AND** if no bundled face loaded at all, Scribe's own default-family text falls back to whatever
  `"sans-serif"` itself resolves to (unchanged, since Scribe never aliases it)

#### Scenario: Another mod's default-family text is never affected by Scribe being installed

- **WHEN** a different `gui`-dependent mod (e.g. HudUI) renders its own text with an unstyled
  `TextStyle` (`FontFamily = "sans-serif"`)
- **THEN** that text resolves to exactly the same typeface it would resolve to with Scribe not
  installed
- **AND** Scribe never calls `FontRegistry.RegisterFontAlias("sans-serif", ...)` or any equivalent
  global override of the shared family name

#### Scenario: Task-font line-box pegging is unaffected

- **WHEN** the default (empty) task-font choice is measured against Caudex's line-box
- **THEN** the pegged row height still matches Caudex within 1 px, regardless of which bundled face
  Scribe's own default family now names internally

