## Why

Delete, Pin, and Unpin hover buttons sit vertically out of line with row text on some Scribe
surfaces, most visibly on cuneiform tablet item rows. The earlier quest-marker correction established
the correct cuneiform-aware first-line measurement, while these floating controls still use a single
Latin input-height calculation.

## What Changes

- Center floating row-action button boxes on the row's actual first text line, accounting for task,
  item, and cuneiform item-row layouts.
- Apply the shared calculation to every `ScribeRowButton` used as a row hover action, including Pin
  on Read, Delete and Pin on Editor, and Delete and Unpin on the Pinned tab.
- Preserve button size, horizontal placement, hover behavior, glyph scale, and action semantics.

## Capabilities

### New Capabilities
- `row-hover-action-alignment`: defines consistent first-line vertical alignment for hover-revealed
  row actions across Scribe document surfaces.

### Modified Capabilities

(none)

## Impact

- `src/Mod/ScribeRowWidgets.cs`: make the shared floating-button offset aware of the row's real
  first-line band.
- `src/Mod/ScribeReadContent.cs`, `ScribeEditorContent.cs`, and `ScribePinnedContent.cs`: pass the
  row layout classification into the shared calculation.
- No Core model, persistence, networking, dependency, or gameplay-semantic changes.
