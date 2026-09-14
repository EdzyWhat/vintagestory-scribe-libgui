## Why

Scribe's handbook and craftinginfo essays (`en.json`) mix `<strong>` and plain quotes
inconsistently for the same kind of reference — the same tab/button/setting name is bolded in
one essay and quoted in another (e.g. `"Settings"` is plain-quoted in
`handbook-scribeassignmentdesk-using-text` but `<strong>Settings</strong>` elsewhere; `"Create
Assignments"` and `"Sent Assignment History"` are quoted despite being real, clickable tab
names that get bolded in other essays). This surfaced while translating locales for the
in-flight `add-player-locales` change: a Russian translator draft added `<strong>` around a
quoted label because English itself gave no consistent signal to copy. A firm, single
convention removes that ambiguity for every future translator (human or agent) and for the
`build/check-locales.py` mechanical checker, which already treats both `<strong>`/`<em>` spans
and quoted spans as candidate label references.

## What Changes

- Establish one emphasis convention for `handbook-*`/`craftinginfo-*` values in `en.json`:
  1. A standalone section heading inside an essay (e.g. a `Read` / `Task Editor` / `Settings`
     line introducing a block of description, followed by `<br>`) stays `<strong>`.
  2. A named, clickable UI element referenced inline in prose — a button, tab, or settings
     name, including tab-like proper nouns such as "Guest Book" — is in plain quotes, not
     bold, everywhere it is mentioned as a reference rather than as the heading itself.
  3. Incidental descriptive emphasis with no UI referent (e.g. "shared, placed" describing a
     block) drops its markup entirely and reads as plain prose. (See design.md for the
     rejected `<em>` alternative and why plain prose won.)
- Rewrite every `handbook-*`/`craftinginfo-*` value in `en.json` to this convention. This is a
  markup/formatting pass only — no string is added, removed, or changed in meaning.
- Re-audit `es-es.json` and `pt-br.json` (the two locales already completed under
  `add-player-locales`) so their `<strong>`/quote/`<em>` usage keeps exact structural parity
  with the revised English, and re-verify both against `build/check-locales.py`.
- Re-run `build/check-locales.py --dump-crossrefs` after the English rewrite and confirm the
  UI-label cross-reference table is still sound (the set of generated cross-references shifts
  when English's own tag usage moves, even though the checker's matching mechanism is
  unchanged).

## Capabilities

### New Capabilities

- `handbook-emphasis-convention`: the single bold/quote/plain-prose rule for
  `handbook-*`/`craftinginfo-*` essay text — which of the three treatments applies to a
  section heading, a named UI-element reference, and incidental descriptive emphasis.

### Modified Capabilities

(none — no existing capability spec governs handbook/craftinginfo markup style; content and
structure requirements in `handbook-scribe-entry` and `item-handbook-entries` are unaffected,
since this change only touches inline `<strong>`/quote/`<em>` markup, not what the essays say)

## Impact

- **Assets:** `src/Mod/assets/scribe/lang/en.json` (`handbook-*`/`craftinginfo-*` values
  rewritten to the new convention), `src/Mod/assets/scribe/lang/es-es.json` and
  `src/Mod/assets/scribe/lang/pt-br.json` (re-audited for matching structural parity).
- **Tooling:** no code change to `build/check-locales.py` expected — its span-matching
  mechanism already recognizes quoted text as a valid label-reference span type — but its
  generated cross-reference table must be re-verified after the English rewrite.
- **Core / Mod C#:** none. No codec, network, or rendering change.
- **Relationship to `add-player-locales`:** this is a companion change, not a sub-task of it.
  It is meant to land *before* `add-player-locales` resumes its nine remaining greenfield
  locales and its in-progress, unfinished Russian draft, so every remaining translation is
  written against the final convention once rather than being fixed twice. `ru.json` is out
  of scope here; its cleanup happens back in `add-player-locales` once this change lands.
- **Out of scope:** any change to string content or meaning, any change to which keys exist,
  and any work on the nine not-yet-started greenfield locales or the unfinished `ru.json`.
