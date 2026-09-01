## ADDED Requirements

### Requirement: The literal default text family never resolves via OS/fontconfig lookup

The mod SHALL redirect LibGUI's literal default text family name (`"sans-serif"`) to one of Scribe's
already-registered bundled typefaces by calling `Gui.Rendering.Text.FontRegistry.RegisterFontAlias`
once at client initialization, before any text is measured or drawn. The mod SHALL select the alias
target from an ordered fallback chain of its bundled faces (preferring a general-purpose sans face)
and SHALL only alias to a face that itself loaded and registered successfully. The mod SHALL NOT
allow any resolution of `"sans-serif"` — whether from an explicit `TextStyle.FontFamily`, the
task-font default (empty `TaskFontFamily`), or Settings chrome — to fall through to
`SKTypeface.FromFamilyName` or any other live OS/fontconfig name lookup.

#### Scenario: The default family resolves to a bundled face without an OS lookup

- **WHEN** the client initializes and Scribe's bundled task fonts register successfully
- **THEN** `FontRegistry.RegisterFontAlias("sans-serif", <bundled family>)` is called before any
  text measurement or draw occurs
- **AND** subsequent resolution of the `"sans-serif"` family returns a bundled `SKTypeface` via
  `FontRegistry.GetCustomTypeface`, never falling through to `SKTypeface.FromFamilyName`

#### Scenario: The alias degrades to another bundled face if the preferred one failed to load

- **WHEN** the preferred alias target (the general-purpose sans face) failed to load
- **THEN** the mod selects the next successfully-loaded face in its fallback chain as the alias
  target
- **AND** if no bundled face loaded at all, the mod does not register an alias rather than pointing
  it at an unregistered family name

#### Scenario: Task-font line-box pegging is unaffected

- **WHEN** the default (empty) task-font choice is measured against Caudex's line-box
- **THEN** the pegged row height still matches Caudex within 1 px, regardless of which bundled face
  `"sans-serif"` now resolves to internally
