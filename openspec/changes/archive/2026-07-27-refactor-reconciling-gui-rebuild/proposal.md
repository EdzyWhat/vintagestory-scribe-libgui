> **ABANDONED (2026-07-27).** The reconciling refactor was implemented on its own branch
> (build-clean, 102/102 Core tests, never playtested) but left UNMERGED. The headline payoff —
> "stock LibGUI animations become free once the tree reconciles" — proved FALSE for the
> deletion-collapse: stock `AnimatedSize` has no completion callback (row removal still needs an
> explicit controller), and LibGUI's reconciler is positional, so a mid-list delete remounts
> trailing rows and restarts an in-flight collapse (the host-owned collapse registry stays
> load-bearing). Scribe therefore KEEPS its self-ticking animation stack
> (`ScribeCollapsible`/`ScribeHeightFactorRender`/registry + `ScribeFadeText`). Full write-up:
> `docs/animation-lessons-learned.md`. Do NOT re-attempt stock animations without reading it
> first. Archived with `--skip-specs`; its spec deltas were never promoted.

## Why

Scribe's GUI hosts (the HUD and the lectern dialog) push almost every state change through
`GuiBase.ForceRebuild()`, which unmounts and recreates the *entire* widget tree — disposing every
`State`, `AnimationController`, and `RenderObject` and the Skia painting context each time. LibGUI
documents `ForceRebuild` as a **dev hot-reload tool** (its only in-framework caller is the
`.ui redraw` debug command); the intended pattern for mutable content, used by LibGUI's own dialogs
(`ExampleGui`, `DebugWindow`), is to build a persistent `StatefulWidget` once and drive changes via
`SetState`/reconciliation.

The mod adopted top-level `ForceRebuild` for one concrete, recorded reason (`VSAPI-NOTES.md:989`):
LibGUI's stock `ListView` caches its child rows and only clears that cache on an item-count or
`DataIdentity` change, so a parent `SetState` can't refresh same-count rows after an external change
(another player toggles a task, an autosave lands). `ForceRebuild` was the honest escape hatch.

But that one limitation has metastasized into the default update path for surfaces that don't even
use a `ListView` — the HUD row list is a `Column`, the lectern editor is a non-virtualized
`Column` — and it forces every animation to snap (already worked around with bespoke self-ticking
widgets: `ScribeFadeText`, `ScribeCollapsible`) while re-running full teardown every frame a window
is animating. This change removes the root cause so reconciliation — and therefore stock implicit
animations — becomes the norm, and confines `ForceRebuild` to the cases where a fresh tree is
genuinely correct.

## What Changes

- Introduce a **reconciling-rebuild discipline** for Scribe's GUI hosts: content that changes is
  built as a persistent `StatefulWidget` (or bound to a `ListenableBuilder`) and updated via
  `SetState`/notification, so matching subtrees (and their `State`/`AnimationController`/
  `RenderObject` instances) are preserved across updates.
- Build a **Scribe-owned scrolling list container** (`ScribeListView` or similar) to replace stock
  `ListView` where the mod needs reconciliation-friendly external resync — a container whose rows
  rebuild from current data on a parent rebuild (no index-keyed child cache that must be manually
  invalidated), giving the mod control over identity, keying, and (future) animated insert/remove.
- **Convert the HUD** (`HudScribePins`) to a persistent content `StatefulWidget` driven by
  `SetState` from the pin-push/tick/toggle paths, instead of `ForceRebuild`.
- **Convert the lectern editor** (non-virtualized `Column`) structural changes (add/delete/reorder)
  and the **settings form** write-through to reconciliation, moving the centralized focus
  coordination into (or callable from) the persistent content state. The drag-reorder path already
  proves this works.
- **Retain `ForceRebuild`** for the cases where it is correct: lectern view-switches (read ⇄ editor
  ⇄ settings are genuinely different trees) and the fresh editor seed / lost-lock recovery. The
  read-view external resync (`RefreshReadView`) moves onto the new list container so it no longer
  needs `ForceRebuild` either.
- **Simplify the animation code** that only exists to survive `ForceRebuild`: once the HUD/editor
  reconcile, `ScribeCollapsible`'s host-owned resume-across-remount registry and the deferred
  ticker-pump cleanup can collapse toward stock `AnimatedSize`/`AnimatedOpacity` (or a much thinner
  wrapper). `ScribeFadeText` similarly.
- **NOTE (branching):** because this is a ground-up rebuild of the HUD and lectern content trees —
  load-bearing components with subtle focus/scroll/animation invariants — the work SHALL be done on
  its own dedicated branch, kept green against the manual playtest suite before it merges, rather
  than layered onto in-flight feature branches.

## Capabilities

### New Capabilities
- `gui-list-container`: a Scribe-owned scrolling list widget whose rows reconcile from current data
  on a parent rebuild (stable identity via keys, no manual child-cache invalidation), replacing the
  stock `ListView` where external resync must work without a full-tree rebuild.

### Modified Capabilities
- `gui-foundation-policy`: add the reconciling-rebuild discipline (persistent content
  `StatefulWidget` + `SetState`; `ForceRebuild` reserved for genuinely-new trees / dev hot-reload).
- `lectern-gui-shell`: the read/editor row lists render via the new container; editor structural
  mutations and external resync reconcile rather than `ForceRebuild`; focus coordination lives in
  the persistent content state.
- `player-pins`: the HUD renders its pin list from a persistent content state updated via `SetState`
  on pin-push/tick/toggle, rather than rebuilding the whole HUD tree.

## Impact

- **New:** `src/Mod/ScribeListView.cs` (the custom list container + its render object), plus a
  persistent content-state split for the HUD and possibly the editor.
- **Modified:** `src/Mod/HudScribePins.cs`, `src/Mod/GuiDialogScribeLecternLibGui.cs`,
  `src/Mod/ScribeSettingsContent.cs`/`ScribeSettingsDialog.cs`; likely simplification of
  `src/Mod/ScribeCollapsible.cs` and `ScribeFadeText`.
- **No Core changes** — this is entirely the Mod/GUI adapter layer; `src/Core/` stays API-free.
- **No new dependencies** — vanilla `VintagestoryAPI` + the existing `gui` (LibGUI) hard dep; the
  custom container is built on LibGUI's public render-object API (as `ScribeMultilineField` already
  is).
- **Risk:** high-touch rewrite of focus/caret survival, scroll-offset preservation, and
  external-resync correctness — the exact invariants the current `ForceRebuild` + persistent
  `FocusNode` machinery guarantees. Mitigated by the dedicated branch, incremental per-surface
  conversion, and re-running the existing manual playtest checklist.
- **Docs:** update `VSAPI-NOTES.md:989` (the `ListView` cache note) to point at the new container as
  the resolution; record the reconciling-rebuild discipline.
