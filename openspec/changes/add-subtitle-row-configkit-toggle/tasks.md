## 1. Tuning value + manifest

- [x] 1.1 Add `public bool ShowSubtitleRow { get; set; } = DefaultShowSubtitleRow;` and
  `public const bool DefaultShowSubtitleRow = true;` to `src/Mod/ScribeVisualTuning.cs`; update its
  class doc comment to describe this third, layout-toggle knob alongside the two rendering
  subsystems. Verify: `dotnet build` succeeds.
- [x] 1.2 Add a `"boolean": { "ShowSubtitleRow": {...} }` block to
  `src/Mod/assets/scribe/config/configlib-patches.json` (`ingui`: "Show subtitle row", `default`:
  `true`, `weight` placing it sensibly relative to the existing separators, `comment` explaining it
  controls the Row 2 subtitle line under each dialog tab's title, `clientSide: true`). Verify: the
  file is valid JSON (`python3 -m json.tool < configlib-patches.json` or equivalent) and matches the
  object-form schema in `reference/configkit/docs/CONFIG-FORMAT.md`.

## 2. Gate Row 2 in `ScribeTabHeader.Build`

- [x] 2.1 Add a `bool showSubtitleRow` parameter to `ScribeTabHeader.Build`
  (`src/Mod/ScribeDialogBase.Layout.cs`) and make the subtitle widget's inclusion in the returned
  `Column` conditional on it; Row 3 content and the trailing divider are unaffected. Verify:
  `dotnet build` succeeds; reading the diff confirms Row 3/divider construction is untouched.

## 3. Wire the 5 `ScribeDialogBase`-subclass call sites

- [x] 3.1 Update the `ScribeTabHeader.Build` calls in `GuiDialogScribeInbox.cs`,
  `GuiDialogScribeNotebook.cs`, `GuiDialogScribeScriptorium.cs`, `GuiDialogClockmakerNotebook.cs`,
  and `ScribeDialogBase.Guestbook.cs` to pass `modSystem.VisualTuning.ShowSubtitleRow`. Verify:
  `dotnet build` succeeds.

## 4. Thread the flag through the 5 plain-widget call sites

- [x] 4.1 Add a `ShowSubtitleRow` constructor property to `ScribeReadContent`, `ScribeEditorContent`,
  `ScribePinnedContent`, `ScribeAssignmentFormContent`, and `ScribeInboxContent`, mirroring how an
  existing settings-derived value (e.g. theme or font scale) is already threaded into each widget.
  Verify: `dotnet build` succeeds.
- [x] 4.2 Update every owning `ScribeDialogBase` call site that constructs these five widgets
  (`ScribeDialogBase.Layout.cs`, `ScribeDialogBase.PinTab.cs`, `ScribeDialogBase.ViewSwitching.cs`,
  `GuiDialogScribeAssignmentDesk.cs`) to pass `modSystem.VisualTuning.ShowSubtitleRow`. Verify:
  `dotnet build` succeeds.
- [x] 4.3 Update each widget's `Build`/`State` method to pass `Widget.ShowSubtitleRow` into its own
  `ScribeTabHeader.Build` call. Verify: `dotnet build` succeeds; `rg "ScribeTabHeader.Build"` shows
  every call site now supplies the new parameter.

## 5. Automated verification

- [x] 5.1 Run `dotnet test tests/Core.Tests` and confirm all tests still pass (no `Core` changes are
  expected, so this should be a no-op regression check).
- [x] 5.2 Run `dotnet build` for the full `Mod` project and confirm zero warnings/errors introduced
  by this change.

## 6. Manual in-game verification

- [ ] 6.1 With no `scribe-visual-tuning.json` present (or `ShowSubtitleRow` absent from it), confirm
  every dialog tab renders its Row 2 subtitle exactly as before (default-on, no regression).
- [ ] 6.2 Set `ShowSubtitleRow: false` in `scribe-visual-tuning.json`, relaunch the client, and
  confirm Row 2 is absent on a representative sample of tabs (Read, Editor, Guest Book, Inbox,
  Create Assignments) while Row 3 (where present) and the trailing divider still render directly
  under the title bar.
- [ ] 6.3 If a ConfigKit build is available for manual testing, confirm the "Show subtitle row"
  toggle appears in its settings screen for Scribe and edits the same `scribe-visual-tuning.json`
  key; otherwise record this as untested (ConfigKit is optional and not a mod dependency).
