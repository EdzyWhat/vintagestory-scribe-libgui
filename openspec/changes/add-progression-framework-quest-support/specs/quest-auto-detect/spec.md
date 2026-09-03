## MODIFIED Requirements

### Requirement: Soft auto-detection supports VS Quest and Progression Framework, each independently gated
When the VS Quest mod (`vsquest`) is installed and enabled, the system SHALL detect accept and
completion state by reading that mod's own server-synced `WatchedAttributes` keys on quest-giver
entities — no reflection is used for accept/completion detection. Only VS Quest's live
kill/place/break progress-count mirroring uses reflection, reading that mod's own open
quest-selection dialog (found by type name); that reflection path is best-effort and covers
progress counts only, never accept/completion state.

When the Progression Framework mod (`progressionframework`) is installed and enabled, the system
SHALL detect accept, completion, and per-objective progress for player-scoped quests by reading
that mod's own server-synced `WatchedAttributes` tree on the player's own entity — no reflection
is used for this backend's detection at all.

Each backend is gated independently on its own `IsModEnabled` check; either or neither may be
active in a given world (`vsquest` and `progressionframework` cannot in practice both be
installed at once). The system SHALL NOT reflect into any other quest mod's dialog or state
(including Alegacy Quest Framework), and SHALL NEVER write into either backend's own state —
every read, for both backends, is observational only.

#### Scenario: Accept-state is detected for VS Quest via its synced entity attributes
- **WHEN** a player with vsquest installed accepts a quest from a quest-giver entity
- **THEN** the system reads that quest's accept-state from the entity's synced
  `WatchedAttributes` without querying or modifying vsquest through any other path

#### Scenario: Accept-state is detected for Progression Framework via the player's own attributes
- **WHEN** a player with Progression Framework installed accepts a player-scoped quest
- **THEN** the system reads that quest's status from `progressionframework:questlog` on the
  player's own entity `WatchedAttributes`, with no reflection involved

#### Scenario: Alegacy Quest Framework is never reflected into or read from
- **WHEN** a player has Alegacy Quest Framework installed instead of (or alongside) either
  supported backend
- **THEN** the system does not attempt to read Alegacy's state under any circumstance

### Requirement: Auto-detection fails safe per backend and never blocks manual Quest Links
Any failure detecting state for a given backend (a reflection error for VS Quest's progress
mirroring, an unexpected attribute shape for either backend, a dialog not found) SHALL be caught
and SHALL silently disable auto-detection **for that backend only**, for the session, rather than
raise a visible error or crash. A failure in one backend's detection SHALL NOT disable the
other's. Manually-created Quest Links (the `link-task` capability's Quest Link requirement) SHALL
continue to work normally regardless of either backend's auto-detection state.

#### Scenario: A reflection failure disables only VS Quest's progress mirroring
- **WHEN** vsquest's dialog fields no longer match what auto-detection expects (e.g. after a
  vsquest update)
- **THEN** VS Quest progress mirroring silently stops attempting further reads for the session,
  while VS Quest accept/completion detection and all Progression Framework detection are
  unaffected

#### Scenario: An attribute-shape failure in one backend doesn't disable the other
- **WHEN** Progression Framework's `WatchedAttributes` tree shape no longer matches what
  auto-detection expects (e.g. after a Progression Framework update)
- **THEN** Progression Framework detection silently stops for the session, while VS Quest
  detection (if active) continues unaffected

#### Scenario: Manually-created Quest Links are unaffected by any detection failure
- **WHEN** any backend's auto-detection has failed and disabled itself for the session
- **THEN** existing and new manually-created Quest Links for either backend continue to work
  normally

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

## ADDED Requirements

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
