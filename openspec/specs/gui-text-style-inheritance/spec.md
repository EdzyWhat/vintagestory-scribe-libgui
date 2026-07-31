# gui-text-style-inheritance Specification

## Purpose
TBD - created by archiving change adopt-libgui-31-improvements. Update Purpose after archive.
## Requirements
### Requirement: Tab text inherits font family and base size from a DefaultTextStyle ancestor

Each Scribe dialog tab subtree (Read, Edit, Pinned, History, Timer, Settings) SHALL establish a
single `DefaultTextStyle` ancestor (LibGUI 3.1.0, `Gui.Widgets.Basic.Theming.DefaultTextStyle`)
carrying the player's resolved Task Text Font family and the window-text-size-scaled base font size.
Descendant text widgets in that subtree SHALL rely on inheritance for font family and base size
rather than re-specifying `FontFamily` on each widget, and SHALL provide per-widget `TextStyle`
values only for attributes that genuinely differ from the inherited default (e.g. color, weight,
alignment, an intentionally divergent size).

`Text` in 3.1.0 resolves its effective style as `StyleOverride?.Merge(DefaultTextStyle.Of(context))
?? DefaultTextStyle.Of(context)`, so a widget that supplies a partial `TextStyle` inherits every
unset field from the ancestor.

#### Scenario: A label with no explicit font uses the tab's inherited Task Text Font

- **WHEN** a text widget inside a tab subtree is built with a `TextStyle` that does not set
  `FontFamily`
- **THEN** it renders in the Task Text Font supplied by the tab's `DefaultTextStyle` ancestor
- **AND** at the base size supplied by that ancestor, unless the widget explicitly overrides the size

#### Scenario: A widget overrides only the delta

- **WHEN** a text widget needs a different color or weight than the tab default
- **THEN** it supplies a `TextStyle` setting only those differing fields
- **AND** the font family and base size are still inherited from the `DefaultTextStyle` ancestor

### Requirement: Adopting inheritance preserves current text rendering

Introducing `DefaultTextStyle` inheritance SHALL be behavior-preserving. Every string that renders in
the player's chosen Task Text Font at the current window text size before the change MUST render in
the same font at the same effective size after the change. Changing the player's Task Text Font or
window-text-size setting MUST continue to live-update every affected label, exactly as it does today.

#### Scenario: No visual regression across font and size settings

- **WHEN** the player has any combination of Task Text Font and window text size selected
- **THEN** every tab's text renders in the same font and effective size as before this change
- **AND** no label that used the Task Text Font before reverts to a default font

#### Scenario: Changing the setting still live-updates every label

- **WHEN** the player changes the Task Text Font or window text size while a dialog is open
- **THEN** every label under the affected tab updates to the new font/size
- **AND** no label is left rendering the previous font because it bypassed inheritance

