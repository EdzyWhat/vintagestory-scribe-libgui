## 1. Core data model

- [x] 1.1 Create `src/Core/HistoryEventKind.cs` — enum with values: `Crafted`, `PickedUp`,
      `Death`, `PvpKill`, `BossKill`, `TemporalStorm`, `Manual`, `LoreDiscovery` (reserved,
      not wired). Add XML doc-comment on `LoreDiscovery` noting it is reserved.
- [x] 1.2 Create `src/Core/HistoryEntry.cs` — sealed class with `Kind`, `ActorName`,
      `Detail`, `InGameDate` (all strings/enum), and `IsManual` bool. No VS API references.
- [x] 1.3 Create `src/Core/HistoryStore.cs` — append-only store with per-kind caps:
      `MaxDeaths=10`, `MaxStorms=5`, `MaxManual=10`, `MaxPvpKills=10`, `MaxBossKills=10`
      (Crafted=1 by dedup, PickedUp unlimited by dedup on ActorName).
      Methods: `TryAddEntry(HistoryEntry) → bool`, `TryEditManualEntry(int index, string text) → bool`,
      `Entries (IReadOnlyList<HistoryEntry>)`.
      Sliding-window drop (oldest of that kind) for capped auto kinds; reject-when-full for Manual.
- [x] 1.4 Add `Serialize() → byte[]` and `static Deserialize(byte[]?) → HistoryStore` to
      `HistoryStore` using magic `"SHST"`, version byte `1`. Include `PriorVersion = 1` constant
      and `ApplyMigrations` no-op stub following the `ScribeDocumentCodec` pattern. Include
      `MaxHistoryBytes` guard (reject payload > 64 KB) as an allocation backstop.

## 2. Core tests

- [x] 2.1 Create `tests/Core.Tests/HistoryStoreTests.cs` with tests covering:
      - Empty store round-trips cleanly
      - Each kind dedup/cap rule: Crafted written once, PickedUp deduped by ActorName,
        Death/PvpKill/BossKill/TemporalStorm sliding-window drop at cap, Manual rejects at cap
      - `TryEditManualEntry` updates text, returns false for out-of-range index
      - `LoreDiscovery` kind round-trips through codec without error
      - Null/empty bytes deserializes to empty store (not crash)
- [x] 2.2 Run `dotnet test tests/Core.Tests` — confirm all pass including new tests.
      **RESULT: 175 passed, 0 failed.**

## 3. NotebookHost — history property and flush extension

- [x] 3.1 In `NotebookHost.cs`, add a `HistoryStore History` property, initialized from
      `ItemStack.Attributes["scribeHistory"]` via `HistoryStore.Deserialize()` in the
      constructor (null attribute → empty store).
- [x] 3.2 Extend `Flush()` to also serialize `History` and write to
      `ItemStack.Attributes["scribeHistory"]`. Added `FlushHistory()` for history-only updates
      (cheaper than full Flush — skips document re-serialization). Both push `HistoryBytes` in
      `ScribeNotebookSaveMessage` (new `[ProtoMember(3)]` field).
- [x] 3.3 `RecordPickedUpIfNew` called from `AttachServerContext` — fires when server context
      is attached (first open by each player). Dedup by ActorName is enforced in `HistoryStore`.

## 4. ItemScribeNotebook — crafted hook

- [x] 4.1 Override `OnCreatedByCrafting` in `ItemScribeNotebook.cs`. Gets crafting player from
      `outputSlot.Inventory.openedByPlayerGUIds.FirstOrDefault()`. Calendar date computed from
      `ElapsedDays / DaysPerYear + 1386` (year) and `DayOfYear % DaysPerMonth + 1` (day).

## 5. ScribeModSystem — server event hooks

- [x] 5.1 `sapi.Event.OnEntityDeath += OnEntityDeath` registered in `StartServerSide()`.
- [x] 5.2 `OnEntityDeath` implemented with Boss/Death/PvP paths. Death message reconstructed
      from `deathmsg-{code}-{N}` lang keys (deterministic N via player name hash). Boss codes:
      `eidolon` → "Eidolon", `erel` → "Mad Crow". All three paths call `FlushHistory()`.
- [x] 5.3 `sapi.World.RegisterGameTickListener(OnStormTick, 5000)` registered. Edge-detects
      `StormData.nowStormActive` rising edge. Null-safe if `SystemTemporalStability` absent.
- [x] 5.4 `FindNotebookInHotbar(IServerPlayer)` helper scans hotbar for `ItemScribeNotebook`.
- [x] 5.5 `ScribeAddHistoryEntryMessage` handler registered and implemented (new/edit paths).

## 6. Network packet

- [x] 6.1 Created `src/Mod/ScribeAddHistoryEntryMessage.cs` with `DocIdBytes`, `Text`, and
      `EditIndex` (-1 = new entry, ≥0 = edit existing). `ScribeNotebookSaveMessage` extended
      with `[ProtoMember(3)] HistoryBytes` for server→client history sync.

## 7. GUI — History tab

- [x] 7.1 `scribehistory` SVG icon registered using `guestbook.svg` (GuestbookC1.svg copied to
      `textures/icons/guestbook.svg`). Guestbook icon also updated to use the same file.
- [x] 7.2 Lang keys added: `scribe-gui-nav-history`, `scribe-gui-history-empty`,
      `scribe-gui-history-add`, and all `scribe-gui-history-kind-*` labels.
- [x] 7.3 History nav button added via `GetExtraNavButtons()` override in
      `GuiDialogScribeNotebook`. Active color: `NavActiveHistory` (#7a597e dusty plum).
- [x] 7.4 `BuildHistoryContent()` override in `GuiDialogScribeNotebook`: newest-first entries,
      kind label + date header row, detail text row. Auto entries read-only.
- [x] 7.5 "Add entry" `Button` shown when manual count < `MaxManual`. Sends
      `ScribeAddHistoryEntryMessage` with `EditIndex=-1`.
- [x] 7.6 Manual entry rows use `ScribeMultilineField` with `onBlur` sending
      `ScribeAddHistoryEntryMessage` with the entry's index for edits.

## 8. History sync to client

- [x] 8.1 `FlushHistory()` pushes `ScribeNotebookSaveMessage` with `HistoryBytes` (no
      `DocumentBytes` — client treats null as no document update). Client's
      `OnClientReceivedNotebookSave` calls `ApplyHistoryUpdate(message.HistoryBytes)` on the
      `NotebookHost`, then `RefreshHistoryView()` triggers a rebuild if the tab is open.
      (Verified 2026-07-31: the `RefreshHistoryView()` call IS wired in `OnClientReceivedNotebookSave`
      — gated on non-null `HistoryBytes` and an open `GuiDialogScribeNotebook` — so the tab auto-rebuilds.
      The earlier "needs to be added" note was stale.)

## 9. Final verification

- [x] 9.1 `dotnet test` — 175 passed, 0 failed.
- [x] 9.2 In-game: craft a notebook → History tab shows Crafted entry. (Confirmed 2026-07-31.)
- [x] 9.3 Open the notebook → PickedUp entry appears; reopen → no duplicate. (Confirmed 2026-07-31.)
- [x] 9.4 Die holding the notebook → Death entry appears with the reconstructed death message.
      (Confirmed 2026-07-31 — works anywhere on the person, not just the hotbar.)
- [x] 9.5 In multiplayer: kill another player while holding notebook → PvpKill entry appears.
      (Confirmed 2026-07-31 MP — works regardless of the notebook's location on the person.)
- [x] 9.6 Trigger a temporal storm (`/time set storm`) → TemporalStorm entry appears on all
      open notebooks. (Confirmed 2026-07-31.)
- [ ] 9.7 Kill an Eidolon within 100 blocks → BossKill entry shows "Eidolon". Verify a
      distant kill (> 100 blocks) records nothing. (Backlogged 2026-07-31 — deferred; the
      Death/PvP/Storm history paths sharing the same OnEntityDeath hook are confirmed.)
- [x] 9.8 Add a manual entry → appears in tab. Edit it → text updates. Add 10 total → "Add
      entry" button disappears. (Confirmed 2026-07-31.)
- [x] 9.9 Give the notebook to another player → they see the full history from before. (Confirmed 2026-07-31, MP.)
- [x] 9.10 Restart the world → all history survives. (Confirmed 2026-07-31.)
