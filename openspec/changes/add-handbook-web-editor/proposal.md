## Why

A single in-game handbook entry is scattered across up to three files: a registration JSON
that lists its sections and order (`itemtypes/*.json`, `blocktypes/*.json`, or
`config/handbook/*.json`), plus one title value and one body value **per section** in
`assets/scribe/lang/en.json`. Editing one entry means hopping between files, matching lang
keys by eye, and hand-authoring VS's HTML-ish markup (`<strong>`, `<br>`,
`<a href="handbook://…">`) with no preview of how the sections stack up. Many entries are too
verbose, and trimming them under those conditions is slow and error-prone. A focused local
web editor that shows a whole entry assembled from its scattered parts — and writes edits back
to the real files — removes that friction and gives direct, visual control over the handbook.

## What Changes

- **New standalone tool `~/claude/handbook-scribe-editor/`** — a local web app (static page +
  a small local server, matching the `vs-playtest-checklist` "browser UI over a local server"
  pattern) that operates on this mod's `src/Mod/assets/scribe/` files. Its own git repo /
  project directory; not part of the mod build. Ships with a double-click Dock launcher
  matching the author's `~/Launchers/` convention.
- **Read + assemble**: the server discovers every handbook entry by scanning the two
  registration sources (block/item `extraSections` and `config/handbook/*.json` pages),
  resolves each section's `title`/`text` lang keys against `en.json`, and serves the fully
  assembled entry (ordered sections, with their prose) to the browser.
- **Three-column layout**: a collapsible **library** of all handbook entries (guide articles
  and item/block entries) on the left, the **editor** in the middle, and a live **preview**
  of the in-game appearance on the right.
- **Navigable library**: every discovered entry is listed and navigable in the left column,
  grouped by kind/subject; selecting one opens it in the editor + preview.
- **Link generator**: from any entry in the library the author can copy a correct
  `handbook://` link to the clipboard (including the concrete variant code for variant items,
  e.g. `item-scribe:scribetablet-clay-red`) — no more hand-deriving link targets.
- **Editor**: a source editor for each section's VS markup with formatting-helper buttons
  that insert `<strong>`, `<br>`, and `<a href="handbook://…">` at the caret. What the author
  sees is exactly what gets saved (no WYSIWYG round-trip conversion).
- **Visualize**: the right column renders the assembled entry as a high-fidelity preview —
  pinned to the game's real handbook text-column width (500 logical units) with approximate
  handbook typography and link styling — so the author can judge how much screen real estate
  an entry occupies and see `handbook://` links rendered as resolvable/unresolvable.
- **Before/after snapshot**: on opening an entry the tool captures a session-original
  baseline (kept across saves) and provides a toggle that flips both the editor and preview
  columns between the baseline and the current state, so the author can see progress and edit
  with confidence.
- **Edit prose**: edit each section's title and body text in place.
- **Edit structure**: add, remove, and reorder sections within an entry, and edit/insert
  `handbook://` cross-link targets, with validation that link targets resolve to a known
  page.
- **Save back**: writing an entry persists changes to the correct files — prose to `en.json`
  (preserving key naming and unrelated keys/ordering), section list/order to the owning
  registration JSON — with the tool creating new lang keys for new sections and pruning keys
  it removes. Relaxed-JSON registration files keep their existing formatting style.
- **Safety**: the tool operates only within the configured mod-assets path, backs up (or
  relies on git for) files before writing, and validates JSON before saving so a bad edit
  can't corrupt `en.json`.
- Manual editor only — no built-in AI trimming. (Verbosity trimming is done conversationally
  in Claude Code.)

## Capabilities

### New Capabilities
- `handbook-entry-model`: a normalized, tool-facing representation of a handbook entry —
  its source registration file, its ordered sections, and each section's resolved
  title/body and lang keys — plus the rules for assembling this model by reading the mod
  asset files and for writing edits back to those files without disturbing unrelated
  content.
- `handbook-editor-app`: the local web editor itself — how entries are listed, displayed
  (assembled sections + markup preview), edited (prose, section add/remove/reorder, link
  targets), validated, and saved through the local server.

### Modified Capabilities
<!-- None. This tool is a standalone sibling project that reads/writes the mod's asset
     files externally; it introduces no requirement changes to the Scribe mod itself. -->

## Impact

- **New code**: a standalone project at `~/claude/handbook-scribe-editor/` (local Node
  server + static browser app). No dependency added to the Scribe mod; the mod build,
  `Core`/`Mod` split, and CI are untouched.
- **Files the tool reads/writes at runtime** (in this repo, not modified by this change
  itself): `src/Mod/assets/scribe/lang/en.json`, `src/Mod/assets/scribe/itemtypes/*.json`,
  `src/Mod/assets/scribe/blocktypes/*.json`, `src/Mod/assets/scribe/config/handbook/*.json`.
- **Root CLAUDE.md**: add a one-line entry describing the new sibling project under the
  container-directory listing.
- **New launcher artifacts**: a `.command` launcher in the tool dir plus a Finder alias in
  `~/Launchers/` (for the Dock), matching the author's existing local-tool launch pattern.
- **No game/runtime impact**: purely an authoring aid; the mod's shipped assets are
  unchanged except by edits the author deliberately saves through the tool.
- **Runtime**: server runtime (Python vs. Node) is an implementation detail; the design
  defaults to Python to match the existing `vs-playtest-checklist` tool, with Node a drop-in
  alternative. The double-click-to-launch experience is the binding contract, not the
  runtime.
