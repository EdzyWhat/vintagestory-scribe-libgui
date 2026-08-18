# Tasks — add-scriptorium-import-export

Clipboard-based JSON + TSV import/export on the Scriptorium Transcribe tab. Core codecs first
(API-free, unit-tested), then the packet + server, then GUI wiring, then in-game verification.

## 1. Core: JSON codec

- [x] 1.1 Add `src/Core/ScribeDocumentJsonCodec.cs`: `static string Serialize(ScribeDocument)` and
      `static bool TryDeserialize(string, out ScribeDocument?)` using `System.Text.Json`. Versioned
      (`"v"` field), indented, human-readable.
- [x] 1.2 Serialize per block: `kind` (as a stable string token, not the raw byte), `text`, `done`,
      `depth`, and the tracker/link references (`targetItemCode`, `targetQuantity`, `linkTarget`,
      `linkLabel`). Serialize the document `title`. OMIT `TaskId`, `DocId`, `assignedToUid`, and
      `currentQuantity` (design D1).
- [x] 1.3 Deserialize defensively: unknown/missing fields default; unknown kind tokens degrade to
      `Task`; enforce the existing `ScribeDocumentCodec` length/count caps (reuse the constants).
      Never throw on malformed input — return false.
- [x] 1.4 Core.Tests: round-trip fidelity (all four kinds), version tolerance (older/newer `v`),
      cap enforcement, malformed-JSON returns false, omitted fields (no TaskId/assignment/live count
      in output), title round-trip.

## 2. Core: TSV codec + escaping

- [x] 2.1 Add `src/Core/ScribeTsvSafe.cs`: structural escaping (quote fields with tab/CR/LF/edge-space,
      double internal quotes) and unescaping; formula-injection defang (prefix `'` when a field starts
      with `= + - @`, tab, or CR) and its inverse (strip a single leading defang `'` on import).
- [x] 2.2 Add `src/Core/ScribeDocumentTsvCodec.cs`: `Serialize`/`TryDeserialize`. Fixed columns in order
      `Type · Done · Text · Special · Count · Depth` with a header row. Serialize the document title as a
      leading `title` row (Text = title, other cells blank). `Type` token = `title`/`note`/`task`/`tracker`/
      `link`; `Done` = `x`/blank; `Text` = human-readable label; `Special` = per-kind comma-separated machine
      payload (tracker → item code, link → target, map → `x,y,z,icon,color`); `Count` = numeric modifier;
      `Depth` = integer indent. Uses `ScribeTsvSafe` for every field. Column set is fixed — no new columns.
- [x] 2.3 Deserialize: parse by header (tolerate unknown trailing columns, default missing ones); block
      sequence = row position (no order column); a `title` row sets the document title and produces no block;
      read `Depth` as an integer and clamp to range; map `Type` token → kind (unknown → `Task`); leave
      per-kind `Special` sub-field parsing (comma split, positional, default-missing) to the kind. Produce
      blocks with the parsed reference strings — no game validation here (that is Mod-side, D5). Enforce caps;
      never throw.
- [x] 2.4 Core.Tests: round-trip all kinds; `title` row sets title and creates no block (absent title row
      leaves title unchanged); integer `Depth` round-trips (child still valid standalone); comma-packed
      `Special` parsed positionally with missing sub-fields defaulted; row-position preserves sequence;
      unknown trailing columns tolerated; missing columns defaulted; escaping round-trips
      tab/newline/quote/leading-space text; formula-injection defang applied on export and stripped on import;
      caps enforced; malformed rows do not abort (degrade to plain Task).

## 3. Mod: import packet + server

- [x] 3.1 Add `src/Mod/ScribeTranscribeImportMessage.cs` (ProtoBuf), mirroring
      `ScribeTranscribeCopyMessage`: target slot identity, serialized document payload (JSON string),
      `Append` bool, `AllowOverwrite` bool.
- [x] 3.2 Server handler `OnServerReceivedTranscribeImport` in `ScribeModSystem.Network.cs`: deserialize
      the payload (JSON codec), mint a fresh `TaskId` per block and a fresh `DocId` on overwrite, run the
      mode-aware capacity check (append: target+incoming; overwrite: incoming), write via the document
      store + `MarkDirty`/re-sync. Reuse the copy path's helpers where possible.
- [x] 3.3 Register the packet on client + server channels alongside the copy message.
- [x] 3.4 Confirm the import path can never create a pin (fresh ids only; no pin store write). Add a note
      in the handler comment.

## 4. Mod: best-effort reconstruction + client send

- [x] 4.1 On the client, before send: for TSV/JSON blocks of item-bound kinds, validate the reference
      against the running game (`AssetLocation`/handbook resolve). Unresolved/blank → degrade the block to
      a plain `Task` carrying its text. Count degradations.
- [x] 4.2 Client `SendTranscribeImport(append, allowOverwrite)` serializes the (validated) document to JSON
      and sends `ScribeTranscribeImportMessage`. On server ack, `PlayStamp(targetSlot, "IMPORTED")`.
- [x] 4.3 Surface the import result toast ("Imported N tasks; M unknown items imported as plain tasks").
      Invalid clipboard → "clipboard is not a valid Scribe export" toast, no send.

## 5. Mod: GUI wiring (replace the placeholder)

- [x] 5.1 In `GuiDialogScribeScriptorium`, convert `BuildImportExportSection` from inert placeholder to
      live: **Copy as JSON**, **Copy as TSV**, **Import** buttons (Caudex, copy-button theme; any caption
      in the player's body font). Remove the disabled-control placeholders and the CSV control.
- [x] 5.2 Bind the placeholder slot as a real source/target `SlotController` (reuse the copy-slot binding
      pattern); update the caption lang key.
- [x] 5.3 Export handlers: read the slotted item's document → codec → `GetClipboard().SetText(...)` →
      `PlayStamp(exportSlot, "EXPORTED")`. Disable exports when the slot is empty.
- [x] 5.4 Import handler: `GetClipboard().GetText()` → auto-detect (`{` → JSON else TSV) → Core parse →
      Mod validate (task 4.1) → `SendTranscribeImport`. The copy-mode Overwrite/Append radio governs import
      (shared `copyMode`); confirm the capacity gate / disabled states mirror copy.
- [x] 5.5 Lang keys: button labels, slot caption, result/error toasts. Verify `IMPORTED`/`EXPORTED` imprint
      keys already exist (they do).

## 6. Docs + build

- [x] 6.1 `docs/CODEC-MIGRATION.md`: add entries for `ScribeDocumentJsonCodec` and `ScribeDocumentTsvCodec`
      (version window + TSV fixed-columns / additive-`Special`-payload rule).
- [x] 6.2 Handbook/README: a short "Import / Export" blurb — the two lanes, the TSV columns, the
      spreadsheet round-trip, and that imports are never pinned.
- [x] 6.3 `dotnet build` clean (0 warnings); `dotnet test` (Core) green; restage Debug (client not running).

## 7. In-game verification

- [x] 7.1 Export a document as JSON; paste into a text editor — confirm it is readable and complete; import
      it into an empty item (Overwrite) and confirm the document reconstructs, unpinned.
- [x] 7.2 Export as TSV; paste into Excel/Google Sheets — confirm columns lay out; edit a task's text and a
      tracker's count; copy the range back; Import (Append) and confirm the edits land and nothing is deleted.
- [x] 7.3 Import a TSV with an unknown tracker item code — confirm it degrades to a plain task and the result
      toast reports the degradation.
- [x] 7.4 Import junk clipboard text — confirm the "not a valid Scribe import" toast and no change.
- [x] 7.5 Formula-injection check: a task whose text is `=1+1`; export TSV, open in a spreadsheet — confirm
      it shows as text (not evaluated); re-import — confirm the text is `=1+1` again.
- [x] 7.6 Overwrite vs Append parity with copy: both modes behave as on the copy path; the stamp reads
      IMPORTED / EXPORTED over the right slot.
- [x] 7.7 Pin safety: pin a task, export+import into the same world — confirm no imported task is pinned and
      the original pin is untouched.
- [ ] 7.8 Multiplayer (backlog-eligible): two clients on one Scriptorium — an import by one re-syncs to the
      other with no dupe/desync.
