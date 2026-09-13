## MODIFIED Requirements

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
