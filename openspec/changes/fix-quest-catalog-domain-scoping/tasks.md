## 1. Fix VS Quest catalog domain scoping

- [x] 1.1 In `ScribeQuestCatalog.ReadCatalog` (`src/Mod/ScribeQuestCatalog.cs`), change the
      `capi.Assets.GetMany<List<RawQuest>>(capi.Logger, "config/quests", VsQuestModId)` call to
      pass `null` for the domain argument instead of `VsQuestModId`, so the search covers every
      installed mod's assets, matching vsquest's own `QuestSystem.AssetsLoaded` loader. Verify by
      reading the diff: no other line in the method changes.
- [x] 1.2 Update the class/method doc-comments that currently describe this read as "Scoped to the
      `vsquest` domain's own catalog only" to reflect the corrected, all-domains behavior.

## 2. Fix Progression Framework catalog domain scoping

- [x] 2.1 In `ScribeProgressionFrameworkQuestCatalog.ReadCatalog`
      (`src/Mod/ScribeProgressionFrameworkQuestCatalog.cs`), change the
      `capi.Assets.GetMany<List<RawQuest>>(capi.Logger, "config/quests", ModId)` call to pass
      `null` for the domain argument instead of `ModId`, matching Progression Framework's own
      `QuestSystem.LoadQuests` (`GetMany("config/quests/", null, true)`). Verify by reading the
      diff: no other line in the method changes.
- [x] 2.2 Update the class doc-comment's Almanac/domain-collision discussion
      (`ScribeProgressionFrameworkQuestCatalog.cs` remarks referencing
      `capi.Assets.GetMany("config/quests", <domain>)` with "an explicit domain per backend") to
      match the corrected behavior. No such passage exists in the current file (the class
      doc-comment never claimed own-domain-scoped behavior, unlike `ScribeQuestCatalog.cs`) — no
      stale text to correct.
- [x] 2.3 Found during 3.3 manual playtest: `ReadCatalog` was also deserializing each
      `config/quests/*.json` asset as `List<RawQuest>` (expects a JSON *array* per file), but every
      real Seafarer quest file is a single JSON *object* — confirmed against
      `reference/ProgressionInvestigations/seafarer-assets/seafarer/config/quests/*.json` (11/11
      files are bare objects) and against Progression Framework's own decompiled
      `QuestSystem.LoadQuests` (`JToken.Parse(item.ToText()).ToObject<Quest>()` — one `Quest` per
      file). This meant the domain fix alone still left the catalog permanently empty for every real
      PF quest pack. Fixed by changing the read to `GetMany<RawQuest>` (one object per asset, no
      inner list). `dotnet build src/Mod` green.
- [x] 2.4 Found during 3.3 manual playtest (round 2, after 2.3): `RawObjective.Items` was typed
      `List<string>?`, but a real delivery objective's `items` field is a JSON array of
      `{item, quantity}` objects (PF's own `QuestItemRequirement` shape) — confirmed against the
      actually-installed `seafarer_0.5.15.zip`'s `config/quests/*.json`. Deserializing an object into
      a string throws, and per `IAssetManager.GetMany<T>`'s own doc-comment ("will log an error ...
      and continue with the next asset"), that silently dropped the WHOLE containing quest file from
      the catalog — only `celeste-rusthunter` (a `kill`-type objective with no `items` field at all)
      survived; every `delivery`-type quest (`celeste-crimsonrose`, `celeste-bearhunter`,
      `drake-seasoned`, `drake-tricks`) silently vanished. This is what made the Quest Link picker
      appear to only ever offer "Rust Hunter", and why already-accepted quests like Crimson Rose
      never got a chat/HUD notification. Fixed by deleting the unused, mismatched `Items` property
      entirely (this reader only ever read `Code`/`Required` off an objective — Newtonsoft ignores an
      undeclared JSON property by default). `dotnet build src/Mod` green. Since `ScribeQuestWatcher`'s
      accept/complete session dedup resets on every relaunch, restaging should retroactively surface
      the accept notification for quests already accepted before this fix (their "active" status is
      already in the world save), not just newly-accepted ones.

## 3. Verification

- [x] 3.1 `dotnet test` (Core) green — this fix touches Mod-layer files only, no Core change
      expected; confirm the suite is unaffected.
- [x] 3.2 `./build/verify.sh Debug --no-restage` green (Core + Atlas) before any push.
- [ ] 3.3 Manual playtest: with Progression Framework + Seafarer installed, spawn a Seafarer
      quest-giver NPC, accept a quest — confirm the catalog entry now appears in the Quest Link
      picker and the HUD accept prompt fires (the exact scenario that failed in the original
      playtest report). Use a PLAYER-scoped quest (e.g. `celeste-bearhunter`, `celeste-crimsonrose`,
      `drake-seasoned`, `drake-tricks` — no `"scope"` field in their JSON, defaults to player) — the
      first real attempt at this task used `dawnmarie-plantation`/`dawnmarie-orchard`, which are both
      explicitly `"scope": "server"` and intentionally excluded from the catalog (no per-player
      `WatchedAttributes` equivalent to auto-detect against), so they will never trigger detection
      even with everything else working correctly. That attempt is also what surfaced 2.3's
      deserialization bug, now fixed.
- [ ] 3.4 Manual playtest: with VS Quest + VS Village installed, interact with a VS Village
      villager quest-giver, accept a quest — confirm the catalog entry appears in the Quest Link
      picker and the HUD accept prompt fires (VS Quest's real dependent-mod case, never previously
      manually tested against anything but vsquest's own bundled example content).
- [ ] 3.5 Regression check: with only a framework mod installed and no dependent content mod (e.g.
      Progression Framework alone, no Seafarer), confirm the catalog is empty and no quest UI
      errors or crashes — an empty result stays a valid, silent outcome.
