## Context

Scribe's GUI hosts subclass LibGUI's `GuiBase`. `GuiBase.Build()` runs **once** per mount (in
`TryOpen`) and once per `ForceRebuild()`; its output is captured as a constant inside the root
`Overlay`/`Theme` wrapper, so there is no dialog-level "re-run Build and reconcile" API — the only
way for a `GuiBase` to push new *top-level* content is `ForceRebuild`, which unmounts and recreates
the whole tree.

The reconciling path is `BuildOwner.BuildDirtyElements()` (pumped every frame in
`GuiBase.OnRenderGUI`): a `State.SetState`/`Element.MarkNeedsBuild` marks an element dirty, and on
the next frame that element re-runs its `Build` and reconciles its children via `UpdateChild`
(matching by type + key, reusing the existing element/`State`/`RenderObject`). Crucially, implicit
animations (`AnimatedOpacity`, `AnimatedSize`, …) only animate on this path — they trigger inside
`UpdateWidget`; a fresh mount initializes `Begin == End == target` and snaps. So `ForceRebuild` is
inherently animation-hostile.

The mod already uses reconciliation pervasively at the **leaf** level (the editable field, both
checkboxes, hover states, drag-reorder, `ScribeFadeText`) — the drag-reorder even carries a comment
that it uses local `SetState` *instead of* `ForceRebuild` because a full rebuild would unmount the
grip mid-drag. What's missing is reconciliation at the **host content** level.

The single recorded reason the hosts use top-level `ForceRebuild` (`VSAPI-NOTES.md:989`): LibGUI's
stock `ListView` caches child widgets by index and clears that cache only when `ItemCount` or a
`DataIdentity` reference changes. A parent `SetState` therefore cannot refresh a same-count row after
an external change. **But** `RenderListViewContent.Update` *does* honor a `DataIdentity` swap
(clearing `_cachedWidgets`) — the stock public `ListView` constructors simply never expose it. That
is the crux: the capability exists in the framework but is unreachable through the public API.

## Goals / Non-Goals

**Goals:**
- Make reconciliation the default update path for Scribe's HUD and lectern content, so stock
  implicit animations work and per-frame full-tree teardown stops.
- Own a scrolling list container so external resync no longer requires `ForceRebuild`, and so the
  mod controls identity/keying/animated insert-remove.
- Preserve every current invariant: focus/caret survival, scroll-offset preservation across updates,
  correct external resync (multi-viewer / autosave), and multiplayer authority.
- Confine `ForceRebuild` to genuinely-new trees (view switches, fresh editor seed, dev hot-reload).

**Non-Goals:**
- Rewriting the read↔editor↔settings **view switch** to reconcile — those are different trees;
  `ForceRebuild` is correct there and stays.
- Changing Core, persistence, sync, or the server-authoritative model.
- Shipping the FLIP reorder-glide (still deferred); this change only makes the *substrate* that would
  make it cheap.
- A general-purpose virtualized list matching every stock `ListView` feature — build what Scribe's
  two surfaces need.

## Decisions

### D1 — Reconciling-rebuild discipline: persistent content `StatefulWidget` + `SetState`
Each host's `Build()` returns a persistent content `StatefulWidget` once; all content changes call
`SetState` on that widget's `State` (or notify a `ListenableBuilder`). This is LibGUI's documented,
intended pattern (`ExampleGui`, `DebugWindow`). `ForceRebuild` is reserved for a genuinely-new tree.
- *Alternative — a dialog-level `MarkNeedsBuild` that re-runs `GuiBase.Build()`:* not offered by
  LibGUI, and would require patching the framework (out of scope; we don't fork `gui`).

### D2 — A Scribe-owned list container (`ScribeListView`), with a tiered fallback
The load-bearing `ForceRebuild` (external read-view resync) exists solely because of the stock
`ListView` child cache. Three tiers, in increasing ownership — the design commits to the **lowest
tier that meets the need**, escalating only if a tier proves insufficient in playtest:

- **Tier 1 (thinnest): thread `DataIdentity`.** Wrap stock `ListView` in a Scribe widget that passes
  a `DataIdentity` token (e.g. a document version/hash) so the existing cache-clear path fires on an
  external change. Smallest change; keeps stock virtualization. Risk: relies on a code path the
  public API doesn't surface — may require a thin subclass or reflection, which is fragile.
- **Tier 2 (recommended default): self-stateful, key-identified rows in a non-virtualized container.**
  A `ScribeListView` = `SingleChildScrollView` + `Column` of rows keyed by `ValueKey<Guid>(TaskId)`,
  each a `StatefulWidget` that re-reads current data on reconcile. No index cache to invalidate;
  parent `SetState` reconciles correctly. The editor already works exactly this way. Cost: no
  virtualization — every row mounts. Fine for Scribe's realistic document sizes; revisit if a
  document can hold hundreds of rows.
- **Tier 3 (most ownership): a custom virtualized render container.** A Scribe `RenderBox`-based
  list (modeled on `ScribeMultilineFieldRender` / `RenderListViewContent`) that virtualizes by
  viewport but keys by stable identity and rebuilds visible rows from current data — full control
  over caching, animated insert/remove, and reconciliation. Only if Tier 2's "mount every row"
  proves too heavy.

**Recommendation:** Tier 2 for correctness-with-modularity now; leave Tier 3 as a documented
escalation. Tier 1 is a stopgap, not the goal. The custom container is the reusable primitive the
proposal's `gui-list-container` capability describes.

### D3 — Convert the HUD first (lowest risk, highest animation payoff)
`HudPinsContent` is a `Column`, not a `ListView` — the cache limitation never applied. Give it a
persistent `HudPinsContentState` that owns the ordered/capped row list; the pin-push, tick-expiry,
and toggle paths call a setter that `SetState`s instead of `HudScribePins.ForceRebuild`. The
self-open/close at the 0⇄1 pin boundary stays a host concern (`TryOpen`/`TryClose`). Host-owned
collapse/fade controllers already survive; once reconciling, they can revert toward stock
`AnimatedSize`/`AnimatedOpacity`.

### D4 — Convert the editor structural mutations; keep view-switch on `ForceRebuild`
Add/delete/reorder mutate a non-virtualized `Column`, so a persistent editor-content `State` can
rebuild its child list via `SetState` (drag-reorder already does). The one real obstacle is the
centralized focus coordination across `editorFocusNodes` on a row-count change — moved into, or
made callable from, the persistent content state; the persistent-`FocusNode` infrastructure (built
to survive `ForceRebuild`) already exists and carries over. `RefreshReadView` moves onto
`ScribeListView` (D2) so it reconciles. View switches and fresh editor seed keep `ForceRebuild`.

### D5 — Dedicated branch, incremental per-surface conversion
This is a ground-up rebuild of load-bearing content trees, so it lands on its **own branch** (e.g.
`refactor-reconciling-gui-rebuild`), converted one surface at a time (HUD → settings form → editor),
each kept green against the manual playtest checklist before the next, and merged only once the whole
suite passes. It is not layered onto an in-flight feature branch.

## Risks / Trade-offs

- **Focus/caret survival regressions** (the hardest invariant) → convert incrementally; the existing
  persistent-`FocusNode` + `autoFocusRowOnRebuild` machinery is reused, and each surface is
  playtested against the caret/focus items already in `TESTING.md` before moving on.
- **External-resync correctness** (multi-viewer / autosave must still repaint) → this is the whole
  point of `ScribeListView`; add explicit playtest coverage for a second client toggling a task.
- **Scroll-offset preservation** → reconciliation *helps* here (the scroll controller and content
  state persist), removing the `pendingRestoreScrollOffset` gymnastics needed across `ForceRebuild`.
- **Tier-1 fragility** if chosen as a stopgap (depends on non-public cache path) → prefer Tier 2.
- **Losing virtualization** (Tier 2 mounts every row) → acceptable at Scribe's document sizes;
  Tier 3 documented as the escalation if profiling shows otherwise.
- **Scope creep into a full rewrite** → the view-switch and fresh-seed `ForceRebuild`s are
  explicitly retained; the change is bounded to content-update reconciliation + the list container.

## Migration Plan

Dedicated branch. Order: (1) build `ScribeListView` (Tier 2) with its own smoke test; (2) convert the
HUD to persistent content + `SetState`; (3) convert the settings form write-through; (4) convert the
editor structural mutations and move `RefreshReadView` onto `ScribeListView`; (5) simplify
`ScribeCollapsible`/`ScribeFadeText` toward stock animations now that reconciliation holds; (6) update
`VSAPI-NOTES.md:989`. Each step is independently playtestable; the branch merges only when the full
manual checklist passes. Rollback = don't merge the branch.

## Open Questions

- Tier 1 vs Tier 2 as the *first* concrete step — start with Tier 2 (recommended) or spike Tier 1 to
  see if the `DataIdentity` path is cleanly reachable without forking `gui`?
- Whether the editor conversion is worth its focus-coordination risk in this change, or should be a
  follow-up once the HUD + list container prove the pattern (the proposal lists it; the branch can
  descope it if the risk/benefit turns unfavorable mid-flight).
- Whether `ScribeCollapsible` simplification happens in this change or a fast-follow (depends on how
  cleanly stock `AnimatedSize` behaves once reconciling).
