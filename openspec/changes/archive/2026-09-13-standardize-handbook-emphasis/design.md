## Context

See proposal.md - Why. Current counts in `handbook-*`/`craftinginfo-*` values of `en.json`:
201 `<strong>` spans, of which ~45 read as standalone section headings (`<br><br><strong>X</strong><br>`
or at string start, introducing a block of description) and the rest are inline mid-sentence
mentions; a handful of plain-quoted spans (`"Settings"`, `"Create Assignments"`, `"Sent Assignment
History"`, `"Inbox"`, `"Editor"`, `"Always Instant"`, `"Always Physical"`, `"Hybrid"`, …) that
already read correctly under the new convention and don't need touching; and four existing `<em>`
spans that are a different, unrelated, already-consistent use (see Decision 1, category 5).

## Goals / Non-Goals

**Goals:**
- One deterministic rule for every `<strong>`/quote/`<em>` span already in `en.json`'s
  `handbook-*`/`craftinginfo-*` values, precise enough that a human or LLM editor can classify
  any given span without asking "but what about this one."
- Zero wording change — every edit is a markup/tag change only. The plain text (with all
  `<strong>`/`<em>`/quote-marks stripped) must be byte-identical before and after.
- `es-es.json` and `pt-br.json` end this change in the same state they'd have been written in
  had the convention existed from the start — same tag positions and types as the revised
  English, same words they already have.

**Non-Goals:**
- Rewriting keyboard/mouse instructional call-outs (`<hotkey>` tags, and fixed-key mentions
  like `<strong>Enter</strong>`/`<strong>Esc</strong>`/`<strong>Tab</strong>` in
  `craftinginfo-scribe-editor-reference-text`). These are a different, already self-consistent
  category — see Decision 1, category 4 — and flattening them under the new "no UI referent →
  plain prose" rule would be a readability regression the user didn't ask for.
- Touching the four existing `<em>` spans (an etymology callout, two third-party app names, one
  spreadsheet column list) — a different, already-consistent use of `<em>`, unrelated to this
  convention. See Decision 1, category 5.
- Any change to the nine not-yet-started greenfield locales or to the in-progress `ru.json` —
  those are `add-player-locales` work, resumed after this lands.
- Any change to `build/check-locales.py`'s matching logic. Its span extraction already
  recognizes quoted text as a label-reference candidate; only the *data* (which spans exist in
  `en.json`, and where) changes.

## Decisions

### 1. Five treatment categories, chosen by syntactic role, not by which word it is

The same word can legitimately land in different buckets in different places (e.g. "Task
Editor" as a heading in one essay, referenced inline in another) — the rule is about the
span's *role in that sentence*, not a fixed per-word mapping. In order of precedence when
classifying a span:

1. **Section heading.** A `<strong>` span that starts a block — immediately after `<br><br>`
   or at the start of the string — and is immediately followed by `<br>` introducing a
   description. **Stays `<strong>`.** This includes headings that happen to name a real UI
   concept (`<strong>Task</strong>`, `<strong>Inbox</strong>`, `<strong>Item Tracker</strong>`
   as section titles in `craftinginfo-scribe-task-types-text`) — being a heading wins over
   being a nameable thing.
2. **Keyboard/mouse instructional call-out.** Anything already using `<hotkey>` (rebindable
   actions), or a fixed physical key named for a shortcut (`Enter`, `Esc`, `Tab`, `Shift+Tab`,
   arrow keys, `Ctrl/Cmd+C`, etc., as seen throughout `craftinginfo-scribe-editor-reference-text`
   and the "press Enter to create one" mentions elsewhere). **Left exactly as-is** (mostly
   `<strong>`, already internally consistent) — out of scope, see Non-Goals.
3. **Term-of-art / foreign word / product name / column list.** The four existing `<em>` spans.
   **Left exactly as-is** — out of scope, see Non-Goals.
4. **Named, clickable UI element referenced inline.** A button, tab, setting name, or
   filter/state label mentioned in prose as a reference rather than as its own heading — the
   test is "does this exact text equal another key's full English value" (the same test
   `build/check-locales.py` already uses to build its cross-reference table) or is it plainly
   one of these categories by inspection (e.g. the assignment filter-pill list "All, New,
   Accepted, Cancelled, and Completed"). **Quoted, not bolded.**
5. **Everything else** — incidental descriptive emphasis with no UI referent (`shared,
   placed`, `duplicate`, `append` used as plain adjectives/verbs, not as button names).
   **Markup dropped entirely; plain prose.** See Decision 2 for why plain prose beats `<em>`.

Categories 2 and 3 are call-outs, not new work — they already read correctly and are listed
here so the tasks phase doesn't accidentally "fix" something that isn't broken.

**Alternatives considered:** a fixed per-word glossary ("Guest Book is always quoted, Task is
always bold") — rejected; it can't handle a heading and an inline mention of the same concept
needing different treatment in different essays, and it would need constant upkeep as new
essays are written. A syntactic-role test generalizes and keeps working for future handbook
content without a lookup table.

### 2. Category 5 drops markup rather than moving to `<em>`

The user's own framing of the "shared, placed" example was "a point of emphasis, but weak," and
leaned toward doing nothing. Going with plain prose over `<em>`:

- Keeps exactly two active typographic tiers in the essays (bold = heading, quotes = clickable
  reference) instead of three, which is less for a future translator (human or agent) to get
  wrong — every additional tier is another thing that must survive translation with exact
  parity, which is the whole problem this change exists to close off.
- Avoids overloading `<em>` with a second, unrelated meaning in the same file — it already
  means "a word being discussed as a word / a foreign or product name" (category 3); reusing
  it for "mild emphasis" would make `<em>`'s own meaning ambiguous, which is the same kind of
  problem this change is fixing for `<strong>`/quotes.
- The affected phrases are genuinely small in number (a handful of distinct adjective/verb
  phrases, `shared, placed` being the most frequent at 4 occurrences) — low cost either way,
  so the tie-breaker is "don't add a third tier for marginal benefit."

**Alternatives considered:** `<em>` for category 5 — rejected for the reasons above, but noted
as the fallback if a future essay genuinely needs a third weight; nothing here forecloses
introducing it later with its own clear rule if that need arises.

### 3. Rewrite order: English first, completely, then re-audit the two finished locales

1. Rewrite every `handbook-*`/`craftinginfo-*` value in `en.json` per Decision 1. Do this in one
   pass across the whole file rather than key-by-key merges, since categories 1 and 4 both
   depend on seeing the essay's full structure (is this span a heading or a reference?).
2. Verify no wording changed: for every touched key, strip all `<strong>`/`<em>` tags and
   `"`/`«»`-style quote characters from the old and new value and diff the result — it must be
   empty. This is a one-off verification step (a short inline script), not a new permanent
   `build/` tool, since this change doesn't recur.
3. Regenerate the cross-reference table (`python3 build/check-locales.py --dump-crossrefs`) and
   read it — expect it to change (new quoted spans become legitimate cross-reference sources;
   some old `<strong>`-only spans stop being sources if the referenced label text moved). This
   is expected, not a bug.
4. Only then update `es-es.json` and `pt-br.json`: for each touched key, move that locale's
   existing tags/quotes to the same positions and types as the revised English, without
   re-translating the underlying words. Apply the same de-tagged-diff check against each
   locale's own previous value (not against English — their prose is a different language) to
   prove nothing but markup moved.
5. Run `python3 build/check-locales.py` as the gate. It will legitimately fail on `es-es.json`/
   `pt-br.json` between steps 1 and 4 (they still mirror the *old* English tag structure) — that
   window is expected, not a regression to chase down mid-way.

### 4. `ru.json` and the nine unstarted locales are untouched here

`ru.json` is a partial, checker-failing draft that belongs to `add-player-locales`; fixing it
against the *old* convention here would be wasted work the moment this change lands. The nine
unstarted greenfield locales simply get written against the new convention when
`add-player-locales` resumes — there is nothing for them to migrate.

## Risks / Trade-offs

- **[Reclassifying ~200 spans by hand/LLM judgment could introduce a genuine wording slip]** →
  Decision 3 step 2's de-tagged-diff check makes this mechanically provable, not just
  reviewed-by-eye, for both the English rewrite and the two locale re-audits.
- **[A category-4 span might not exactly match another key's value due to minor punctuation]**
  (e.g. a trailing period inside the quote) → when in doubt, quote the same substring
  `build/check-locales.py --dump-crossrefs` already resolves as a cross-reference source before
  the rewrite, or that clearly matches a real settings/tab/filter name; the checker's exact-match
  cross-reference check is the final arbiter after the rewrite, not a judgment call left standing.
- **[Category 1 vs. category 4 could be ambiguous for a short list-style heading]** (e.g. is
  `<strong>Read</strong>` in a comma-separated tab list a heading or a reference?) → the
  heading test in Decision 1 is positional (must start a block right after `<br><br>`/string-start
  and be followed by `<br>`); a comma-separated in-sentence mention never satisfies that test, so
  it is unambiguously category 4.

## Migration Plan

No runtime, save, or codec migration — this is a text-asset content change to three JSON files.
Rollback is reverting `en.json`, `es-es.json`, and `pt-br.json` to their pre-change versions.
Restage (`build/restage.sh`) after landing so a running dev client picks up the new text.
