## Context

Handbook content lives in two forms: (1) standalone guide articles registered by numbered JSON page
definitions under `src/Mod/assets/scribe/config/handbook/` (`00-getting-started` … `04-transcribe`),
each pointing at a `craftinginfo-scribe-*-title`/`-text` lang key; and (2) per-object `extraSections`
arrays on each block/item type JSON, each pointing at a `handbook-<object>-<section>-title`/`-text`
lang key. All prose is HTML-ish rich text in `lang/en.json` with `<a href="handbook://…">` cross-links.

Today the shared tab/view tour is duplicated: `handbook-scribelectern-views-text` and
`handbook-scriptorium-views-text` are near-identical, and `handbook-scribenotebook-editor-text` is a
trimmed copy. The Chalkboard and the Notebook already model the target pattern (uniqueness-first + link
out); the Chalkboard is short specifically because it links to the Lectern and the shared HUD reference
instead of re-explaining them.

## Goals / Non-Goals

- **Goal:** One canonical home for the shared tab/view tour; per-object entries reduced to their unique
  delta plus a link.
- **Goal:** No loss of genuinely object-unique information.
- **Non-Goal:** No code change, no behavior change, no new capability, no fork.
- **Non-Goal:** Not rewriting the deep editor-reference mechanics article (`01-editor-reference`); the new
  article links to it, it stays as-is.
- **Non-Goal:** Full `pt-br` translation — mirror the key set; VS falls back to English for missing keys.

## Decisions

### A new standalone "Tabs & Views" article, not an extension of editor-reference

`craftinginfo-scribe-editor-reference` is about keyboard mechanics and text editing, not the tab tour, so
folding the tour into it would muddy both. Add a new numbered page `05-views.json` with
`pageCode: "craftinginfo-scribe-views"` and lang keys `craftinginfo-scribe-views-title` / `-text`,
following the exact shape of `00-getting-started.json`. The article describes Read / Task Editor / Pinned
/ Guest Book / History once and calls out per-surface availability, then links to editor-reference for
mechanics. This is additive registration — no patch or code change (the guide-page loader already
enumerates the folder).

### Per-object entries: trim + link, keep the delta

Rewrite the duplicated sections to a one-line "which tabs this surface has" + a link to
`craftinginfo-scribe-views`, mirroring `handbook-scribenotebook-editor-text`'s existing link-out.
Preserve object-unique sections verbatim where they already describe a distinguishing feature
(Scriptorium Transcribe/Import-Export, Lectern/Chalkboard Guest Book, Notebook History, Clockmaker timer,
Tablet wax/hard/fired states). Where a section mixes shared + unique prose, split it: the shared half
becomes a link, the unique half stays.

### Editing instrument

The edits are surgical, style-preserving changes to `lang/en.json` and the `extraSections` JSON — exactly
what the sibling `handbook-scribe-editor` tool is built for (it preserves the load-bearing `scribe:` prefix
and per-file style on write-back). Prefer it over hand-editing to avoid the tree-corruption class of bug
that tool was written to prevent. Hand edits are acceptable for the small `extraSections`/page-def JSON.

## Risks / Trade-offs

- **Broken cross-links.** New/removed sections change the set of `handbook://` targets. After editing,
  verify every `<a href="handbook://…">` in the touched entries resolves (the new `craftinginfo-scribe-views`
  page, the retained per-object anchors).
- **Losing a unique detail while trimming.** Mitigate by diffing each removed sentence against the shared
  article — anything not covered by the shared article must be kept in the object entry.
- **`*ByType` deep-merge trap** (see project memory): `extraSections` is an array, and VS
  `attributesByType`/`handbookByType` CONCATENATES arrays rather than replacing. If any touched entry uses
  a `ByType` map, edit through it correctly (a `"*"` catch-all) rather than adding a section that silently
  doubles. Verify no entry ends up with a duplicated section after the edit.

## Migration Plan

None — static content only; no persisted data, no format version.
