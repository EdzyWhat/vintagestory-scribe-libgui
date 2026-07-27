All GUI/HUD work is in `src/Mod/`. Line numbers are from the research pass and may drift — locate by
symbol. Source items: playtest submission `2026-07-27T10-16-26`.

## 1. Editor hotkey trap (v1 blocker — `696dd143`)

- [x] 1.1 Gate `CaptureAllInputs()` (`GuiDialogScribeLecternLibGui.cs:330`) on a field actually holding
  focus, not on `isEditorMode`: return true only when an editor field is focused (`focusedEditIndex is
  not null`) or a Pin Tab field is focused (`focusedPinTaskId is not null`), else false. Confirm the
  macOS Cmd-translation in `OnKeyDown` still guards correctly (it already checks `isEditorMode` and only
  rewrites keys a focused field would consume).
- [ ] 1.2 Manually test in-game: open the editor, add a task via "New Task", click away to unfocus — the
  Handbook key (H) and other global hotkeys now fire. Then click into a row and type movement/hotbar keys
  — they still edit the field and do NOT leak to the game (no player move, no hotbar change).

## 2. Sink reorders every surface (`0c09d185`)

- [x] 2.1 Pinned view: order rows with the HUD's sink rule. In `BuildPinnedContent`
  (`GuiDialogScribeLecternLibGui.cs:~1460`), render `modSystem.MyPins` through the same completed-sinks-
  below-not-completed ordering the HUD uses (`ScribePinOrdering.ForDisplay`) instead of raw pin-list
  order.
- [x] 2.2 Decide (design Open Question) whether the Pinned view also needs the HUD's undo-window "stay
  then sink" overlay (`HudScribePins.sunkOrder`/`SinksForOrder`). If yes, factor that overlay into a small
  shared helper both surfaces call; if no, apply the plain Core resting order and accept an immediate
  sink. Record which was chosen.
- [x] 2.3 Read/Edit views reflect the owner's sink reorder promptly: the server Sink completion already
  calls `ScribeDocument.MoveTaskToBottom` on the shared doc, so ensure the acting player's open Read view
  repaints from the resync (and the editor's scratch reflects the move — already handled by
  scribe-lectern-view-consistency's editor Sink branch). Do NOT invent a new reorder path; wire the
  existing reorder to refresh the surface.
- [x] 2.4 Add/confirm Core coverage: `ScribePinOrdering.ForDisplay` sink ordering is already unit-tested;
  add a test only if 2.2 introduces a new shared ordering helper.
- [ ] 2.5 Manually test in-game: with policy *sink*, complete a pinned task from the Pinned view → it sinks
  to the bottom of the Pinned list (not just the HUD). Complete an owned task under *sink* from the Read
  and Edit views → it moves to the bottom there too, and the HUD agrees.

## 3. Read-view pin keeps scroll (`32f807d9`)

- [x] 3.1 In `OnReadViewTogglePinned` (`GuiDialogScribeLecternLibGui.cs:1441`), call
  `CaptureScrollForRestore()` (`:471`) before `SendSetPin`, so the pending `OnMyPinsChanged` →
  `ForceRebuild` has an offset for the existing `OnRenderGUI` re-apply loop (`:~1060`) to restore. Guard so
  it only arms in the read view (editor/Pin Tab keep their own focus-restore paths).
- [ ] 3.2 Manually test in-game: scroll the read view down, pin then unpin a task → the list stays at the
  scrolled position instead of jumping to the top; a genuinely shorter list still clamps correctly.

## 4. Polish (general notes — visual/layout only)

- [x] 4.1 HUD legibility (`HudScribePins.cs`): nudge the pinned-task row text toward white (not fully
  white); slightly darken the outer text glow and slightly reduce its range (e.g. ~5px → ~4px). Pick exact
  values by eye in-game.
- [x] 4.2 Lectern title padding: give the title text ("Lectern") 10px of `padding-left` in the title-bar
  band build (supersedes the earlier 4px value).
- [x] 4.3 Settings layout (`ScribeSettingsContent.cs`, HUD Appearance section): place HUD Text Size
  (`hudfontscale`) in a column beside the HUD position (offsets) row, reusing the `PairedControls`
  two-column helper.
- [ ] 4.4 Manually test in-game: HUD text/glow read better without washing out; the Lectern title has a
  clear 10px left gap; HUD Text Size sits beside HUD position in Settings.

## 5. Build, test, restage, verify

- [x] 5.1 `dotnet build src/Mod/Mod.csproj --nologo` clean; `dotnet test tests/Core.Tests/Core.Tests.csproj`
  green.
- [x] 5.2 Restage (`bash build/restage.sh Debug`) and fully relaunch the client.
- [x] 5.3 Update `TESTING.md` with the retest items (the `696dd143`, `0c09d185`, `32f807d9` retests plus
  the three polish checks).
