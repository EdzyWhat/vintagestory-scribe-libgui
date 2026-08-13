## Context

Scribe renders task rows on several surfaces. Today each surface hand-builds its own row
list in a `Column`, and only some have removal animation:

- **Editor** (`ScribeEditorContent` + a state machine in `ScribeDialogBase`): height-collapse
  on delete. The collapse orchestration is host-owned — `departingEditorRows`
  (`Dictionary<Guid,(row,index)>`), `editorCollapseRegistry`, `DeleteEditorBlock` (snapshot →
  mark departing → delete from scratch → `RebuildBody`), the build-time splice of each ghost as
  a `ScribeRowSizeAnimation(Collapse)` wrapping a `ScribeFrozenEditorRow`, `OnEditorRowCollapsed`
  cleanup, and three `OnRenderGUI` loops (scroll-pin-during-collapse, hover-refresh latch, clamp).
- **Tablet** (`GuiDialogScribeTablet : ScribeDialogBase`): inherits the editor path wholesale by
  calling `BuildEditorContent()` — zero animation code of its own.
- **HUD** (`HudScribePins`): its *own* copy of that state machine (`departing`, `awaitingRemoval`,
  `BeginDeparting`, `ReconcileDeparting`, `OnDepartingCollapsed`, `needsCollapseCleanup`), plus a
  fade (`ScribeFadeText`) and sink-mute (`AnimatedOpacity`) that gate on a short **undo window**.
- **Pinned tab** (`ScribePinnedContent`): **no animation.** Remove handlers fire a network packet;
  the server re-push lands in `OnMyPinsChanged` and the row is simply absent on rebuild → it
  vanishes and the list snaps up.

The reusable *primitives* already exist and are view-agnostic (`ScribeRowSizeAnimation`,
`ScribeAnimationRegistry`, `ScribeHoverRefreshLatch` in `ScribeRowSizeAnimation.cs`); the
`gui-list-collapse` capability specs the collapse mechanism. What is **not** packaged is the
*choreography* that decides what departs, splices ghosts, and drives the scroll/hover loops —
it is duplicated (editor ↔ HUD) and missing (Pinned).

The just-completed `reconcile-animating-surfaces` change is the enabling foundation: reconcile
(`ScribeDialogBody` + `RebuildBody`, persistent-root `SetState`) means a widget subtree now
survives a data mutation. A container can therefore compare the row set it built last frame to
the incoming one — impossible under the old `ForceRebuild`, which unmounted everything.

## Goals / Non-Goals

**Goals:**
- One reusable, **rendering-agnostic** container (`ScribeAnimatedList`) that animates row
  **departures** by diffing an id-keyed item set frame-to-frame, so any surface gets removal
  animation by rendering it and mutating only its data — no per-view departing-map, ghost widget,
  cleanup flag, or `OnRenderGUI` loop.
- Abstract **motion only**, never content. The container touches exactly two things about a row —
  its stable identity (`Guid`) and its height (to collapse it) — and never inspects what the row
  renders. Heterogeneous, intentionally-divergent row/column builders across views (an editable
  task row, a static Read line, a multi-column Guestbook entry, a History line) are a first-class
  assumption: each view supplies its own builder and they are *meant* to stay different. Extracting
  the shared motion is precisely what lets the content diverge safely.
- Removal **timing** selectable by policy: immediate collapse (default, for Editor/Read/Pinned
  and all future tabs) vs. fade + undo-window delay (HUD opt-in only).
- Prove it on the **Pinned tab** first (no animation today → highest payoff, no risk to
  already-playtested surfaces).
- Leave a clear seam for future **insert / reorder** animations (the diff already knows which
  ids appeared, not just which departed).

**Non-Goals:**
- **A "view behavior profile" or any object that unifies row *behavior*** (editable vs static,
  drag-reorder, checkbox, columns, per-row tools, sort/ordering). Explicitly rejected: those
  differences are expressed by each view composing different elements in its own row/column builder
  and are supposed to stay divergent. There is nothing to unify there, and such a layer would fight
  the divergence we want. The only thing extracted is motion. (See D6.)
- Migrating the editor, Tablet, or HUD onto the container. Deferred to a follow-up once proven on
  Pinned; this change must not destabilize working, playtested surfaces.
- Changing the HUD's undo-window behavior or the Pinned tab's instant-completion semantics
  (Pinned still completes immediately — we add the *animation*, not an undo delay).
- Row virtualization / large-list windowing (lists stay non-virtualized `Column`s, as today).
- Any `src/Core/` change or new dependency.

## Decisions

### D1 — A diffing container, not a base class or a copied handler
`ScribeAnimatedList` is a `StatefulWidget`. Its state caches the id-keyed rows it built last
frame. On rebuild it diffs incoming ids against cached ids: **departed** ids (present last frame,
absent now) are retained as frozen ghosts, spliced back at their last display index, wrapped in
`ScribeRowSizeAnimation(Collapse)`; on the animation's `onEnd` the ghost is dropped from the
cache. The host **only mutates data** (delete from scratch / drop the pin) and calls its normal
`RebuildBody()` — the container infers and animates the departure.

*Why over alternatives:* (a) a shared **base class** was rejected — the editor already ships a
working hand-wired path and forcing all surfaces under one base is the exact over-coupling that
made this hard to change; a *component* a surface renders is looser and adoptable one surface at a
time. (b) **Copying the editor handler into Pinned** (the HUD's approach) is what created the
duplication we're removing. (c) The container inferring departures from the data diff (rather than
the host explicitly calling `BeginDeparting`) is what makes it "come along for free" — the host
never learns the animation vocabulary.

### D2 — Frozen-ghost snapshot supplied by the row-builder, captured by the container
The container can't render a departed item's row from live data (the data is gone). It caches the
**built widget** (or a builder-supplied frozen snapshot) for each id at build time and re-renders
that cached widget as the ghost. Each surface's rows are already value-snapshot records keyed by
`ValueKey<Guid>(TaskId)`, so caching the last built row per id is well-defined. This generalizes
the bespoke `ScribeFrozenEditorRow` — the container holds the last-good render instead of each
view hand-authoring a static twin. (The row-builder MAY provide an explicit non-interactive
snapshot for a given id if a live row isn't safe to freeze; default is to reuse the last build.)

### D3 — Removal timing is a policy enum, decoupled from the collapse
`ScribeAnimatedList` takes a `RemovalPolicy`: **Immediate** (default — ghost begins collapsing the
frame it departs) or **Delayed** (ghost holds full-height for an undo window, optionally fading via
`ScribeFadeText`, then collapses). The collapse mechanism is identical either way; only *when* it
starts differs. This is the load-bearing separation: "animate the departure" is universal, "delay
behind a fade/undo window" is HUD-only. Pinned uses **Immediate** (its Completion Policy is visible
and editable and it has discrete unpin/delete controls → affirmative choices, no misclick grace
needed). The HUD's **Delayed** exists because it *hides* the Completion Policy to save screen space,
so a completion may be a silent delete-with-no-undo — the fade is the rescue. See the
`hud-undo-window-is-policy-hiding` rationale.

### D4 — Container owns registry, cleanup, scroll-pin, and hover-latch internally
The container instantiates its own `ScribeAnimationRegistry` (host-owned lifetime so a motion
survives rebuilds/reconcile), retires ghosts on collapse-end without a host-level cleanup flag, and
drives scroll-pin-during-collapse + the hover-refresh latch off its own animating state. The
surface passes in the scroll controller (or the container discovers the enclosing one) so scroll-pin
still works. This removes the per-host `OnRenderGUI` loops.

### D5 — Adopt on Pinned only; keep editor/HUD untouched
Pinned routes its `Column` of `ScribePinRow`s through `ScribeAnimatedList`; the pin remove handlers
(`OnPinDeleteTask`/`OnPinUnpinTask`/`OnPinCompleteTask`) keep firing their packet and let the
container animate the now-absent row on the next `OnMyPinsChanged` rebuild. Editor and HUD are not
edited. This bounds the playtest surface to one view while proving the component end-to-end.

### D6 — Motion is the only shared layer; row/column content is per-view and stays divergent
The container abstracts **motion**, not content. It is defined entirely by (item identity, row
height); it never inspects a row's internals, so an editable task row, a static Read line, a
multi-column Guestbook entry, and a History line are all just "a widget of some height at some id"
to it. Each view keeps its own bespoke row/column builder, and those builders are *meant* to be
different — the Guestbook composing columns-in-a-row is the motivating example. We deliberately do
**not** introduce a "view profile" / behavior-descriptor layer that unifies editability, reorder,
checkbox, columns, tools, or sort.

*Why:* the earlier framing flirted with two layers (motion + a behavior profile). On reflection
that second layer is a miscut — it would try to unify exactly the row differences the views should
be free to diverge on, becoming a constraint that fights composition. There is only one genuinely
shared, rendering-agnostic thing worth extracting (the departing-ghost/slot/cleanup/scroll/hover
choreography), and extracting *only* that is what lets each view's content stay meaningfully
different while still getting animation for free. If a future need to share *behavior* emerges, it
belongs in shared **row-builder helpers** (as `ScribeMultilineField`/`ScribeRowButton` already are),
composed per view — not in a profile object the container is aware of.

## Risks / Trade-offs

- **[Ghost from a stale cached render looks wrong]** (e.g. the row was mid-edit) → Pinned rows are
  server-snapshot value records, not live editors; the last built row is a faithful static image.
  If a live row proves unsafe to freeze, D2's builder-supplied-snapshot escape hatch covers it.
- **[Re-pin / re-add arrives mid-collapse]** (id departs, then reappears before the collapse ends) →
  the container must treat a reappearing id as "cancel the departure, revive as a live row" — mirror
  the HUD's `ReconcileDeparting`. Called out as an explicit test scenario, not left implicit.
- **[Display-index drift]** — a ghost must collapse *at the slot it left*, and multiple simultaneous
  departures must not fight over indices (the editor's display-index math handled this by hand).
  The container must reproduce that ordering when splicing several ghosts. Test with rapid
  multi-row removal.
- **[Scroll-pin coupling]** — the editor's scroll-pin reads `MaxScrollExtent` after layout in
  `OnRenderGUI`; a self-contained container must hook an equivalent post-layout point. If it can't
  cleanly, fall back to the surface passing the scroll controller and the container exposing a
  per-frame tick the surface calls (still packaged, just not fully autonomous). Resolve during impl.
- **[Over-generalizing before a second consumer exists]** → deliberately scoped to Pinned-only
  adoption; the editor/HUD migration is a separate change, so the container's API is validated
  against one real adopter before being locked as the standard. Insert/reorder are left as seams,
  not built.

## Migration Plan

1. Build `ScribeAnimatedList` + its internal state machine (registry, ghost cache, cleanup,
   scroll-pin, hover-latch), Immediate policy first.
2. Adopt in the Pinned tab; verify removal animation parity with the editor (collapse + slide-up +
   scroll ease), plus re-pin-mid-collapse and rapid-removal edge cases.
3. Playtest gate on the Pinned tab before considering the editor/HUD migration follow-up.
4. Rollback = revert the Pinned adoption; the container is additive and unused elsewhere, so
   reverting one file restores today's behavior.

## Open Questions

- **Scroll-pin autonomy** (D4/risk): can the container hook a post-layout point itself, or must the
  surface feed it the scroll controller + call a per-frame tick? Resolve in implementation against
  the real Pinned scroll view.
- **Ghost source** (D2): default to caching the last built row for every surface, or require an
  explicit snapshot widget? Lean toward last-built-row default with an opt-in override; confirm the
  Pinned row freezes cleanly.
- **Insert/reorder seam depth** (a *motion* seam — insert/reorder are future animations, not row
  behaviors): expose the "appeared ids" from the diff now (cheap) even though no animation consumes
  it yet, or add it with the follow-up? Lean toward exposing it now so the API doesn't churn later.
