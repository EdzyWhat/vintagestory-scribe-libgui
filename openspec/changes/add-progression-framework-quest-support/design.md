## Context

Scribe's existing Quest Link support (`ScribeQuestCatalog.cs`, `ScribeQuestWatcher.cs`,
`ScribeModSystem.Quest.cs`) targets exactly one backend, `vsquest`, via two layers:

- **Layer 1 (catalog)**: reads `vsquest`'s own `config/quests/*.json` through the shared asset
  system into a local DTO (`ScribeQuestCatalog.RawQuest`) — no reflection, gated by
  `IsModEnabled("vsquest")`.
- **Layer 2 (auto-detect)**: a client tick listener (`ScribeQuestWatcher`) that reads
  `vsquest`'s own server-synced `WatchedAttributes` keys on quest-giver entities
  (`lastaccepted-{id}[-{uid}]`, `playercompleted-{uid}`) for accept/complete — zero reflection —
  and, only for live kill/place/break progress counts (no attribute equivalent exists for
  those), reflects into the open `VsQuest.QuestSelectGui`'s `activeQuests` field, wrapped in
  try/catch with permanent self-disable on first failure.

Progression Framework (`progressionframework`, backing Seafarer and other dependent mods) is a
second, unrelated quest framework with the same shape but different specifics, decompiled and
surveyed this session (`reference/ProgressionInvestigations/`, gitignored):

- Catalog: `config/quests/*.json` via the same `api.Assets.GetMany` convention, different JSON
  schema (`code`, `npc`, `scope`, `*LangKey`, `objectives[]`, `rewards[]`, `prerequisites[]`).
- Auto-detect for **player-scoped** quests (`scope: "player"`, the common case — confirmed via
  Seafarer's real quest files): `ProgressionFramework.Quests.QuestSystem` writes status +
  per-objective progress directly into `player.Entity.WatchedAttributes` under a **public const
  key**, `QuestSystem.PlayerQuestTreeKey = "progressionframework:questlog"`. This is simpler than
  vsquest's Layer 2 — no reflection is needed even for progress counts, since PF's tree already
  carries `{questCode}/objectives/{objectiveCode}/{status,progress,baseline}`.
- **Server-scoped** quests (shared world-state; Seafarer's "rebuild the town" storyline) have no
  `WatchedAttributes` equivalent — they live in a private `Dictionary<string, TreeAttribute>` on
  the `QuestSystem` instance, synced to clients only via a private network channel. Out of scope
  for this change (see Non-Goals).

Separately, a real gap surfaced in the *existing* vsquest flow while investigating this: quest
auto-link's destination resolution (`OnServerReceivedAutoLinkQuest` →
`FindNotebookInInventory` → first carried Notebook/Tablet, no choice) predates and diverges from
the Assignment system's Accept flow, which already solved the identical problem
(`ComputeAcceptCandidates` in `ScribeDialogBase.ViewSwitching.cs` — multiple eligible carried
documents get a real picker; a prior "last-opened book wins silently" shortcut was deliberately
removed for exactly this reason). Both quest backends should share the fixed behavior.

## Goals / Non-Goals

**Goals:**
- Add Progression Framework as a second, independently-gated Quest Link backend, matching the
  existing vsquest integration's soft-dependency shape (no compiled reference, `IsModEnabled`
  gate, catalog + auto-detect layers).
- Support either backend independently, with a given Quest Link always resolving against the
  one backend it actually came from. (In practice `vsquest` and `progressionframework` cannot
  both be installed at once, so per-record backend attribution is a defensive invariant verified
  at the unit level — see task 1.3 — not a live dual-backend scenario.)
- Surface per-objective progress for Progression Framework quests with multiple delivery
  objectives (common in real Seafarer data — 6 of 11 sampled quests have 2–17 objectives),
  reusing the existing progress-text rendering path.
- Fix quest auto-link's destination resolution (both backends) to use the same multi-candidate
  picker as Assignment's Accept flow, instead of silently picking the first carried document.

**Non-Goals:**
- Progression Framework Training progress (`progressionframework:training`) — a separate
  subsystem with no bearing on quests; not touched by this change.
- Progression Framework server-scoped quests — no `WatchedAttributes` path exists; would need
  reflection into `QuestSystem`'s private state or its public helper methods
  (`GetStatus`/`GetObjectiveProgress`/`IsObjectiveComplete`), which is a legitimate future
  extension but adds a second reflection surface this change doesn't need for the common case.
- A third-party datapack contributing quests under its own domain to either backend (matches the
  existing, already-disclosed vsquest limitation — scoped to each backend's own catalog domain).
- Any change to how a Quest Link renders, is created manually, or degrades when orphaned — only
  detection/attribution/destination-resolution change.

## Decisions

### Decision 1: Backend attribution lives in the `LinkTarget` string, not a new `ScribeBlock` field

`ScribeLinkTarget`'s scheme extends from `quest:{code}` to `quest:{source}/{code}`, where
`source` is a fixed short token (`vsquest` or `progressionframework`) and `{code}` is the
backend's own (already domain-qualified, e.g. `seafarer:dawnmarie-orchard`) quest id. A `/`
separator is unambiguous because quest codes are always `domain:path` (colon-delimited) and never
contain `/`.

**Alternative considered**: add a `QuestSource` field to `ScribeBlock` alongside `LinkTarget`.
Rejected — `ScribeBlock` is part of the document codec (`src/Core/`), so a new field means a codec
version bump and a migration path for a purely cosmetic attribution detail. Keeping it inside the
existing string scheme costs nothing at the codec level and matches how `page:`/`quest:` already
distinguish link kinds within one string field.

### Decision 2: Progression Framework gets its own catalog reader + watcher, not a shared abstraction

Add `ScribeProgressionFrameworkQuestCatalog` (mirrors `ScribeQuestCatalog`'s shape: `IsAvailable`,
`ReadCatalog`, a PF-specific `RawQuest`/`RawObjective` DTO, `FormatProgress`) and extend
`ScribeQuestWatcher` with a second, independently-gated tick path that reads
`progressionframework:questlog` off `capi.World.Player.Entity.WatchedAttributes` — no entity
scan needed (unlike vsquest's per-questgiver-entity scan, since PF's tree is keyed on the
player's own entity, not an NPC's).

**Alternative considered**: extract a shared `IQuestBackend` abstraction now, so a third backend
slots in without touching existing code. Rejected for this change — with exactly two backends
whose detection mechanisms differ in kind (entity-attribute scan vs. own-player-attribute read;
reflection-required vs. reflection-free), a shared interface would either leak backend-specific
concepts through it or add indirection with no current second caller. Two concrete, parallel
readers (matching each other's shape by convention, not by inheritance) stays simplest; revisit
if a third backend ever appears.

### Decision 3: Quest auto-link's destination resolution reuses Assignment's candidate logic, extracted to a shared helper

`ComputeAcceptCandidates` (currently private to `ScribeDialogBase.ViewSwitching`) and its
supporting types (`ScribeAcceptCandidate`, `ScribeAssignmentDestinationLabel`, both currently
`internal` to `ScribeInboxContent.cs`) are extracted into a small shared internal helper (e.g.
`ScribeAcceptCandidates.Compute(ICoreClientAPI, EntityPlayer, docId?)`) usable from both the
Inbox dialog and the Quest HUD banner's Accept action. The Quest HUD banner (`ScribeModSystem
.Quest.cs`'s `AcceptQuestPrompt`, rendered by `HudScribePins.cs`) gains the same
zero/one/many rendering rule already used for the Inbox Accept control: 0 eligible → banner's
Accept renders disabled with the same explanatory tooltip; 1 → unchanged one-tap behavior; 2+ →
a dropdown picker appears before the confirming Accept tap. The chosen `(InventoryId, SlotId)`
travels in `ScribeAutoLinkQuestMessage` (new fields, mirroring `ScribeAssignmentActionMessage`'s
`TargetInventoryId`/`TargetSlotId`); the server re-resolves and re-validates the slot itself
(matching Assignment's server-side defensive re-check), never trusting the client's choice as
proof of eligibility.

**Alternative considered**: leave "Always" policy exempt from the picker (auto-send using the
first eligible candidate, matching current behavior) and only fix "Prompt". Rejected — "Always"
already implies "don't interrupt me," but silently guessing among 2+ candidates is exactly the
behavior being removed from the codebase for Assignment; instead, Always is defined to only
auto-fire when there is exactly one eligible candidate. With 2+ candidates, Always falls back to
raising a Prompt-style banner for that one link (still no interruption for the 0-or-1 case, which
covers the overwhelming majority of players who carry a single Scribe document).

### Decision 4: Per-objective progress for Progression Framework reuses `ScribeQuestCatalog.FormatProgress`'s shape, not a new formatter

Since PF's `WatchedAttributes` tree already carries `{status, progress}` per objective (no
positional zipping against a separately-fetched live count needed, unlike vsquest's tracker
reflection), the PF catalog's `ScribeQuestObjectiveDef`-equivalent carries a `Required` count
per objective read directly from the catalog, and progress is read directly by objective code
(not by position) — simpler and less fragile than vsquest's positional zip, since PF's own data
already correlates status to a stable `objectiveCode` string, not an index.

### Decision 5: Following a Quest Link opens the source mod's own UI, for Progression Framework only

Every quest Link click already funnels through one function, `ScribeItemRef.OpenHandbookPage`,
which handles `page:` links and then explicitly no-ops for `quest:` links
(`if (ScribeLinkTarget.IsQuest(code)) return;`). Replacing that no-op with a dispatch on the
Link's recorded backend (Decision 1) activates "follow the link" identically across every surface
that already calls this one function (read view, editor, Pin Tab, HUD) — no new plumbing, no new
button.

For **Progression Framework**, this is cheap: `GuiDialogLedger.ToggleKeyCombinationCode =>
"progressionframeworkledger"` and its constructor takes no scene-specific arguments — it's a
standalone toggleable dialog, exactly like Scribe's own dialogs. `capi.Input.HotKeys
["progressionframeworkledger"].Handler.Invoke(...)` opens it with no reflection into private
state, only a lookup by PF's own public hotkey code (same risk category as the `WatchedAttributes`
key names this change already depends on — degrades to "click does nothing" if PF renames the
code, not a crash).

For **VS Quest**, this is not cheap: `QuestSelectGui.ToggleKeyCombinationCode => null` (no
standalone hotkey at all), and its constructor requires a live `questGiverId` plus that NPC's
`availableQuestIds`/`activeQuests`/`questConfig` — it's built fresh per NPC interaction, not
toggleable on its own. Opening it from a Link click would require capturing and persisting the
originating quest-giver's entity id at accept time (a new field on the Quest Link, and a bet that
the NPC entity is still loaded/nearby later) — a materially larger, less reliable feature than
the PF case, not a symmetric one-line addition.

**Decision**: implement the follow-to-ledger dispatch for Progression Framework only in this
change; leave VS Quest Links as the existing no-op (Non-Goal). Revisit VS Quest parity as its own
change if it becomes a real ask, rather than block or complicate this one on it.

**Alternative considered**: a dedicated "Open Ledger" button on the Quest HUD pin, independent of
the Link-click paradigm. Rejected — Scribe already has a working, one-function link-follow
dispatch point that every other link kind uses; adding a parallel button would be new surface
area for a behavior the existing paradigm already covers for free once the no-op is replaced.

### Decision 6: Almanac requires no compatibility work

Investigated both Almanac mods (Illuminated and Trades/Callings & Mastery) directly —
decompiled into `reference/AlmanacInvestigations/` (gitignored, scratch). Neither has any
reference to `progressionframework`, its `questlog`/`training` attribute trees, or any live
integration with it. Illuminated's guide content is loaded purely from a content-mod-authored
`almanac/guides/*.json` convention (`GuidePackLoader.cs`); its "cross-compatibility" with PF is a
documentation-authoring convention (someone could describe a PF quest in prose in that format),
not a data or code path Scribe could conflict with. TCM is an unrelated trade-leveling system;
its own "Ledger" naming (`PracticeLedger`/`LedgerSystem`) tracks trade practice, not PF's quest
ledger — a naming coincidence only. Separately, Scribe's own two catalog readers already call
`capi.Assets.GetMany("config/quests", <domain>)` with an explicit domain per backend
(`ScribeQuestCatalog.cs:102`, `ScribeProgressionFrameworkQuestCatalog.cs:71`), so even the
VSQuest/PF `config/quests/` collision risk a user flagged (real, but between those two mods'
own asset loaders, not Scribe's) cannot cause Scribe to cross-read the wrong backend's files.
**Decision**: no code change; no further investigation needed unless new evidence surfaces.

## Risks / Trade-offs

- **[Risk] Progression Framework could rename `PlayerQuestTreeKey`/`TrainingTreeKey` or its
  attribute shape in a future release, silently breaking detection.** → **Mitigation**: same
  category of risk already accepted for vsquest's attribute keys and for the Harmony patch on
  `gui`'s `Register()` elsewhere in this codebase — no upstream contract exists for either. Reads
  are defensive (`GetTreeAttribute`/`GetString` return null/default on a missing/renamed key
  rather than throwing), so a rename degrades to "auto-detect silently stops working," not a
  crash — matching the existing fail-closed convention.
- **[Risk] A quest code collision between the two backends' domains** (unlikely but not
  impossible if two datapacks reuse a domain string) → **Mitigation**: Decision 1's explicit
  `source` tag in the `LinkTarget` means resolution never has to guess which backend a linked
  quest came from; a collision at the catalog level (two backends both claiming the same
  domain:path) is a pre-existing, disclosed, out-of-scope edge case for either mod individually
  and isn't made worse by this change.
- **[Trade-off] Extracting `ComputeAcceptCandidates` touches Assignment's existing, playtest-
  confirmed code path**, not just new Quest code. → **Mitigation**: the extraction is a pure move
  (same logic, same eligibility rule, same ordering-by-last-opened-docid), not a behavior change
  for Assignment; the Inbox dialog's call site becomes a call to the shared helper. Existing
  Assignment tests/playtest coverage for the Accept picker continue to exercise the shared code.
- **[Trade-off] "Always" policy gains a new interruption case** (2+ candidates) that didn't exist
  before. → **Accepted**: the alternative (silent first-match) is the exact bug this change fixes;
  a rare interruption for players carrying multiple Scribe documents is preferable to a wrong
  silent placement.
- **[Risk] PF could rename or remove the `progressionframeworkledger` hotkey code.** →
  **Mitigation**: same fail-closed category as the other PF risk above — look up the hotkey by
  code defensively (missing key → no-op, not a crash); this is strictly additive UX on top of
  detection that already works without it.
- **[Trade-off] VS Quest Links stay a no-op on click, an asymmetry between the two backends.** →
  **Accepted**: real parity requires capturing/persisting a quest-giver entity id VS Quest itself
  doesn't expose a stable long-lived handle for; scoping that in here would block the
  cheap/ready PF case on a harder, separate problem (Decision 5).
