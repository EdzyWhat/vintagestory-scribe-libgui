## Context

The Scriptorium Transcribe tab already copies a document between two slots (server-authoritative, via
`ScribeTranscribeCopyMessage`), with an Overwrite/Append radio and a wax-seal stamp flourish that was
generalized to stamp any word over any slot (`PlayStamp(targetSlot, label)`). Below the copy pair sits an
inert Import/Export placeholder (`BuildImportExportSection`): a greyed slot and disabled Export JSON /
Export CSV / Import buttons.

The data model is favorable. `ScribeBlock` carries `Kind` (Task/Text/Tracker/Link, a byte enum, append-only),
`Text`, `Done`, `Depth` (reserved for future sub-item nesting, persisted now), `TaskId` (stable Guid),
Tracker fields (`TargetItemCode`, `TargetQuantity`, live `CurrentQuantity`), and Link fields (`LinkTarget`,
`LinkLabel`). Item references are **plain strings**, never parsed `AssetLocation`/`ItemStack`, so Core stays
API-free. **Pinning is not a block field** — it is a per-player store keyed by `(DocId, TaskId)`. A binary
`ScribeDocumentCodec` exists but is not human-readable.

The clipboard is available today: `Element?.Owner?.GetClipboard()?.GetText()/.SetText(text)` is already used
by `ScribeMultilineField` / `ScribeCuneiformTitleField`, backed by `Gui.Clipboard.GameClipboard`.

## Goals / Non-Goals

**Goals:**
- Round-trip a document out of and back into a save via the clipboard, in two lanes: **JSON** (lossless,
  versioned, human-readable) and **TSV** (legible, spreadsheet-native, nearly as rich as JSON).
- Keep both codecs in Core, API-free and unit-tested.
- A single Import that auto-detects the format and applies with Overwrite/Append semantics.
- Best-effort typed-kind reconstruction on TSV import; never abort on a bad row.
- Injection/escaping safety on both directions.
- Never create or resurrect a pin.
- Future-proof the TSV schema for Map, Crafting, and Subtask kinds without a format break — via a
  **fixed column set** whose per-kind richness lives inside the `Special` cell, not via new columns.

**Design tenet — looseness is the point.** This is a scratchpad for people playing a video game, not a
reporting database. Import is permissive and degrade-don't-reject: a malformed row becomes a plain task, an
unknown kind becomes a plain task, a bad reference degrades — one bad row never aborts an import. There is
**no referential integrity**: `Depth` is a purely visual grouping with no parent links, and "crafting" is
just a loose cluster of tracker rows near each other that nothing actually tracks. Putting things in "wrong"
is allowed by design.

**Non-Goals:**
- File export/import (no on-disk files, no in-GUI file picker) — clipboard only this change.
- Comma-CSV — TSV supersedes it for the clipboard.
- Cross-format editing guarantees beyond what each lane documents (TSV is lossy by design).
- Multiplayer import conflict UX beyond the existing server-authoritative re-sync.
- Creating the future Map/Crafting/Subtask kinds — only reserving how they encode into `Special`.
- Parent/child task relationships or subtree integrity — `Depth` is loose grouping only.
- Exporting assignment (`AssignedToUid`) or live counts (`CurrentQuantity`).

## Decisions

### D1 — Two Core codecs, `System.Text.Json` for JSON
`ScribeDocumentJsonCodec.Serialize(doc) : string` / `TryDeserialize(string, out doc) : bool` live in Core.
JSON uses `System.Text.Json` (BCL in net10.0 — not a NuGet/mod package, satisfies the no-new-deps guardrail).
The JSON is versioned (`"v": <n>`) mirroring the binary codec's forward/backward window rule, human-readable
(indented), and carries every block field that matters for sharing: `kind, text, done, depth`, and the
Tracker/Link references. It **omits** `AssignedToUid` (assignment is place-bound, not shareable) and
`CurrentQuantity` (live/derived — recomputed from carried inventory). `TaskId`/`DocId` are **not** written —
import always mints fresh ones (see D6), so there is nothing to preserve.

### D2 — Fixed 6-column TSV schema: `Type · Done · Text · Special · Count · Depth`
A header row plus one row per block. The column set is **fixed forever** — per-kind richness lives inside
the `Special` cell (see the extensibility rule), never in new columns. Columns:
- **Type** — the kind token: `title` (document title), `note` (Text block), `task`, `tracker`, `link`
  (extensible: `map`, `craft`). Stable, additive tokens.
- **Done** — `x` / blank. A shared task list needs completion state. Ignored for `note`/`title`.
- **Text** — the row's **human-readable label**: the task text; a Link/Tracker's display name; or, for a
  `title` row, the document title.
- **Special** — the row's **machine reference**, a per-kind **comma-separated payload** the kind parses
  itself: `tracker` → item code; `link` → link target; `map` → `x,y,z,icon,color`; `craft` → output code.
  Blank for `note`/`task`/`title`.
- **Count** — the primary numeric modifier: `TargetQuantity` for a tracker (later crafting count). Blank
  otherwise.
- **Depth** — integer nesting, `0` default. **Loose visual grouping only** — no parent links. A craft is a
  `craft` row followed by `tracker` rows at `Depth+1`; nothing enforces the relationship, order doesn't
  matter, and pulling a child out still leaves a valid standalone tracker.

**Title as a `title` row (not a preamble).** The document title round-trips as a normal typed row
(`Type=title`, `Text=`the title, other cells blank), so both JSON and TSV carry it with no special syntax
and it reads fine in a spreadsheet. On import a `title` row sets the target document's title and creates no
block; if absent, the target keeps its existing title. First `title` row wins; extras are ignored (loose).

Rules (documented in the codec and a lang/handbook blurb):
- **Fixed columns; fat `Special` cell for extensibility.** New kinds do **not** add columns — they define
  how they pack their fields into the comma-separated `Special` payload (Map's `x,y,z,icon,color` is the
  worked example). This keeps the table narrow and stable and matches the loose-scratchpad tenet. Import
  still **ignores unknown trailing columns** and **defaults missing ones** so a future extra column (should
  one ever be added) and old exports both load.
- **Row position is the sequence.** There is no Order column — a block's position in the document is its row
  position in the table (after the header). A user reorders by moving rows in the spreadsheet. (An earlier
  authoritative Order column was dropped: it was redundant with row position and its `3-1` outline form was
  auto-coerced to a date/number by spreadsheets.)
- **`Special` sub-field parsing is per-kind and positional.** A kind splits its `Special` on commas and
  reads fields by position; missing/blank trailing sub-fields default. Item/link codes contain no commas,
  and coords/icon/color names are comma-free, so a plain comma split is safe.

### D3 — Escaping and injection safety (`ScribeTsvSafe` helper in Core)
- **Structural escaping.** A field containing a tab, CR, LF, or a leading/trailing space is wrapped in
  double quotes with internal quotes doubled (RFC-4180 style, which Excel/Sheets honor even for TSV).
  Newlines inside a quoted field survive the round-trip; unquoted fields never contain a tab/newline.
- **Formula-injection neutralization (export).** Any field whose first character is `= + - @`, tab, or CR
  is prefixed with a single `'` (apostrophe) — the standard spreadsheet defang — so a pasted cell can never
  execute as a formula. On import the leading defang apostrophe is stripped so the text round-trips.
- **In-game literal.** Imported `Text` is stored verbatim and rendered through the same task-text path as
  typed text (which does not interpret VS rich-text `<...>` tags), so imported content cannot inject markup,
  hotkeys, or handbook links. The codec still hard-caps text length to the existing document-codec clip.

### D4 — Import auto-detects format
`Import` reads the clipboard once. If the trimmed payload starts with `{`, it is parsed as JSON; otherwise
as TSV. A parse failure surfaces a toast ("Clipboard is not a valid Scribe export") and does nothing —
no partial apply.

### D5 — Best-effort TSV reconstruction (Mod-side validation)
Core's TSV deserializer produces a `ScribeDocument` of blocks with the parsed kind + reference strings. The
**Mod** layer then validates item-bound kinds against the running game: a `tracker`/`link` row whose
`Special` code resolves to a real collectible/handbook target stays typed; an unresolved/blank code degrades
the block to a plain `Task` carrying its `Text`. Degradations are counted and reported in the result toast
("Imported 12 tasks; 2 unknown items imported as plain tasks"). JSON import does the same validation so a
hand-broken JSON binding degrades gracefully rather than dangling. One bad row is never fatal.

### D6 — Server-authoritative import, fresh TaskIds, never pinned
Import sends a `ScribeTranscribeImportMessage` (mirrors `ScribeTranscribeCopyMessage`): the serialized
payload (or the already-parsed document as the copy path serializes it), target slot identity, `Append`
flag, and `AllowOverwrite`. The server deserializes, **mints a fresh `TaskId` per block** (and a fresh
`DocId` on overwrite), runs the mode-aware capacity check (append: target+incoming; overwrite: incoming),
and writes via the existing document store + `MarkDirty`/re-sync. Because every id is fresh and pins live in
a separate `(DocId, TaskId)` store, no import can create or resurrect a pin. Export needs no packet — it
reads the client's already-synced document.

### D7 — GUI: replace the placeholder with live controls, reuse existing affordances
`BuildImportExportSection` becomes live: **Copy as JSON**, **Copy as TSV**, **Import** buttons (Caudex, same
theme as the copy button; labels in the button font, any helper caption in the player's body font per the
established rule). The placeholder slot becomes the real source/target slot bound to a `SlotController` like
the copy slots. Export reads the slotted item's document → codec → `SetClipboard`, then `PlayStamp(slot,
"EXPORTED")`. Import reads clipboard → detect → validate → send message → on ack `PlayStamp(slot, "IMPORTED")`.
The copy-mode Overwrite/Append radio governs import too (single shared `copyMode`).

### D8 — Reuse, don't fork, the codec-migration discipline
Both new codecs get an entry in `docs/CODEC-MIGRATION.md` and follow the versioned forward/backward window
pattern, so a future field addition is a version bump, not a break. For TSV the discipline is expressed as
**fixed columns + additive `Special` payload / `Type` tokens**: a new kind adds a token and a `Special`
encoding, never a column, so old and new exports stay mutually loadable.

## Risks / Trade-offs

- **TSV is lossy and users can hand-break it.** Accepted and mitigated by best-effort reconstruction (D5),
  tolerant parsing (D2), and a clear result toast — the loose-scratchpad tenet means a broken row degrades,
  it never blocks. JSON remains the lossless lane for anyone who needs it.
- **Clipboard is a single shared channel.** Export overwrites whatever the player had copied; Import trusts
  arbitrary clipboard text. Mitigated by format auto-detect + validation + the length/size caps; a
  non-Scribe payload simply fails to import.
- **Formula-injection defang alters exported text cosmetically** (a leading apostrophe on `=…` cells). This
  is the accepted, standard trade-off; the apostrophe is stripped on import so the round-trip is clean, and
  most task text never triggers it.
- **`System.Text.Json` in Core** is BCL, but Core has been kept dependency-lean; a hand-rolled writer is the
  fallback if the team prefers zero framework-JSON surface. Flagged, not blocking.
- **Fat `Special` cell is less atomically editable.** A Map's color sits inside a comma list, not its own
  spreadsheet cell, so a user edits it by hand within the payload. Accepted: it keeps the table narrow and
  stable (fixed 6 columns forever) and matches the loose ethos; the common cases (tracker item + count) each
  already have their own legible column. The codec splits `Special` per-kind and defaults missing sub-fields.
- **`Depth` is a bare integer with no parent links.** By design (looseness tenet) — a mis-set depth just
  looks odd, it can't corrupt anything; the codec clamps it to a sane range.
- **Server trusts the client's parsed document** — same trust model as the existing copy path (the codec's
  size/count caps are the guard); no new attack surface beyond what copy already accepts.
