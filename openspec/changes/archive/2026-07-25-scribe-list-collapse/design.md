## Context

Scribe renders two dynamic row lists — the on-screen pinned-task HUD (`HudScribePins`) and the
lectern editor (`GuiDialogScribeLecternLibGui`). Both are built on LibGUI and both rebuild via
`GuiBase.ForceRebuild()`, which **unmounts and recreates the whole widget tree** (it does not
reconcile). Today, when a row leaves a list — a HUD unpin/delete completion after its ~1.5s
undoable window, or a lectern editor delete — the row is dropped from the render in a single
frame and the rows below snap up. The `scribe-settings-followups` change already fades the HUD
row's *text* to zero during the window (via the self-ticking `ScribeFadeText` widget), but the
row *height* then vanishes abruptly.

The archived `add-settings-tab` change recorded (D7 / task 8.4) that a row-motion animation was
deferred because LibGUI's implicit `AnimatedSlide` can't animate a `Column` reorder under
`ForceRebuild`. This change resolves the **deletion** half of that deferral; the reorder-glide
(FLIP) half remains deferred.

Verified LibGUI facts grounding this design (read from `reference/vslibgui/`):
- Any stock/implicit animation widget (`AnimatedOpacity`, `AnimatedSize`, `AnimatedContainer`)
  recreated fresh under `ForceRebuild` inits `Begin == End == target` and **snaps** — no motion.
  The only pattern that animates under `ForceRebuild` is a self-ticking `StatefulWidget` owning
  its own `AnimationController` (started in `InitState`, repaint via `MarkNeedsBuild`), exactly
  as `ScribeFadeText` does.
- `RenderAnimatedSize` is the one truly layout-driven LibGUI animation: it reports its
  interpolated size as its own layout `Size` and calls `MarkNeedsLayout` per tick, so a parent
  `Column` reflows and pushes siblings each frame. But it snaps under `ForceRebuild` and exposes
  **no completion callback** — so we replicate its layout behavior in our own render box and add
  the completion hook.
- `AnimationController.OnStatusChanged` fires `Completed` at value 1.0 — this is the removal hook.

## Goals / Non-Goals

**Goals:**
- A departing row's layout height animates smoothly from full to zero (~200ms), so the rows
  below slide up to meet it rather than snapping.
- The row is removed from the model only *after* the collapse completes.
- Works correctly under each surface's `ForceRebuild`-only rebuild path (no snap, no restart
  stutter across intervening rebuilds).
- One reusable widget shared by both the HUD and the lectern editor.
- `src/Core/` stays untouched and API-free; no new dependencies.

**Non-Goals:**
- The FLIP reorder-*glide* (a completed task sliding to the bottom on Sink; editor rows gliding
  on drag-reorder). Reorder keeps today's instant jump — deferred to a future `scribe-list-reorder`.
- An insertion/appear animation for newly added rows.
- Any change to completion policy, undo-window semantics, or server-authoritative edit flow.

## Decisions

### D1 — A self-ticking collapse widget, not a stock `AnimatedSize`
Build `ScribeCollapsible` (a `StatefulWidget` in `src/Mod/ScribeCollapsible.cs`) mirroring the
proven `ScribeFadeText` pattern: on mount in the collapsing state it creates an
`AnimationController`, subscribes `OnValueChanged -> MarkNeedsBuild` and
`OnStatusChanged -> (Completed => onCollapsed)`, and `Forward()`s. Its `Build` computes a height
factor `1 - curve.Transform(value)` and wraps the child in a small custom render box,
`ScribeHeightFactorBox` (modeled on the existing `ScribeMultilineFieldRender : RenderBox`), which
lays the child out at full constraints, reports `Size = (childWidth, childHeight * factor)`, and
clips paint to that box — the same layout-shrink behavior as `RenderAnimatedSize`, so the parent
`Column` reflows and slides siblings up.
- *Alternative considered — subclass/reuse `RenderAnimatedSize`:* rejected. It snaps under
  `ForceRebuild` (no self-tick) and has no completion callback, so it can't drive removal.
- *Alternative — collapse via an implicit `AnimatedContainer` height tween:* rejected for the
  same `ForceRebuild`-snap reason.

### D2 — Host-owned collapse state keyed by task identity (resume, don't restart)
Because every `ForceRebuild` remounts `ScribeCollapsible`, a `State`-owned controller would
restart from zero on each intervening rebuild and the collapse would stutter or never finish.
A small host-owned `ScribeCollapseRegistry` (mirroring `ScribeNumericFocusRegistry`) holds one
`AnimationController` per departing task identity; the remounted widget looks up its controller
by id and **resumes** from elapsed progress. The host owns and disposes the registry.
- *Alternative — suppress `ForceRebuild` while a collapse is active:* rejected as too broad and
  fragile (the HUD/lectern rebuild for many unrelated reasons; gating them risks staleness).

### D3 — HUD: a "departing rows" set replaces the instant drop
`HudScribePins` gains `Dictionary<(Guid,Guid), HudPinRow> departing`. On an unpin/delete window
expiry (where today `awaitingRemoval` hides the row), the row's last-known `HudPinRow` snapshot is
moved into `departing`; `Build()` appends departing rows (keyed by their existing
`ValueKey<Guid>(TaskId)`) wrapped in `ScribeCollapsible(collapsing: true)`. The text is already
faded to ~0 by `ScribeFadeText`, so the collapse reads as the now-empty row closing up
(fade → collapse, two sequential phases). `onCollapsed` removes the entry and rebuilds.
`departing` is cleared on server-push reconciliation (`OnMyPinsChanged`, like `awaitingRemoval`)
so a re-pin can't get stuck invisible, and `sunkOrder` pruning must not drop a `departing` key
early. Undo is only valid *within* the window and the collapse starts only *after* expiry, so
"undo mid-collapse" is structurally impossible.

### D4 — Lectern editor: collapse a frozen snapshot in place
`DeleteEditorBlock(index)` snapshots the deleted `ScribeEditRowData` into
`Dictionary<Guid, ScribeEditRowData> departingEditorRows` **before** removing it from `scratch`
(the scratch deletion stays, so the data model and autosave remain correct immediately).
`BuildEditorContent` splices the departing row back in *at its old index*, wrapped in
`ScribeCollapsible`, rendered as a **static, non-interactive snapshot** (read-style row / read-only
field, no focus node — its scratch block and focus node are already gone). `onCollapsed` removes
the entry and rebuilds.

### D5 — Defer the scroll re-clamp until the collapse completes
The lectern's post-delete `RequestClampToExtent()` re-clamps scroll to the shrunk content extent.
During a collapse the content height is unchanged until the row reaches zero, so clamping is
**moved from the delete site into `onCollapsed`** to avoid a clamp that fights the shrink.

## Risks / Trade-offs

- **Controller restart on remount** → D2's host-owned registry keyed by task id resumes from
  elapsed progress instead of restarting.
- **Rapid repeated deletes** → the departing sets key by identity, so multiple rows collapse
  independently; each has its own registry controller and `onCollapsed`.
- **Departing set desync with `awaitingRemoval`/`sunkOrder`** → clear `departing` on server-push
  reconciliation and guard `sunkOrder` pruning against departing keys; verified in playtest by
  unpin-then-immediately-repin.
- **Scroll re-clamp fighting the collapse (lectern)** → D5 defers the clamp to `onCollapsed`.
- **Deleting the focused editor row mid-nothing** → the departing snapshot is non-interactive and
  its focus node is already resynced away by the existing delete path, so no focus lands on it.
- **Not Core-testable** → animation is VS/LibGUI-bound; verification is manual playtest per the
  repo's `TESTING.md` workflow. No game-agnostic rule is extractable (the departing-vs-live
  ordering already lives in the Mod layer).

## Migration Plan

Purely additive client-side rendering behavior. No persistence, network, or save-format change;
no server involvement. Rollback is reverting the three touched files. No data migration.

## Open Questions

- Exact collapse duration and easing curve (~200ms, `EaseOut`/`EaseInOutCubic`) to be tuned in
  playtest; the spec fixes the behavior, not the numeric constants.
