## ADDED Requirements

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
