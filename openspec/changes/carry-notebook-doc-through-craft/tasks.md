## 1. Carry the document + history through the craft

- [x] 1.1 In `ItemClockmakerNotebook.OnCreatedByCrafting` (server-side block), before the
      existing "Crafted" history stamp, scan `allInputSlots` for the first non-empty slot whose
      `Itemstack` has a `"scribeDocument"` attribute (the source Notebook). Capture that stack.
      (Used `ScribeDocumentAttributes.DocumentAttributeKey` + `HasAttribute`.)
- [x] 1.2 If a source stack was found, copy its `"scribeDocument"` bytes onto
      `outputSlot.Itemstack` under the same key (raw attribute copy — do NOT round-trip through
      `ScribeDocumentCodec`, to preserve the `DocId` exactly). If none was found, leave the
      output's document untouched (fresh-`DocId` fallback).
- [x] 1.3 If the source stack has a `"scribeHistory"` attribute, copy those bytes onto the
      output BEFORE the existing `HistoryStore.Deserialize(... GetBytes("scribeHistory"))` line,
      so the "Crafted" entry appends onto the carried-over chronicle rather than a blank one.
- [x] 1.4 Confirm the existing "Crafted" stamp still runs after the copies and reads back the
      just-copied history (i.e. deserialize → `TryAddEntry` → serialize order is preserved).
      Added an `outputSlot.Itemstack is null` guard, which also cleared the method's pre-existing
      CS8602 warning (3 → 2 total).

## 2. Handbook copy

- [x] 2.0 In `src/Mod/assets/scribe/lang/en.json`, extend `handbook-scribenotebook-craft-text`
      with a short clause that upgrading to the Clockmaker's Notebook keeps your tasks and
      History (append to the existing sentence that already mentions the upgrade path).
      Appended "— the upgrade keeps your tasks and History."; JSON re-validated.

## 3. Build + verify

- [x] 3.1 `dotnet build src/Mod/Mod.csproj -c Release` compiles clean (no new warnings).
      2 warnings total, both pre-existing elsewhere; this change added none.
- [x] 3.2 `dotnet test tests/Core.Tests` still passes (Core is untouched; this confirms no
      accidental Core coupling). 183 passed.
- [x] 3.3 Restage (`build/restage.sh Debug`) and verify in-game: craft a Clockmaker's Notebook
      from a Notebook that has a title, several tasks (some done), and prior History — the
      crafted Clockmaker's Notebook opens showing the SAME title/tasks/done-state and the prior
      History plus a new "Crafted" entry. Confirmed in-game 2026-07-31 (user): tasks + History
      both carry over through the upgrade craft. (TESTING.md `fb219286`.)
- [ ] 3.4 In-game negative check: obtain a Clockmaker's Notebook whose craft had no source
      document (e.g. creative `giveitem`) and confirm it opens with a fresh empty document and a
      crafted-only (or empty) history — no crash, no stale data.
