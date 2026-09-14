## Purpose

Ship player-facing Scribe strings in the languages Vintage Story players actually use, as locale files next to English, so a non-English client sees UI, handbook, hover text, History sentences, and dates in its own language without mixing in stale or English leftover keys.

## ADDED Requirements

### Requirement: Scribe ships one locale file per supported language
The mod SHALL ship a JSON locale file at `assets/scribe/lang/<code>.json` for each of the following Vintage Story language codes from vanilla `languages.json`: `ru`, `uk`, `pl`, `de`, `zh-cn`, `ja`, `es-es`, `cs`, `sv-se`, `fr`, `pt-br`, `it`. Canonical English SHALL remain `en.json`. Keys inside each file SHALL be bare (no `scribe:` prefix); the game registers them as `scribe:<key>`. Filenames SHALL match those codes exactly (`sv-se.json`, not `sv.json`; `zh-cn.json`, not `zh.json`; `pt-br.json`, not `pt.json`; `es-es.json`, not `es.json`).

#### Scenario: A Russian client loads Russian UI
- **WHEN** a player whose client language is Russian opens any Scribe surface
- **THEN** buttons, tabs, empty states, and hover text resolve from `ru.json` rather than showing English (except for any key that file intentionally omits, which falls back to English)

#### Scenario: Swedish uses the vanilla language code
- **WHEN** the Swedish locale file is present in the shipped mod
- **THEN** its filename is `sv-se.json`, which is the code Vintage Story uses for Swedish

### Requirement: Required keys are translated; optional History flavor may be short
Each shipped locale file SHALL contain a translation for every non-`_comment-*` key in `en.json` except the optional History flavor keys listed below. Optional flavor keys MAY be omitted so a locale can ship a short contiguous pool; if a locale includes an optional flavor key, its value SHALL be a real translation, not a copy of the English string. A locale file SHALL NOT contain keys that are absent from `en.json` (including translator-comment keys, JSON comment-keys, and keys from another domain).

Optional History flavor keys (omit freely, keep contiguous from `-0` if any extras are kept):

- `scribe-mob-death-1` and higher
- `scribe-pvp-verb-generic-1` and higher
- `scribe-pvp-verb-tool-*`
- `scribe-pvp-verb-damage-*`
- `scribe-pvp-verb-generic-0-participle` and any other `*-participle` override

Every locale SHALL include at least `scribe-mob-death-0` (a literal death sentence, not a joke), `scribe-pvp-verb-generic-0` (a literal verb), `scribe-pvp-death-message`, `scribe-pvp-kill-message`, `death-generic`, `death-slain-by`, the boss and storm-strength keys, and `date-format`.

#### Scenario: A short locale still produces a History death line
- **WHEN** a locale file ships only `scribe-mob-death-0` from the mob-death pool, and a player using that language views a live creature-death History row
- **THEN** the row is formatted from that one translated template, not from an English joke mixed into the translated sentence

#### Scenario: Extra stale keys are not shipped
- **WHEN** a locale file is assembled from an older community translation that still contains removed keys or JSON comment-keys
- **THEN** those extra keys are not present in the shipped file

### Requirement: Placeholders and handbook VTML survive translation
Every translated value SHALL preserve the same `{0}` / `{1}` / `{2}` (and any further) placeholders as the English source, in number and index, though word order around them MAY change. Handbook and crafting-info values SHALL preserve VTML structure: tag names (`<strong>`, `<em>`, `<br>`, `<hotkey>`, `<a>`), `handbook://` link targets, and hotkey ids. A locale SHALL NOT inject extra markup (for example `<font color="…">`) that the English source does not have. `_comment-*` keys in `en.json` are translator notes and SHALL NOT be copied into other locale files.

#### Scenario: A handbook page keeps its links
- **WHEN** a player whose client language is not English opens a Scribe handbook guide that contains `handbook://` links in English
- **THEN** those links still target the same handbook pages, even though the surrounding prose is translated

#### Scenario: A date format can reorder parts
- **WHEN** a locale translates `date-format`
- **THEN** the value still includes `{0}`, `{1}`, and `{2}` (day, localized month name, year), which MAY appear in a different order than English

### Requirement: Missing keys fall back to English; present-but-wrong keys are forbidden
A key omitted from a locale file SHALL render in English via the game's default-locale fallback. A key present in a locale file SHALL be a translation of the current English meaning of that key, not an outdated meaning and not the English string copied through as a stand-in. Existing community files used as seeds (C4B Spanish, Arquimago Portuguese) SHALL be meaning-audited against current `en.json` before shipping: every overlapping key SHALL be checked, not only keys the seed is missing. A seed value whose English source was rewritten in place (for example a tablet capacity of 10 instead of 15, or a handbook page that grew from a Lectern-only intro into a full tour) SHALL be re-translated even though the key name did not change. Filling new keys while leaving rewritten keys in their old wording SHALL NOT count as current.

#### Scenario: An omitted key shows English, not a blank
- **WHEN** a locale file omits an optional flavor key
- **THEN** `Lang.Get` for that key falls back to English, and History pool probing in that locale does not count the English fallback as a hit

#### Scenario: Stale Portuguese tablet copy is not shipped
- **WHEN** a player whose client language is Brazilian Portuguese reads the tablet-full message
- **THEN** the message matches the current English meaning (at most 15 entries), not the older “10 tasks” wording

#### Scenario: A rewritten handbook page is re-translated, not kept because the key still exists
- **WHEN** C4B or Arquimago already had `craftinginfo-scribe-getting-started-text` (or another handbook key) from an older English draft, and current English for that same key is a different article
- **THEN** the shipped locale value is a translation of the current English article, not the leftover translation of the old one

### Requirement: Spanish is seeded from C4B; Portuguese is updated in place
The shipped `es-es.json` SHALL start from C4B Traducciones-ES's Scribe Spanish file (author permission to take and update) and SHALL be meaning-audited and updated to the current English key set and meanings. The shipped `pt-br.json` SHALL start from the existing Arquimago file in this repo and SHALL be meaning-audited and updated the same way. Other listed locales SHALL be authored from current `en.json`, not by copying `pt-br.json` or `es-es.json`.

#### Scenario: Spanish keeps a C4B-named Lectern
- **WHEN** a player whose client language is Spanish (`es-es`) looks at the Lectern
- **THEN** the item/block name is the C4B Spanish name for that block, not a fresh unrelated coinage, and keys that C4B had not yet translated (Inbox, Task Notice, delivery, and other 1.4.x additions) are present in Spanish

#### Scenario: Portuguese keeps Arquimago's voice where it is still accurate
- **WHEN** a player whose client language is Brazilian Portuguese looks at a key Arquimago already translated and whose English meaning has not changed
- **THEN** the shipped value is that existing Portuguese translation, not a wholesale rewrite

### Requirement: The Clockmaker worldconfig line is translated in the game domain
Each supported locale SHALL ship `assets/game/lang/<code>.json` containing a translation of `worldattribute-scribeClockmakerRequiresTrait`. That string SHALL NOT be stuffed into the `scribe` locale file under a `game:`-prefixed key.

#### Scenario: A German worldconfig tooltip is German
- **WHEN** a player whose client language is German reads the Clockmaker-requires-Tinkerer worldconfig line
- **THEN** the line is German, resolved from `assets/game/lang/de.json`

### Requirement: Locale files are mechanically checked before they ship
A checker run as part of local verification SHALL reject a locale file that has extra keys, missing required keys, placeholder-count mismatches, or VTML tag / `handbook://` target mismatches against `en.json`. Optional flavor keys that are present SHALL be contiguous from `-0` with no gap. The checker SHALL treat `_comment-*` keys in `en.json` as notes, not as required translations. The checker SHALL also generate a UI-label cross-reference table from `en.json` — every `<strong>`/`<em>`/quoted span inside a `handbook-*` or `craftinginfo-*` value whose exact English text equals another key's full English value (a button, tab, or action label) — and SHALL reject a locale file where the translated span at that position does not equal that locale's own translated value of the referenced label key.

#### Scenario: A copied-English handbook essay fails the check
- **WHEN** a locale file includes a long handbook value that is byte-for-byte the English string
- **THEN** the checker reports that key as untranslated

#### Scenario: A broken placeholder fails the check
- **WHEN** a locale value for a key whose English contains `{0}` and `{1}` drops `{1}`
- **THEN** the checker rejects that file

#### Scenario: A mistranslated button reference fails the check
- **WHEN** a handbook essay in a locale quotes a UI label (for example the Add Item Tracker button) whose English text exactly matches that button's own lang key value
- **THEN** the checker rejects the file if the locale's quoted text does not match the locale's own translated value of that button's key
