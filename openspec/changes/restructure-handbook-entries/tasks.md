## 1. Author the shared "Tabs & Views" article

- [ ] 1.1 Add `src/Mod/assets/scribe/config/handbook/05-views.json` mirroring `00-getting-started.json`:
      `pageCode: "craftinginfo-scribe-views"`, `title: "scribe:craftinginfo-scribe-views-title"`,
      `text: "scribe:craftinginfo-scribe-views-text"`.
- [ ] 1.2 Add the lang keys `craftinginfo-scribe-views-title` (e.g. "Scribe Mod: Tabs & Views") and
      `craftinginfo-scribe-views-text` to `src/Mod/assets/scribe/lang/en.json`. Describe the shared tabs
      ONCE — Read, Task Editor, Pinned, Guest Book, History — and note per-surface availability (Guest
      Book on placed surfaces; History on portable items; Transcribe / Import-Export on the Scriptorium).
      End with a link to `handbook://craftinginfo-scribe-editor-reference` for deeper mechanics. Draft the
      body by consolidating the current `handbook-scribelectern-views-text` / `handbook-scriptorium-views-text`
      prose (they are near-identical) so nothing shared is lost.
- [ ] 1.3 Add matching (English-fallback) keys to `pt-br.json` so the key set stays parallel.

## 2. Reduce the duplicated per-object sections to link-outs

- [ ] 2.1 Lectern (`handbook-scribelectern-views-text`): replace the full tab tour with a one-line "the
      Lectern has these tabs" + a link to `handbook://craftinginfo-scribe-views`; keep only what is unique
      to the Lectern (its Guest Book framing, placed-surface behavior).
- [ ] 2.2 Scriptorium (`handbook-scriptorium-views-text`): same trim + link; KEEP the Transcribe and
      Import-Export description (unique), framed as the Scriptorium's delta over the shared surfaces.
- [ ] 2.3 Notebook (`handbook-scribenotebook-editor-text`): already links to editor-reference — repoint /
      add a link to the new `craftinginfo-scribe-views` article and drop the inline tab list it still
      restates; keep the History pointer (unique to the Notebook).
- [ ] 2.4 Confirm the Chalkboard entry (`handbook-chalkboard-about-text`) already follows the pattern
      (uniqueness-first + link to the Lectern / HUD ref); adjust only to also link the new Tabs & Views
      article if it currently restates any shared tab prose. It is the reference for tone/length.
- [ ] 2.5 Tablet: leave the wax/hard/fired material-state sections intact (unique). Only if a Tablet
      section restates the shared tab tour, replace that portion with the link.
- [ ] 2.6 Sweep the remaining per-object entries for any other duplicated shared prose (Clockmaker's
      Notebook, mod-wide content) and apply the same trim + link; keep the Clockmaker timer/craft sections
      (unique).

## 3. Wire the links + verify content integrity

- [ ] 3.1 Update the `extraSections` arrays in the touched block/item type JSONs
      (`blocktypes/{lectern,scriptorium,chalkboard}.json`, `itemtypes/{scribenotebook,scribeclockmakernotebook,
      scribetablet}.json`) to reflect the trimmed section set. If any uses a `*ByType` map, edit through the
      `"*"` catch-all — arrays CONCATENATE under `handbookByType`, so a careless add doubles a section.
- [ ] 3.2 Verify every `<a href="handbook://…">` in the touched entries resolves: the new
      `craftinginfo-scribe-views` page and all retained per-object anchors. No dangling links.
- [ ] 3.3 Diff each removed sentence against the new shared article — anything not covered there must be
      preserved in the object entry (no lost unique detail).
- [ ] 3.4 Prefer the sibling `handbook-scribe-editor` tool for the `lang/en.json` write-back (style- and
      prefix-preserving); hand-edit the small page-def / `extraSections` JSON.

## 4. In-game verification

- [ ] 4.1 `bash build/restage.sh Debug` (client NOT running) so the asset changes are staged.
- [ ] 4.2 In-game: open the Handbook → the new "Scribe Mod: Tabs & Views" article is present and describes
      the shared tabs once, with a working link to the editor-reference article.
- [ ] 4.3 In-game: open the Lectern, Scriptorium, and Notebook entries → each is noticeably shorter, reads
      uniqueness-first, and links to the Tabs & Views article instead of restating the tour. No section is
      duplicated (no `*ByType` doubling).
- [ ] 4.4 In-game: confirm object-unique content still reads correctly — Scriptorium Transcribe, Guest
      Book, Notebook History, Clockmaker timer, Tablet material states — and every cross-link in the touched
      entries resolves.
