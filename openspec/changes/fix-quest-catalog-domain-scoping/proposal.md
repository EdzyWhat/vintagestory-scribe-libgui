## Why

Both Quest Link backends' catalog readers (`ScribeQuestCatalog` for VS Quest, added
`ScribeProgressionFrameworkQuestCatalog` for Progression Framework) scope their
`capi.Assets.GetMany("config/quests", ...)` read to the backing framework mod's own domain
(`vsquest` / `progressionframework`). Neither framework ships real quest content itself — real
quests are always contributed by a separate dependent mod under *that mod's own* domain
(`seafarer:config/quests/*.json`, `vsvillage:config/quests.json`). So both readers always return
an empty catalog against any real installation, which was only just discovered because this
session's playtest was the first end-to-end test against real third-party quest content for
either backend — confirmed by decompiling both frameworks' own loaders
(`reference/ProgressionInvestigations/`, `reference/QuestsInvestigations/vsquest-src/`), which
both search across every installed mod's domain, not just their own. An empty catalog silently
starves two things per backend: `ScribeQuestWatcher`'s tick-based auto-detect bails before ever
reading `WatchedAttributes` (no HUD accept popup fires), and the Quest Link picker has nothing to
offer (no manual-add path either) — exactly the two symptoms a playtester hit with Seafarer.

## What Changes

- `ScribeQuestCatalog.ReadCatalog` (VS Quest backend) and
  `ScribeProgressionFrameworkQuestCatalog.ReadCatalog` (Progression Framework backend) stop
  scoping their `config/quests` asset read to the backing framework's own mod domain. Instead they
  search across every installed mod's assets for that path prefix, matching the exact convention
  each framework's own quest loader already uses (vsquest's `QuestSystem.AssetsLoaded` loops every
  installed mod; Progression Framework's `QuestSystem.LoadQuests` passes `domain: null`).
- No other logic changes: both readers already resolve titles/lang-keys and domain-prefix bare
  quest codes using each entry's own asset location domain (not the framework's modid), so nothing
  downstream of the search call needs to change.
- Corrects the quest-auto-detect capability's implicit assumption (never previously written down
  as a requirement, but present in both prior changes' design docs) that a backend's catalog is
  scoped to its own mod domain — that was always wrong for real-world use and is now specified as
  a requirement: the catalog must include quest content from any installed mod.
- **Not a regression fix for new code** — this bug predates today's Progression Framework work; it
  was already present in the original VS Quest integration (`ScribeQuestCatalog`) and has likely
  never worked against a real vsquest-dependent content mod (e.g. VS Village), only against
  vsquest's own bundled example content.

## Capabilities

### New Capabilities
(none)

### Modified Capabilities
- `quest-auto-detect`: add a requirement that each backend's quest catalog must be read across all
  installed mods' domains (not scoped to the backing framework's own modid), since real quest
  content always ships from a separate dependent mod. This capability's read-only, fail-safe, and
  UI-visibility requirements are otherwise unchanged — this is a correction to a previously
  undocumented, incorrect assumption baked into the two catalog readers' implementation.

## Impact

- `src/Mod/ScribeQuestCatalog.cs` — `ReadCatalog`'s `GetMany` call: drop the `VsQuestModId` domain
  argument (search all domains).
- `src/Mod/ScribeProgressionFrameworkQuestCatalog.cs` — `ReadCatalog`'s `GetMany` call: drop the
  `ModId` domain argument (search all domains).
- No changes to `ScribeQuestWatcher.cs`, `ScribeModSystem.Quest.cs`, `ScribeLinkTarget.cs`, or any
  picker/UI plumbing — this fix is scoped to the two catalog readers' asset-search call and the
  capability spec text describing catalog scope.
- No `src/Core/` changes; no new mod dependency.
