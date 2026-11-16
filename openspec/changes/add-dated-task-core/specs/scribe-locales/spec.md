## MODIFIED Requirements

### Requirement: Required keys are translated; optional History flavor may be short
Each shipped locale file SHALL contain a translation for every non-`_comment-*` key in `en.json` except the optional History flavor keys listed below. Optional flavor keys MAY be omitted so a locale can ship a short contiguous pool; if a locale includes an optional flavor key, its value SHALL be a real translation, not a copy of the English string. A locale file SHALL NOT contain keys that are absent from `en.json` (including translator-comment keys, JSON comment-keys, and keys from another domain).

Optional History flavor keys (omit freely, keep contiguous from `-0` if any extras are kept):

- `scribe-mob-death-1` and higher
- `scribe-pvp-verb-generic-1` and higher
- `scribe-pvp-verb-tool-*`
- `scribe-pvp-verb-damage-*`
- `scribe-pvp-verb-generic-0-participle` and any other `*-participle` override

Every locale SHALL include at least `scribe-mob-death-0` (a literal death sentence, not a joke), `scribe-pvp-verb-generic-0` (a literal verb), `scribe-pvp-death-message`, `scribe-pvp-kill-message`, `death-generic`, `death-slain-by`, the boss and storm-strength keys, `date-format`, `date-format-dmy`, `date-format-mdy`, and `date-format-ymd`.

#### Scenario: A short locale still produces a History death line
- **WHEN** a locale file ships only `scribe-mob-death-0` from the mob-death pool, and a player using that language views a live creature-death History row
- **THEN** the row is formatted from that one translated template, not from an English joke mixed into the translated sentence

#### Scenario: Extra stale keys are not shipped
- **WHEN** a locale file is assembled from an older community translation that still contains removed keys or JSON comment-keys
- **THEN** those extra keys are not present in the shipped file

### Requirement: Placeholders and handbook VTML survive translation
Every translated value SHALL preserve the same `{0}` / `{1}` / `{2}` (and any further) placeholders as the English source, in number and index, though word order around them MAY change. The exception is the three fixed-order date templates: `date-format-dmy`, `date-format-mdy` and `date-format-ymd` SHALL keep their placeholders in the order their key names, where `{0}` is the day, `{1}` the month and `{2}` the year: `{0}` `{1}` `{2}` for `-dmy`, `{1}` `{0}` `{2}` for `-mdy`, and `{2}` `{1}` `{0}` for `-ymd`, so a player's chosen Date order is never undone by a translation; only the surrounding words (such as the word for "Year") MAY be translated. Handbook and crafting-info values SHALL preserve VTML structure: tag names (`<strong>`, `<em>`, `<br>`, `<hotkey>`, `<a>`), `handbook://` link targets, and hotkey ids. A locale SHALL NOT inject extra markup (for example `<font color="…">`) that the English source does not have. `_comment-*` keys in `en.json` are translator notes and SHALL NOT be copied into other locale files.

#### Scenario: A handbook page keeps its links
- **WHEN** a player whose client language is not English opens a Scribe handbook guide that contains `handbook://` links in English
- **THEN** those links still target the same handbook pages, even though the surrounding prose is translated

#### Scenario: A date format can reorder parts
- **WHEN** a locale translates `date-format`
- **THEN** the value still includes `{0}`, `{1}`, and `{2}` (day, localized month name, year), which MAY appear in a different order than English

#### Scenario: A fixed-order date template keeps its order
- **WHEN** a locale translates `date-format-mdy`
- **THEN** the value has `{1}` before `{0}` before `{2}`, with only the surrounding words translated
