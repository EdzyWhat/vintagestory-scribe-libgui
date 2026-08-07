# handbook-entry-model Specification

## Purpose
TBD - created by archiving change add-handbook-web-editor. Update Purpose after archive.
## Requirements
### Requirement: Entry discovery from registration sources

The tool SHALL discover handbook entries by scanning both registration sources under the
configured mod-assets root: block/item type files that declare
`attributes.handbook.extraSections` (`itemtypes/*.json`, `blocktypes/*.json`), and
standalone page files under `config/handbook/*.json` that declare a `pageCode`, `title`,
and `text`. Each discovered entry SHALL record which source file owns it and the entry
kind (item/block section-list vs. standalone page).

#### Scenario: Item/block entry discovered

- **WHEN** the tool scans a `blocktypes/*.json` or `itemtypes/*.json` file that contains
  `attributes.handbook.extraSections`
- **THEN** it produces one entry whose ordered sections are that `extraSections` array, each
  section carrying its `title` and `text` lang-key references, and records the owning file
  path

#### Scenario: Standalone guide page discovered

- **WHEN** the tool scans a `config/handbook/*.json` file declaring `pageCode`, `title`, and
  `text`
- **THEN** it produces one single-section entry keyed by `pageCode`, carrying that page's
  `title`/`text` lang-key references and the owning file path

#### Scenario: File without handbook data is ignored

- **WHEN** a scanned type file has no `attributes.handbook.extraSections` (and is not a
  handbook page file)
- **THEN** it contributes no entry and does not cause an error

### Requirement: Lang-key resolution and assembly

The tool SHALL resolve each section's `title` and `text` lang-key references against
`assets/scribe/lang/en.json` (stripping the `scribe:` domain prefix) and assemble a
normalized entry model that pairs each section with its resolved title string and body
string. A referenced lang key that is missing from `en.json` SHALL be surfaced as an
unresolved section rather than silently dropped.

#### Scenario: Section text resolved from en.json

- **WHEN** a section references `text: "scribe:handbook-scribelectern-about-text"` and that
  key exists in `en.json`
- **THEN** the assembled section body equals that key's value

#### Scenario: Missing lang key flagged

- **WHEN** a section references a lang key absent from `en.json`
- **THEN** the assembled section is marked unresolved and reports the missing key, and the
  tool does not crash

### Requirement: Cross-link target model

The tool SHALL parse `handbook://` links inside section bodies and, for each, determine
whether the target resolves to a known page — a `block-<domain>:<code>` or
`item-<domain>:<code>` collectible reference (variant items require a concrete variant
code), or a `craftinginfo-*` / other `pageCode` known from discovery. Unresolvable link
targets SHALL be reported without blocking assembly.

#### Scenario: Resolvable link classified

- **WHEN** a body contains `handbook://item-scribe:scribenotebook` and a discovered entry
  corresponds to that collectible
- **THEN** the link is classified as resolvable

#### Scenario: Unresolvable link reported

- **WHEN** a body contains a `handbook://` link whose target matches no discovered page or
  collectible
- **THEN** the link is reported as unresolvable and the section still assembles

### Requirement: Write-back without disturbing unrelated content

The tool SHALL persist edits back to the owning files such that: prose edits update only the
targeted keys in `en.json`; section add/remove/reorder edits update only the owning
registration file's `extraSections` array (or the page file's fields); and no unrelated lang
key, unrelated file, comment-free relaxed-JSON structure, or formatting of untouched entries
is altered beyond what the edit requires. New sections SHALL create new, uniquely-named lang
keys following the existing `<entry>-<section>-title` / `-text` convention, and removed
sections SHALL have their now-orphaned lang keys pruned.

#### Scenario: Prose edit touches only its keys

- **WHEN** the author edits one section's body and saves
- **THEN** only that section's `-text` key value changes in `en.json`; every other key and
  the file's overall key ordering are preserved

#### Scenario: Added section creates keys and registration entry

- **WHEN** the author adds a new section to an item/block entry and saves
- **THEN** a new `title`/`text` key pair is written to `en.json` with names that do not
  collide with existing keys, and the owning file's `extraSections` array gains a
  correspondingly-referenced section at the chosen position

#### Scenario: Removed section prunes its keys

- **WHEN** the author removes a section and saves
- **THEN** that section is dropped from the owning `extraSections` array and its orphaned
  lang keys are removed from `en.json`, leaving other keys intact

### Requirement: Save integrity and scope confinement

The tool SHALL validate that `en.json` remains parseable JSON before committing a write, and
SHALL confine all reads and writes to the configured mod-assets root. If a write would
produce invalid JSON, the tool SHALL abort the save and leave the file unchanged.

#### Scenario: Invalid result aborts the write

- **WHEN** a save operation would render `en.json` unparseable
- **THEN** the tool aborts the write and the on-disk file is unchanged

#### Scenario: Path outside the assets root refused

- **WHEN** a request targets a file path outside the configured mod-assets root
- **THEN** the tool refuses the operation

