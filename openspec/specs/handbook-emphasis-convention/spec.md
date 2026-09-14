# handbook-emphasis-convention

## Purpose

Defines the single bold/quote/plain-prose convention for emphasis inside Scribe's
`handbook-*`/`craftinginfo-*` lang values, so the same kind of reference (a section heading, a
named UI element, or incidental description) is always marked up the same way across every
essay, in every shipped locale.

## Requirements

### Requirement: Section headings are bolded

A `handbook-*`/`craftinginfo-*` value's standalone section heading — a short span that starts a
block of description immediately after `<br><br>` or at the start of the string, and is itself
immediately followed by `<br>` — SHALL be wrapped in `<strong>`, regardless of whether that
heading's text also happens to name a real UI concept (e.g. `Task`, `Inbox`, `Item Tracker`
used as section titles).

#### Scenario: A section title stays bold even though it names a UI concept
- **WHEN** an essay introduces a section with a heading like `<strong>Item Tracker</strong>`
  followed by `<br>` and a description
- **THEN** that heading remains `<strong>`, even though "Item Tracker" is also a real add-row
  kind a player can create

### Requirement: Inline references to named, clickable UI elements are quoted, not bolded

A `handbook-*`/`craftinginfo-*` value that mentions a button, tab, setting name, or filter/state
label inline in prose — as a reference, not as that span's own heading — SHALL wrap that
mention in plain quotes and SHALL NOT wrap it in `<strong>` or `<em>`. This applies whenever the
mentioned text exactly equals another lang key's full value (the same test
`build/check-locales.py` uses to build its UI-label cross-reference table) or is otherwise
plainly a clickable UI name, such as a filter-pill list.

#### Scenario: A settings name mentioned inline is quoted
- **WHEN** an essay mentions a settings panel option inline, such as "...covered in Scribe
  Settings" or a reference to the "Settings" panel by name
- **THEN** the reference reads as `"Settings"` in quotes, not `<strong>Settings</strong>`

#### Scenario: A list of filter-pill names is quoted
- **WHEN** an essay describes a row of filter pills by name in a single sentence (e.g. All,
  New, Accepted, Cancelled, and Completed)
- **THEN** each name in that list is quoted (`"All"`, `"New"`, `"Accepted"`, `"Cancelled"`,
  `"Completed"`), not bolded

### Requirement: Incidental descriptive emphasis carries no markup

A `handbook-*`/`craftinginfo-*` value's incidental descriptive emphasis — a word or short
phrase with no UI-element referent and no heading role (e.g. describing a block as "shared,
placed") — SHALL carry no `<strong>` or `<em>` markup; it reads as plain prose.

#### Scenario: A descriptive adjective phrase loses its bold
- **WHEN** an essay describes a block using a phrase like "shared, placed" with no
  corresponding clickable UI element
- **THEN** that phrase appears as plain text, with no `<strong>` or `<em>` wrapping

### Requirement: Keyboard/mouse call-outs and term-of-art italics are unaffected

This convention SHALL NOT change how keyboard/mouse instructional call-outs are marked up
(existing `<hotkey>` tags for rebindable actions, and `<strong>` around fixed key names such as
`Enter`, `Esc`, `Tab`, or arrow-key combinations used to describe a shortcut), and SHALL NOT
change existing `<em>` usage for a term-of-art, foreign word, third-party product name, or a
column-header list. Both categories keep whatever markup they already have.

#### Scenario: A keyboard shortcut callout keeps its existing markup
- **WHEN** an essay describes pressing Enter to create a new task
- **THEN** `<strong>Enter</strong>` (or an existing `<hotkey>` tag for a rebindable action) is
  unchanged by this convention

#### Scenario: An etymology or product-name italic is unaffected
- **WHEN** an essay explains where the word "scriptorium" comes from, or names a third-party
  text editor by example
- **THEN** the existing `<em>` markup around that word or name is unchanged

### Requirement: Every shipped locale keeps the same span structure as English

For every `handbook-*`/`craftinginfo-*` key, each shipped non-English locale file SHALL use the
same tag type (`<strong>`, quotes, or no markup) at the same structural position as the current
English value for that key, translating only the words inside each span, not the choice of
markup around it.

#### Scenario: A translated locale mirrors the English span structure
- **WHEN** a locale translates a `handbook-*`/`craftinginfo-*` value that contains a quoted
  UI-element reference and a bolded section heading
- **THEN** that locale's translation keeps the same reference in plain quotes and the same
  heading in `<strong>`, using its own translated words in each span
