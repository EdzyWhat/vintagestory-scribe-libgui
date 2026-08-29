## Why

`ScribeAnimatedList` (`animated-task-list`) already animates a row's own entry (`ScribeSlideIn`) and
departure (`ScribeRowSizeAnimation(Collapse)`). Departure genuinely animates layout height, so rows
below a removed row smoothly slide up. Entry does not: `ScribeSlideIn` is a paint-only translate+fade
(the "fade-vs-grow split" was deliberately killed for "just do slide" — see
`animate-row-insertion`), so an entering row's full height is reserved in layout from frame 1. Every
row already below it is shoved to its new slot instantly, with no motion. This was barely visible
while every insert always landed at the bottom (nothing below it to shove). Now that **New Task
Insert** and **Pin Insert** can default to/be set to Top (`update-pins-1-3-3`), a Top-inserted
row/pin instantly jumps every existing row down in one frame — a visible regression in feel.

## What Changes

- `ScribeAnimatedList` gains a **reposition** animation: any surviving row (neither entering nor
  departing) whose rendered slot changes between builds animates a displacement from its old
  position to its new one, instead of snapping. This is cause-agnostic — it fires uniformly whether
  the shift was caused by an insertion above, a removal elsewhere, or an explicit reorder (e.g. a
  Sink completion moving a pin to the bottom) — because the container detects it purely from the
  before/after render order, the same way it already detects departures from the before/after item
  set.
- Because the mechanism lives in the shared container (not per-surface code), all four surfaces that
  already render through it — editor, Read view, Pin Tab, pinned-task HUD — get row-reposition
  animation automatically, with no surface-specific wiring. A future surface that renders through
  `ScribeAnimatedList` inherits it the same way it already inherits entry/departure animation.
- The existing `gui-row-insertion-animation` scenario claiming rows "settle downward... rather than
  jumping to their final positions in one frame" is corrected to actually hold (today it does not,
  per the paint-only entry above) by riding on the new reposition mechanism rather than an
  entry-side height-grow.

## Capabilities

### New Capabilities
(none — this generalizes an existing container's charter rather than introducing a new one)

### Modified Capabilities
- `animated-task-list`: `ScribeAnimatedList` additionally animates row **repositioning** (a
  surviving row's slot changing between builds), not just departure.
- `gui-row-insertion-animation`: the sibling-reflow scenario is corrected to describe the real
  mechanism (the shared reposition animation, not an entry-side height-grow) and to hold uniformly
  regardless of insertion edge (Top or Bottom).
- `gui-hud-shared-row-animation`: a Sink/UnpinSink reorder (a pin moving to the bottom without
  departing the item set) now animates via the same reposition mechanism instead of jumping.

## Impact

- `src/Mod/ScribeAnimatedList.cs` — the diff/render-order bookkeeping already tracks each row's slot
  index every build; this adds detecting a live id's slot change and driving a displacement through
  the same host-owned `ScribeAnimationRegistry` self-ticking pattern `ScribeSlideIn`/
  `ScribeRowSizeAnimation` already use (a new registry key namespace, e.g. `move:<id>`).
- No per-surface changes expected in `HudScribePins.cs`, `ScribeDialogBase.*`, or the Pin Tab/Read
  view row builders — they already route through `ScribeAnimatedList` and supply nothing beyond
  their item list and layout builder.
- The central open technical risk (see design.md) is how the container cheaply learns each row's
  actual pixel displacement (rows can vary in height — multi-line wrapped task text) without a full
  post-layout geometry diff every build; this needs a short implementation spike before locking the
  mechanism.
