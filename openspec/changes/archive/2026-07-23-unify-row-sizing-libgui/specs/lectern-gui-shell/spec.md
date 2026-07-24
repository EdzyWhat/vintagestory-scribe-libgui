## MODIFIED Requirements

### Requirement: Read and editor views share a single row-list width
The lectern's row list SHALL be a single consistent width across both the read view and the
editor view. Switching between views on the same lectern SHALL NOT change the row-list width.

In addition, a task row SHALL occupy the same vertical space in the read view and the editor
view: for a single-line task, the read-view row and the editor-view row SHALL have identical
rendered height, and each task SHALL remain at the same vertical position when the player
switches views on the same lectern. This parity SHALL be achieved by unifying the row font
size, vertical alignment, per-row padding, and inter-row spacing between the two views. The
read-view row SHALL NOT draw a text-field border, while the editor-view row's field border
(drawn inside its existing internal padding) SHALL NOT change the row's height. Multi-line
rows are best-effort: they need not be pixel-identical when the read and editor wrap widths
or field chrome differ.

#### Scenario: Row-list width is identical in both views
- **WHEN** the player switches between read and editor view on the same lectern
- **THEN** the row list occupies the same width in both views, with no visible reflow or
  resize of the list column

#### Scenario: A single-line task keeps its position across a view switch
- **WHEN** the player switches between read and editor view on a lectern whose tasks each fit
  on a single line
- **THEN** each task's row occupies the same vertical height and the same vertical position in
  both views, so no task visibly jumps or shifts when the view changes

#### Scenario: Read-view rows have no border while matching the editor field's box
- **WHEN** a task row is shown in the read view
- **THEN** it draws no text-field border
- **AND** its text is inset vertically and horizontally to match the editor field's internal
  padding, so the text's top edge and left edge align with the editor field's text across a
  view switch

## ADDED Requirements

### Requirement: Lectern row sizing is sourced from client configuration
The LibGUI lectern dialog SHALL source its task-row sizing values (row font size, per-row
vertical and horizontal padding, checkbox size, checkbox-to-text gap, and the editor field's
internal horizontal and vertical padding) from a `ScribeClientConfig` instance loaded from
the client config file, rather than from hardcoded literals. The dialog SHALL load this
config when it is opened, so that editing the config file on disk and reopening the lectern
applies the new values. When no config file exists, the dialog SHALL fall back to built-in
defaults without error.

#### Scenario: Editing the config file and reopening applies new sizing
- **WHEN** the player edits a row-sizing value in the client config file and then opens the
  lectern
- **THEN** the lectern's rows render at the edited size

#### Scenario: Missing config file falls back to defaults
- **WHEN** the client config file does not exist and the player opens the lectern
- **THEN** the lectern opens normally using built-in default sizing values, with no error

### Requirement: Row sizing scales through a single factor
The lectern's scalable row-sizing values SHALL be derived by multiplying their configured
base values by a single client-side text-size scale factor, applied at one place before the
values reach the row widgets. With the scale factor at its default of `1.0`, the rendered
sizes SHALL equal the configured base values (a no-op).

#### Scenario: Default scale reproduces the configured base sizes
- **WHEN** the text-size scale factor is at its default value of `1.0`
- **THEN** each row's font size and scalable padding equal the configured base values

#### Scenario: A non-default scale multiplies the sizes uniformly
- **WHEN** the text-size scale factor is set to a value other than `1.0`
- **THEN** the scalable row-sizing values are multiplied by that factor for both the read and
  editor views

### Requirement: Row-sizing config is exposable via ConfigLib without a hard dependency
The mod SHALL expose its row-sizing configuration fields through ConfigLib's in-game settings
panel via a no-code manifest that reads and writes the same client config file. Every exposed
setting SHALL be declared as a floating-point type. The mod SHALL NOT declare a hard
dependency on ConfigLib: when ConfigLib is not installed, the lectern SHALL load and function
normally and the manifest SHALL simply go unread.

#### Scenario: Fields are tunable in the ConfigLib panel when ConfigLib is present
- **WHEN** ConfigLib is installed and the player edits a row-sizing field in its settings
  panel and saves
- **THEN** the value is written to the client config file and the next lectern open renders at
  the new size

#### Scenario: Mod works without ConfigLib installed
- **WHEN** ConfigLib is not installed
- **THEN** the mod loads and the lectern opens normally, with no missing-dependency warning
  and no reliance on ConfigLib being present
