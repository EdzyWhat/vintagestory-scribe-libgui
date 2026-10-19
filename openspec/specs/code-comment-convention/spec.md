# code-comment-convention Specification

## Purpose
TBD - created by archiving change adopt-current-behavior-comment-convention. Update Purpose after archive.

## Requirements

### Requirement: Comments describe current behavior

A source comment (`//` or `///`) in `src/` or `tests/` SHALL describe what the code does now, or a
constraint that is still true now, in the present tense. A comment SHALL NOT narrate how the code
came to be this way.

#### Scenario: A comment explains a live constraint
- **WHEN** code works around a rendering or API quirk that still exists
- **THEN** the comment states the quirk and why the workaround is needed, in the present tense

#### Scenario: A comment would only record a past change
- **WHEN** a comment would say a behavior "used to" be different, "no longer" applies, or was fixed
  after a regression or playtest
- **THEN** that sentence is omitted from the code and left to the commit message or OpenSpec archive

### Requirement: Comments carry no history markers

A source comment SHALL NOT contain calendar dates, OpenSpec change names, spec or design IDs (such as
"design D6" or "§5.4"), commit hashes, or playtest-round references.

#### Scenario: A reference to a design decision is replaced
- **WHEN** a comment cites a design or spec ID to justify behavior
- **THEN** the citation is removed and the justification is stated directly, or dropped if it only
  restates the code

### Requirement: Comments do not restate the code

A source comment SHOULD be removed when it only repeats what the adjacent code already says plainly.
A comment that explains intent, a non-obvious reason, or an invariant SHALL be kept.

#### Scenario: A restating comment is trimmed
- **WHEN** a comment only paraphrases the statement beneath it
- **THEN** it is removed without replacement

#### Scenario: A non-obvious reason is kept
- **WHEN** a comment explains why code is ordered, shaped, or guarded in a way that is not evident
  from the code
- **THEN** it is kept, rewritten in the present tense

### Requirement: Durable API facts live in VSAPI-NOTES.md

A fact about Vintage Story API or LibGUI internals that was learned the hard way and applies beyond
one call site SHALL be recorded in `VSAPI-NOTES.md`; the code comment MAY point to it in one line.

#### Scenario: A comment holds a reusable engine fact
- **WHEN** a sweep finds a comment whose main content is a general API or engine behavior
- **THEN** the fact is recorded in `VSAPI-NOTES.md` and the code comment is shortened to what is
  specific to that site

### Requirement: A comment-only sweep preserves compiled behavior

A change that edits only comments SHALL leave the compiled intermediate language of `Scribe.dll` and
`Scribe.Core.dll` identical to the base commit's, and SHALL leave Core tests passing.

#### Scenario: A comment-only edit is verified
- **WHEN** a batch of files is edited under this change
- **THEN** building before and after and comparing the decompiled IL shows no difference

#### Scenario: A stray code edit is caught
- **WHEN** the IL comparison for a file shows a difference
- **THEN** that file's edit is reverted and reported, and is not merged
