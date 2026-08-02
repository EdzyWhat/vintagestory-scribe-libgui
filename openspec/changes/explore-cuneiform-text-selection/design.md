## Context

EXPLORE STUB. Caret-only cuneiform input is shipping; selection is deferred. State-side
selection (anchor/range) already exists in `ScribeMultilineFieldState`; only the cuneiform
render side is missing.

## Goals / Non-Goals

**Goals:** Later paint a selection highlight over cuneiform strokes reusing existing State selection.

**Non-Goals:** Not implementing now. No new selection state model. No sync/persistence changes.

## Decisions

- Reuse `ScribeMultilineFieldState` selection range; do not invent parallel state.
- Add a range→pixel-X map in `src/Core/Cuneiform/CuneiformLineLayout.cs`, mirroring the caret char→X map.

## Risks / Trade-offs

- [Wrapped multi-line highlight is fiddly] → resolve in Open Questions before implementing.

## Open Questions

- How to draw a multi-line highlight box over wrapped cuneiform (per-line rects)?
- Clipboard copy of a cuneiform selection = the underlying plain text, confirm.
