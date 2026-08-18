## Why

The Scriptorium's Transcribe tab ships a visible-but-inert Import/Export section (a placeholder slot
plus disabled "Export JSON / Export CSV / Import" buttons). The v7 spec calls for real round-tripping:
take a document out of a save, refine it in a spreadsheet or text editor, and load it back into the
same or another world. The stamp flourish was already generalized (arbitrary target slot, arbitrary
imprint word) as groundwork for exactly this. This change wires the section up.

Two refinements to the original v7 plan, decided during design:

- **Clipboard, not files.** The mod already reads and writes the OS clipboard (`GetClipboard().GetText()/
  .SetText()`, used by the text fields). Clipboard is zero-friction and needs no in-GUI file picker.
- **TSV, not comma-CSV.** The clipboard format Excel and Google Sheets actually parse into rows/columns
  on paste is **tab-separated**, not comma-CSV (which lands in a single column until a "split" step). So
  the spreadsheet-friendly lane is TSV; comma-CSV is dropped.

## What Changes

- **Two export lanes, one import, all via the clipboard:**
  - **Copy as JSON** — full-fidelity, versioned, human-readable. The lossless round-trip lane.
  - **Copy as TSV** — a legible, spreadsheet-native table. Paste straight into Excel/Sheets, edit in
    columns, copy back, import. Nearly as rich as JSON via a hybrid column schema (below).
  - **Import** — reads the clipboard, **auto-detects** JSON vs TSV (JSON starts with `{`), and applies
    onto the target slot's Scribe item using the **same Overwrite / Append radio** the copy path uses.
- **New Core codecs (API-free, unit-tested):**
  - `ScribeDocumentJsonCodec` — a human-readable, versioned `ScribeDocument ⇄ JSON` codec parallel to the
    existing binary `ScribeDocumentCodec`, using `System.Text.Json` (BCL — no new package).
  - `ScribeDocumentTsvCodec` — a fixed-column **Type · Done · Text · Special · Count · Depth** table (the
    document title rides as a leading `title` row). `Type` names the kind (title/note/task/tracker/link,
    extensible to map/craft); `Text` is the human-readable label; `Special` is the kind's machine reference
    as a comma-separated payload (item code, link target, later `x,y,z,icon,color` for map); `Count` the
    numeric modifier; `Depth` an integer indent for loose grouping (no parent links). Row position is the
    sequence — no order column. The column set is fixed forever: new kinds extend the `Special` payload, not
    the columns. Import tolerates unknown trailing columns and defaults missing ones.
- **Best-effort TSV import.** A row whose `Type` + reference resolves to a real game item rebuilds that
  typed task; an unresolved/blank binding degrades to a plain Task carrying the row's text; one bad row
  never aborts the import. JSON import reconstructs full fidelity.
- **Injection & escaping safety.** TSV export escapes tabs/newlines/delimiters so text round-trips, and
  neutralizes spreadsheet formula injection (cells leading with `= + - @`, tab, or CR are defanged).
  Imported text is stored literally — never interpreted as VS rich-text markup in-game.
- **Never creates pinned tasks.** Import mints fresh `TaskId`s server-side (like copy), so no import can
  resurrect or create a pin; the player pins manually afterward. (Copy/paste already behaves this way —
  pinning is a separate per-player store keyed by `(DocId, TaskId)`, not a block field. No copy/paste
  change is needed.)
- **Server-authoritative import.** Import mutates a document, so it flows through a packet (like the copy
  message); export is a pure client-side read of the already-synced document.
- **Reuses the packaged stamp flourish** — Import stamps `IMPORTED`, Export stamps `EXPORTED`, over the
  relevant slot, via the already-generalized `PlayStamp(targetSlot, label)`.
- The Transcribe tab's inert placeholder section is replaced by the live controls; the placeholder slot
  becomes the real target slot for import (and the source slot for export).

## Capabilities

### New Capabilities
- `scriptorium-import-export`: Clipboard-based JSON and TSV export/import of a Scribe document on the
  Scriptorium Transcribe tab — the two Core codecs, the hybrid TSV schema, best-effort typed-kind
  reconstruction, injection/escaping safety, server-authoritative import with Overwrite/Append semantics
  and fresh TaskIds, and the IMPORTED/EXPORTED stamp flourish.

### Modified Capabilities
- `transcribe-copy-paste`: the "Import/Export section is present but unwired" placeholder requirement is
  removed — the section is now live (its behavior lives in `scriptorium-import-export`).

## Impact

- **Core (`src/Core/`)**: new `ScribeDocumentJsonCodec` and `ScribeDocumentTsvCodec` (+ a small escaping
  helper); both API-free and unit-tested. Uses `System.Text.Json` (BCL, no new dependency). No change to
  the binary codec or the block model (`Depth`, `Kind`, Tracker/Link fields already carry everything).
- **Mod (`src/Mod/`)**: clipboard read/write wiring; a new `ScribeTranscribeImportMessage` packet + server
  handler (mirrors `ScribeTranscribeCopyMessage`, fresh TaskIds, Overwrite/Append, capacity re-check);
  best-effort item-code validation on import (needs the game to resolve `AssetLocation`); GUI wiring in
  `GuiDialogScribeScriptorium` replacing `BuildImportExportSection` placeholders with live controls,
  reusing `PlayStamp` and the copy-mode radio.
- **Assets**: lang keys for the live controls, import result/error toasts, and the two new stamp imprints
  (`IMPORTED`/`EXPORTED` already added).
- **No new mod dependencies.** Clipboard, `System.Text.Json`, and the existing packet channel only.
- **No save-migration.** The placeholder slot was never persisted; the real target slot reuses the
  existing Transcribe slot plumbing.
- **Multiplayer**: export is client-local; import is server-authoritative and re-syncs like copy.
