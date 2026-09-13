## 1. Core data model

- [x] 1.1 Add `HistorySchema` (`Baked = 0`, `Live = 1`) and the fact fields (`SubjectName`, `OtherName`, `RefCode`, `RefCode2`, `FlavorSeed`, `Schema`) to `HistoryEntry`, defaulting to baked/empty, and verify `tests/Core.Tests` still compile and existing `HistoryStoreTests` pass unchanged
- [x] 1.2 Add a Core-only `HistoryFlavor.Slot(int seed, int poolSize)` helper (`abs(seed) % poolSize`, 0 when poolSize < 1) with unit tests for positive, negative, zero, and oversized seeds, and verify `src/Core` has no `Vintagestory` usings

## 2. Core codecs

- [x] 2.1 Extract shared `WriteEntry` / `ReadEntry(version)` for one History row (existing v3 fields plus the v4 fact fields behind `version >= 4`) and bump `HistoryStore` to `SHST v4` with `ApplyV3ToV4Migrations` defaulting schema to `Baked`, and verify a new round-trip unit test preserves live facts and that a hand-built v3 blob still deserializes as baked with empty facts
- [x] 2.2 Switch `PendingHistoryStore` to those shared entry helpers, accept `SPHS` v1–v2 (v1 = baked, no fact fields), and verify a unit test that a v1 pending blob still loads and a v2 blob round-trips live facts (a v3+ pending blob still fails safe / loads empty)

## 3. Mod write path

- [x] 3.1 Change creature, environmental, and PvP Death / PvpKill recording in `ScribeModSystem.History.cs` to write `Schema = Live` facts (entity code, cause token, tool/damage names, names, flavor seed) with empty `Detail`, without calling `Lang.Get` / `TryLang`, and verify the existing "skip when no relevant notebook" gate still short-circuits before any fact capture
- [x] 3.2 Change BossKill and TemporalStorm recording the same way (boss key + slayer name; strength token), and verify queued Death entries carry the same live facts as the immediate write (pending store, not a baked sentence)
- [x] 3.3 Update `/scribe seed` history rows in `ScribeModSystem.DevTools.cs` to live facts (no `Lang.Get` / hardcoded English `Detail` for Death, PvpKill, BossKill, or Storm), and verify seeding still produces one of each of those kinds

## 4. Mod display path

- [x] 4.1 Add a Mod-side `HistoryDisplay` formatter that, for live rows only: probes mob-death and generic-PvP pools with `Lang.HasTranslation` (never `Lang.Get` for existence), maps `FlavorSeed` through `HistoryFlavor.Slot`, resolves PvP verbs with locale-only `HasTranslation` fall-through (tool → damage → generic), resolves creature names via `prefixandcreature-*` then `generic-wildanimal`, and formats dates from `InGameTimestamp` through `NotebookHost.FormatCalendarDate`, and verify a unit or narrow integration test that a two-key viewer pool never returns an out-of-range index or an English fallback verb
- [x] 4.2 Wire `GuiDialogScribeNotebook` to use `HistoryDisplay.Sentence` / `Date` for live Death, PvpKill, BossKill, and TemporalStorm rows, keep today's concat for Crafted / PickedUp / Manual / baked rows, and verify in-game (or Atlas, if a History-tab assertion exists) that a v3 baked Death still shows its stored `Detail` while a new Death shows a localized sentence

## 5. Tests, docs, changelog

- [x] 5.1 Update Atlas / integration scenarios that assert a fully-substituted English `Detail` on Death or PvpKill (`MobDeathMessageScenarios` and any PvP cousins) to assert live facts on the store and, where they can observe UI or a formatter, a substituted viewer-locale sentence, and verify those tests pass
- [x] 5.2 Update the HistoryStore row in `docs/CODEC-MIGRATION.md` to `SHST v4` / `SPHS v2` and the class doc-comments on both stores, and verify the accepted-version tables match the code
- [x] 5.3 Add a CHANGELOG entry under Unreleased describing live-localized History (facts in the blob, viewer-language sentences, legacy rows unchanged), and verify `dotnet test` for `tests/Core.Tests` is green
