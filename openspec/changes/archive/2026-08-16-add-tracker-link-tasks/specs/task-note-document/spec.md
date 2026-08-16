## MODIFIED Requirements

### Requirement: Document is an ordered sequence of blocks

The system SHALL represent a Scribe document as an ordered sequence of blocks, where each
block is one of a fixed set of **kinds**: a **task** (text plus a completed flag), a **text
section** (freeform text, no checkbox), a **tracker** (a task that counts progress toward a
target item and quantity), or a **link** (a task referencing a Handbook page / location). Blocks
of any kind MAY be interspersed in any order. Each block SHALL carry a depth value (reserved for
future sub-item nesting; 0 for now). Kind values SHALL be assigned in append-only order — existing
kinds are never renumbered when a new kind is added. This model MUST NOT depend on the Vintage
Story API.

#### Scenario: A new document is empty

- **WHEN** a new document is created
- **THEN** it has zero blocks

#### Scenario: Block order is preserved

- **WHEN** a task, then a text section, then another task are added
- **THEN** the document lists them in that exact order with their respective kinds

#### Scenario: Blocks of every kind may be interspersed

- **WHEN** a task, a tracker, a link, and a text section are added in that order
- **THEN** the document lists all four in that order, each reporting its own kind

## REMOVED Requirements

### Requirement: Serialization round-trip includes assignment field and stable identifiers
**Reason**: This requirement folded two concerns together — round-trip field fidelity *and* the
format-version acceptance model — and its version language encoded the now-superseded strict
single-transition model ("accept only the immediately prior version; older fails"). The
version-window model is authoritatively owned by the `codec-migration` capability (see "Document
codec reads an accepted window of prior versions via progressive reads"), which this change
corrects to the shipped progressive `[MinVersion, Version]` window. Keeping a parallel,
now-contradictory copy here would let the two capabilities disagree. Replaced by "Serialization
round-trip preserves identifiers, assignment, and kind-specific fields" below, scoped to field
fidelity only.
**Migration**: No data migration — on-disk byte formats are unchanged and append-only. Version
acceptance behavior is unchanged in code; only its spec home moves to `codec-migration`.

## ADDED Requirements

### Requirement: Serialization round-trip preserves identifiers, assignment, and kind-specific fields
The system's document serialization SHALL preserve the document's `DocId`, each block's `TaskId`,
each block's assignment field, and each block's kind-specific fields (the tracker
`TargetItemCode`/`TargetQuantity`/`CurrentQuantity` and the link `LinkTarget`), in addition to the
fields already preserved (kind, text, completed flag, depth). The accepted format-version window
and how prior versions are read (progressively, supplying documented defaults for absent fields)
is specified by the `codec-migration` capability; this requirement covers only which fields
survive a current-format round-trip.

#### Scenario: Round-trip preserves identifiers, assignment, and kind-specific fields
- **WHEN** a document containing tasks, a tracker, and a link is serialized in the current format
  and then deserialized
- **THEN** the resulting document has the same `DocId`, each block has the same `TaskId` and
  assignment field, and each tracker/link block has the same kind-specific fields
  (`TargetItemCode`, `TargetQuantity`, `CurrentQuantity`, `LinkTarget`) as the original

### Requirement: Add a tracker task
The system SHALL allow adding a tracker block for a given target item code and target quantity to
the document. The stored block SHALL have kind `Tracker`, `CurrentQuantity` 0, and `TargetQuantity`
clamped to at least 1. Adding a tracker SHALL follow the same ordering and identifier rules as any
other block (appended in order, assigned a distinct `TaskId`).

#### Scenario: Add a tracker with a target
- **WHEN** a tracker for `game:ingot-copper` with target quantity 5 is added to a document
- **THEN** the document gains one `Tracker` block with `TargetItemCode` `game:ingot-copper`,
  `TargetQuantity` 5, `CurrentQuantity` 0, and a distinct `TaskId`

#### Scenario: Setting tracker progress clamps to the target range
- **WHEN** a tracker's current quantity is set to a value below 0 or above its target
- **THEN** the stored `CurrentQuantity` is clamped into `[0, TargetQuantity]` and the operation
  reports success

### Requirement: Add a link task
The system SHALL allow adding a link block for a given reference target to the document. The stored
block SHALL have kind `Link`, carry the supplied `LinkTarget`, and have no tracker quantity fields.
Adding a link SHALL follow the same ordering and identifier rules as any other block.

#### Scenario: Add a link with a reference
- **WHEN** a link referencing the Handbook page for `game:ingot-copper` is added to a document
- **THEN** the document gains one `Link` block with `LinkTarget` `game:ingot-copper` and a distinct
  `TaskId`
