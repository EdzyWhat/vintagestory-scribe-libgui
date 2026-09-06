## Context

`ScribeDialogBase.Editor.cs`'s `QuestCatalogForPicker` lazily reads and caches the FULL merged
catalog (`ScribeQuestCatalog.ReadCatalog` + `ScribeProgressionFrameworkQuestCatalog.ReadCatalog`)
once per dialog session, then hands it straight to `ScribeAddKindPicker`. Neither reader has any
concept of per-player state — they only parse the static `config/quests/*.json` assets. Player
quest state instead lives in `ScribeQuestWatcher`, which already polls it every tick for the
existing accept/complete auto-detect flow (see proposal.md — Why):

- **Progression Framework**: `pfStatusFingerprint` (keyed by quest code) tracks the last-observed
  `status` string read directly off the player's own `WatchedAttributes` tree — reliable regardless
  of proximity to any NPC, refreshed every tick the mod is installed.
- **VS Quest**: `_acceptedSeen`/`_completedSeen` (also keyed by quest code) are populated only when
  a nearby quest-giver ENTITY carrying the accept-state attribute has been scanned at least once
  this session (`ScanGiver`). There is no player-centric vsquest signal Scribe can read without
  either being near the giver or reflecting into `VsQuest.QuestSystem`'s private per-player save
  data — out of scope here (a much larger, separate reflection surface with its own fragility, not
  justified for a picker filter).

## Goals / Non-Goals

**Goals:**
- Trim the picker to quests the player has actually engaged with, using state Scribe already
  collects for auto-detect — no new polling loop, no new reflection surface.
- Keep the two backends' filtering logic independent and fail-closed, matching
  `ScribeQuestWatcher`'s existing per-path isolation (a PF failure never touches vsquest state or
  vice versa).

**Non-Goals:**
- Do not attempt to make vsquest's "started" detection session-independent (e.g. by reflecting into
  `QuestSystem`'s private per-player save data to learn about a quest whose giver hasn't been seen
  yet). Confirmed with the user (2026-09-05): the tighter, session-scoped list is preferred over a
  noisier one that risks showing quests never actually touched.
- Do not change auto-detect, progress mirroring, or the Quest Accept/Completion prompt flow — only
  the manual "Add Quest Link" picker's candidate list changes.
- Do not add an "available but not yet started" tier (e.g. quests offered by a currently-loaded
  giver but not yet accepted). The playtest note's "or are live on the server" phrasing is
  interpreted here as "started" for PF (`active`/`completed`) and "session-observed accepted" for
  vsquest — not a third distinct "offered" state, which would need tracking each giver's available-
  quest list and is not justified by the reported pain point (an overwhelming list of everything,
  not a request for an "offered nearby" tier).

## Decisions

**Decision 1 — Expose a single `ScribeQuestWatcher.HasStarted(string questCode)` query.**
Adds one small public method reading the watcher's existing session-cached sets:
`_acceptedSeen.Contains(code) || _completedSeen.Contains(code)` (vsquest) OR
`pfStatusFingerprint.ContainsKey(code)` (PF — any recorded status, active or completed, counts as
started). No new state, no new tick work — purely a read of data the watcher already maintains for
auto-detect.
- *Alternative considered*: duplicate the tracking inside the picker/dialog layer instead of
  querying the watcher. Rejected — `ScribeQuestWatcher` is the one place this state is already
  correctly and independently maintained per backend; a second copy would drift.

**Decision 2 — Filter at picker-open time, not by changing what `QuestCatalogForPicker` caches.**
`QuestCatalogForPicker`'s cache holds the STATIC catalog (which never changes at runtime) and stays
as-is. The "started" filter is applied fresh each time the picker's candidate list is actually
built for display (each footer/"Add" tile open), so a quest accepted mid-session appears in the
picker the next time it's opened without needing a dialog re-open or relog.
- *Alternative considered*: bake the filter into the cached list itself. Rejected — the cache's
  whole point is that the catalog is static; baking in a live filter would require cache
  invalidation logic for no benefit over just filtering at read time (the catalog is small; a
  per-open filter pass is cheap).

**Decision 3 — Hide-until-confirmed for vsquest (2026-09-05 user decision).**
An entry Scribe cannot yet confirm as started this session is hidden, not shown. This accepts the
disclosed gap (a legitimately-started vsquest quest may be briefly invisible in the picker after a
fresh login, until its giver is encountered again) as the correct trade-off against a noisier list
that might include a quest the player never actually started — see proposal.md's "What Changes".

## Risks / Trade-offs

- **[Risk]** A player who wants to manually link a vsquest quest they started last session, but
  whose giver they haven't revisited yet, will see it missing from the picker and may think Scribe
  is broken. → **Mitigation**: this is a disclosed, accepted limitation (Decision 3); if it proves
  confusing in practice, a future follow-up could add a picker empty-state hint ("visit the quest's
  giver to make it selectable here") — not in scope for this change.
- **[Risk]** `HasStarted` silently returns false forever if `ScribeQuestWatcher`'s per-backend
  detection has self-disabled (e.g. a PF/vsquest update breaks the existing reflection/tree-shape
  assumptions) — the picker would then show nothing even for genuinely started quests. →
  **Mitigation**: this mirrors the watcher's existing fail-closed posture for auto-detect (a
  disabled path already silently stops firing accept/complete notifications too); no new failure
  mode is introduced, and fixing the underlying detection (not the picker filter) is the correct
  response if it happens.
