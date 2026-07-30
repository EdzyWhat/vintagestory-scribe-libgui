## Why

The Notebook is a personal artifact that accumulates meaning over a playthrough, but today
it records nothing about its own story — who made it, who has held it, what they survived.
A History tab gives the Notebook a passive chronicle of significant events, making it feel
like a real object with a life rather than a stateless storage block.

## What Changes

- Add a `HistoryEntry` / `HistoryEventKind` / `HistoryStore` data model in `src/Core/`,
  with a versioned binary codec (`SHST v1`) modeled on `GuestbookStore`.
- Persist history in `ItemStack.Attributes["scribeHistory"]` so it physically travels with
  the item (traded, dropped, given away) with no extra routing.
- Wire seven auto-recorded event types server-side in `ScribeModSystem` and
  `ItemScribeNotebook`:
  - **Crafted** — who crafted the notebook and when (once only)
  - **PickedUp** — first time each new player opens the notebook (one entry per player, deduped)
  - **Death** — the holder died while carrying the notebook; stores the reconstructed vanilla
    death message (e.g. "JunkMuffin got killed by a nightmare shiver"); capped at last 10
  - **PvpKill** — the holder killed another player while carrying the notebook; capped at last 10
  - **BossKill** — an Eidolon or Mad Crow (Erel) died within 100 blocks of the holder;
    capped at last 10
  - **TemporalStorm** — a temporal storm began anywhere in the world; capped at last 5
  - **Manual** — player-authored free-text entries; capped at 10 (reject when full)
- Add a **History nav tab** to `GuiDialogScribeNotebook` showing all entries newest-first,
  with an inline edit affordance for manual entries and an "Add entry" button.
- Add `ScribeAddHistoryEntryMessage` network packet for manual entry creation/editing.
- Reserve `HistoryEventKind.LoreDiscovery` in the enum for future lore-discovery tracking
  (not wired in this change).

## Capabilities

### New Capabilities

- `notebook-history`: The `HistoryStore` data model, codec, cap enforcement, and the seven
  auto-recorded event types; the History tab GUI on the Notebook dialog.

### Modified Capabilities

- `notebook-item`: The Notebook now carries a second persisted blob (`scribeHistory`) in its
  `ItemStack`, and its dialog gains a History nav tab. The requirement that the Notebook has
  no Guestbook tab is unchanged — History is a distinct tab, not a guestbook variant.

## Impact

- **New files:** `src/Core/HistoryEntry.cs`, `src/Core/HistoryEventKind.cs`,
  `src/Core/HistoryStore.cs`, `tests/Core.Tests/HistoryStoreTests.cs`,
  `src/Mod/ScribeAddHistoryEntryMessage.cs`
- **Modified files:** `src/Mod/NotebookHost.cs` (history property + flush extension),
  `src/Mod/ScribeModSystem.cs` (event hooks), `src/Mod/ItemScribeNotebook.cs`
  (crafted hook), `src/Mod/GuiDialogScribeNotebook.cs` (History tab),
  `src/Mod/assets/scribe/lang/en.json` (new keys)
- **No changes to:** `src/Core/` document model, Lectern, pin system, network channel
  structure (reuses existing `IScribeDocumentHost` flush path)
- **Soft dependency:** temporal storm detection via
  `sapi.ModLoader.GetModSystem<SystemTemporalStability>()` — if the survival mod is absent
  (unusual but possible on custom servers), the storm hook silently skips; no hard dep added
