## Context

`reconcile-animating-surfaces` converted the editor, Pin Tab, and Read view to reconcile in place
and routed their row lists through one shared motion container, `ScribeAnimatedList`. That container
today animates **removals**: the Core diff (`ScribeListDiff.Compute`) reports `Departed` ids, and the
container wraps each departing row's frozen snapshot in a `ScribeRowSizeAnimation` with
`ScribeRowSizeDirection.Collapse` (rendered height `1 → 0` via `ScribeHeightFactorWidget`). The
controller is host-owned, keyed by the row id in `ScribeAnimationRegistry`, so it survives
`ForceRebuild`/reconcile remounts.

Three on-ramps for insertion were built ahead of need and are currently dead code:
- `ScribeListDiff.Appeared` — live ids present neither as a live row nor a ghost last frame.
- `ScribeAnimatedList.lastAppeared` — the container already assigns `diff.Appeared` to this field
  each Build; nothing reads it.
- `ScribeRowSizeDirection.Reveal` — the same animation widget mapped to grow `0 → 1` instead of
  `1 → 0`. Fully implemented, no consumer.

The known hazard is documented in `docs/animation-lessons-learned.md`: the newly-added editor row is
**auto-focused for immediate typing**, and a height-grow entry shrinks the row during the animation,
which hides the caret and mislocates pointer hit-testing mid-grow. Delete-collapse never had this
problem because a departing row is not focused. So insertion has a focus-safety split that removal
does not.

## Goals / Non-Goals

**Goals:**
- New rows enter with motion on all three animating surfaces, using the existing `ScribeAnimatedList`
  container — adopters gain the behavior for free, no per-surface animation code.
- The auto-focused new editor row enters focus-safely (full height, opacity fade), preserving the
  caret/first-click guarantees the reconcile conversion won.
- Entry motion is rebuild-/reconcile-stable, mirroring the collapse discipline (host-owned,
  id-keyed controller that resumes across remounts).
- Turn the `Appeared` / `lastAppeared` / `Reveal` seams into live, tested code.

**Non-Goals:**
- **Reorder/swap animation** (an existing row sliding to a new slot). The positional reconciler and
  the harness could support it later; out of scope here.
- Any Core model, persistence, or sync change. This is view-layer motion only.
- Changing the removal-collapse behavior. (A matching opacity fade on the slide paths is optional
  polish — see Decisions — but the collapse timing/shape stays as shipped.)
- Reworking the auto-focus mechanism itself (which row gets focus on add is unchanged).

## Decisions

### D0 — Migrate the editor onto `ScribeAnimatedList` first (folds in `extract-animated-task-list` §6.1)
When `reconcile-animating-surfaces` shipped, only Read and Pin Tab were routed through the
`ScribeAnimatedList` container; the **editor** (and the HUD) kept a hand-wired copy of the same
collapse choreography. That was a deliberate deferral — but it means the editor, which carries the
*only* focus-safe fade case, is not a container consumer, so D1 ("entry mode chosen inside the
container") could not apply to it without either migrating it or replicating the entry logic inline.
Replicating it inline would fork the focus-safety invariant across the container AND the editor —
the exact drift this change should be reducing, not adding to. So this change **migrates the editor
onto the container** as its first step:
- `ScribeEditorContent` builds `ScribeAnimatedListItem`s (live `ScribeEditRow` + a
  `ScribeFrozenEditorRow` ghost per row, the same ghost it already uses) and hands them to
  `ScribeAnimatedList` with its existing `Scrollbar > SingleChildScrollView > Column` as the
  `layoutBuilder` (identical to Read's).
- The dialog's hand-wired departing-row machinery is deleted: `DepartingRows` /
  `OnDepartingCollapsed` bookkeeping and the `needsEditorCollapseCleanup` deferred flag go away
  (the container computes departures from the data diff and self-cleans via deferred `SetState`).
  Delete's focus fix-up moves onto the container's `OnDepartureSettled` settle hook.
- Drag-reorder state (`dragFromIndex`/`dragOverIndex`) stays in the editor's `State`; it computes
  `isDropTarget`/`isDragSource`/`dragActive` and bakes them into each item's live child closure, so
  the container stays content-agnostic (D6 from the container's own design).

After D0 the editor, Read, and Pin Tab all share one animation path; only the HUD remains bespoke,
for the principled `Delayed`-policy reason ([[hud-undo-window-is-policy-hiding]]), and its migration
is promoted to its own follow-up change (`migrate-hud-onto-animated-list`, was
`extract-animated-task-list` §6.2). This is the load-bearing reason D1/D2 below can live purely in
the container.
- *Alternative considered:* keep the editor hand-wired and replicate only the entry (fade) logic
  inline in `ScribeEditorContent`. Rejected — it duplicates the focus-safety rule across two files
  where it can rot, and leaves the editor's collapse choreography un-deduplicated, which is the
  opposite of the "one surface mechanism" goal driving this fold-in.

### D1 — Entry mode is chosen by focus, inside the container, not by the caller's surface
The container already knows the appeared ids (`lastAppeared`). It selects the entry animation for
each appeared row by a single predicate: *is this the auto-focused newly-created row?* If yes →
opacity fade at full height; if no → height-grow `Reveal`. The adopter surfaces pass which new-row id
(if any) is auto-focused — a small addition to the `ScribeAnimatedListItem` inputs or a
`focusedAppearedId` parameter on the container — rather than each surface choosing an animation. This
keeps the focus-safety rule in ONE place and makes it impossible for a surface to accidentally
height-grow a focused row.
- *Alternative considered:* let each surface pass a per-row entry mode. Rejected — spreads the
  focus-safety invariant across three call sites where it can rot; the container is the natural owner.

### D2 — Reuse `ScribeRowSizeAnimation` (Reveal) for the grow; reuse an opacity primitive for the fade
The height-grow path is already built (`Reveal` → `ScribeHeightFactorWidget`, factor `0 → 1`). For the
fade, use the existing opacity mechanism (`AnimatedOpacity` / the `ScribeFadeText`-style self-ticking
opacity, whichever is already reconcile-stable in this codebase) wrapping the full-height row. Both
must be driven by the same host-owned, id-keyed controller pattern as collapse so they resume across
`ForceRebuild`.
- *Alternative considered:* a single combined "reveal" widget that does height+opacity together.
  Rejected for now — the two cases have different height behavior (grow vs. fixed-full), so one widget
  would just branch internally on the same focus predicate D1 already evaluates. Keep them as two
  small, independently-correct wrappers.

### D3 — First-frame opacity floor for the fade
`RenderOpacity` skips painting below α≈0.001, which for a focused row would mean a one-frame
invisible-but-focused row (caret in an unpainted row). Start the fade at a small non-zero α (or let
the controller's first sampled value be > 0). Documented gotcha from `animation-lessons-learned.md`;
call it out in the task so it isn't rediscovered.

### D4 — Symmetric fade on the slide paths is optional polish, gated on cheapness
Today's collapse is height-only (no fade). If layering a matching opacity fade onto BOTH slide paths
(enter grow + exit collapse) is a small, low-risk addition, do it for visual consistency. It is NOT a
requirement — the spec only mandates the focus-safe fade-in for the auto-focused row. If it adds
meaningful complexity or risks the collapse timing, drop it and file as a follow-up.

### D5 — Editor-adopt validated first within an all-surfaces change
Scope is all three surfaces, but implement + build-verify the editor path first (it carries the only
focus-safety case and is the most-used), then extend to Pin Tab and Read view in the same change. The
harness is already proven live on all three from reconcile, so this is sequencing discipline, not a
staged gate — all three ship together.

## Risks / Trade-offs

- **Focused-row height-grow slips in by accident** (the exact documented failure) → D1 puts the
  fade-vs-grow decision in the container keyed off the auto-focused id, so no surface can request a
  grow for a focused row; a Core/unit assertion on the selection predicate backs it.
- **Entry controller not released → leak or a stale animation on re-insert of the same id** → mirror
  the collapse retirement path exactly: release the id's controller from the registry on entry
  completion, same as `OnGhostCollapsed` does for departures.
- **Reconcile/ForceRebuild restarts the entry mid-flight** (the classic `Animated*`-snaps-on-mount
  trap) → use the host-owned, id-keyed controller (`ScribeAnimationRegistry`) that already makes
  collapse rebuild-stable; verify with a rebuild-during-entry playtest.
- **Scroll jump when a row grows at/near the viewport edge** → the reconcile scroll-pin machinery
  (`pendingClampToExtent` / bottom-pin during collapse) handles the shrinking case; the growing case
  is the mirror. Verify add-at-bottom and add-past-a-page don't snap; reuse the collapse-era scroll
  handling rather than inventing new.
- **One-frame invisible focused row** → D3 opacity floor.

## Open Questions

- ~~Is there already a reconcile-stable opacity wrapper to reuse for the fade, or does this add a
  small `ScribeFade`-style widget?~~ **Resolved (read the source):** neither existing option is
  reconcile+ForceRebuild-stable. `AnimatedOpacity` is implicitly-animated — recreated fresh under
  `ForceRebuild` it inits `Begin==End==target` and snaps. `ScribeFadeText` owns its controller in
  its own `State`, which likewise restarts on a `ForceRebuild` remount (it only survives *reconcile*
  reuse). The fade must use the **same host-owned, id-keyed `ScribeAnimationRegistry` controller**
  the collapse/grow paths use — so add a small `ScribeFade` widget (an `Opacity` wrapper) that reads
  its controller from the registry by id, exactly parallel to `ScribeRowSizeAnimation`. This lands
  inside the container next to the `Reveal` grow, so both entry modes resume across
  ForceRebuild/reconcile identically.
- Entry duration/curve: match the collapse duration for symmetry, or tune the grow slightly faster so
  adds feel snappy? Decide in-game.
- Does `db3c8ff4`/Sink-style reordering ever present an add as an "appear at a non-end slot"? If so,
  confirm the grow reads correctly when the appeared row is not the last row. (Likely fine — the
  container splices by render order — but worth an in-game check.)
