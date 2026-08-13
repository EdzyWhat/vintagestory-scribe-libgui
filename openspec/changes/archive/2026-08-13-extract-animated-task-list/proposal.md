## Why

The editor view's smooth row-removal animation (a departing row collapses its height so
rows below slide up, with the scroll viewport easing in lockstep) is welded to
`ScribeEditorContent` plus a hand-wired state machine living in `ScribeDialogBase`. The
Tablet gets it for free only because it is a `ScribeDialogBase` subclass that reuses that
exact editor path. The HUD re-implements the *same* choreography as its own copy. The
**Pinned tab has none of it** — a removed pin simply vanishes and the list snaps up.

The animation *primitives* (`ScribeRowSizeAnimation`, `ScribeAnimationRegistry`,
`ScribeHoverRefreshLatch`) are already clean and reusable. What is duplicated (editor ↔ HUD)
and missing (Pinned, and every future surface) is the **orchestration**: the departing-row
bookkeeping and display-index math, the "snapshot → mark departing → delete → rebuild"
handler, the build-time splice of frozen ghosts, the deferred collapse-cleanup, and the
`OnRenderGUI` scroll-pin / hover-refresh / clamp loops. Every new surface (Pinned now; Desk,
richer read views later) must re-wire all of that by hand and be re-playtested for the same
behavior. The author wants removal (and future insert/reorder) animations to be
**inheritable — to come along for free** on any surface, without bespoke per-view wiring.

Now is the moment because the just-completed `reconcile-animating-surfaces` change is the
enabling foundation: a diffing container must survive a data change to see the before/after
row set, which `ForceRebuild` (a full unmount) prevented — reconcile/`SetState` is precisely
what makes a reusable animated container possible.

## What Changes

- Introduce a reusable **`ScribeAnimatedList`** container widget (Mod-side, view-agnostic).
  A surface renders it, feeding **(a)** its ordered items keyed by stable `Guid` and **(b)** a
  row-builder. The container internally diffs the incoming item list against the rows it built
  last frame: an id that disappeared is kept as a frozen ghost, wrapped in
  `ScribeRowSizeAnimation` (collapse), and dropped on completion; the host only mutates its data
  (delete from scratch / drop the pin) and the container **infers** the departure. It owns its
  own `ScribeAnimationRegistry` and collapse-cleanup, and hosts the scroll-pin-during-collapse
  and hover-refresh-latch behavior internally.
- **Removal-timing is an opt-in policy, not baked in.** The default (Editor / Read / Pinned and
  all future tabs) is **immediate action + smooth collapse** — the model the author wants
  standard. The **HUD** opts into its existing **fade + undo-window delay**, which is retained
  because it is a misclick-rescue coupled to the HUD deliberately *hiding* the Completion Policy;
  the Pinned tab shows and lets you change that policy and has discrete unpin/delete buttons, so
  it needs no undo grace. The container must keep "animate the departure" (universal) separate
  from "delay the departure behind a fade/undo window" (HUD-only opt-in).
- **Adopt `ScribeAnimatedList` in the Pinned tab first** (highest payoff, zero animation today,
  lowest risk — it does not touch the already-playtested editor/HUD paths). Pinned removals
  (complete / unpin / delete) gain the immediate collapse-and-slide-up, matching the editor feel.
- Migrating the editor and HUD onto the shared container (collapsing their duplicated
  choreography) is explicitly **out of scope** for this change — deferred to a follow-up once the
  container is proven on Pinned, so this change never destabilizes two working surfaces.

## Capabilities

### New Capabilities
- `animated-task-list`: a reusable, view-agnostic list-container component that animates row
  departures (and provides the hooks for future insert/reorder animations) by diffing an
  id-keyed item set frame-to-frame, so any surface gets removal animation by rendering it —
  with removal timing (immediate vs. fade/undo-delayed) selectable by policy.

### Modified Capabilities
- `pinned-task-tab`: add a requirement that pin removal (complete / unpin / delete) animates —
  the departing row collapses and neighbors slide up immediately, with **no** undo window
  (affirmative controls + visible, editable Completion Policy mean no misclick grace is needed),
  contrasting with the HUD's delayed path.
- `gui-list-collapse`: broaden the mechanism from a host-wired primitive to one that a reusable
  container can drive by inferring departures from a data diff, so the departing-row bookkeeping
  (snapshot map, display-index math, deferred cleanup) is packaged rather than re-implemented per
  host.

## Impact

- **New code:** `src/Mod/ScribeAnimatedList.cs` (the container + its internal ghost/registry/
  cleanup state machine); a generic frozen-row snapshot mechanism (today each view hand-rolls its
  own, e.g. `ScribeFrozenEditorRow`).
- **Modified:** `ScribePinnedContent.cs` and `ScribeDialogBase.PinTab.cs` — route pin rows through
  `ScribeAnimatedList`; pin remove handlers stop relying on the server re-push to silently drop the
  row and instead let the container animate the departure.
- **Reused unchanged:** `ScribeRowSizeAnimation` / `ScribeAnimationRegistry` / `ScribeHoverRefreshLatch`
  (`ScribeRowSizeAnimation.cs`), and the reconcile plumbing (`ScribeDialogBody`, `RebuildBody`).
- **Not touched (this change):** editor (`ScribeEditorContent` + `ScribeDialogBase` delete path),
  HUD (`HudScribePins`), Tablet — all keep working as-is; their migration onto the container is a
  follow-up.
- **`src/Core/` untouched** — this is purely a Mod-side GUI-composition change. No new dependencies.
