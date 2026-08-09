# Proposal: Refresh hover while a list row collapses

## Why

When a Scribe list row is removed, its `gui-list-collapse` animation smoothly shrinks the departing
row and slides the row below up under the cursor — but LibGUI only recomputes hover on real mouse
motion (`EventDispatcher.DispatchPointerMove` is called only from `GuiBase.OnMouseMove`), with no
post-layout hover re-check. So the row that slides under a *stationary* cursor keeps its stale
`hovered=false`, and its hover-gated delete/pin buttons stay hidden until the user physically wiggles
the mouse. Mass-deleting therefore forces a tiny mouse wiggle between every delete — a frustrating
loop (docs §4.1). This is high-value polish and a good standalone first change for the v1.1 cycle.

## What Changes

- While **any** list-collapse animation is in progress, the host dialog re-dispatches a synthetic
  pointer-move at the **current cursor position every frame**, so the element that slides under a
  stationary cursor receives its `onEnter` and its hover-gated affordances (delete/pin) reappear
  immediately — no mouse movement required.
- The re-dispatch is driven off the existing per-frame render loop and gated by a new
  "is any collapse still animating?" predicate on the host-owned collapse registry; it does nothing
  when no collapse is in flight (zero steady-state cost).
- Coverage is **all** collapse paths that use `gui-list-collapse` — editor-row delete, HUD unpin, and
  empty-row cleanup — because the trigger is registry-driven and path-agnostic, not delete-specific.
- No new dependency and no change to the `gui` (LibGUI) dep: the fix uses only members already
  reachable from the `GuiBase` subclass (`EventDispatcher`, `RootElement`, `WindowPos`, `GetUiScale`)
  plus the cursor from `capi.Input`, and reuses LibGUI's own synthetic-`PointerEvent` idiom.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `gui-list-collapse`: add a requirement that hover state is kept current for the duration of a
  collapse, so an element sliding under a stationary cursor gains hover (and its hover-gated
  affordances) without requiring physical mouse motion.

## Impact

- **Affected specs:** `gui-list-collapse` (one new requirement + scenarios via a delta spec).
- **Affected code (Mod-side only):** `src/Mod/ScribeCollapsible.cs` (`ScribeCollapseRegistry` gains an
  "any controller animating" predicate); `src/Mod/ScribeDialogBase.Lifecycle.cs` (`OnRenderGUI`
  re-dispatches pointer-move while a collapse animates); `src/Mod/HudScribePins.cs` (same per-frame
  hook for the HUD's collapse registry). Coordinate reconstruction mirrors the existing drag-grip math
  in `src/Mod/ScribeDialogBase.Layout.cs`.
- **`src/Core/`:** untouched — this is pure GUI/input timing behavior with no Core logic (honors the
  API-free-Core invariant).
- **Dependencies:** none added; no `gui`-dep change (the "no library dependencies" guardrail rules out
  upstreaming/forking LibGUI, and its maintainer is unresponsive regardless).
- **Testing:** manual in-game only (hover + pointer dispatch cannot be exercised in the API-free Core
  suite); a small pure helper may be extracted for unit testing if the fix cannot be landed in one
  pass.
- **Not in scope:** row **expand**-into-view animation (a future feature blocked on task-type design)
  — though this fix's direction-agnostic re-hover will cover expansion's identical stale-hover bug for
  free once that animation exists.
