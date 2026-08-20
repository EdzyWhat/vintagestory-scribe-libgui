## Context

`ScribeItemLabel.Build` (`src/Mod/ScribeRowWidgets.cs`) is the single shared choke point that renders a
Tracker/Link/Craft row's item NAME across every surface and both views (read + wet editor). On the
cuneiform (Tablet) path it currently emits a `CuneiformText` widget, which is a **single-line** stroke
renderer that ignores `MaxWidth` — so a long item name clips mid-word and runs off the row. Off the
cuneiform path it emits a plain `Text` with `SoftWrap = true`, which wraps — hence the divergence: only
the Tablet clips.

A wrapping cuneiform renderer already exists and is already used for Task/Text row bodies on the Tablet:
`ScribeCuneiformFieldRenderWidget` (`src/Mod/ScribeCuneiformField.cs`). Its `SingleLine` constructor
parameter defaults to `false` (wrapping); the read view uses it display-only for note text
(`ScribeReadContent.cs:458`), and the title band uses it with `singleLine: true`. So the wrapping we
want is already implemented — the item-name path just isn't using it.

## Goals / Non-Goals

- **Goal:** Tracker/Link/Craft item names wrap to width on the Tablet, in one place, for parent rows and
  subtasks, in both read and editor views.
- **Non-Goal:** No change to the single-line dialog title band (must stay single-line).
- **Non-Goal:** No change to non-cuneiform surfaces (already wrap via `Text`/`SoftWrap`).
- **Non-Goal:** No fork of `gui`, no `src/Core/` change, no new dependency.

## Decisions

### Reuse `ScribeCuneiformFieldRenderWidget` display-only in the cuneiform branch

Replace the `CuneiformText(...)` construction in `ScribeItemLabel.Build`'s cuneiform branch with a
display-only `ScribeCuneiformFieldRenderWidget`, mirroring the read-view note usage
(`ScribeReadContent.cs:458-479`): `caret: 0`, `selectionAnchor: 0`, `hasFocus: false`,
`caretVisible: false`, `selectionColor: Vector4.Zero`, transparent `boxColor`/`borderColor`, and
`SingleLine` left at its default `false` so the name wraps. Pass through the existing style knobs the
old `CuneiformText` used (`fontSizeEm: style.FontSize`, `inkColor: color`, `bundle`,
`jitterStrength: style.CuneiformJitter`, `rotationDegrees: style.CuneiformRotation`,
`glow: style.CuneiformGlow`, `padX: style.FieldPadX`, `padY: style.FieldPadY`).

**Alternatives rejected:**
- *Teach `CuneiformText` to wrap* — duplicates layout logic that `ScribeCuneiformFieldRenderWidget`
  already implements; more code, two renderers to keep in sync.
- *Fork `gui`* — disallowed and unnecessary.

### Jitter seed

`ScribeItemLabel.Build` receives only `(label, color, style)` — no `TaskId` to seed jitter with (the
read-view note path seeds from `TaskId.GetHashCode()`). Use a stable seed derived from the label
(e.g. `label.GetHashCode()`) or leave the default `0`; either is stable frame-to-frame, which is what
matters (the seed only needs to be deterministic so the strokes don't shimmer, not unique per row).

## Risks / Trade-offs

- **Row height grows when a name wraps.** This is the intended, correct behavior and already how Task/
  Text rows and every non-cuneiform surface behave; the row/list layout already accommodates multi-line
  content. Verify subtask indentation still reads correctly with a wrapped name.
- **Padding parity.** `CuneiformText` did not take `padX/padY`; the field renderer does. Pass the
  style's field padding (as the read-view note path does) so the wrapped name's left edge and vertical
  rhythm match the row's other content; confirm in-game that the name doesn't shift versus today.

## Migration Plan

None — pure render-path swap, no persisted data, no format change.
