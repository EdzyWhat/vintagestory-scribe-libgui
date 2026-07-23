## ADDED Requirements

### Requirement: Inspect overlay is off by default and gated by a config field

The lectern dialog SHALL expose a `ScribeClientConfig.InspectOverlayMode` integer field defaulting
to `0`, where `0` disables the overlay entirely, `1` draws outlines with labels, and `2` draws
outlines only (no labels). When the mode is `0`, the dialog MUST NOT render any inspect geometry and
MUST perform at most a single integer check per frame for the overlay.

#### Scenario: Overlay off by default

- **WHEN** the lectern dialog is opened with `InspectOverlayMode` at its default of `0`
- **THEN** no outlines, labels, or gap bands are drawn over the dialog
- **AND** the only per-frame overlay cost is one integer comparison

#### Scenario: Toggle applies on lectern reopen

- **WHEN** the player sets `InspectOverlayMode` to `1` (by editing the config JSON) and reopens the
  lectern
- **THEN** the overlay renders over the real dialog
- **AND** setting it back to `0` and reopening the lectern removes the overlay

### Requirement: Overlay ships in Release

The inspect overlay SHALL be compiled and available in Release builds, NOT gated behind `#if DEBUG`,
because its purpose is inspecting the GUI on platforms where the Debug/VSImGui tuning path is
unavailable (Apple Silicon, OpenGL 4.1).

#### Scenario: Overlay available in a Release build

- **WHEN** the mod is built in Release and staged, and `InspectOverlayMode` is set to `1`
- **THEN** reopening the lectern shows the overlay
- **AND** no `#if DEBUG` guard prevents it from rendering

### Requirement: Every keyed element is outlined and labeled

When `InspectOverlayMode >= 1`, the overlay SHALL outline every element the lectern composes that it
can resolve by key, in both the read and editor views, and (when mode is `1`) label each with its
element key and pixel size (`WxH`). Elements MUST be resolved by key live each frame via the
composer's base element lookup, and a key absent from the current view MUST be skipped without error.

#### Scenario: Rows, controls, and chrome are outlined in both views

- **WHEN** the overlay is active in the read view
- **THEN** each row, the scrollbar, the switch-mode button, the viewport/content region, and the
  dialog chrome are outlined
- **AND** switching to the editor view additionally outlines the per-row pin/delete/grip
  affordances, the checkbox, and (when a row is focused) the floating edit input

#### Scenario: Labels show key and size in mode 1

- **WHEN** the overlay is active with `InspectOverlayMode == 1`
- **THEN** each outlined box shows a label with its element key and its pixel width×height

#### Scenario: Outlines only in mode 2

- **WHEN** the overlay is active with `InspectOverlayMode == 2`
- **THEN** boxes are outlined but no text labels are drawn

#### Scenario: Absent keys are skipped safely

- **WHEN** a key the overlay looks up does not exist in the current view (e.g. `rowEditInput` with no
  focused row)
- **THEN** that box is simply omitted and no exception is thrown

### Requirement: Labels report the driving config field or formula where known

When `InspectOverlayMode == 1`, for boxes whose size/position is driven by a known
`ScribeClientConfig` field or layout formula, the overlay SHALL additionally report that driver, and
the reported value MUST be re-derived from the same `RowTextLayout`/`ScribeRowElement` calls the
compose uses (never hardcoded), so a label cannot drift from the real layout. A box with no known
driver MUST still show its key and size.

#### Scenario: Per-row column driver is re-derived live

- **WHEN** the overlay labels a row's text column or affordance square in the editor view
- **THEN** the label reports the driving field/formula (e.g. `TextX`, `AffordanceButtonSizeFixed`)
  computed from the same layout call the row composed with
- **AND** changing the corresponding config value and reopening the lectern updates the reported number

#### Scenario: Unknown-driver box degrades gracefully

- **WHEN** the overlay labels a box that has no entry in the driver table
- **THEN** the label still shows the element key and pixel size, with no driver line

### Requirement: Inter-element gaps are drawn and labeled

When `InspectOverlayMode >= 1`, the overlay SHALL draw the spacing that is not itself an element —
`TopContentGap`, `ElementToDialogPadding`, `ScaledRowSpacing`, `ListToControlsGap`, and
`ControlRowGap` — as tinted bands derived from the config values and neighboring element bounds, and
(in mode `1`) label each band with its config field name.

#### Scenario: Gap bands appear with their config field names

- **WHEN** the overlay is active with `InspectOverlayMode == 1`
- **THEN** the gap below the title bar is drawn as a band labeled `TopContentGap`
- **AND** the space between consecutive rows is drawn as bands labeled `ScaledRowSpacing`
- **AND** the dialog-edge inset is drawn as a band labeled `ElementToDialogPadding`

### Requirement: Overlay does not alter the dialog it inspects

The overlay SHALL render as a draw pass on top of the composed dialog without changing the dialog's
layout, composition, or interactive behavior, and its label textures MUST be released when the dialog
closes.

#### Scenario: Interaction is unaffected while inspecting

- **WHEN** the overlay is active and the player interacts with the lectern (toggling a task, editing
  text, scrolling)
- **THEN** the dialog behaves exactly as it does with the overlay off, only with outlines/labels drawn over it

#### Scenario: Label textures are freed on close

- **WHEN** the lectern dialog is closed
- **THEN** all cached label textures generated by the overlay are disposed
