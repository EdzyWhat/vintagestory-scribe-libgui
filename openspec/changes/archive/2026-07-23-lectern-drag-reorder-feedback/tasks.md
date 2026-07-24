> **Icon hand-off (2026-07-21, from `add-custom-svg-row-icons`):** the custom SVG code
> `scribegrip` (a six-dot grip glyph) is now registered at client init and available for the
> drag handle to draw via `ScribeHoverIconButton`/`DrawIcon` — no asset or registration work
> needed here, just repoint. See `docs/specs/scribe-icon-svgs.md`. **Also note the architecture
> has shifted since this change was drafted:** `ScribeBlockRowCell` / `ScribeDragHandleElement`
> are dead code after the S2 merge; the live row is `ScribeRowElement` (checkbox + text + ruling,
> no gutters). Re-adding the drag-handle *column* to `ScribeRowElement` is upstream of this
> feedback work — the pin/delete columns are owned by the new `restore-row-affordance-columns`
> change, and the drag handle rides alongside it. This change (drag *feedback*) still applies on
> top of a restored, working drag handle.

## 1. Pre-flight

- [ ] 1.1 Re-read the current drag lifecycle in `src/Mod/GuiDialogScribeLectern.cs`
      (`OnRowDragMouseDown`, `OnRowDragMouseUp`, `OnMouseMove`, `OnMouseUp`, `HitTestRowIndex`,
      and the `OnRenderGUI` per-frame hook) and the custom-element pattern in
      `src/Mod/ScribeBlockRowCell.cs` (`ScribeDragHandleElement`, `ScribeHoverIconButton`),
      since this change builds directly on both. Confirm no uncommitted changes first.
- [ ] 1.2 Confirm `scribe-drag-reorder-feedback` is editor-view-only: verify the read view
      composes no drag handles, so none of this work needs a read-view branch.

## 2. Config knobs

- [ ] 2.1 Add tunable fields to `ScribeClientConfig` for the feedback visuals, following the
      existing layout-knob pattern (base values, documented): ghost opacity, ghost/dim overlay
      colors, insertion-indicator thickness/color, indicator-lerp duration, and drop-settle
      tween duration. Keep names consistent with the existing `Row*`/`*Width` style.

## 3. Lift-ghost element

- [ ] 3.1 Add a `ScribeDragGhostElement` custom interactive-pass element (own file or alongside
      the others in `ScribeBlockRowCell.cs`, matching the existing pattern). It draws, per
      frame, a semi-transparent rounded box containing the dragged row's text (via `RowFont()`)
      and, for a task, a checkbox glyph — at a caller-supplied Y, at a z above the row content
      (design.md Decision 1/Risk: above ~50, below the scrollbar's 200). Non-interactive:
      overrides no mouse handlers and does no hit-testing.
- [ ] 3.2 In `GuiDialogScribeLectern`, add the ghost element once (composed with the editor
      view) and drive its visibility + Y from drag state each frame in `OnRenderGUI`: visible
      only while `draggedBlockIndex` is set, centered on the live cursor Y, clamped to the
      row-list region (design.md Decision 1/Open Question).
- [ ] 3.3 Feed the ghost the dragged block's text/kind from
      `scratchDocument.Blocks[draggedBlockIndex]` so it reads as the grabbed row (design.md
      Decision 2). No live text-input element in the ghost.

## 4. Source-row dim overlay

- [ ] 4.1 Draw a subtle semi-transparent overlay over the source row's current on-screen rect
      each frame while dragging (design.md Decision 3 — per-row static alpha is unavailable, so
      this is an interactive-pass overlay, not real dimming of the baked row). Use the source
      row's live `bounds.absY` (read fresh per frame, never cached) so it stays aligned if the
      list scrolls.

## 5. Insertion indicator

- [ ] 5.1 Add the insertion indicator (a horizontal high-contrast line spanning the list width)
      as a custom per-frame element or a second draw in the same hook, positioned at the top
      edge of the `hoverTargetIndex` slot — or the bottom of the last row when dropping at the
      end (design.md Decision 4). Read live bounds each frame.
- [ ] 5.2 Ease the indicator's Y toward its target slot (simple lerp using `OnRenderGUI`'s
      `deltaTime`, over the configured indicator-lerp duration) so it slides between slots
      rather than snapping (design.md Decision 4/5).

## 6. Drop-settle tween

- [ ] 6.1 On `OnMouseUp` with an active drag, capture `from`/`to`; if `from == to`, clear all
      feedback immediately with no tween (spec: drop-in-place no-op). Otherwise start a
      drop-settle tween of the ghost from its current cursor Y to the resolved target slot Y.
- [ ] 6.2 Advance the tween in `OnRenderGUI` using `deltaTime` over the configured duration;
      on completion, call `scratchDocument.MoveBlock(from, to)`, mark dirty, recompose (via the
      existing recompose path), and clear all drag/ghost/indicator/tween state together.
- [ ] 6.3 Ensure the mid-drag recompose safety already in place is respected: the reorder +
      recompose must run from the per-frame hook / mouse-up path, not from inside a dispatch
      loop (consistent with the existing `pendingRecomposeAction` discipline).

## 7. State-reset integration

- [ ] 7.1 Extend the existing drag-state resets in `EnterMode` and `OnGuiClosed` (and the
      abandon paths) to also clear the new ghost/indicator/tween fields together, so an
      abandoned drag leaves no lingering feedback (spec: feedback clears if drag abandoned).

## 8. Build and verify

- [ ] 8.1 `dotnet build src/Mod/Mod.csproj` (Debug) and `--configuration Release` — both clean.
- [ ] 8.2 `dotnet test tests/Core.Tests/Core.Tests.csproj` — all green (no Core change expected;
      confirm no regression).
- [ ] 8.3 Restage on the Mac (`bash build/restage.sh`) and fully relaunch.
- [ ] 8.4 Manual playtest: in the editor view with several rows, grab a row by its handle and
      confirm — (a) a semi-transparent ghost of that row follows the cursor; (b) the source row
      reads as picked-up (dimmed); (c) an insertion indicator shows the drop slot and updates
      continuously as the cursor moves between rows, including dropping at the very end; (d) on
      release over a new slot the ghost eases into place and the row lands there; (e) dropping
      in the original slot is a clean no-op; (f) closing the dialog mid-drag leaves no leftover
      ghost/indicator. Note the z-order check (ghost/overlay must render above rows) explicitly.
- [ ] 8.5 Confirm no regression to the existing scroll/drag machinery: drag-reorder while the
      list is scrolled still lands on the correct row (live `bounds.absY`), and normal
      scrolling (wheel, thumb) is unaffected.
