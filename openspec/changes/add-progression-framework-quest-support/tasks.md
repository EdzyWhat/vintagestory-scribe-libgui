## 1. Backend attribution in `ScribeLinkTarget` (Core)

- [x] 1.1 Extend `ScribeLinkTarget`'s `quest:` scheme to `quest:{source}/{code}` (Decision 1);
      add a `QuestSource` enum or string constants for `vsquest`/`progressionframework`.
- [x] 1.2 Update `ForQuest`/`QuestCode`/`IsQuest` to take/return the source alongside the code;
      keep all logic pure string parsing, no VS API reference.
- [x] 1.3 Core unit tests: round-trip both backends' target strings, reject/handle a malformed
      or unknown source token defensively (never throw).
- [x] 1.4 `ScribeDocument.AddQuestLink`/`InsertQuestLink` (or their callers) thread the source
      through when creating a Link block.

## 2. Progression Framework catalog reader (Layer 1)

- [x] 2.1 Add `ScribeProgressionFrameworkQuestCatalog` mirroring `ScribeQuestCatalog`'s shape:
      `IsAvailable(capi)` → `IsModEnabled("progressionframework")`; `ReadCatalog(capi)` reading
      `config/quests/*.json` via `capi.Assets.GetMany`.
- [x] 2.2 Define the PF-specific `RawQuest`/`RawObjective` DTOs matching the decompiled schema
      (`code`, `npc`, `scope`, `*LangKey`, `objectives[]` with `code`/`type`/`items[]`/
      `required`/`pattern`, `rewards[]`, `prerequisites[]`) — verified against Seafarer's real
      `config/quests/*.json` in `reference/ProgressionInvestigations/` (gitignored). Also found
      (verified against the decompiled `QuestSystem.LoadQuests`/`PrefixIfBare`) that PF quest
      `code`/`*LangKey` fields are frequently BARE in real data and get domain-prefixed by PF's own
      loader at load time — replicated that exact prefixing here so a picker-created Link's code
      matches the domain-qualified code that later appears in `WatchedAttributes` verbatim.
- [x] 2.3 Resolve title/description via each quest's `titleLangKey`/`descriptionLangKey` (PF
      quests carry their own lang keys directly, unlike vsquest's `{id}-title`/`{id}-desc`
      convention — confirm this during implementation and adjust if the decompile missed a
      fallback).
- [x] 2.4 Only surface player-scoped quests in the picker for v1 (Non-Goal: server-scoped); filter
      server-scoped entries out of `ReadCatalog`'s result with a code comment citing the Non-Goal.
      CORRECTION (2026-09-02): real Seafarer data mostly OMITS the `"scope"` field rather than
      writing `"scope": "player"` explicitly — PF's own `QuestScope` enum defaults to Player when
      absent (confirmed against the decompiled source), so the filter excludes only an explicit
      `"server"`, not "anything not literally `player`" (the latter would have wrongly excluded
      most of Seafarer's actual player-scoped quests).

## 3. Progression Framework auto-detect (Layer 2)

- [x] 3.1 Extend `ScribeQuestWatcher` with a second, independently-gated tick path: read
      `progressionframework:questlog` off `capi.World.Player.Entity.WatchedAttributes` (public
      const key `QuestSystem.PlayerQuestTreeKey`), no entity scan needed.
- [x] 3.2 Detect newly-`active` and newly-`completed` quest codes from that tree (per-quest
      `status` string), matching the existing accept/complete dedup pattern
      (`_acceptedSeen`/`_completedSeen`). CORRECTION: the decompiled source's own status string is
      `"completed"`, not `"complete"` — confirmed against `QuestSystem.CompleteQuest`/
      `GetOrAddObjectiveTree`.
- [x] 3.3 Read each active quest's `objectives` sub-tree (`{code}/status`, `{code}/progress`)
      directly by objective code (Decision 4 — no positional zip needed, unlike vsquest).
- [x] 3.4 Expose PF live progress through the same `TryGetLiveProgress`/`TryGetObjectives`-style
      surface `ScribeModSystem.Quest.cs`'s `TryGetQuestProgressText` already calls, dispatching
      by the Link's recorded backend (task 1) rather than trying both.
- [x] 3.5 A failure in PF detection (missing/malformed attribute tree) disables PF detection only
      for the session — mirror vsquest's `dialogReflectionDisabled` fail-closed pattern; VS
      Quest detection must be provably unaffected by construction (separate try/catch scope, own
      disable flag) — NOT covered by an automated test: this is Mod-layer runtime resilience with
      no game-free way to unit-test it, same as vsquest's own `dialogReflectionDisabled` has none.

## 4. Progress text formatting for multi-objective PF quests

- [x] 4.1 Add a PF-specific `FormatProgress`-equivalent (or extend the existing one) that renders
      "N of M objectives complete" (or per-objective detail) from the by-code status/progress
      map, distinct from vsquest's positional kind-labeled format.
- [x] 4.2 Verify formatting against real multi-objective examples (Seafarer's
      ~~`dawnmarie-orchard.json` = 12 objectives~~, ~~`potatoking-lastpotato.json`~~ single-objective) —
      CORRECTION (2026-09-02): both of those files are `"scope": "server"` (out of scope per this
      change's Non-Goal, confirmed against the decompiled `Quest`/`QuestScope` source), not
      player-scoped as this task assumed when written. Verified instead against the real
      player-scoped quests: `celeste-bearhunter.json` (5 objectives), `drake-tricks.json` (3),
      `celeste-crimsonrose.json`/`celeste-rusthunter.json`/`drake-seasoned.json` (1 each) — a scope
      field absent entirely defaults to Player (PF's own `QuestScope` enum default), which is how
      most of Seafarer's player-scoped quests are actually written. Cross-checked the reader's
      domain-prefixing + scope filter against all 11 real quest files with a standalone script;
      output matched exactly.

## 5. Shared Accept-candidate picker extraction

- [x] 5.1 Extract `ComputeAcceptCandidates`'s logic out of `ScribeDialogBase.ViewSwitching.cs`
      into a shared internal helper (e.g. `ScribeAcceptCandidates.Compute(...)`), taking the
      client API and an optional "prefer this docId" hint; same eligibility rule
      (`EnumerateCarriedSlots` filtered to `IScribeDocumentItem` + `IsSlotWriteable`), same
      last-opened-first ordering.
- [x] 5.2 Move `ScribeAcceptCandidate` and `ScribeAssignmentDestinationLabel` out of
      `ScribeInboxContent.cs`'s file-private scope to wherever the shared helper lives (still
      `internal`, just not scoped to one class).
- [x] 5.3 Update the Inbox Accept control's call site to use the shared helper — pure move, no
      behavior change. NOT independently re-playtested here (no game session run this session) —
      the move is byte-for-byte the same logic/ordering, so existing Assignment Accept-picker
      playtest coverage should still apply, but that's an inference, not a fresh manual check.

## 6. Quest auto-link destination resolution (Part B — both backends)

- [x] 6.1 Add `TargetInventoryId`/`TargetSlotId` fields to `ScribeAutoLinkQuestMessage`, mirroring
      `ScribeAssignmentActionMessage`'s existing fields; null/absent falls back to server-side
      resolution for backward compatibility with any in-flight message shape.
- [x] 6.2 HUD banner's Accept action (`ScribeModSystem.Quest.cs`'s `AcceptQuestPrompt`, rendered
      by `HudScribePins.cs`) computes candidates via the task-5 shared helper: 0 → Accept
      disabled with explanatory tooltip; 1 → proceeds unchanged; 2+ → shows a picker (same
      dropdown-then-confirm shape as the Inbox row) before sending.
- [x] 6.3 Quest Accept Policy = Always (`OnQuestAccepted`) checks candidate count before
      auto-sending: exactly 1 → sends immediately as today; 2+ → raises a Prompt-style banner for
      that one link instead of guessing (Decision 3's alternative-considered resolution).
- [x] 6.4 Server-side `OnServerReceivedAutoLinkQuest` accepts the chosen slot, re-resolves and
      re-validates it (writeable, has capacity) exactly like `TryPlaceAcceptedAssignment` does —
      never trusts the client's choice as proof of eligibility; falls back to
      `FindNotebookInInventory` only when no target was sent (defensive compatibility path).
- [ ] 6.5 Manual test: carry two eligible Notebooks, trigger a VS Quest accept under Prompt policy
      — confirm the picker appears and the link lands on the chosen one.
- [ ] 6.6 Manual test: same as 6.5 but Accept Policy = Always — confirm a Prompt-style banner
      appears instead of a silent pick.

## 7. Settings/UI generalization

- [x] 7.1 Settings dialog's Quest policy rows gate on `IsAvailable` of *either* backend (currently
      gates on vsquest only); update the gating condition and its `showQuestSettings`-style flag.
- [x] 7.2 Quest Link picker in the New Task / Link-creation flow merges both backends' catalogs
      into one list (when both installed), tagging each entry with its source for display and
      for the created Link's recorded backend.
- [x] 7.3 Handbook/quest-related documentation text is reviewed for any VS-Quest-specific wording
      that needs generalizing now that a second backend exists.

## 8. Verification

- [x] 8.1 `dotnet test` (Core) green — Core-side changes are limited to `ScribeLinkTarget` (task
      1) and stay VS-API-free. 651/651 passed.
- [x] 8.2 `./build/verify.sh` green (Core + Atlas) before any push. Ran
      `./build/verify.sh Debug --no-restage` (restage skipped — deferred to the user, since it
      tears the DLL if the client is running): build ✓, Core 651/651 ✓, Atlas 25/25 ✓.
- [ ] 8.3 Manual playtest: install Progression Framework + Seafarer, place an NPC quest-giver
      offering a multi-objective delivery quest, confirm catalog entry appears in the Quest Link
      picker, accept it in-world, confirm auto-detect fires and progress mirrors as objectives
      are delivered.
- [x] 8.4 ~~Manual playtest: both vsquest and Progression Framework installed simultaneously~~ —
      not testable: the two mods cannot in practice both be installed at once, so this scenario
      was dropped from the spec (design.md Goals, quest-auto-detect spec.md) in favor of the
      per-record backend-attribution invariant already covered by 1.3's unit tests.
  - Obsolete 2026-09-02: TESTING.md `00000085` (playtest submission 2026-09-02T20-53-17) — see
    design.md correction.
- [x] 8.5 Manual playtest: neither backend installed — confirm no quest UI appears anywhere
  - Confirmed 2026-09-02: TESTING.md `00000086` "(no note)" (submission 2026-09-02T20-53-17)
      (Settings, Link picker, handbook).

## 9. Quest Link follow-to-ledger (Progression Framework only)

- [ ] 9.1 In `ScribeItemRef.OpenHandbookPage`, replace the unconditional `IsQuest(code) => return`
      no-op with a dispatch on the Link's recorded backend (task 1): Progression Framework opens
      its ledger; VS Quest stays a no-op (Non-Goal, design.md Decision 5).
- [ ] 9.2 Open PF's ledger via `capi.Input.HotKeys["progressionframeworkledger"].Handler.Invoke(...)`
      — defensive: missing/renamed hotkey code degrades to a no-op, never throws.
- [ ] 9.3 Confirm this activation path is reached identically from every existing link-click call
      site (read view's `OpenRowLink`, Pin Tab's `OnPinOpenLink`, the tablet editor's
      `EditorRowsOpenLinks`) — no per-surface special-casing needed, since all three already
      funnel through the one function.
- [ ] 9.4 Core unit tests: n/a (this is Mod-layer, VS-API-dependent — no Core change).
- [ ] 9.5 Manual test: with Progression Framework installed, click a PF-backed Quest Link in the
      read view, editor, Pin Tab, and HUD pin — confirm the ledger opens from each surface.
- [ ] 9.6 Manual test: click a VS-Quest-backed Quest Link — confirm no change from current
      (no-op) behavior.
