## 1. Catalog: include server-scoped quests

- [x] 1.1 In `ScribeProgressionFrameworkQuestEntry` (`src/Mod/ScribeProgressionFrameworkQuestCatalog.cs`),
      add an `IsServerScoped` bool field, threaded through `ToPickerEntry`'s callers as needed (the
      picker-facing `ScribeQuestCatalogEntry` itself needs no change — this flag is only consumed by
      `ScribeQuestWatcher`, not rendered). Verify `dotnet build src/Mod` succeeds.
- [x] 1.2 In `ReadCatalog`, remove the `if (string.Equals(q.Scope, ServerScope, ...)) continue;`
      exclusion, and set the new `IsServerScoped` field from the same already-parsed `q.Scope`
      value. Verify by reading the diff: catalog entries are still built identically for
      player-scoped quests (no other field changes), and a manual check (e.g. a quick console
      write or debugger) shows a server-scoped entry like `dawnmarie-plantation` now present in the
      returned list, with `IsServerScoped == true`.
- [x] 1.3 Update the class doc-comment's Non-Goal paragraph ("Only player-scoped quests are
      surfaced...") to describe the corrected, both-scopes behavior instead.

## 2. Watcher: reflect Progression Framework's shared server-quest state

- [x] 2.1 In `ScribeQuestWatcher`, add the fields needed for the reflected read: a cached
      `ModSystem?` for Progression Framework's `QuestSystem` (looked up once via
      `capi.ModLoader.GetModSystem("ProgressionFramework.Quests.QuestSystem")`), a cached
      `FieldInfo?` for `clientServerQuestState` (via `AccessTools.Field`), and a
      `serverQuestDetectionDisabled` bool mirroring the existing `pfDetectionDisabled`/
      `dialogReflectionDisabled` self-disable fields. Verify `dotnet build src/Mod` succeeds.
- [x] 2.2 Add `ScanProgressionFrameworkServerQuests()`, called from `OnTick` alongside the existing
      `ScanProgressionFramework()` call, gated on `ScribeProgressionFrameworkQuestCatalog.IsAvailable`
      and `!serverQuestDetectionDisabled`. It: resolves the cached ModSystem/field once; reads the
      field's current value and casts it to `Dictionary<string, TreeAttribute>`; for every
      `IsServerScoped` entry in the (already-loaded) PF catalog, looks up its code in that
      dictionary and reads `GetString("status")` and the `objectives` subtree exactly like
      `ScanProgressionFramework` already does for the player-scoped tree — writing into the SAME
      `pfAcceptedSeen`/`pfCompletedSeen`/`pfObjectiveStatus` dictionaries and calling the same
      `onAccepted`/`onCompleted` callbacks, so no changes are needed in `ScribeModSystem.Quest.cs`.
      Any exception (missing type, missing field, unexpected cast failure) is caught, logged once,
      and sets `serverQuestDetectionDisabled = true` permanently for the session — mirroring the
      existing reflection paths' fail-safe posture exactly. Verify `dotnet build src/Mod` succeeds
      and no existing player-scoped detection code path changed.
- [x] 2.3 Update the class doc-comment (the `<para>` blocks describing VS Quest and Progression
      Framework detection) to document this third path: what it reads, why reflection is needed
      here specifically (no `WatchedAttributes` equivalent for shared state, unlike the player-scoped
      tree), and its independent self-disable behavior.

## 3. Verification

- [x] 3.1 `dotnet test` (Core) green — this change touches Mod-layer files only, no Core change
      expected; confirm the suite is unaffected.
- [x] 3.2 `./build/verify.sh Debug --no-restage` green (Core + Atlas) before any push.
- [x] 3.3 Manual playtest: with Progression Framework + Seafarer installed, trigger a server-scoped
      quest's activation (e.g. talk to Dawn Marie for `dawnmarie-plantation` if not already active
      in this world) — confirm the chat message + HUD accept prompt fires per the Accept policy,
      and accepting creates a Quest Link that shows live objective progress.
- [x] 3.4 Manual playtest: with a server-scoped quest already active from a prior session (this
      world's `dawnmarie-plantation` already active from earlier testing), confirm restaging
      retroactively surfaces its activation notification on the next login (mirrors the existing
      player-scoped retroactive-detection behavior — session dedup resets on relaunch).
- [x] 3.5 Manual playtest: deliver an objective item toward a linked server-scoped quest and
      confirm the Quest Link's displayed progress updates to match Progression Framework's own
      Quest Log (`L`) — both should always show the same shared count.
- [x] 3.6 Manual playtest: pick a server-scoped quest from the Quest Link picker BEFORE it has ever
      been activated (still unavailable/inactive) — confirm the Link is created successfully and
      shows an inactive/no-progress state rather than erroring, matching how a not-yet-detected
      player-scoped Link behaves.
- [x] 3.7 Regression check: with only Progression Framework installed and no dependent content mod
      (no Seafarer), confirm no server-scoped catalog entries exist, the new detection path finds
      nothing to scan, and no errors or crashes occur.
