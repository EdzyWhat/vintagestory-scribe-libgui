## 1. Empty-state action wiring

- [x] 1.1 Add a localized `scribe:scribe-assignment-create-tasks` label with the text “Create Tasks to Assign” in `src/Mod/assets/scribe/lang/en.json`; verify the JSON remains valid and the key resolves in a Mod build.
- [x] 1.2 Extend `ScribeAssignmentStageContent` and `ScribeAssignmentFormContent` with an Editor-entry callback, thread it from `GuiDialogScribeAssignmentDesk`, and verify the callback chain compiles without adding view or networking knowledge to the widgets.
- [x] 1.3 Render the new primary action in the empty-state `Column` immediately after the hint and before the conditional pull-from-Desk action; verify it remains present when `CanPullFromDesk` is false and both buttons retain the requested order when true.
- [x] 1.4 Wire the dialog callback to the inherited `TryEnterEditor` path; verify code inspection confirms the new action uses the existing lock refusal, server access request, and granted Editor transition.

## 2. Verification

- [x] 2.1 Run `dotnet build src/Mod/Mod.csproj --configuration Debug --no-restore` and confirm zero errors.
- [x] 2.2 Run `VINTAGE_STORY="/Applications/Vintage Story.app" ./build/verify.sh Debug --no-restage` and confirm Core plus Atlas pass.
- [x] 2.3 Manually test an empty Assignment Desk: with no staged item and no Desk-local tasks, confirm “Create Tasks to Assign” is visible and opens the local Editor; create and commit a task, return to Create Assignments, and confirm the create button appears before “Pull existing tasks from this Desk.”
- [x] 2.4 In multiplayer, hold the Desk’s Editor lock from another client, activate “Create Tasks to Assign,” and confirm the standard locked-editor feedback appears and the current view stays open.
  - Confirmed 2026-09-09: TESTING.md `000000c2` "(no note)" (submission 2026-09-09T17-15-17)
- [x] 2.5 Run `openspec validate add-assignment-desk-create-tasks-button --strict` and confirm the change is valid.
