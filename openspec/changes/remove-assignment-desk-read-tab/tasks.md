## 1. Remove the Read tab

- [x] 1.1 In `src/Mod/GuiDialogScribeAssignmentDesk.cs`'s `BuildRightColNav`, delete the `readBtn`
      `TitleButton` and remove it from the returned `Column`'s `children` array, leaving
      `{ assignmentBtn, sentHistoryBtn, inboxBtn, editorBtn, settingsBtn }`. Verify `dotnet build
      src/Mod` succeeds.
- [x] 1.2 Confirm (read `DefaultToAssignmentView`/`EnterGrantedView`/the base `viewMode` default in
      `ScribeDialogBase.cs`) that nothing else can land this dialog's `viewMode` on
      `ScribeLecternView.Read` now that its only nav entry point is gone. **Found a real gap, not just
      verification**: the Editor footer's "Done editing" button (`ShowEditorSwitchToRead`, base default
      `true`) calls `LeaveEditorMode()`, whose `isEditorMode` setter unconditionally lands `viewMode` on
      `Read` — stranding the dialog with no active nav button. `EnterGrantedView`'s own incidental
      editor-teardown path (`LeaveEditorIfActive`) shares the same root cause. Fixed by adding a
      `protected virtual void OnLeftEditorMode()` hook at the shared choke point (`LeaveEditorMode()` in
      `ScribeDialogBase.ViewSwitching.cs`), a no-op by default (Lectern/Notebook/Scriptorium/Chalkboard
      unaffected), overridden on `GuiDialogScribeAssignmentDesk` to call `DefaultToAssignmentView()` — the
      "Done editing" button stays visible and now lands on Create Assignments instead of Read.
      `EnterGrantedView`/`DefaultToAssignmentView` themselves were already safe as originally assumed.
      `dotnet build src/Mod` and `dotnet test` (Core) both succeed.
- [x] 1.3 Update the doc-comments on `BuildRightColNav` and `EnterGrantedView` that describe the
      Desk's "six-tab"/"Read/Editor" nav set to match the new five-tab set (Create Assignments,
      Sent Assignment History, Inbox, Editor, Settings). Verify `dotnet build src/Mod` succeeds.

## 2. Verification

- [x] 2.1 `dotnet test` (Core) green — this change touches Mod-layer files only, confirm the suite
      is unaffected.
- [x] 2.2 `./build/verify.sh Debug --no-restage` green (Core + Atlas) before any push.
- [ ] 2.3 Manual playtest: open the Assignment Desk — confirm the nav column shows exactly five
      tabs (Create Assignments, Sent Assignment History, Assignment Inbox, Edit View, Settings)
      with no Read tab/button present.
- [ ] 2.4 Manual playtest: switch to Edit View, confirm it still opens the Desk's own document with
      full normal affordances (checkbox, pin, delete, reorder, Tracker/Link/Craft rows) and the
      usual server-lock gating — unchanged from before this removal.
- [ ] 2.5 Regression check: close and reopen the Assignment Desk (and separately, have another
      player open it while it was last left on a non-default tab) — confirm it never opens onto a
      blank/broken view; it should land on Create Assignments as before.
