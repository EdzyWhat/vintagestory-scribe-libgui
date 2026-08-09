## Context

Scribe's GUI hosts subclass LibGUI's `GuiBase`. `GuiBase.Build()` runs once per mount and once per
`ForceRebuild()`; there is no dialog-level "re-run Build and reconcile" API, so the only way a
`GuiBase` pushes new *top-level* content today is `ForceRebuild()` — which unmounts and recreates the
whole tree, disposing every `State`, `AnimationController`, `RenderObject`, the Skia paint context,
and orphaning the `EventDispatcher`'s press-capture (`_capturedElement`/`_pressedElement` are concrete
Element **references**, with no invalidation hook on unmount).

The reconcile path already exists and is used pervasively at the **leaf** level (the editable field,
checkboxes, hover tints, drag-reorder — the last with an explicit "uses `SetState` INSTEAD of
`ForceRebuild`" comment). `State.SetState` → `Element.MarkNeedsBuild` marks an element dirty; on the
next frame `BuildOwner.BuildDirtyElements()` re-runs `Build()` and diffs children via `UpdateChild`,
**reusing** matching elements (type + `Key`) and their `State`/controller/render object. What is
missing is reconciliation at the **host content** level — that gap is why the HUD and editor
`ForceRebuild`, and why every identity-bearing thing (hover, focus, caret, press-capture, animation
controllers) is destroyed on each update and must be rebuilt bespoke.

This is the second attempt. The first (`refactor-reconciling-gui-rebuild`, archived 2026-07-27) built
the reconcile conversion clean (102/102 tests) but was **abandoned unmerged, never playtested**,
because its stated payoff — "reconcile makes animations stock, delete the self-ticking stack" — is
false (`docs/animation-lessons-learned.md`, R1/R2). That branch is now 259 commits behind main and
rewrote `GuiDialogScribeLecternLibGui.cs`, since split into the `ScribeDialogBase*.cs` partials, so it
is un-rebaseable; only its `src/Mod/ScribeListView.cs` (107 lines) is worth mining as reference.

## Goals / Non-Goals

**Goals:**
- Make reconciliation (persistent content `StatefulWidget` + `SetState`) the default update path for
  Scribe's animating surfaces, so hover / focus / caret / press-capture / animation controllers are
  **preserved** across an update rather than torn down and re-smuggled.
- Kill the identity-loss bug class on the converted surfaces: the post-rebuild flicker, hover lost
  under a still cursor, the mass-delete first-click that doesn't register, and caret/scroll loss.
- Lower the cost of **future** animations: a single reusable host-owned, identity-keyed self-ticking
  harness for row enter/exit/reorder, so a new animation is "instantiate the harness," not "invent a
  survival scheme."
- Prove the approach on the **editor** — the hardest identity case — behind an explicit gate with a
  written bail-out, before converting any other surface.
- Preserve every current invariant on each converted surface: focus/caret survival, scroll-offset
  preservation, correct external resync (multi-viewer / autosave), multiplayer authority.

**Non-Goals:**
- **Making animations stock.** The self-ticking harness STAYS and is generalized, not replaced by
  `AnimatedSize`/`AnimatedOpacity`. R1 (no `AnimatedSize` completion callback) and R2 (positional
  reconciler restarts in-flight collapses) are permanent; do not re-litigate them.
- Rewriting the read⇄editor⇄settings **view switch** to reconcile — those are genuinely different
  trees; `ForceRebuild` is correct there and stays. Same for fresh editor seed and lost-lock recovery.
- Changing Core, persistence, sync, or the server-authoritative model.
- Shipping any new animation (reorder-glide, add-grow, fades) in this change — this builds the
  *substrate and harness*; new animations are follow-ups that ride it.
- Converting every surface at once. Tablet and other surfaces are in-scope for the strategy but
  sequenced after the editor and pinned surfaces prove out; the change may descope later surfaces.

## Decisions

### D1 — Reconciling-rebuild discipline: persistent content `StatefulWidget` + `SetState`
Each converted surface's `Build()` returns a persistent content `StatefulWidget` once; content changes
call `SetState` on that widget's `State` instead of `GuiBase.ForceRebuild()`. This is LibGUI's
documented, intended pattern (`ExampleGui`, `DebugWindow`; `ForceRebuild`'s only in-framework caller is
`.ui redraw`). `ForceRebuild` is reserved for a genuinely-new tree.
- *Alternative — a dialog-level `MarkNeedsBuild` that re-runs `GuiBase.Build()`:* not offered by
  LibGUI; would require forking `gui` (out of scope, no new deps).

### D2 — Editor first, as the proof-of-concept gate (with a written bail-out)
Convert the editor before any other surface, because it is the **hardest identity case**: a live
`ScribeMultilineField` caret, cross-row focus coordination, and an optimistic `done` flag must all
survive a mid-list structural mutation (add/delete/reorder). If reconcile holds the caret and focus
through a mid-list delete, it holds everywhere; if it fights us, we learn the cost cheapest and can
drop the branch with the least sunk work.
- **Gate criterion (all must hold before converting the next surface):** on the editor, a delete /
  insert / reorder via `SetState` (a) preserves the caret position and in-progress unsaved text of an
  actively-edited row, (b) preserves cross-row focus (no leak/loss), (c) preserves scroll offset
  without the `ForceRebuild`-era capture-restore gymnastics, (d) lands the mass-delete first click
  mid-collapse, and (e) survives an async external resync landing mid-edit without dropping a
  legitimately-local in-flight row.
- **Bail-out:** if the gate can't be met without either forking `gui` or a restructuring larger than
  the whole standalone fallback would cost, abandon the branch; `fix-mass-delete-click-target` ships
  the narrow delete fix instead, and this change is archived `--skip-specs` with the reason recorded.
- *Alternative — HUD first (the 2026-07-27 order, "lowest risk / highest animation payoff"):* rejected
  as a proving ground precisely *because* it's low-risk — it would let us convert the easy surface,
  declare success, and only discover the editor's focus/caret wall after committing to the strategy.
  Prove the hard case first.

### D3 — Stable row identity: `ValueKey<Guid>(TaskId)`, no type-swaps at a slot
This is the real, bounded cost of reconcile. Under `ForceRebuild` everything remounts, so today's
**array-index keys** (`ValueKey<int>(b.Index)`) and **departing-row type-swaps** (a slot flipping
`AnimatedOpacity`⟷`ScribeCollapsible`, or `Opacity(Text)`⟷`ScribeFadeText`) are invisible. Under
reconcile they are fatal: an index key that shifts on insert/delete fails `CanUpdate`, destroying the
row's `State` (caret, focus listener, optimistic flag); a type-swap at a slot destroys the descendant
`State` just as thoroughly as a `ForceRebuild` would.
- Rows key by **stable `ValueKey<Guid>(TaskId)`**, not index.
- A row must keep the **same widget type at its slot** across the live→departing transition — the
  departing/collapsing state becomes an *internal* state of one stable row widget (the harness, D5),
  not a different widget type spliced in at that slot.
- Header/footer siblings whose presence shifts sibling ordering (the `moreCount` footer, the timer
  divider+row) must be keyed or made structurally stable so the positional reconciler
  (`MultiChildElement.Update`, `_children[i]` vs `nextWidgets[i]`) doesn't misalign rows against them.
- *Note (permanent, not solvable here):* LibGUI's reconciler does **no keyed reordering** — a mid-list
  delete still remounts trailing rows positionally. Stable keys protect a row from being *misupdated*
  at a slot; they do **not** let a row keep its element when its index shifts. That is exactly why the
  animation harness (D5) must remain host-owned and identity-keyed to *resume* across the remount.

### D4 — Read-view external resync: reuse the `ListView`, or a Scribe-owned container (tiered)
The one genuinely load-bearing `ForceRebuild` is the read view's external resync: LibGUI's stock
`ListView` caches children by index and clears that cache only on an item-count or `DataIdentity`
change, and the public constructor doesn't expose `DataIdentity`. Resolve in the lowest sufficient
tier:
- **Tier 1 (thinnest):** thread a `DataIdentity` token (document version/hash) into the `ListView` so
  the existing cache-clear path fires on an external change. Smallest, keeps virtualization; risk =
  relies on a path the public API doesn't surface cleanly.
- **Tier 2 (default fallback):** a Scribe-owned `ScribeListView` = `SingleChildScrollView` + `Column`
  of `ValueKey<Guid>(TaskId)` self-stateful rows that re-read current data on reconcile (no index
  cache to invalidate). Reference implementation exists on the abandoned branch — mine, don't merge.
  Cost: no virtualization (mounts every row); fine at Scribe's document sizes.
- **Tier 3:** a custom virtualized render container. Only if Tier 2 profiles too heavy. Documented, not
  built.
- The read view is out of the editor-first gate's critical path; sequence it with the pinned surfaces.

### D5 — Generalize the self-ticking harness into one reusable primitive
`ScribeCollapsible` + `ScribeCollapseRegistry` already are the correct answer to *motion* under any
rebuild mode (self-ticking `AnimationController`, host-owned, keyed by TaskId, removal deferred out of
the ticker callback via a `needs*Cleanup` bool). Generalize this into one primitive that all animating
rows share for enter (grow 0→1, no `onEnd` needed — see the "ScribeRevealable" sketch in
`animation-lessons-learned.md`), exit (collapse 1→0 then remove), and reorder. The harness is the
deliverable; it survives both reconcile and `ForceRebuild`, so future animations inherit survival for
free.
- *Alternative — delete the harness for stock widgets once reconciling:* this is the exact 2026-07-27
  mistake. Rejected permanently (R1/R2).

### D6 — Dedicated droppable branch, incremental per-surface conversion, playtest gate each
Work lands on `reconcile-animating-surfaces`, converted one surface at a time — **editor (proof gate)
→ pinned HUD / pinned tab → read-view resync → tablet/others** — each **playtested green before the
next**. Playtest is a first-class per-surface gate, the step the prior attempt skipped. Rollback =
don't merge the branch.

## Risks / Trade-offs

- **Focus/caret survival regression** (the hardest invariant, and the editor gate's core test) → convert
  editor first behind the explicit D2 gate; reuse the existing persistent-`FocusNode` infrastructure;
  playtest caret/focus items before proceeding. If it can't be met cheaply, bail per D2.
- **Structural-stability traps** (index keys, departing-row type-swaps) silently destroy `State` under
  reconcile where `ForceRebuild` hid them → D3 makes stable keys + no-type-swap-at-slot an explicit,
  audited precondition of each conversion, not an afterthought.
- **External-resync correctness** (multi-viewer / autosave must still repaint, and must not prune a
  legitimately-local in-flight row) → D4 owns the resync path; the async-resync-prunes-local-row guard
  from the prior scroll work (`animation-lessons-learned.md`) carries over and is a gate criterion.
- **Positional reconciler restarts in-flight motion on mid-list delete** (permanent) → the harness
  (D5) stays host-owned and identity-keyed to resume across the remount; this is not something reconcile
  removes and must not be assumed away.
- **Repeating the 2026-07-27 abandonment** → the value proposition is identity, not stock animations;
  the harness is kept; playtest is gated per surface; the branch is droppable. The dated reframing in
  `docs/animation-lessons-learned.md` is the guardrail against the goal drifting back.
- **Scope creep into a full GUI rewrite** → view-switch / fresh-seed / lost-lock `ForceRebuild`s are
  explicitly retained; later surfaces (tablet) may be descoped; only content-update reconciliation +
  the harness are in scope.
- **Concurrent-session git index** (10 `claude` processes share this checkout) → stage by explicit path,
  never `git add -A`.

## Migration Plan

Dedicated branch `reconcile-animating-surfaces`. Order: (1) baseline capture (build/tests green, note
the `TESTING.md` focus/caret/scroll/resync items that must still pass); (2) generalize the harness
(D5) as the shared primitive; (3) convert the **editor** to persistent content + `SetState` with
stable `TaskId` keys (D1/D3), then **meet the D2 gate in-game** — this is the go/no-go; (4) on pass,
convert the pinned HUD + pinned tab; (5) move read-view resync onto the D4 container; (6) evaluate
tablet/other surfaces (may descope); (7) update `VSAPI-NOTES.md` `## LibGUI` with the discipline.
Each surface is independently playtestable; the branch merges only when its converted surfaces pass the
full manual checklist. Rollback = abandon the branch; `fix-mass-delete-click-target` ships the fallback.

## Open Questions

- **Harness shape — RESOLVED 2026-08-09 (task 2.1):** *one widget with a direction/mode over the one
  shared registry*, not a family. Rationale, decided with the code open: the substrate is already
  direction-neutral (`ScribeHeightFactorRender/Widget` just renders a `factor ∈ [0,1]`; the registry
  just owns controllers by id and runs them `Forward()` 0→1, resuming across remount and releasing on
  completion). The *only* difference between exit (collapse) and enter (reveal) is (a) the Build factor
  mapping — exit `1 − curve(value)` vs enter `curve(value)` — and (b) whether the terminal callback
  removes the row (exit: required) or just settles (enter: optional). A family would duplicate the
  load-bearing survival `State` (registry lookup, resume-on-remount, fire-if-already-completed,
  detach-but-don't-dispose) across two classes that must stay in lockstep; one widget with a
  `direction` enum keeps a single survival story that can't drift between the two motions, and matches
  task 2.2's "into *the* reusable primitive … supporting exit … and enter." Concretely:
  `ScribeCollapseRegistry` → `ScribeAnimationRegistry` (direction-neutral substrate);
  `ScribeCollapsible` → `ScribeRowSizeAnimation` taking `ScribeRowSizeDirection { Collapse, Reveal }`
  and an optional `onEnd`. Reveal ships with no caller yet (Non-Goal: no new animations this change);
  it is the future-animation on-ramp per goal 3, verified only by build + the resume test (2.3).
- **Read-view tier (D4):** spike Tier 1 (`DataIdentity`) to see if it's cleanly reachable without a
  `gui` fork, or go straight to Tier 2? Deferred until after the editor gate — not on the critical path.
- **Scroll machinery:** how much of the `pendingEnsureVisible` / `pendingRestoreScrollOffset` /
  `pendingClampToExtent` settling apparatus can be *removed* once reconcile preserves the scroll
  controller's offset, vs. how much is still needed for view-switches that keep `ForceRebuild`? Measure
  during the editor conversion.
- **Does the mass-delete first click actually land purely from reconcile,** or does a host `ForceRebuild`
  still fire mid-gesture on the editor (completion cleanup, async push) and need additional deferral?
  This is gate criterion (d); confirm in-game, don't assume.
