## Context

`ItemClockmakerNotebook.OnCreatedByCrafting` already runs server-side after the grid craft
and stamps a "Crafted" `HistoryEntry` onto the output stack. But it operates on a *fresh*
output: a vanilla `GridRecipe` builds the output stack from the recipe's `output` definition,
not from any input, so the source Notebook's `"scribeDocument"` (title/tasks/state, keyed by
`DocId`) and `"scribeHistory"` attributes never reach the Clockmaker's Notebook.

Both attributes are plain `ItemStack` byte attributes with codecs that already live in
`src/Core` and its adapters:
- `"scribeDocument"` ⇄ `ScribeDocumentAttributes.TryReadFrom` / `WriteTo` (wrapping
  `ScribeDocumentCodec`), the same path the Notebook and the Lectern break/place flow use.
- `"scribeHistory"` ⇄ `HistoryStore.Deserialize` / `Serialize`, already used two lines later
  in this same method.

The recipe (`recipes/grid/scribeclockmakernotebook.json`) has exactly one Notebook ingredient
(`"B": scribe:scribenotebook`), so exactly one input slot carries a source document. The fix
is entirely within this one server-side override; `src/Core` is untouched.

## Goals / Non-Goals

**Goals:**
- Copy the source Notebook input's `"scribeDocument"` onto the crafted output, preserving
  `DocId`, title, tasks, and task state.
- Copy the source Notebook's `"scribeHistory"` onto the output *before* the existing "Crafted"
  entry is appended, so the entry lands on the carried-over chronicle.
- Preserve today's behavior when no source document/history is present (fresh `DocId`,
  crafted-only history).

**Non-Goals:**
- No change to the recipe JSON, to `src/Core`, or to the network/persistence contract (reuses
  the existing attribute keys and codecs).
- No document merging: this is a straight copy from the single source Notebook, not a
  reconciliation of multiple inputs.
- No client-side or GUI change — the Clockmaker's dialog already reads whatever document the
  stack holds.

## Decisions

**Locate the source by attribute, not by item type.** Scan `allInputSlots` for the first
non-empty slot whose `Itemstack` has a `"scribeDocument"` attribute (equivalently, the
`ItemScribeNotebook`/`ItemClockmakerNotebook` input). Keying on the presence of the document
attribute is robust to recipe changes and avoids a hard assumption about grid position.
_Alternative considered:_ hard-code the "B" slot index — rejected as brittle against any future
recipe edit.

**Copy document + history first, then stamp "Crafted".** Reorder the method so the two copies
happen at the top (right after the server-side guard), and the existing "Crafted" append runs
*after* — reading back the just-copied `"scribeHistory"` so the new entry is added to the
carried-over chronicle. This keeps the DocId-preserving copy and the history stamp in one
coherent server-side block.

**Copy raw bytes, don't round-trip through Core.** For the document, copy the source stack's
`"scribeDocument"` bytes straight onto the output (raw attribute copy) rather than
deserialize→reserialize. It preserves `DocId` exactly, is codec-version-agnostic, and avoids
reconstructing a `ScribeDocument` just to write it back unchanged. History already round-trips
through `HistoryStore` because it must append an entry.

**Fresh-fallback stays implicit.** If no source slot has a `"scribeDocument"`, skip the
document copy — the output keeps the fresh stack the recipe produced, and `NotebookHost`
initializes an empty document with a new `DocId` on first open, exactly as today. Likewise an
absent `"scribeHistory"` deserializes to an empty store, yielding a crafted-only history.

## Risks / Trade-offs

- **[Multiple document-bearing inputs]** A future recipe could include more than one
  document-carrying input. → The scan takes the *first* match and this is documented; today's
  single-Notebook recipe makes it unambiguous, and multi-input merging is an explicit
  Non-Goal.
- **[DocId collision if source persists]** Preserving `DocId` means the crafted Clockmaker's
  Notebook shares the source's id — but the source Notebook is *consumed* by the craft, so no
  two live stacks share the id. → No mitigation needed; if a future recipe stopped consuming
  the input, that recipe would need its own handling.
- **[Attribute key drift]** Hard-coding `"scribeDocument"`/`"scribeHistory"` string keys here
  duplicates the constants used elsewhere. → Reuse the same string constants the existing code
  already references (the method already uses `"scribeHistory"`), so drift is caught by the one
  place these keys are defined.

## Migration Plan

Pure additive server-side logic; no data migration. Existing Clockmaker's Notebooks already in
worlds are unaffected (they keep whatever document they have). Rollback is reverting the single
method. Verified by the Core suite (unchanged) plus in-game craft verification.

## Open Questions

None — the History-carryover choice (carry over, then append the Crafted entry) was confirmed
with the user.
