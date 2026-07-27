# Implementation specs (roadmap exploration)

Architect-level implementation specs derived from `ROADMAP.md` (exploration pass,
2026-07-21). Each file is a *design/exploration* document — NOT an OpenSpec change and NOT
implemented code. When a tier or feature is picked up, its spec here becomes the input to a
real `openspec-propose`.

Each spec follows this structure:

- **Summary** — one paragraph: what it is, why, which roadmap item(s) it merges.
- **VS API hooks** — the concrete Vintage Story API surface (types, methods, events,
  block/item behaviors, GUI elements, network channels) the feature needs. Cite where each
  was confirmed (VSAPI-NOTES.md, wiki, anegostudios source, or a decompile finding).
- **C# data structures** — the Core models (game-agnostic; NO VS API) and the Mod-side
  adapters/packets, with field-level sketches. Respect the `src/Core` vs `src/Mod` split.
- **Implementation spec** — the step-by-step approach: block/item defs, GUI composition,
  persistence (Sign pattern), sync, and the ordering of work.
- **Dependencies & sequencing** — what must land first (other tiers, the row-list rework,
  external mods), and where this sits in the staged plan.
- **Open questions** — decisions deferred or needing playtest/user input.

Guardrails these specs must respect (from CLAUDE.md):
- `src/Core/` MUST NOT reference the Vintage Story API.
- Vanilla `VintagestoryAPI` only; ConfigLib is the sole optional soft dependency. No new
  hard mod dependencies without a flagged decision.
- Persistence/sync follows the vanilla Sign block pattern.

## Shared Core-model conventions (cross-spec, binding)

The specs above were written **independently and in parallel** — no spec saw the others.
A coherence review (2026-07-21) found that their collisions all cluster on the same shared
`src/Core/` surface: several specs each extend the codec, the block, or the access model in
mutually-incompatible ways. These conventions reconcile those overlaps. **A spec here must
follow these before it becomes an `openspec-propose`; a proposal that contradicts one of
them is a bug, not a fresh decision.**

### 1. One codec version line — not two "v4"s

`ScribeDocumentCodec.Version` is a single global counter. Both `v6-bulletin-board.md`
and `chronicle-and-integrations.md` independently claim "bump 3 → 4" with **different**
appended fields. Only one feature set can be v4; the next is v5, and its fields append
*after* the earlier version's, never interleaved (append-only codec discipline). When
either lands, record the exact field order here so the next migration extends it rather
than colliding. **Whichever migration lands first also owns the version-aware *read*
change** (today the codec hard-rejects any version ≠ current); write that read to tolerate
*all* planned future fields, not just its own. Only `v6` currently specs this migration —
`chronicle` assumes it silently.

**v4 has landed** (`add-document-task-identity`, 2026-07-24) — it, not v6/chronicle, was the
first migration to ship, so it owns the version-aware read (the codec now accepts v4 *and*
v3, rejecting older). The exact on-disk layout, which any v5 field appends *after*:

- Header: `[4 bytes magic "SCRB"][1 byte version=4][16 bytes DocId][int blockCount]`
- Per block: `[16 bytes TaskId][byte kind][bool done][int depth][bool hasAssignedToUid][string assignedToUid?][string text]`
- v4 vs v3: v3 had no DocId/TaskId and carried a per-block `bool pinned` after `depth`; v4
  dropped `pinned` (pinning moved to the per-player store) and added the two ids. The v3 read
  path generates fresh ids and surfaces the previously-pinned tasks' ids via the migration seam
  `TryDeserialize(bytes, out doc, out IReadOnlyList<Guid> legacyPinnedTaskIds)`.

So v6/chronicle now bump **3 → 4 → 5** (append their fields after v4's per-block `text`), and
they no longer own the read-migration — v4 already made the reader version-tolerant.

### 2. One timestamp/stamp representation — `ChronicleStamp`

`chronicle-and-integrations.md` defines `ChronicleStamp` (Year, DayOfYear, TotalHours,
season key, display string, optional X/Y/Z — all game-agnostic primitives).
`v6-bulletin-board.md` invents a bare `double? WrittenOnTotalDays` on the block for the
same purpose (guestbook "day X"). **Use `ChronicleStamp` (or an explicitly-agreed trimmed
subset) everywhere an entry is timestamped** — guestbook entries and chronicle entries are
the same "attributed, timestamped line." Do not add a second parallel timestamp field.

### 3. One immutability primitive — per-block `ReadOnly`

Two specs model "entries you can't edit" on different axes: `chronicle` uses a per-block
`ReadOnly` flag (mutators reject it); `v6` uses a document-level
`ScribeAccessPolicy.PublicAppendOnly`. These are the *same* need — a guestbook wants both
at once. **Per-block `ReadOnly` is the primary primitive** (it's the more general one); an
append-only document is expressed as "newly-appended blocks are born `ReadOnly`, and no
existing block may be mutated," not as a second independent enforcement path. Keep one
enforcement site in the Core mutators.

### 4. One access-policy enum, designed for a future group member

`v6` introduces `ScribeAccessPolicy { OwnerOnly, Public, PublicAppendOnly }` and correctly
notes it must leave room for `v4`'s faction/player-group sharing (a `SharedGroup` member).
This enum is a **shared Core type** that `v4` and `v6` both extend — they **cannot be
specced into proposals independently**. Whoever lands first defines the enum; the other
slots into it rather than inventing a parallel public/private notion.

### 5. `AssignedToUid` is a v4 field — don't assume it early

Both `v6` and `chronicle` list `ScribeBlock.AssignedToUid` among "existing" fields. It does
**not** exist yet — it's a `v4` (faction task-assignment) addition. Signature/attribution
(`AuthorUid`, "who wrote this") is a *different* concept from assignment ("who a task is
for"); keep them distinct. If `v6` or `chronicle` is built before `v4`, this field arrives
early and its owning spec must say so.

### 6. The `docId` document store gates the whole held-item half

`v2` introduces the `docId`-on-item + server-side document store; `v4`, `v5`, and all three
buildable `chronicle` features ("the player's current document") hard-depend on it — a
positional v1 lectern has no "current document." These specs are mutually consistent (all
assume one shared `"scribe:doc:"` store), but the "one shared store vs. separate stores,
duplicate vs. generalize the packets" fork is still open (ROADMAP Open decision #4). **It's
not a per-spec choice — it gates five specs at once** and is the single highest-leverage
decision to settle before any held-item tier is proposed.

### Don't re-derive shared VS API facts

Two facts are independently re-derived across specs and should be cited from one source
(`VSAPI-NOTES.md`), not re-established per spec: (a) the in-game calendar / `PrettyDate()`
→ "store numeric `TotalDays`/`TotalHours` in Core, format in Mod" convention (in `v6`,
`chronicle`, and `timers-and-alarms`); (b) server-side player identity at write time (`fromPlayer.PlayerUID`/
`PlayerName`, server-authoritative, client can't self-assert) — the same trust boundary in
both `v6` signatures and `chronicle` death entries.
