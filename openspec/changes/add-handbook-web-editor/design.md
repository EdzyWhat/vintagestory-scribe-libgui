## Context

Scribe's in-game handbook entries are authored across three file kinds under
`src/Mod/assets/scribe/`:

- **Item/block section-lists** — `itemtypes/*.json` and `blocktypes/*.json` declare
  `attributes.handbook.extraSections`, an ordered array of `{ title, text }` where both are
  `scribe:`-prefixed lang-key references. (5 files today: lectern, scribenotebook,
  scribeclockmakernotebook, scribetablet, clockmakerschematic.)
- **Standalone guide pages** — `config/handbook/*.json` declare `{ pageCode, title, text }`.
  (2 files today: getting-started, editor-reference.)
- **Prose** — every `title`/`text` key resolves against `lang/en.json`, whose values are
  Vintage Story's HTML-ish subset (`<strong>`, `<br>`, `<a href="handbook://…">`).

So editing one logical entry means opening up to three files and matching lang keys by eye,
with no assembled preview. Registration JSONs are **relaxed JSON** (unquoted keys, allowed by
VS's parser); `en.json` is **strict JSON**. The tool must respect both.

This design covers a standalone authoring aid, agreed via AskUserQuestion to: edit **prose +
structure**, save via a **local server**, live as a **new top-level sibling project**
(`~/claude/handbook-scribe-editor/`), and be a **manual editor** (no built-in AI trimming —
trimming happens conversationally in Claude Code). It reads/writes the mod's asset files
externally and adds nothing to the mod build.

## Goals / Non-Goals

**Goals:**
- Assemble and display a whole handbook entry from its scattered parts, in section order,
  with a high-fidelity preview (correct text-column width, approximate typography and link
  styling) so the author can judge on-screen real estate and catch broken links.
- Ship a double-click Dock launcher (a `.command` in the tool + a Finder alias for
  `~/Launchers/`), matching the author's existing local-tool launch pattern.
- Present a three-column workspace (collapsible library | source editor | live preview),
  a per-entry `handbook://` link-to-clipboard generator, and a session-original before/after
  snapshot toggle that updates both the editor and preview.
- Let the author edit section title/body prose, add/remove/reorder sections, and edit
  `handbook://` link targets.
- Save edits back to the correct files in place, preserving unrelated keys/entries and the
  relaxed-JSON style of registration files, and never producing invalid `en.json`.
- Match the author's existing local-tool ergonomics (browser UI over a small local server,
  one-command start) so it feels like `vs-playtest-checklist` / `glyph-forge`.

**Non-Goals:**
- Byte-exact raster fidelity to the in-game handbook (VS's Cairo/`RichTextComponent` renderer
  is not reproduced). Fidelity target is "trust the width and line-count," not a pixel clone;
  the game remains ground truth for final appearance.
- Editing anything beyond handbook entries (no recipes, no other lang domains, no non-Scribe
  mods).
- Built-in AI/LLM trimming or content generation.
- Multi-language support — only `en.json` (the mod ships English only today).
- Concurrent multi-user editing, auth, or remote/LAN access as a primary mode.

## Decisions

### D1: Standalone sibling project at `~/claude/handbook-scribe-editor/`
Per the AskUserQuestion answer. Its own git repo, gitignored `node_modules`. The mod-assets
root it operates on is configured (env var or a small `config.json`, defaulting to the
sibling path `../vintagestory-scribe-libgui/src/Mod/assets/scribe`). Root `CLAUDE.md` gets a
one-line entry.
- *Alternative considered*: a `tools/` subfolder inside the mod repo — rejected by the
  author in favor of a reusable standalone tool (potentially usable for other mods later via
  the configured path).

### D2: Local server + static browser client (runtime not committed)
A minimal local HTTP server serves the static page and exposes a small JSON API
(`GET /api/entries`, `GET /api/entry/:id`, `POST /api/entry/:id`); the browser client is
plain HTML/CSS/JS, matching the dependency-light style of the author's other tools. The
author is **not committed to Node vs. Python** — the runtime is an implementation detail as
long as the launcher deliverable (D7) is met. We will use **Python (stdlib `http.server`)**
to match the existing `vs-playtest-checklist` precedent, reuse its `.command`-launcher
pattern, and avoid a `node_modules` footprint — but nothing in the specs depends on the
choice, so Node remains a drop-in alternative if preferred during implementation.
- *Rationale*: outcome-equivalent for a localhost single-user tool; matching the existing
  Python tool minimizes new machinery and keeps the launcher ergonomics identical.

### D3: Relaxed-JSON-preserving edits to registration files
Registration JSONs use unquoted keys; a naive `JSON.parse`→`JSON.stringify` round-trip would
quote every key and reflow the whole file, producing a huge noisy diff. The tool will make
**targeted, structure-preserving edits** to the `extraSections` array (and page fields)
rather than reserializing the whole file — e.g. parse leniently to locate the array, then
splice/reorder section objects while leaving surrounding formatting intact. Section objects
the tool writes will match the file's existing quoting style.
- *Alternative considered*: full reserialize with a relaxed-JSON writer — rejected because it
  churns untouched formatting and fights the "don't disturb unrelated content" requirement.

### D4: Key-preserving edits to `en.json`
`en.json` is strict JSON but large and order-meaningful to humans. Prose edits update only
the targeted key's value; added sections append new keys near their sibling keys following
the `<entry>-<section>-title` / `-text` naming convention; removed sections prune orphaned
keys. The writer preserves existing key order and only-touched-lines diffs where practical.
A JSON-parse validation gate runs before any write commits (abort-on-invalid).
- *Alternative considered*: rewriting `en.json` sorted/canonical — rejected; it would
  reorder the whole file and break the author's mental map of it.

### D5: High-fidelity markup preview at the game's true text width
The preview must let the author judge **on-screen real estate**, so it approximates the
in-game handbook faithfully rather than loosely:
- **Width**: render each section's body inside a fixed column matching the game's handbook
  detail-text width. Verified against `GuiDialogHandbook`: the detail view lays text out in
  `ElementBounds.Fixed(9.0, 45.0, 500.0, …)` — a **500 logical-unit** text column. The
  preview column is pinned to that width (with a visible ruler/width indicator) so wrapping
  and vertical extent match what the player will see. A note documents that VS multiplies
  logical units by a runtime GUI scale, so the preview shows *relative* real estate at the
  game's default scale, not device pixels.
- **Typography**: approximate the handbook's font family, size, weight, and line-height
  (VS handbook body ≈ `CairoFont.WhiteSmallText`-class), and its link styling, closely enough
  that line counts and paragraph breaks line up with the game.
- **Markup**: `<strong>`→bold, `<br>`→line break, `<a href="handbook://…">`→a styled anchor
  annotated resolvable/unresolvable (resolvability comes from the entry model's link
  classification). Unknown tags render literally so the author notices them.
- Still not a pixel-exact clone of Cairo's renderer (see Non-Goals) — the fidelity goal is
  "trust the width and line-count," not "byte-identical raster."

### D7: Dock launcher is a required deliverable
The author launches local tools from `~/Launchers/` (files added to the macOS Dock).
Inspection shows those entries are **Finder alias/bookmark files that point at a real
`.command` living inside the tool's own directory** (e.g. `scribe-libgui-testing.command`
resolves to `…/tools/launch-playtest-checklist.command`). So the deliverable is two parts:
1. A real launcher script in the tool dir (e.g. `start.command`) that starts the server and
   opens the browser at the app URL — double-clickable, matching `glyph-forge`'s
   `start.command` pattern.
2. A Finder alias to that script placed in `~/Launchers/` so the author can drag it onto the
   Dock. (Creating the alias is a documented manual step — `.command` alias files are macOS
   bookmark blobs that can't be reliably hand-generated; the README gives the "right-click →
   Make Alias → move to ~/Launchers" recipe, or the author drags it themselves.)
The runtime (D2) is explicitly subordinate to this: whatever starts the server, the
double-click-to-launch experience is the contract.

### D8: Three-column layout (collapsible library | editor | preview)
The app is a single-screen three-column workspace:
- **Left — Library** (collapsible): a navigable hierarchy of every discovered entry, grouped
  by kind/subject (guide articles vs. item/block entries), with per-entry actions (open,
  copy-link). Collapsing it hands the horizontal room to editor + preview when the author is
  focused on one entry.
- **Middle — Editor**: the selected entry's ordered sections, each with an editable title and
  a source body editor (D9), plus structure controls (add/remove/reorder sections).
- **Right — Preview**: the high-fidelity in-game preview (D5), re-rendered live from the
  editor's current content.
- *Alternative considered*: separate list → detail pages (like the game's own handbook
  overview/detail split) — rejected; a persistent library + live side-by-side preview is the
  whole point (edit-and-see-immediately), and matches the author's stated mental model.

### D9: Source editor with formatting helpers (no WYSIWYG)
The middle column edits the **actual VS markup source** — what the author sees is byte-for-byte
what gets saved. Formatting-helper buttons (`B` → `<strong>…</strong>`, `Break` → `<br>`,
`Link` → an `<a href="handbook://…">…</a>` scaffold) insert markup at the caret / around the
selection. No WYSIWYG editing.
- *Rationale*: converting a rich-text DOM back into VS's exact tag subset is lossy and
  round-trip-risky; a source editor guarantees fidelity and keeps diffs clean. The live
  preview (D5) already provides the "see the formatted result" half, so WYSIWYG buys little.
- *Alternative considered*: a `contenteditable` WYSIWYG editor with markup generated on save —
  rejected per the author's choice (round-trip risk, harder to keep en.json diffs minimal).

### D10: Session-original snapshot, survives saves
On opening an entry, the tool captures a **session-original baseline** — the entry's assembled
state at first open in this session — and keeps it across subsequent saves (it is NOT reset to
the last-saved state). A before/after toggle flips **both** the editor and preview columns
between this baseline and the working state. The baseline is in-memory (per open tool session);
it does not need to persist across tool restarts.
- *Rationale*: the author wants to compare against "where this started today," not just the
  last save, to judge cumulative progress and edit with confidence.
- *Note*: because edits are git-tracked file writes, deeper history (across restarts) is
  already recoverable via git; the in-session baseline covers the fast toggle-to-compare need
  without new persistence machinery.
- *Alternative considered*: reset-baseline-on-save — rejected by the author; persist-to-disk
  snapshots — deferred (git already backstops cross-session recovery).

### D11: Link generator from the model's page-code knowledge
Because the entry model already computes each entry's canonical page code (D6), the library
offers a per-entry "copy handbook:// link" action that puts the correct
`handbook://<pagecode>` on the clipboard — resolving the variant-code trap for variant items
(e.g. `item-scribe:scribetablet-clay-red`, not a bare `scribetablet`). Clipboard-only; the
author pastes it wherever (editor, chat, notes).
- *Rationale*: hand-deriving link targets (especially variant codes) is the exact friction
  the author called out. The tool is the authority on valid page codes, so it should hand
  them over on demand.

### D6: Link resolution mirrors VS's page-code scheme
Verified against `GuiHandbookItemStackPage.PageCodeForStack` (VSSurvivalMod.dll): a
collectible's page code is `item-<fullcode>` / `block-<fullcode>` using the short code, and
**variant items require a concrete variant code** (e.g. `item-scribe:scribetablet-clay-red`,
not a bare `scribetablet`). The model classifies a `handbook://` target as resolvable when it
matches a discovered collectible/page under that scheme. Guide pages resolve by `pageCode`
(e.g. `craftinginfo-scribe-getting-started`).

## Risks / Trade-offs

- **[Relaxed-JSON edit corrupts a registration file]** → Confine edits to the located
  `extraSections`/page fields, back up (or rely on git working-tree cleanliness) before
  writing, and re-read + sanity-check the file after write. Small file count (7) makes manual
  verification cheap.
- **[`en.json` write drops or reorders unrelated keys]** → Targeted value replacement +
  parse-validate gate + git diff review after each save. The tool never rewrites the whole
  file wholesale.
- **[Preview width/typography drifts from the real handbook, misjudging real estate]** →
  Pin the preview column to the verified 500-unit text width and calibrate font metrics
  against a screenshot of a known in-game entry during implementation; the game remains
  ground truth for final appearance. Preview is a high-fidelity structural aid, not a
  pixel-exact WYSIWYG contract.
- **[Path traversal / editing files outside the mod]** → Server confines all file operations
  to the configured assets root; reject any resolved path escaping it.
- **[`~/Launchers` alias can't be auto-generated]** → macOS alias/bookmark blobs are not
  reliably hand-writable; the tool ships the real `.command` and the README documents the
  one-time "Make Alias → move to ~/Launchers → drag to Dock" step (the author already does
  this for their other tools).
- **[Concurrent Claude session or manual edit races the tool's write]** → Single-user local
  tool; document "close other editors of these files while saving," and the post-write
  re-read catches an unexpected on-disk state.

## Migration Plan

New standalone tool; nothing to migrate in the mod. Rollout:
1. Scaffold `~/claude/handbook-scribe-editor/` (git init, README, gitignore `node_modules`).
2. Build the entry model + file I/O (server side) against the mod-assets root; verify by
   round-tripping every current entry (read → write-back-unchanged → confirm zero/near-zero
   git diff).
3. Build the browser editor (list, assembled view, prose edit, structure edit, preview,
   save).
4. Add the one-line entry to `~/claude/CLAUDE.md`.
- **Rollback**: the tool touches only Scribe asset files via git-tracked writes; any bad save
  is recoverable with `git checkout`/`git restore`. Deleting the tool directory removes the
  tool with no residue in the mod.

## Open Questions

- Font-metric calibration source: which in-game screenshot(s) to measure against to tune the
  preview's font size/line-height so line counts match at the 500-unit width — pick during
  the preview task.
- Whether new-section lang keys are inserted adjacent to their entry's sibling keys or
  appended at the end of `en.json` — pick whichever yields the cleaner diff during
  implementation (leaning adjacent-to-siblings).
- Backup strategy: rely purely on git working-tree recovery vs. also writing a `.bak` before
  each save — decide during the file-I/O task (leaning git-only to avoid clutter).
