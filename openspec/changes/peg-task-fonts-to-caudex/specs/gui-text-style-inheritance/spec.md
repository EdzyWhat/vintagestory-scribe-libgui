## MODIFIED Requirements

### Requirement: Tab text inherits font family and base size from a DefaultTextStyle ancestor

Each Scribe **task-text** dialog tab subtree (Read, Edit, Pinned, History, Timer — not Settings)
SHALL establish a single `DefaultTextStyle` ancestor (LibGUI 3.1.0,
`Gui.Widgets.Basic.Theming.DefaultTextStyle`) carrying the player's resolved Task Text Font family
and the Caudex-pegged effective font size for that family at the window-text-size-scaled nominal
size (`task-font-metrics`). Descendant text widgets in that subtree SHALL rely on inheritance for
font family and base size rather than re-specifying `FontFamily` on each widget, and SHALL provide
per-widget `TextStyle` values only for attributes that genuinely differ from the inherited default
(e.g. color, weight, alignment, an intentionally divergent size).

The settings form SHALL use a separate ancestor (`WrapSettingsChrome`) that carries LibGUI's
default body face (`sans-serif`) at the unscaled settings size. It SHALL NOT inherit Task Text Font
or the Caudex-pegged effective size.

`Text` in 3.1.0 resolves its effective style as `StyleOverride?.Merge(DefaultTextStyle.Of(context))
?? DefaultTextStyle.Of(context)`, so a widget that supplies a partial `TextStyle` inherits every
unset field from the ancestor.

#### Scenario: A label with no explicit font uses the tab's inherited Task Text Font

- **WHEN** a text widget inside a task-text tab subtree is built with a `TextStyle` that does not set
  `FontFamily`
- **THEN** it renders in the Task Text Font supplied by the tab's `DefaultTextStyle` ancestor
- **AND** at the pegged effective size supplied by that ancestor, unless the widget explicitly
  overrides the size

#### Scenario: A widget overrides only the delta

- **WHEN** a text widget needs a different color or weight than the tab default
- **THEN** it supplies a `TextStyle` setting only those differing fields
- **AND** the font family and pegged effective size are still inherited from the
  `DefaultTextStyle` ancestor

#### Scenario: Changing the task font updates inherited size as well as family

- **WHEN** the player selects a different task font while a dialog is open
- **THEN** labels under the task-text tab inherit the new family and that family's Caudex-pegged
  effective size
- **AND** no label is left at the previous family's effective size because it bypassed
  inheritance
- **AND** Settings labels stay on LibGUI's default face at 100%

#### Scenario: Settings chrome does not inherit Task Text Font

- **WHEN** the player opens Settings with a non-default Task Text Font selected
- **THEN** the settings form's labels, helptext, dropdowns, and numeric fields render in LibGUI's
  default body face
- **AND** they stay at the unscaled settings size, not the window-scaled pegged size

### Requirement: Adopting inheritance preserves current text rendering

Introducing `DefaultTextStyle` inheritance SHALL be behavior-preserving for family routing and
live updates on task-text tabs. Every string that renders in the player's chosen Task Text Font
MUST keep rendering in that font. Changing the player's Task Text Font or window-text-size setting
MUST continue to live-update every affected **task-text** label. Effective *size* is the
Caudex-pegged size from `task-font-metrics`, not the pre-metrics native size of the selected face.
Settings chrome is excluded (see `settings-tab`).

#### Scenario: No family regression across font and size settings

- **WHEN** the player has any combination of Task Text Font and window text size selected
- **THEN** every task-text tab's task text renders in the selected family
- **AND** no label that used the Task Text Font before reverts to a default font

#### Scenario: Changing the setting still live-updates every label

- **WHEN** the player changes the Task Text Font or window text size while a dialog is open
- **THEN** every label under the affected task-text tab updates to the new font and pegged
  effective size
- **AND** no label is left rendering the previous font because it bypassed inheritance
- **AND** the settings form does not restyle itself to the new Task Text Font or window scale
