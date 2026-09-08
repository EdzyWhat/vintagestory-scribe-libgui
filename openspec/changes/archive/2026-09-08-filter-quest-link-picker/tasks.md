## 1. Watcher: expose a "started" query

- [x] 1.1 Add a public `ScribeQuestWatcher.HasStarted(string questCode)` returning
      `_acceptedSeen.Contains(questCode) || _completedSeen.Contains(questCode)` (vsquest) OR
      `pfStatusFingerprint.ContainsKey(questCode)` (Progression Framework) — a pure read of
      existing session-cached state, no new tracking added. Verify `dotnet build src/Mod` succeeds.

## 2. Picker: filter the candidate list

- [x] 2.1 In `ScribeDialogBase.Editor.cs`, change wherever `QuestCatalogForPicker` is consumed (the
      call site(s) passing `questCatalog:` to `ScribeAddKindPicker`/`ScribeEditorContent`) to filter
      the cached catalog through `ScribeQuestWatcher.HasStarted` at the point the picker's candidate
      list is built, leaving `questCatalogCache` itself unfiltered (design.md Decision 2). Verify
      `dotnet build src/Mod` succeeds.
- [x] 2.2 Confirm (read the call path) the "Quest Link" tile in `ScribeAddKindPicker` is hidden
      whenever the FILTERED list is empty, not the raw catalog — i.e. a player with zero started
      quests this session sees no Quest Link option at all, even with a quest mod installed and a
      non-empty static catalog. Verify by manual read/diff, no behavior test needed beyond Task 3's
      playtests.

## 3. Verification

- [x] 3.1 `dotnet test` (Core) green — this change touches Mod-layer files only, confirm the suite
      is unaffected.
- [x] 3.2 `./build/verify.sh Debug --no-restage` green (Core + Atlas) before any push.
- [x] 3.3 Manual playtest: with Progression Framework installed, accept a quest, then open the
      Quest Link picker — confirm that quest appears and quests you haven't touched do not.
- [x] 3.4 Manual playtest: with vsquest installed, accept a quest from a nearby quest-giver, then
      open the Quest Link picker — confirm that quest appears. **Confirmed 2026-09-08.**
- [x] 3.5 Manual playtest: with vsquest installed, relog (or join fresh) without visiting any
      quest-giver this session, then open the Quest Link picker — confirm a quest you started in a
      PRIOR session does NOT appear (the disclosed hide-until-confirmed gap, design.md Decision 3).
      Then visit that quest's giver and re-open the picker — confirm it now appears.
      **Tested 2026-09-08 — the disclosed gap did NOT reproduce: the picker showed the
      prior-session quest immediately, with no giver revisit.** Root cause: `ScanGiver`
      (`ScribeQuestWatcher.cs` `OnTick`) scans every loaded `questgiver` entity's
      `WatchedAttributes` each tick, not just ones the player is actively in dialogue with — so
      any giver merely within simulation/render range gets scanned passively, closing the gap far
      more readily than "visit the giver" implies. User's verdict: this is better, more desirable
      behavior than the disclosed limitation described. No code change needed; Decision 3's
      contract ("hidden unless confirmed") still holds, it's just confirmed sooner than assumed.
- [x] 3.6 Manual playtest: with a quest mod installed but the player having started nothing this
      session, open the picker — confirm the Quest Link tile/option is hidden entirely (Task 2.2),
      not shown with an empty list.
- [x] 3.7 Regression check: confirm creating a Quest Link from an entry that does appear still
      behaves exactly as before (correct name/description captured, auto-detect/progress mirroring
      unaffected) — this change only narrows the candidate list, not Link creation/resolution.
