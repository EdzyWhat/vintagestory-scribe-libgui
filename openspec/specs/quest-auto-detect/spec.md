# quest-auto-detect

## Purpose

TBD - created via spec sync from change `add-assignment-and-quest-support`. This capability
covers soft, read-only auto-detection of VS Quest progress (via reflection into its own open
dialog) and the two independent per-player Accept/Completion policies that govern what happens
when auto-detection fires.

## Requirements

### Requirement: Soft auto-detection targets VS Quest only, via read-only reflection
When the VS Quest mod (`vsquest`) is installed and enabled, the system SHALL attempt to read that
mod's own open quest-selection dialog (found by type name) via reflection to learn each active
quest's accept-state and kill/place/break objective progress. The system SHALL NOT reflect into
any other quest mod's dialog (including Alegacy Quest Framework), and SHALL NEVER write into
vsquest's state — every read is observational only.

#### Scenario: Accept-state is detected while the vsquest dialog is open
- **WHEN** a player with vsquest installed opens its quest-selection dialog showing an active
  quest
- **THEN** the system reads that quest's id and accept-state from the open dialog without
  querying or modifying vsquest through any other path

#### Scenario: Alegacy Quest Framework is never reflected into
- **WHEN** a player has Alegacy Quest Framework installed instead of (or alongside) vsquest
- **THEN** the system does not attempt to read Alegacy's dialog state under any circumstance

### Requirement: Progress mirroring covers kill/place/break objectives, not gather
When auto-detection succeeds, the system SHALL mirror kill, block-place, and block-break
objective progress counts for a linked quest. Gather-objective progress SHALL NOT be mirrored
(vsquest itself has no incremental counter for gather objectives), and the linked task SHALL show
accept-state only for a gather-type objective.

#### Scenario: Kill objective progress is mirrored
- **WHEN** a linked quest has an active kill objective at 2 of 5
- **THEN** the linked Scribe task shows the same 2 of 5 progress

#### Scenario: Gather objective shows no progress count
- **WHEN** a linked quest has an active gather objective
- **THEN** the linked Scribe task shows the quest as active but displays no progress count for
  that objective

### Requirement: Auto-detection fails safe and never blocks manual Quest Links
Any failure reading vsquest's dialog (reflection error, unexpected field shape, dialog not found)
SHALL be caught and SHALL silently disable auto-detection for the session rather than raise a
visible error or crash. Manually-created Quest Links (the `link-task` capability's Quest Link
requirement) SHALL continue to work normally regardless of auto-detection's state.

#### Scenario: A reflection failure disables auto-detect, not manual linking
- **WHEN** vsquest's dialog fields no longer match what auto-detection expects (e.g. after a
  vsquest update)
- **THEN** auto-detection silently stops attempting further reads for the session, and existing
  and new manually-created Quest Links are unaffected

### Requirement: Accept and Completion policies are independent, each defaulting to Prompt
The system SHALL provide two independent per-player, client-local settings — a Quest Accept
policy and a Quest Completion policy — each with values Always, Never, and Prompt, each
defaulting to Prompt. The Accept policy SHALL govern what happens when auto-detection sees a
newly-active quest (create the Quest Link automatically, never create it, or ask); the
Completion policy SHALL govern what happens when auto-detection sees a linked quest complete in
vsquest (mark the Scribe task done automatically, never touch it, or ask). Changing one policy
SHALL NOT affect the other.

#### Scenario: Prompt is the default for both policies
- **WHEN** a player has never changed either policy
- **THEN** both the Accept and Completion policies are Prompt

#### Scenario: Policies apply independently
- **WHEN** a player sets Accept to Always and leaves Completion at Prompt
- **THEN** newly-detected active quests are auto-linked with no prompt, while a detected
  completion still asks for confirmation

### Requirement: All quest-related UI is hidden unless VS Quest is installed
The Quest Accept/Completion settings rows, any quest-related handbook documentation, and the
Quest Link option in any Link-creation picker SHALL be hidden entirely unless the VS Quest mod is
installed and enabled. No quest-related affordance SHALL be visible to a player without it.

#### Scenario: No quest UI without vsquest
- **WHEN** a player does not have vsquest installed
- **THEN** the Settings dialog shows no Quest policy rows, the handbook shows no quest-related
  documentation, and no Link picker offers a Quest Link option

#### Scenario: Quest UI appears once vsquest is installed
- **WHEN** a player installs vsquest and starts a new session
- **THEN** the Quest policy rows, quest handbook documentation, and the Quest Link picker option
  all become visible
