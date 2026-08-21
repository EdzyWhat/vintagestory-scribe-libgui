## 1. Author the shared "Tabs & Views" article

- [x] 1.1 Add `src/Mod/assets/scribe/config/handbook/05-views.json` mirroring `00-getting-started.json`:
      `pageCode: "craftinginfo-scribe-views"`, `title: "scribe:craftinginfo-scribe-views-title"`,
      `text: "scribe:craftinginfo-scribe-views-text"`. — Done; byte-for-byte format-identical to the sibling
      relaxed-JSON page-defs (auto-enumerated by VS's handbook loader, no code change).
- [x] 1.2 Add the lang keys `craftinginfo-scribe-views-title` (e.g. "Scribe Mod: Tabs & Views") and
      `craftinginfo-scribe-views-text` to `src/Mod/assets/scribe/lang/en.json`. Describe the shared tabs
      ONCE — Read, Task Editor, Pinned, Guest Book, History — and note per-surface availability (Guest
      Book on placed surfaces; History on portable items; Transcribe / Import-Export on the Scriptorium).
      End with a link to `handbook://craftinginfo-scribe-editor-reference` for deeper mechanics. Draft the
      body by consolidating the current `handbook-scribelectern-views-text` / `handbook-scriptorium-views-text`
      prose (they are near-identical) so nothing shared is lost. — Done; also covers the Timer (Clockmaker's
      Notebook) and links to the Transcribe article. Added a discoverability link to it from getting-started.
- [x] 1.3 Add matching (English-fallback) keys to `pt-br.json` so the key set stays parallel. — Done; English
      body per the "translation deferred / falls back to English" convention.

## 2. Reduce the duplicated per-object sections to link-outs

- [x] 2.1 Lectern (`handbook-scribelectern-views-text`): replace the full tab tour with a one-line "the
      Lectern has these tabs" + a link to `handbook://craftinginfo-scribe-views`; keep only what is unique
      to the Lectern (its Guest Book framing, placed-surface behavior). — Done.
- [x] 2.2 Scriptorium (`handbook-scriptorium-views-text`): same trim + link; KEEP the Transcribe and
      Import-Export description (unique), framed as the Scriptorium's delta over the shared surfaces. — Done;
      Transcribe kept as a brief delta + link to the dedicated Transcribe article.
- [x] 2.3 Notebook (`handbook-scribenotebook-editor-text`): already links to editor-reference — repoint /
      add a link to the new `craftinginfo-scribe-views` article and drop the inline tab list it still
      restates; keep the History pointer (unique to the Notebook). — Done; kept both the Tabs & Views and
      editor-reference links; the dedicated History section (`-history-text`) is untouched.
- [x] 2.4 Confirm the Chalkboard entry (`handbook-chalkboard-about-text`) already follows the pattern
      (uniqueness-first + link to the Lectern / HUD ref); adjust only to also link the new Tabs & Views
      article if it currently restates any shared tab prose. It is the reference for tone/length. — Confirmed:
      it does NOT restate any tab tour (links to the Lectern, editor-reference, getting-started). No change
      made; it reaches Tabs & Views indirectly via the Lectern's now-trimmed views section.
- [x] 2.5 Tablet: leave the wax/hard/fired material-state sections intact (unique). Only if a Tablet
      section restates the shared tab tour, replace that portion with the link. — Confirmed: no Tablet section
      restates the tab tour (about/states/hard/fired/wax are all Tablet-unique). Left intact.
- [x] 2.6 Sweep the remaining per-object entries for any other duplicated shared prose (Clockmaker's
      Notebook, mod-wide content) and apply the same trim + link; keep the Clockmaker timer/craft sections
      (unique). — Swept: Clockmaker about/timer/craft are all unique (no tab tour). Getting-started's brief
      "views" overview is intentional (not a full duplicate); left as-is but given a link to Tabs & Views.

## 3. Wire the links + verify content integrity

- [x] 3.1 Update the `extraSections` arrays in the touched block/item type JSONs
      (`blocktypes/{lectern,scriptorium,chalkboard}.json`, `itemtypes/{scribenotebook,scribeclockmakernotebook,
      scribetablet}.json`) to reflect the trimmed section set. If any uses a `*ByType` map, edit through the
      `"*"` catch-all — arrays CONCATENATE under `handbookByType`, so a careless add doubles a section. — No
      edits needed: the trim keeps every section KEY (about / views|editor / hud-ref), only the section TEXT
      shrank and now links out. Section set is unchanged, so no `extraSections` array touched and no
      `*ByType` doubling risk (the Tablet's `handbookByType` was not modified).
- [x] 3.2 Verify every `<a href="handbook://…">` in the touched entries resolves: the new
      `craftinginfo-scribe-views` page and all retained per-object anchors. No dangling links. — Verified: all
      targets exist — `craftinginfo-scribe-views` (new 05-views), `craftinginfo-scribe-transcribe` (04),
      `craftinginfo-scribe-editor-reference` (01), `block-scribe:scribelectern`, `block-scribe:scriptorium`,
      `item-scribe:scribenotebook`, `item-scribe:scribeclockmakernotebook`.
- [x] 3.3 Diff each removed sentence against the new shared article — anything not covered there must be
      preserved in the object entry (no lost unique detail). — Done: all shared tab prose (Read/Task Editor/
      Pinned/Guest Book/History/Transcribe/Settings/Timer) is preserved once in the new article; each object
      keeps its unique framing (Lectern editor-lock, Scriptorium Transcribe delta, Notebook private History).
- [x] 3.4 Prefer the sibling `handbook-scribe-editor` tool for the `lang/en.json` write-back (style- and
      prefix-preserving); hand-edit the small page-def / `extraSections` JSON. — Hand-edited surgically with
      exact-match string replacement (preserving tab indentation, key order, and the `scribe:` prefix); both
      lang files re-validated as strict JSON afterward.

## 4. In-game verification

- [x] 4.1 `bash build/restage.sh Debug` — done as one combined restage after all four changes merged to main
      (client confirmed not running); 137 files staged, build 0/0.
- [x] 4.2 In-game: open the Handbook → the new "Scribe Mod: Tabs & Views" article is present and describes
      the shared tabs once, with a working link to the editor-reference article.
  - Confirmed 2026-08-21 (playtest verdict on file in TESTING.md).
- [x] 4.3 In-game: open the Lectern, Scriptorium, and Notebook entries → each is noticeably shorter, reads
      uniqueness-first, and links to the Tabs & Views article instead of restating the tour. No section is
      duplicated (no `*ByType` doubling).
  - Confirmed 2026-08-21 (playtest verdict on file in TESTING.md).
- [x] 4.4 In-game: confirm object-unique content still reads correctly — Scriptorium Transcribe, Guest
      Book, Notebook History, Clockmaker timer, Tablet material states — and every cross-link in the touched
      entries resolves.
  - Confirmed 2026-08-21 (playtest verdict on file in TESTING.md).
