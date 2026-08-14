## ADDED Requirements

### Requirement: Typed arrow digraphs are substituted for Unicode arrows in every Scribe editor

Every Scribe text editor — the Lectern, the Notebook, the Clockmaker's Notebook, and the editable (wet) Tablet — SHALL rewrite the ASCII digraph `->` to `→` (U+2192) and `<-` to `←` (U+2190) as the player types, in BOTH task-row text and freeform note text. The substitution SHALL be applied to the editor's live text buffer at keystroke time (not only at render time), so the character committed to and stored in the document is the real Unicode arrow, keeping the read view, search, and copy/paste consistent with what is displayed. Because all editor surfaces share one input-widget state, the behavior MUST be identical across every surface with no per-surface exception.

#### Scenario: Typing `->` produces a right arrow

- **WHEN** the player types `-` then `>` at the caret in any Scribe editor
- **THEN** the two characters are replaced by a single `→` (U+2192) in the buffer
- **AND** the stored/committed text contains `→`, not `->`

#### Scenario: Typing `<-` produces a left arrow

- **WHEN** the player types `<` then `-` at the caret in any Scribe editor
- **THEN** the two characters are replaced by a single `←` (U+2190) in the buffer
- **AND** the stored/committed text contains `←`, not `<-`

#### Scenario: Substitution applies in both task and note text on every surface

- **WHEN** the digraph is completed in a task row OR in freeform note text, on the Lectern, Notebook, Clockmaker's Notebook, or a wet Tablet
- **THEN** the arrow substitution occurs identically in all of these cases

### Requirement: Substitution fires only on digraph completion and adjusts the caret

The substitution SHALL fire only on the keystroke that COMPLETES a digraph — the second character arriving immediately after the first at the caret — and SHALL leave a partial or non-matching sequence untouched. After replacing the two-character digraph with the one-character arrow, the caret SHALL sit immediately after the inserted arrow (a net advance of one character for the two keystrokes), so continued typing flows naturally. Only the characters immediately before the caret SHALL be examined, so a matching digraph elsewhere in the buffer that the player is not currently completing is never rewritten.

#### Scenario: A lone first character is left literal

- **WHEN** the player types `<` (or `-`) but has not yet typed the completing character
- **THEN** the literal character remains in the buffer and no arrow is produced

#### Scenario: The caret lands after the arrow

- **WHEN** a digraph is completed and replaced by an arrow
- **THEN** the caret is positioned immediately after the arrow character
- **AND** typing another character inserts it directly after the arrow

#### Scenario: A separated sequence does not trigger

- **WHEN** the two digraph characters are not adjacent at the caret (for example `- >` with a space between, or the second character is typed elsewhere in the text)
- **THEN** no substitution occurs and the literal characters remain

#### Scenario: A pre-existing digraph elsewhere is not clobbered

- **WHEN** the player completes a digraph at the caret while an unrelated literal `->` sits earlier in the same field
- **THEN** only the run at the caret is converted and the earlier literal text is unchanged

### Requirement: The digraph table is fixed to the two horizontal arrows

The substitution SHALL be driven by a fixed, exactly-two-entry digraph table (`->`→`→`, `<-`→`←`) and MUST NOT be a general autocorrect, emoji, or text-expansion mechanism. Vertical and bidirectional arrows (`↑`, `↓`, `↔`) and the `<->` → `↔` triple are explicitly out of scope. The mapping logic SHOULD be a pure, game-API-free helper so it is unit-testable without a game install.

#### Scenario: Out-of-scope sequences are not substituted

- **WHEN** the player types sequences such as `<->`, `|`, `v`, or `^` intending an arrow
- **THEN** no substitution occurs — only `->` and `<-` are recognized

#### Scenario: The transform is testable in isolation

- **WHEN** the digraph-completion logic is exercised by a unit test
- **THEN** it can be evaluated without the Vintage Story API or a game install
