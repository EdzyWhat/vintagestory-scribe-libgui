## Why

The 2026-07-27 playtest (submission `2026-07-27T10-16-26`) surfaced a batch of v1-blocking defects
and polish asks against the just-landed Lectern work. They're small and cross-cutting, so they're
bundled into one v1 fix pass rather than spun into six separate changes. Two are functional bugs
(one a hard v1 blocker), one is an incomplete feature from `scribe-lectern-view-consistency`, and
three are visual/layout polish notes from the same session.

## What Changes

**Functional fixes**

1. **Editor no longer traps global hotkeys (v1 blocker).** In editor view, adding a task via "New
   Task" and clicking away to unfocus leaves global hotkeys (e.g. H → Handbook) dead, because
   `CaptureAllInputs()` returns `isEditorMode` — trapping ALL input for the whole editor view, not
   just while a field is focused. Gate input capture on "an editor field actually holds focus"
   instead, so hotkeys fire whenever nothing is focused.

2. **Sink reorders every surface, not just the HUD.** Completing a task under the Sink policy sinks
   it to the bottom on the HUD but leaves the **Pinned-view list** (and the Read/Edit views for a
   task the player owns) unchanged. Extend the sink-to-bottom ordering the HUD already applies
   (`ScribePinOrdering.ForDisplay` + the HUD's undo-aware overlay) to the Pinned view, and make the
   real document reorder (`MoveTaskToBottom`, already wired server-side) reflect in the Read/Edit
   views for the acting player's own completions.

3. **Pinning from the Read view keeps the scroll position.** Pin/unpin from the read view jumps the
   scroll list to the top (the `MyPinsChanged` → `ForceRebuild` re-clamps the virtualized ListView
   against a stale content height). Reuse the existing `CaptureScrollForRestore()` / `OnRenderGUI`
   re-apply machinery so the offset is captured before the pin rebuild and restored after.

**Polish (same playtest, general notes)**

4. **HUD legibility.** Nudge the pinned-task HUD text a bit more toward white (not full white),
   slightly darken the outer text glow, and slightly tighten its range (e.g. ~5px → ~4px).

5. **Lectern title padding.** Give the Lectern title text ("Lectern") 10px of left padding
   (supersedes the earlier 4px value).

6. **Settings layout.** In Scribe Settings, place **HUD Text Size** in a column beside **HUD
   position** (the offsets row), reusing the two-column `PairedControls` grouping.

## Capabilities

### Modified Capabilities
- `lectern-gui-shell`: input capture is gated on a focused editor field (not merely editor mode), so
  hotkeys work whenever no field is focused; the read-view pin toggle preserves scroll position.
- `player-pins`: completing under Sink reorders the Pinned view (and the owner's Read/Edit views),
  not only the HUD display order.

The HUD text/glow legibility tweak, the 10px Lectern title padding, and the HUD-Text-Size-beside-HUD-
position settings layout are pure visual/layout polish (no behavior/requirement change) — they are
implementation tasks only, not spec deltas.

## Impact

- **Code:** `src/Mod/GuiDialogScribeLecternLibGui.cs` (`CaptureAllInputs`, `OnReadViewTogglePinned`,
  pinned-view ordering, title padding), `src/Mod/HudScribePins.cs` (text/glow style), possibly
  `src/Mod/ScribeSettingsContent.cs` (paired row). No `src/Core/` API growth expected —
  `ScribePinOrdering` and `MoveTaskToBottom` already exist; the Sink-everywhere work is wiring the
  Pinned view to the same ordering the HUD uses.
- **No** wire-format, persistence, or dependency change. Rollback is reverting the GUI/HUD edits.
- **Follow-up sources:** `696dd143` (hotkey trap), `0c09d185` (Sink), `32f807d9` (read-view scroll),
  plus three general-note polish items, all from playtest `2026-07-27T10-16-26`.
