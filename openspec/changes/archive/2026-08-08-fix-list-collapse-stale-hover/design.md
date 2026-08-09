# Design: Refresh hover while a list row collapses

## Context

The `gui-list-collapse` mechanism (`src/Mod/ScribeCollapsible.cs`) animates a departing row's height
1→0 over ~200ms and slides the rows below up to fill the gap. That animation is correct and is **not**
the bug. The bug is in LibGUI's hover model:

- Hover enter/exit is computed in exactly one place — `EventDispatcher.DispatchPointerMove`
  (`reference/vslibgui/Gui/Gui/Widgets/Gestures/EventDispatcher.cs:120`), which hit-tests at the
  pointer position, compares the hit element to `_hoveredElement`, and fires exit/enter.
- That method is called from exactly one non-test site: `GuiBase.OnMouseMove`
  (`reference/vslibgui/Gui/Gui/GuiBase.cs:720`) — i.e. **real cursor motion**.
- There is **no post-layout / post-frame hover re-evaluation** anywhere in LibGUI (no Flutter-style
  `MouseTracker`; confirmed by an exhaustive grep of the `gui` source).

Consequence: when a collapse reflows the list and a *different* row slides under a *stationary*
cursor, LibGUI never notices. The freshly-mounted row states default `hovered=false`, the dispatcher's
`_hoveredElement` is stale, and the hover-gated delete/pin buttons stay hidden until the user moves the
mouse. This is docs §4.1 ("Faster delete") — the mouse-wiggle-to-reveal loop that makes mass-deleting
painful.

Relevant already-existing infrastructure this design builds on:
- `ScribeCollapseRegistry` (host-owned; `editorCollapseRegistry` at `ScribeDialogBase.cs:213`, and the
  HUD's own registry in `HudScribePins.cs`) already tracks each in-flight `AnimationController` and
  exposes `IsComplete(id)`.
- `ScribeCollapsibleState.OnValueChanged → Element.MarkNeedsBuild()` (`ScribeCollapsible.cs:222`)
  already rebuilds the tree **every frame** while a collapse animates.
- `OnRenderGUI(deltaTime)` (`ScribeDialogBase.Lifecycle.cs:34`) already runs every frame and already
  hosts the collapse-cleanup and scroll-settle logic.
- The drag-grip (`ScribeDialogBase.Layout.cs:293-305`) already reads `capi.Input.MouseX/MouseY` and
  converts raw→logical by dividing by the UI scale — the exact conversion needed here.

## Goals / Non-Goals

**Goals:**
- After a row is removed, its hover-gated controls (delete/pin) become available under a **stationary**
  cursor without any mouse movement, for the whole duration of the collapse.
- Keep hover current under a stationary cursor after **any** tree rebuild (`ForceRebuild`), not only
  collapse-driven ones — unpin, new-row insert, title-edit toggle — since every rebuild hits the same
  stale-hover-on-a-fresh-tree problem (found in the first playtest: HUD unpin, which isn't collapse-
  animated, and new-row creation both dropped hover).
- Cover **all** collapse paths (editor delete, HUD unpin, empty-row cleanup) with one path-agnostic
  mechanism.
- No new dependency; no change to the `gui` dep; `src/Core/` untouched.

**Non-Goals:**
- **Fluid mass-delete where the CLICK lands mid-collapse** (deleting the next row *before* the prior
  collapse finishes). The playtest showed the hover half works — you can see the delete button mid-
  collapse — but the *click* misses until the collapse completes, because the departing ghost-snapshot
  row still occupies the shrinking space and intercepts the hit-test. That is a click-target problem,
  not a hover problem; it needs the departing snapshot to become transparent to hit-testing (or the
  click routed to the live row). Backlogged as low-value ("90% there") — see `docs/vnext-ideas.md`.
- Row **expand**-into-view animation (blocked on task-type design; a separate future change). Noted
  only because this fix's re-hover is direction-agnostic and will cover expand's identical bug for free.
- Adding a collapse animation to the HUD pinned surface's unpin/delete (user's stated preference for a
  follow-up). This change makes hover *recover* after the unpin rebuild; animating that removal is a
  separate, larger change that would build on this same latch.
- Any change to *how* the collapse animates (duration, curve, ghost-snapshot logic) — that is correct.
- A general LibGUI `MouseTracker` (see Decision 1 — out of our control and violates the deps rule).

## Decisions

### Decision 1 — Fix mod-side, not in LibGUI

The architecturally "correct" fix is a post-layout hover re-check inside LibGUI's `EventDispatcher`.
**Rejected.** `gui` is a downloaded hard dependency, not a fork we control. Upstreaming means a PR to
an unresponsive maintainer (a logged issue has sat unanswered for weeks), and shipping our own forked
`gui` build would make us the maintainer of a divergent hard dep that collides with every other mod
depending on the real `gui`. The project's "no library dependencies" guardrail exists precisely for
this. So the fix lives in the Scribe Mod layer, using only public/protected LibGUI surface.

### Decision 2 — Re-dispatch a synthetic pointer-move at the current cursor position

The only lever available is "make something re-answer *what is under the cursor now*," because the
mouse genuinely will not tell us. We do that by calling
`EventDispatcher.DispatchPointerMove(RootElement, new PointerEvent(localX, localY))` at the current
cursor position, which re-runs the hit-test and self-heals `_hoveredElement`.

This is **not** a kludge that fights the hover model — it reuses LibGUI's own idiom: `OnMouseMove`
itself fabricates `new PointerEvent(-1, -1)` (off-screen) to force a `PointerLeave` when another dialog
handled the move (`GuiBase.cs:689`). Synthesizing a pointer event to correct hover state is how LibGUI
already works.

**Coordinate reconstruction** (mirrors the private `ToWindowLocal(ToLogicalScreen(...))`):
```
local = new Vector2(capi.Input.MouseX, capi.Input.MouseY) / GetUiScale() - WindowPos;
```
All members are reachable from the `ScribeDialogBase`/`HudScribePins` subclass: `EventDispatcher`
(public), `DispatchPointerMove` (public), `RootElement` (public), `WindowPos` (protected),
`GetUiScale()` (protected). `_lastMouseLocal` is private, which is why the cursor is sourced from
`capi.Input` rather than LibGUI's cache — the same choice the drag-grip already made. Guard the
dispatch on a non-null `RootElement`/`RenderObject` (as `OnMouseMove` does).

### Decision 3 — Continuous (every frame while collapsing), not once at completion

**Alternative considered:** fire the re-dispatch once, when a collapse completes (in
`OnEditorRowCollapsed`). **Rejected** — during the ~200ms collapse the geometry slides *continuously*,
so a completion-only refresh leaves hover stale for that whole window; a fast mass-delete (deleting the
next row mid-collapse) would still stutter. Correcting hover only when motion *stops* misses the moving
target.

**Chosen:** re-dispatch **every frame while any collapse controller is animating**. This costs almost
nothing over the completion-only approach because the frame loop is already spinning during the
collapse (`OnValueChanged → MarkNeedsBuild`) and the registry already tracks in-flight controllers. The
added machinery is a single predicate.

### Decision 4 — Drive it from `OnRenderGUI`, gated by a new registry predicate

Add `bool AnyAnimating` to `ScribeCollapseRegistry` (sibling to `IsComplete`): true iff any owned
controller's `Status != Completed`. In `OnRenderGUI`, after the existing collapse-cleanup block, if the
registry reports `AnyAnimating`, reconstruct the local cursor position and re-dispatch the pointer-move.
When nothing is collapsing the predicate is false and the block is a no-op — zero steady-state cost, and
normal on-motion hover is untouched.

### Decision 4b — Linger a few frames past the last animating frame (frame latch)

**Discovered in the first playtest:** hover self-heals *during* the collapse (the new row correctly shows
as hovered) but is lost *exactly when the collapse ends* — the delete button that was visible mid-collapse
disappears at completion. Root cause is a one-frame gate gap: a collapse's completion callback flips its
controller to `Completed` (so `AnyAnimating` is *already* false) **and** arms the deferred
`ForceRebuild()`. `ForceRebuild` unmounts the tree and mounts a brand-new one where every element is
`hovered=false`, and that fresh tree is not laid out until a later frame. So on the cleanup frame the
`AnyAnimating`-gated refresh is skipped, and even if it fired it would hit-test nothing (new tree has no
geometry yet) — no synthetic move ever lands on the rebuilt, laid-out tree.

**Chosen:** a tiny `ScribeHoverRefreshLatch` (a 3-frame countdown) re-armed both while `AnyAnimating` is
true *and* on the cleanup-rebuild frame; the refresh dispatches whenever the latch ticks. Three frames
comfortably spans completion → `ForceRebuild` → layout → paint on both hosts' `OnRenderGUI` orderings (the
dialog rebuilds after its `base` layout call, the HUD before it). This is the same general LibGUI rule that
anything reacting to a `ForceRebuild` must allow the fresh tree a later frame to lay out.

### Decision 4c — Arm the latch on ANY rebuild, detected by `RootElement` identity

**Discovered in the same playtest:** HUD **unpin** and **new-row creation** both drop hover the same way,
but neither is collapse-animated — they just `ForceRebuild`. So an `AnyAnimating`-only trigger never fires
for them, and the row that ends up under the stationary cursor in the fresh tree stays un-hovered. This is
the "it will happen for *every* rebuild" generalization: the stale-hover-on-a-fresh-tree problem is a
property of `ForceRebuild` itself, independent of any animation.

**Chosen:** detect a rebuild centrally by watching `RootElement` **identity**. `GuiBase.ForceRebuild`
assigns a brand-new `RootElement` instance (`GuiBase.cs:1414`) and that is the *only* thing that replaces it
post-mount, so "`RootElement` is a different instance than the frame before" is an exact, zero-false-positive
rebuild signal. `ScribeHoverRefreshLatch.ArmIfRebuilt(RootElement)`, called once per frame from
`OnRenderGUI`, arms the same linger whenever it changes. This needs **no per-call-site wiring** — every
current and future `ForceRebuild` path (unpin, new-row, title-edit, corruption rebuild, collapse cleanup)
is covered automatically, and it subsumes the explicit collapse-cleanup arm from Decision 4b. Rejected
alternatives: (a) arming at each `ForceRebuild` call site — brittle, must remember every new one; (b) a
LibGUI hook/callback on rebuild — needs a `gui`-dep change (Decision 1 forbids).

### Decision 5 — Cover all collapse paths via the registry, not per-call-site

Because the trigger is "the registry has an animating controller," it is inherently path-agnostic:
editor delete, empty-row cleanup, and HUD unpin all flow through a collapse registry. The lectern
dialog wires it in its `OnRenderGUI`; the HUD (`HudScribePins.cs`) wires the same per-frame re-dispatch
against its own registry. No per-removal-site code.

## Risks / Trade-offs

- **[Re-dispatch side effects beyond enter/exit]** `DispatchPointerMove` also fires `OnPointerMove` on
  the hovered element and updates cursor resolution. → Low risk: it is the same call real motion makes,
  at the real cursor position, so any handler sees a legitimate "pointer is here" event. Verify in-game
  that tooltips/press states don't flicker during a collapse.
- **[Coordinate/scale mismatch]** A wrong scale or window-pos offset would hover the wrong element. →
  Mitigated by reusing the exact conversion the drag-grip already ships and relies on; verify by
  watching the correct row's controls appear under the cursor.
- **[Per-frame cost during collapse]** One extra hit-test per frame for ~200ms. → Negligible; the tree
  is already being rebuilt every one of those frames anyway.
- **[Cursor outside the window]** If the cursor is not over the dialog, the re-dispatch should simply
  hit nothing (or leave). → Acceptable; matches real-motion behavior. Optionally skip the dispatch when
  the local point is outside window bounds (`IsInsideWindow` logic is trivial to replicate) to avoid
  needless work.
- **[Can't unit-test]** Hover + pointer dispatch require a live game. → Manual in-game verification
  (see tasks); if the fix can't be landed in one pass, extract a pure helper (given cursor xy, window
  pos, scale → expected local point; given controller states → should-rehover bool) that `Core` can
  test, leaving only the dispatch call itself untested.

## Migration Plan

Pure additive behavior change; no persistence, no save-compat, no data model. Rollback = revert the
Mod-side edits. No `gui`-dep or CI/workflow changes.

## Open Questions

- Whether to skip the re-dispatch when the cursor is outside the dialog window (minor optimization vs.
  simplicity) — resolve during implementation based on whether an out-of-window dispatch causes any
  observable flicker.
