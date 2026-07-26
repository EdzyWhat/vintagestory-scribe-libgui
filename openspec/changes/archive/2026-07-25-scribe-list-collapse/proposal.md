## Why

When a task leaves a Scribe list, its row vanishes in a single frame and the rows below snap
up to fill the gap — jarring in both the on-screen pinned-task HUD (on unpin/delete) and the
lectern editor (on delete). A smooth height-collapse gives the removal a clear, calm cue and
resolves the "row-reorder/animation deferred" note recorded in the archived `add-settings-tab`
change (D7 / task 8.4) for the deletion half.

## What Changes

- Add a reusable, self-ticking **collapse** widget that animates a departing row's height from
  full to zero over a short duration (~200ms), so the rows below slide up to meet it and the
  row is removed only once the collapse completes.
- Wire the collapse into the **HUD pinned-task list**: an unpin/delete completion now collapses
  the (already text-faded) row's height instead of dropping it from the render in one frame.
- Wire the collapse into the **lectern editor**: deleting a row collapses it in place (as a
  frozen, non-interactive snapshot) before removal, and the post-delete scroll re-clamp is
  deferred until the collapse finishes so it doesn't fight the shrink.
- Because both surfaces rebuild via `GuiBase.ForceRebuild` (which unmounts + recreates the tree,
  making stock/implicit animations snap), the collapse controller is **host-owned and keyed by
  task identity** so it resumes rather than restarts across remounts.
- **Out of scope (explicitly):** the FLIP reorder-*glide* (a completed task sliding to the bottom
  on Sink, or editor rows gliding on drag-reorder). Reorder keeps today's instant jump; that half
  is deferred to a future `scribe-list-reorder` change.

## Capabilities

### New Capabilities
- `gui-list-collapse`: a reusable GUI mechanism for smoothly collapsing a list row's layout
  height to zero on removal, self-driven so it animates correctly under the mod's
  `ForceRebuild`-only rebuild path, with host-owned per-row animation state and a completion
  callback that triggers the actual removal.

### Modified Capabilities
- `player-pins`: the HUD's unpin/delete completion behavior gains a height-collapse on the
  departing row (the row's removal is deferred until the collapse completes).
- `lectern-gui-shell`: the editor's row deletion gains an in-place height-collapse of a frozen
  snapshot of the deleted row, with the scroll re-clamp deferred until the collapse completes.

## Impact

- **New file:** `src/Mod/ScribeCollapsible.cs` — the `ScribeCollapsible` widget, a
  `ScribeHeightFactorBox` render box (modeled on the existing `ScribeMultilineFieldRender`), and
  a host-owned `ScribeCollapseRegistry` (per-row `AnimationController` keyed by task id, mirroring
  the `ScribeNumericFocusRegistry` pattern).
- **Modified:** `src/Mod/HudScribePins.cs` — a "departing rows" set + collapse wiring, reusing the
  `ScribeFadeText` self-ticking pattern already in this file; `awaitingRemoval`/`sunkOrder`
  interactions accounted for.
- **Modified:** `src/Mod/GuiDialogScribeLecternLibGui.cs` — `DeleteEditorBlock`,
  `BuildEditorContent`, and deferral of `RequestClampToExtent`.
- **No Core changes** — animation is VS/LibGUI-bound; `src/Core/` stays API-free and untouched.
- **No new dependencies** — vanilla `VintagestoryAPI` + the existing `gui` (LibGUI) hard dep only.
- **Testing:** manual playtest (GUI is not Core-unit-testable); new items added to `TESTING.md`.
