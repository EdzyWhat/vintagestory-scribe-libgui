# quest-auto-detect

## Purpose

TBD - created via spec sync from change `add-assignment-and-quest-support`. This capability
covers soft, read-only auto-detection of VS Quest progress (via reflection into its own open
dialog) and the two independent per-player Accept/Completion policies that govern what happens
when auto-detection fires.

## Requirements

### Requirement: Soft auto-detection supports VS Quest and Progression Framework, each independently gated
When the VS Quest mod (`vsquest`) is installed and enabled, the system SHALL detect accept and
completion state by reading that mod's own server-synced `WatchedAttributes` keys on quest-giver
entities — no reflection is used for accept/completion detection. Only VS Quest's live
kill/place/break progress-count mirroring uses reflection, reading that mod's own open
quest-selection dialog (found by type name); that reflection path is best-effort and covers
progress counts only, never accept/completion state.

When the Progression Framework mod (`progressionframework`) is installed and enabled, the system
SHALL detect accept, completion, and per-objective progress for **player-scoped** quests by
reading that mod's own server-synced `WatchedAttributes` tree on the player's own entity — no
reflection is used for this path's detection at all. Per-objective progress SHALL include both the
objective's status (pending/completed) and its numeric progress count, read from the same tree —
the numeric count drives that objective's `quest-objective-task` subtask, not just its status text.

The system SHALL ALSO detect activation, completion, and per-objective progress for Progression
Framework's **server-scoped** quests — one shared quest instance every player on the world
contributes to together, with no per-player membership concept in the source mod's own data model.
Because this shared state is not exposed via `WatchedAttributes` (it syncs over Progression
Framework's own private network channel instead), the system SHALL read it via best-effort reflection
against that mod's own `QuestSystem` ModSystem instance, mirroring the same reflection posture already
used for VS Quest's progress-count mirroring: read-only, self-disabling on failure, never throwing.
Per-objective numeric progress SHALL be read from this reflected state the same way as status.

Each of the three paths (VS Quest, Progression Framework player-scoped, Progression Framework
server-scoped) is gated independently on its own mod/scope check; any subset may be active in a
given world (`vsquest` and `progressionframework` cannot in practice both be installed at once,
but a world with Progression Framework may have only player-scoped, only server-scoped, or both
kinds of quests catalogued). The system SHALL NOT reflect into any other quest mod's dialog or
state (including Alegacy Quest Framework), and SHALL NEVER write into any backend's own state —
every read, across all three paths, is observational only.

#### Scenario: Accept-state is detected for VS Quest via its synced entity attributes
- **WHEN** a player with vsquest installed accepts a quest from a quest-giver entity
- **THEN** the system reads that quest's accept-state from the entity's synced
  `WatchedAttributes` without querying or modifying vsquest through any other path

#### Scenario: Accept-state is detected for Progression Framework via the player's own attributes
- **WHEN** a player with Progression Framework installed accepts a player-scoped quest
- **THEN** the system reads that quest's status from `progressionframework:questlog` on the
  player's own entity `WatchedAttributes`, with no reflection involved

#### Scenario: Activation is detected for a Progression Framework server-scoped quest
- **WHEN** a server-scoped Progression Framework quest's shared status transitions from
  unavailable/inactive to active (any player on the world triggering it, typically by talking to
  its quest-giver NPC)
- **THEN** the system detects that transition via its best-effort reflection read and treats it as
  that quest's activation, the same way a player-scoped accept is treated

#### Scenario: Per-objective numeric progress is read for a player-scoped quest
- **WHEN** a player-scoped Progression Framework objective's reported progress count changes
- **THEN** the system reads the new count from the player's own `WatchedAttributes` tree and drives
  that objective's subtask progress from it

#### Scenario: Per-objective numeric progress is read for a server-scoped quest
- **WHEN** a server-scoped Progression Framework objective's reported progress count changes
- **THEN** the system reads the new count via the same reflected read already used for that
  objective's status, and drives that objective's subtask progress from it

#### Scenario: Alegacy Quest Framework is never reflected into or read from
- **WHEN** a player has Alegacy Quest Framework installed instead of (or alongside) either
  supported backend
- **THEN** the system does not attempt to read Alegacy's state under any circumstance

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

### Requirement: Auto-detection fails safe per backend and never blocks manual Quest Links
Any failure detecting state for a given path (a reflection error for VS Quest's progress
mirroring or for Progression Framework's server-scoped state, an unexpected attribute shape for
either `WatchedAttributes`-based path) SHALL be caught and SHALL silently disable auto-detection
**for that path only**, for the session, rather than raise a visible error or crash. A failure in
one path's detection SHALL NOT disable any other path's. Manually-created Quest Links (the
`link-task` capability's Quest Link requirement) SHALL continue to work normally regardless of
any path's auto-detection state. A failure reading an objective's numeric progress specifically
(e.g. an unexpected attribute shape) SHALL disable only that objective's live progress updates,
leaving its subtask at its last-known count — it SHALL NOT disable status detection (accept/complete)
for the same quest.

#### Scenario: A reflection failure disables only VS Quest's progress mirroring
- **WHEN** vsquest's dialog fields no longer match what auto-detection expects (e.g. after a
  vsquest update)
- **THEN** VS Quest progress mirroring silently stops attempting further reads for the session,
  while VS Quest accept/completion detection and all Progression Framework detection (both
  scopes) are unaffected

#### Scenario: An attribute-shape failure in one backend doesn't disable the other
- **WHEN** Progression Framework's player-scoped `WatchedAttributes` tree shape no longer matches
  what auto-detection expects (e.g. after a Progression Framework update)
- **THEN** Progression Framework player-scoped detection silently stops for the session, while VS
  Quest detection (if active) and Progression Framework server-scoped detection continue
  unaffected

#### Scenario: A reflection failure in server-scoped detection doesn't disable player-scoped detection
- **WHEN** Progression Framework's internal `QuestSystem` shape no longer matches what the
  server-scoped reflection read expects (e.g. after a Progression Framework update)
- **THEN** Progression Framework server-scoped detection silently stops for the session, while
  Progression Framework player-scoped detection (which uses `WatchedAttributes`, not reflection)
  continues unaffected

#### Scenario: A malformed progress count leaves the subtask at its last-known value
- **WHEN** an objective's numeric progress cannot be read (missing or unexpectedly-typed field)
- **THEN** that objective's subtask keeps its last successfully-read `CurrentQuantity`, and the
  quest's own status detection (accept/complete) is unaffected

#### Scenario: Manually-created Quest Links are unaffected by any detection failure
- **WHEN** any path's auto-detection has failed and disabled itself for the session
- **THEN** existing and new manually-created Quest Links for any backend/scope continue to work
  normally

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

### Requirement: All quest-related UI is hidden unless its backing mod is installed
The Quest Accept/Completion settings rows, any quest-related handbook documentation, and the
Quest Link option in any Link-creation picker SHALL be shown only for backends whose mod is
actually installed and enabled. With neither `vsquest` nor `progressionframework` installed, no
quest-related affordance SHALL be visible. With exactly one installed, the picker SHALL offer
Quest Links from that backend's catalog only. (`vsquest` and `progressionframework` cannot in
practice both be installed at once, so the both-installed picker-merge behavior below is a
defensive code path, not a live scenario.)

#### Scenario: No quest UI with neither backend installed
- **WHEN** a player has neither vsquest nor progressionframework installed
- **THEN** the Settings dialog shows no Quest policy rows, the handbook shows no quest-related
  documentation, and no Link picker offers a Quest Link option

#### Scenario: Quest UI appears once a backend is installed
- **WHEN** a player installs either vsquest or progressionframework and starts a new session
- **THEN** the Quest policy rows, quest handbook documentation, and the Quest Link picker option
  all become visible, scoped to that backend's catalog

### Requirement: A Quest Link's backend is explicit, never inferred
Every Quest Link SHALL record which backend mod (VS Quest or Progression Framework) it targets,
captured at creation time. Auto-detection, progress mirroring, and destination resolution for a
given Quest Link SHALL always consult that recorded backend — never attempt to infer it from the
quest code's domain, and never fall back to checking the other backend if the recorded one is
absent or fails.

#### Scenario: A quest code that happens to exist in both catalogs is not ambiguous
- **WHEN** a Quest Link records `vsquest` as its backend for a given quest code
- **THEN** its status/progress are read only from VS Quest's detection mechanism, even if a
  Progression Framework quest happens to share the same code string

### Requirement: Progression Framework per-objective progress is mirrored by objective code, not position
When a linked Progression Framework quest has multiple objectives, the system SHALL mirror each
objective's status and progress count individually, matched by that objective's own stable code
(as read from the same `WatchedAttributes` tree), not by list position. This differs from VS
Quest's progress mirroring, which zips a live count list positionally against a separately-read
catalog because vsquest exposes no stable per-objective key.

#### Scenario: A multi-objective delivery quest shows aggregate progress
- **WHEN** a linked Progression Framework quest has 12 delivery objectives, 3 of which are
  complete
- **THEN** the linked Scribe task shows progress reflecting 3 of 12 objectives complete

#### Scenario: A single-objective quest shows accept/complete state only
- **WHEN** a linked Progression Framework quest has exactly one objective
- **THEN** the linked Scribe task shows that objective's status without needing an aggregate count

### Requirement: Quest auto-link destination resolution uses the same multi-candidate picker as Assignment accept
When Quest Accept Policy is Prompt and the player accepts the resulting HUD prompt, or when
Quest Accept Policy is Always and exactly one eligible Scribe document is carried, the system
SHALL resolve the auto-link's destination the same way Assignment's Accept flow resolves its
placement target: the player's carried (hotbar + backpack), writeable Scribe documents are the
eligible candidates; zero eligible candidates disables the prompt's Accept action with an
explanatory tooltip; exactly one proceeds with no further interaction; two or more presents a
picker naming each candidate before proceeding. When Quest Accept Policy is Always and two or
more eligible candidates exist, the system SHALL raise a Prompt-style choice for that one link
instead of silently choosing among them.

#### Scenario: Always policy auto-links with a single carried document
- **WHEN** Quest Accept Policy is Always and the player carries exactly one eligible Scribe
  document
- **THEN** the quest is auto-linked to that document with no interruption

#### Scenario: Always policy falls back to a choice with multiple carried documents
- **WHEN** Quest Accept Policy is Always and the player carries two or more eligible Scribe
  documents
- **THEN** the player is shown a picker to choose the destination instead of one being chosen
  silently

#### Scenario: Prompt policy's Accept action offers a picker with multiple candidates
- **WHEN** Quest Accept Policy is Prompt, the player accepts the resulting HUD banner, and the
  player carries two or more eligible Scribe documents
- **THEN** a picker naming each eligible document appears before the link is created

#### Scenario: Accept is disabled with no eligible destination
- **WHEN** the player has no carried, writeable Scribe document at the time of accepting a quest
  link prompt
- **THEN** the prompt's Accept action is disabled with an explanatory tooltip rather than allowing
  a click that silently fails

### Requirement: A server-scoped Quest Link tracks the shared quest, not a personal copy
A Quest Link created for a Progression Framework server-scoped quest SHALL display the same
shared status and objective progress every player on the world sees for that quest — matching
what Progression Framework's own Quest Log shows — rather than any player-specific state. Linking
a server-scoped quest from two different players' documents SHALL show identical live progress on
both, since there is only one underlying quest instance.

#### Scenario: Two players' Links to the same server-scoped quest show identical progress
- **WHEN** two different players each have a Quest Link to the same server-scoped quest code
- **THEN** both Links display the same shared objective progress, updating together as either
  player (or any other player on the world) contributes toward it

#### Scenario: A server-scoped quest's completion is shared, not personal
- **WHEN** a server-scoped quest's shared status transitions to completed
- **THEN** every player with a Quest Link to that quest code sees it as completed, regardless of
  which player's contribution finished it

### Requirement: Server-scoped quests are included in the Progression Framework catalog
The Progression Framework catalog SHALL include server-scoped quests alongside player-scoped
ones, available in the Quest Link picker on the same terms (title/description resolved the same
way, no additional gating beyond the existing per-backend `IsModEnabled` visibility rule). A
server-scoped quest's catalog entry SHALL be indistinguishable in the picker from a player-scoped
one except for the live progress it later displays once linked.

#### Scenario: A server-scoped quest appears in the Quest Link picker
- **WHEN** Progression Framework is installed and its catalog includes a server-scoped quest
- **THEN** that quest appears in the Quest Link picker exactly like any player-scoped catalog
  entry, selectable regardless of whether its shared status is currently active

### Requirement: A per-quest accept or dismiss decision persists across sessions and suppresses repeat prompts
For each player, the system SHALL record whether the player has accepted-via-Scribe or explicitly
dismissed a given quest's accept-prompt, and separately whether they have dismissed its
completion-prompt, keyed by quest source (`vsquest` or Progression Framework) and quest code. This
record SHALL be persisted per-player and per-world — a decision made in one world SHALL NOT affect
prompting in a different world — and SHALL survive a client relog and a server restart. Once a
decision is recorded for a given quest and prompt kind, the system SHALL NOT re-enqueue that same
prompt in a later session unless the underlying quest genuinely re-enters a fresh prompt-worthy
state (for example, a repeatable quest resetting and becoming active again after having been
previously decided).

#### Scenario: Accepting suppresses the accept-prompt across a relog
- **WHEN** a player accepts a quest's accept-prompt, then disconnects and rejoins the same world
- **THEN** the accept-prompt for that quest does not reappear

#### Scenario: Dismissing suppresses the accept-prompt across a relog
- **WHEN** a player dismisses a quest's accept-prompt, then disconnects and rejoins the same world
- **THEN** the accept-prompt for that quest does not reappear

#### Scenario: Decisions are independent per prompt kind
- **WHEN** a player dismisses a quest's accept-prompt, and that quest later becomes completable
- **THEN** the completion-prompt for that quest is still raised — the earlier accept-prompt
  decision does not suppress it

#### Scenario: Decisions are scoped to one world
- **WHEN** a player dismisses a quest's accept-prompt in one world, and the same quest code is
  active in a different world that player also plays
- **THEN** the accept-prompt is still raised in the other world

#### Scenario: A repeatable quest resetting re-raises a decided prompt
- **WHEN** a player has previously decided (accepted or dismissed) a quest's accept-prompt, and
  that quest later resets to a fresh not-yet-decided active state (a repeatable quest cycling
  again)
- **THEN** the accept-prompt is raised again for the new active instance
