## Context

`GuiDialogScribeLectern` (editor view) already supports drag-reorder: `ScribeDragHandleElement`
per row fires `OnRowDragMouseDown(index)`, the dialog tracks `draggedBlockIndex` and updates
`hoverTargetIndex` on every `OnMouseMove` (via `HitTestRowIndex`), and `OnMouseUp` calls
`scratchDocument.MoveBlock(from, to)` then recomposes. **All of this is already correct and
stays.** What's missing is any on-screen feedback between mouse-down and mouse-up — the list
is visually frozen during the drag.

The hard constraint carried over from the archived `skeuomorphic-lectern-gui` work
(`VSAPI-NOTES.md` "static vs interactive render pass"): content baked into the composer's
static texture at compose time cannot move per-frame. Anything that must move smoothly during
a drag has to be drawn in the **interactive render pass** (`RenderInteractiveElements`, redrawn
every frame). The dialog already has a per-frame hook — the `OnRenderGUI` override that drains
`pendingRecomposeAction` — which is the natural place to advance an animation clock.

The dialog also has an established pattern for small custom elements: `ScribeDragHandleElement`
(extends `GuiElementStaticText`) and `ScribeHoverIconButton` (extends `GuiElementToggleButton`),
both in `ScribeBlockRowCell.cs`, override render/mouse behavior for a narrow purpose. New
feedback elements follow that same pattern.

## Goals / Non-Goals

**Goals:**
- A dragged row visibly **lifts**: a semi-transparent ghost of its content follows the cursor;
  the source row dims in place.
- A **live insertion indicator** shows the exact drop slot and updates continuously as the
  cursor moves between rows.
- The drop **eases**: on release, the ghost tweens into the resolved slot before the real
  reorder+recompose swaps in, so the change doesn't feel like an instant jump.
- Reuse the existing drag state (`draggedBlockIndex`/`hoverTargetIndex`) and `MoveBlock` —
  no new reorder logic, only feedback.

**Non-Goals:**
- Non-dragged rows do **not** shift to open a physical gap ("just the row lifts" model).
- No drag-reorder in read view (editor-only, unchanged).
- No change to `ScribeDocument.MoveBlock` or persistence/networking (no codec bump).
- No fade/opacity animation of *static* row chrome (confirmed infeasible per-row — see the
  scroll-fade research; the ghost sidesteps this by being a purpose-built per-frame element).

## Decisions

**1. The ghost is a single custom interactive-pass element, drawn per-frame at the cursor —
not a moved copy of the real row.**
A row is a cluster of static+interactive sub-elements at a fixed composed Y; it cannot be
relocated per-frame. Instead add one `ScribeDragGhostElement` (custom `GuiElement`) that, each
frame while a drag is active, renders a simple representation of the dragged row — its text,
and a checkbox glyph for a task — inside a semi-transparent rounded box centered on the current
mouse Y (clamped to the list region). It is **non-interactive** (swallows nothing, tests
nothing; it's pure decoration), which is exactly the "make the dragged row static/simple"
intent from scoping — a lightweight stand-in rather than the live editable row.
*Alternative:* animate the real row's elements — rejected, static chrome can't move per-frame
(the core render-pass limitation).

**2. The ghost draws a rendered snapshot, kept cheap.** The ghost's text is the dragged
block's current text (already in `scratchDocument.Blocks[draggedBlockIndex]`); render it with
the same `RowFont()` at the same width. No live text-input element — a drag ghost is never
edited. Match the row's internal layout (drag glyph / checkbox / text at the same relative x)
closely enough to read as "the row I grabbed," per the scoping note ("same size and relative
internal positioning").

**3. Source-row dimming reuses the whole-dialog is impractical; dim via the ghost's presence
instead.** Per-row static alpha is not available (same limitation as the fade research). Rather
than truly dimming the source row's baked pixels, draw a subtle semi-transparent overlay
rectangle over the source row's current on-screen rect from the same per-frame hook (a plain
`Render2DTexture` of a translucent fill, interactive pass, above the row). This reads as "this
row is lifted" without needing per-row static alpha.
*Alternative:* recompose the source row at lower alpha — rejected, static alpha is dialog-wide
only.

**4. The insertion indicator is a second custom per-frame element (or the same one) drawn at
the boundary Y for `hoverTargetIndex`.** A horizontal line/arrow spanning the list width,
positioned at the top edge of the target slot (or bottom of the last row when dropping at end).
`hoverTargetIndex` already updates every `OnMouseMove`; the indicator just reads it each frame.
When it changes slot, the indicator can **ease** to the new Y over a few frames rather than
snapping (a simple lerp toward the target Y in the per-frame hook).

**5. Animation timing lives in `OnRenderGUI` using its `deltaTime`.** No new tick listener —
`OnRenderGUI(float deltaTime)` already runs per frame and receives the frame delta. Advance the
indicator-lerp and the drop-settle tween there. Since `Date.now`-style wall-clock isn't needed,
accumulate `deltaTime`. Keep tweens short (~100–150 ms) so they feel responsive, not sluggish.
*Alternative:* a `RegisterGameTickListener` — rejected, `OnRenderGUI` already exists, runs at
frame rate (smoother than a fixed tick), and avoids another listener to register/unregister.

**6. Drop-settle sequence.** On `OnMouseUp` with an active drag: capture `from`/`to`, start a
short tween of the ghost from its current cursor Y to the resolved slot Y; when the tween
completes (next-frame check in `OnRenderGUI`), call `MoveBlock(from,to)` + recompose + clear
the drag/ghost state. If `from == to` (dropped in place), skip the tween and just clear. Guard:
if the dialog closes mid-drag, clear all feedback state (extend the existing `EnterMode`/
`OnGuiClosed` resets).

**7. All feedback state resets alongside the existing drag state.** `draggedBlockIndex`,
`hoverTargetIndex`, and the new ghost/tween fields are cleared together in `EnterMode` and on
drop, mirroring how the current code already nulls the drag indices.

## Risks / Trade-offs

- **Z-ordering of the ghost/overlay above rows** → the ghost and dim overlay must render above
  the row content. Draw them at a z above the rows' ~50 (the scrollbar uses 200; the scroll
  research confirmed higher-z interactive draws land on top). Verify in-game. → Mitigation:
  use an explicit high-ish z on the ghost's `Render2D*` calls; confirm on a real build.
- **Ghost fidelity vs. cost** → a pixel-perfect clone of a row (live text box, real checkbox
  widget) would be expensive and pointless. → Mitigation: draw a simplified snapshot (text +
  glyph in a translucent box); it only needs to read as the grabbed row, not be editable.
- **Interaction with the existing scroll/recompose machinery** → a drag that also scrolls (auto-
  scroll near edges) is out of scope here; if the list is scrolled during a drag the indicator
  math must use live `bounds.absY` like `HitTestRowIndex` already does. → Mitigation: read live
  bounds every frame, never cache composed Y.
- **Tween adds a brief delay before the reorder is visible** → keep it short (~120 ms) and skip
  it entirely for a drop-in-place. → Mitigation: tunable duration constant (config knob,
  consistent with the existing `ScribeClientConfig` layout knobs).
- **Manual-test-only verification** → no server-observable behavior, so no automated coverage;
  relies on playtest. → Mitigation: a concrete manual test task, consistent with prior GUI work.

## Open Questions

- **Exact ghost visual** (opacity, border, whether it shows the checkbox state) — pick concrete
  values at implementation and confirm in-game; leave as tunable `ScribeClientConfig` knobs so
  they can be adjusted without a rebuild, matching the existing pattern.
- **Indicator style** — a thin line vs. a line-with-arrowhead vs. a caret. Start with a simple
  high-contrast horizontal line (cheapest, unambiguous) and revisit if it reads poorly.
- **Should the ghost be clamped to the list rect or allowed to follow the cursor anywhere?**
  Lean toward clamping to the list region so it never overlaps the toolbar/title; confirm feel
  in-game.
