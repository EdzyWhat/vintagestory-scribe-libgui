## Why

The Chalkboard's handbook entry is short and pleasant to read: a brief "what makes this unique"
paragraph plus a link out to the shared reference for everything it has in common with the Lectern. The
older per-object entries are the opposite — exhaustive, and they duplicate the same shared material.
Concretely, `handbook-scribelectern-views-text` (~1000 chars) and `handbook-scriptorium-views-text`
(~1450 chars) are near-verbatim copies of the same Read / Task Editor / Pinned / Guest Book tab tour,
and the Notebook's own "editor" section repeats a trimmed version of it. That prose is copied N times,
drifts out of sync as tabs change, and buries what actually distinguishes each surface.

The mod already has the right pattern in two places: (1) five standalone guide articles under
`assets/scribe/config/handbook/` (`00-getting-started` … `04-transcribe`), and (2) the Notebook's
editor section, which describes its tabs briefly and links to `craftinginfo-scribe-editor-reference`
for the mechanics. What's missing is a single canonical home for the *shared tab/view tour* itself
(the existing editor-reference article covers keyboard mechanics, not the tab tour), and the
discipline of making each per-object entry uniqueness-first and linking out to it.

## What Changes

- ADD one standalone shared guide article — "Scribe Mod: Tabs & Views" (a new numbered file under
  `assets/scribe/config/handbook/`, e.g. `05-views.json`, plus its lang keys) — that describes the
  shared tabs ONCE: Read, Task Editor, Pinned, Guest Book, History, and which surfaces carry which
  (Guest Book on placed surfaces; History on portable items; Transcribe / Import-Export on the
  Scriptorium). It links to `craftinginfo-scribe-editor-reference` for the deeper editor mechanics.
- REDUCE each per-object handbook entry (Lectern, Notebook, Scriptorium, Chalkboard, Tablet, Clockmaker's
  Notebook) to what is UNIQUE about that object, replacing duplicated shared tab/view prose with a link
  to the new Tabs & Views article — the same link-out pattern the Chalkboard and Notebook already use.
  The Chalkboard entry is the exemplar for tone and length.
- The redundant `*-views` / `*-editor` sections collapse to a one-line "which tabs this surface has +
  what's unique" plus the link; genuinely unique material (e.g. the Scriptorium's Transcribe, the
  Lectern/Chalkboard Guest Book, the Notebook's History, the Clockmaker timer) stays, framed as the
  delta from the shared baseline.
- Editorial only: no code and no behavior change. Edits are confined to `assets/scribe/lang/en.json`
  (guide text) and the `extraSections` arrays in the block/item type JSONs (which sections each entry
  shows and where it links). The sibling `handbook-scribe-editor` tool is the intended instrument for
  the style-preserving lang/JSON write-back.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `handbook-scribe-entry`: adds a requirement for a standalone shared "Tabs & Views" explainer article
  that the per-object entries link to.
- `item-handbook-entries`: adds a requirement that per-object entries describe only what is unique to
  the object and link out to shared explainer articles instead of duplicating shared tab/view prose.

## Impact

- Content: `src/Mod/assets/scribe/lang/en.json` (new `craftinginfo-scribe-views-*` keys; trim
  `handbook-scribelectern-views-*`, `handbook-scriptorium-views-*`, `handbook-scribenotebook-editor-*`,
  and any other duplicated per-object sections). `pt-br.json` mirrors the key set (translation deferred /
  falls back to English by VS convention).
- Assets: a new `src/Mod/assets/scribe/config/handbook/05-views.json` page definition; edits to the
  `extraSections` arrays in `blocktypes/{lectern,scriptorium,chalkboard}.json` and
  `itemtypes/{scribenotebook,scribeclockmakernotebook,scribetablet}.json`.
- No `src/Core/` change, no code change, no new dependency, no fork of `gui`.
- Save/persistence: none (handbook is static content).
- The Tablet's material-state sections (wax/hard/fired) are unique to the Tablet and are NOT collapsed;
  only their shared tab/view prose (if any) links out.
