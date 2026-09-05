## 1. Open the Progression Framework Quest Log

- [x] 1.1 Add a helper (e.g. on `ScribeProgressionFrameworkQuestCatalog` or a new small static class)
      that finds `capi.Gui.LoadedGuis.FirstOrDefault(d => d.ToggleKeyCombinationCode ==
      "progressionframeworkledger")` and calls `TryOpen()` on it if found; no-ops (returns false) if
      not found (mod not installed, or dialog not yet registered). Verify `dotnet build src/Mod`
      succeeds.
- [x] 1.2 Add the best-effort reflected `currentTab` write: `AccessTools.Field(dialog.GetType(),
      "currentTab")`, cached once, set to the `QuestLog` enum value (read via reflection on the
      private nested `LedgerTab` enum, or by ordinal `1` with a comment citing the decompiled
      declaration order `Training = 0, QuestLog = 1`) before calling `TryOpen()`. Wrap in try/catch;
      on any failure, log once and permanently skip this step for the session (the plain open from
      1.1 still happens). Verify `dotnet build src/Mod` succeeds.

## 2. Wire up the Quest Link click path

- [x] 2.1 In `ScribeItemRef.OpenHandbookPage` (or its caller in `ScribeDialogBase.Layout.cs`'s
      `OpenRowLink`), branch a Progression Framework Quest Link (`ScribeLinkTarget.IsQuest(target) &&
      ScribeLinkTarget.QuestSource(target) == ScribeQuestSource.ProgressionFramework`) to the new
      helper from Task 1 instead of returning early. Leave the VS Quest branch's existing no-op
      behavior unchanged (Non-Goal — design.md). Verify `dotnet build src/Mod` succeeds and no other
      Link-click path (item, guide page, VS Quest) changed behavior.

## 3. Verification

- [x] 3.1 `dotnet test` (Core) green — this change touches Mod-layer files only, confirm the suite is
      unaffected.
- [x] 3.2 `./build/verify.sh Debug --no-restage` green (Core + Atlas) before any push.
- [ ] 3.3 Manual playtest: with Progression Framework installed, click a Progression Framework Quest
      Link's label from the Notebook (or any Scribe surface) — confirm the Ledger dialog opens on the
      Quest Log tab, whether or not it was already open on the Training tab.
- [ ] 3.4 Manual playtest: click a Progression Framework Quest Link from the pinned-task HUD — confirm
      the same open-to-Quest-Log behavior.
- [ ] 3.5 Manual playtest: click an ordinary item Link and a guide-page Link — confirm both still open
      their Handbook page exactly as before (no regression from the branch added in Task 2.1).
- [ ] 3.6 Regression check: with Progression Framework NOT installed, confirm a (legacy/orphaned)
      Progression Framework-sourced Quest Link's click does nothing and logs no error (the
      `LoadedGuis` lookup finds nothing, matching `link-task`'s existing "orphaned Quest Link" scenario).
