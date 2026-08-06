## 1. Project scaffolding

- [x] 1.1 Create `~/claude/handbook-scribe-editor/` as its own git repo (`git init`), with a `.gitignore` that ignores `node_modules/` and any local `config.json`.
- [x] 1.2 Add a `README.md` describing what the tool does, how to start it, and the configured mod-assets root (default: sibling path `../vintagestory-scribe-libgui/src/Mod/assets/scribe`).
- [x] 1.3 Add config resolution for the mod-assets root (env var or `config.json`), with the sibling-path default, and a startup check that the root exists and contains `lang/en.json`.
- [x] 1.4 Add a `start.command` launcher that starts the server (localhost only by default) and opens the app URL in the browser, matching the `glyph-forge`/`vs-playtest-checklist` `.command` pattern.
- [x] 1.5 Document in the README how to make a Finder alias of `start.command` and place it in `~/Launchers/` for the Dock; verify the launcher works by double-click.
- [x] 1.6 Add the one-line project entry to `~/claude/CLAUDE.md` under the container-directory listing.

## 2. Entry model — discovery & assembly (server side)

- [x] 2.1 Scan `itemtypes/*.json` and `blocktypes/*.json` for `attributes.handbook.extraSections`; produce one entry per file with its ordered sections and owning file path.
- [x] 2.2 Scan `config/handbook/*.json` for `{ pageCode, title, text }`; produce one single-section entry per page with its owning file path.
- [x] 2.3 Ignore scanned type files that declare no handbook data without erroring.
- [x] 2.4 Resolve each section's `title`/`text` lang key against `en.json` (stripping the `scribe:` prefix); pair each section with its resolved strings.
- [x] 2.5 Flag sections whose referenced lang key is missing from `en.json` as unresolved (do not drop, do not crash).
- [x] 2.6 Parse `handbook://` links in section bodies and classify each target resolvable/unresolvable using VS's page-code scheme (`item-`/`block-<fullcode>` with concrete variant codes; `pageCode` for guide pages), verified against discovered entries/collectibles.

## 3. Entry model — write-back (server side)

- [x] 3.1 Implement key-preserving `en.json` writes: update only targeted key values, preserving existing key order and untouched keys.
- [x] 3.2 Implement relaxed-JSON-preserving edits to registration files: locate and splice/reorder the `extraSections` array (or page fields) without reserializing/reflowing the whole file, matching the file's existing quoting style.
- [x] 3.3 On add-section: create uniquely-named `title`/`text` keys following the `<entry>-<section>-title`/`-text` convention and insert the referencing section object at the chosen position.
- [x] 3.4 On remove-section: drop the section from `extraSections` and prune its now-orphaned lang keys from `en.json`.
- [x] 3.5 Add a pre-write validation gate: parse the resulting `en.json` and abort the save (leaving the file unchanged) if it would be invalid.
- [x] 3.6 Confine all file reads/writes to the configured assets root; reject any resolved path that escapes it.
- [x] 3.7 Round-trip test: for every current entry, read → write-back-unchanged → confirm zero/near-zero git diff.

## 4. HTTP API

- [x] 4.1 `GET /api/entries` — return the discovered entry list (labels/grouping + ids).
- [x] 4.2 `GET /api/entry/:id` — return one assembled entry (ordered sections, resolved prose, per-link resolvability, unresolved-key flags).
- [x] 4.3 `POST /api/entry/:id` — accept an edited entry and persist it via the write-back layer; return success or a structured failure (invalid JSON, path refused, etc.).
- [x] 4.4 Serve the static browser client from the server.

## 5. Browser client — three-column workspace & view

- [x] 5.1 Build the three-column layout (library | editor | preview) with a collapsible library column that yields its space to editor + preview when collapsed.
- [x] 5.2 Render the library: all entries grouped/labeled by kind and owning subject (guide articles vs. item/block entries); selecting one loads it into editor + preview.
- [x] 5.3 Per-entry "copy handbook:// link" action in the library that copies the canonical page code (concrete variant code for variant items) to the clipboard.
- [x] 5.4 Render the selected entry's sections in order in the editor, each with editable title + body source.
- [x] 5.5 Render a high-fidelity markup preview pinned to the game's text-column width (500 logical units) with a visible width indicator: `<strong>` bold, `<br>` line breaks, `handbook://` links styled by resolvability; show unknown tags literally.
- [x] 5.6 Calibrate the preview's font size/weight/line-height against an in-game handbook screenshot so line wrapping and vertical extent track the game; make the entry's total real-estate (vertical length) visually apparent.
- [x] 5.7 Visibly flag unresolvable links and unresolved (missing-key) sections in the preview.

## 6. Browser client — edit, snapshot & save

- [x] 6.1 Source editing: edit section title/body as raw VS markup (what's shown is what's saved); live preview updates before save.
- [x] 6.2 Formatting-helper actions that insert `<strong>…</strong>`, `<br>`, and an `<a href="handbook://…">…</a>` scaffold at the caret / around the selection.
- [x] 6.3 Structure editing: add section, remove section, reorder sections within the entry.
- [x] 6.4 Link editing: edit/insert a `handbook://` target with a pre-save warning when it resolves to no known page.
- [x] 6.5 Session-original snapshot: capture the entry's baseline on open, retain it across saves, and provide a before/after toggle that updates both the editor and preview columns.
- [x] 6.6 Explicit Save action that POSTs the current entry and reports success/failure without discarding in-progress edits on failure.

## 7. Verification

- [ ] 7.1 Manually edit one section's prose via the tool, save, and confirm the git diff touches only that key in `en.json`.
- [ ] 7.2 Add, reorder, and remove a section via the tool; confirm the owning registration file and `en.json` update correctly and the relaxed-JSON formatting of untouched entries is preserved.
- [ ] 7.3 Launch Vintage Story and confirm a tool-edited entry renders correctly in the in-game handbook (structure, prose, and working cross-links).
- [ ] 7.4 Compare the tool's preview against the in-game handbook for the same entry; confirm wrapping/line-count and apparent width match closely enough to trust real-estate judgments.
- [x] 7.5 Confirm an intentionally-broken save (would-be invalid JSON) is rejected and leaves files unchanged.
- [ ] 7.6 Confirm the `~/Launchers/` Dock alias launches the tool by double-click.
- [x] 7.7 Copy a variant item's link (e.g. the clay Tablet) and confirm it contains a concrete variant code and resolves in-game when pasted into another entry.
- [ ] 7.8 Make edits, save, then toggle before/after and confirm both editor and preview show the session-original baseline (not the just-saved state) and restore correctly.
