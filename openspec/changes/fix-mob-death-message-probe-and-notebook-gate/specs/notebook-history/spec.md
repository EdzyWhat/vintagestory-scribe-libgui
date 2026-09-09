## ADDED Requirements

### Requirement: Mob-death flavor pool size discovery does not trigger translation-format warnings
The system SHALL discover the size of the `scribe-mob-death-N` flavor pool without formatting any
pool template against zero arguments. Discovering the pool's size SHALL NOT trigger the
translation service's own error/warning logging, regardless of how many entries the pool contains
or how many placeholders its templates use. The flavor pool's existing per-death behavior — a
randomly-selected, variant-correct, fully-substituted line naming the killing creature — SHALL be
unaffected: the same pool, the same random selection, the same final message text as before this
change.

#### Scenario: Creature kill selects a flavored line with no warning logged
- **WHEN** a player carrying a Notebook is killed by a creature
- **THEN** the Death entry's message is a fully-substituted line from the `scribe-mob-death-N` pool
  naming the creature, and no translation-format warning is logged as a side effect of selecting it

#### Scenario: Pool size reflects all shipped entries
- **WHEN** the mob-death flavor pool contains N contiguous entries (`scribe-mob-death-0` through
  `scribe-mob-death-(N-1)`)
- **THEN** every one of those N entries remains reachable as a possible selection, identical to
  before this change

### Requirement: Death and PvpKill message construction is skipped when no relevant Notebook exists
Before constructing any Death or PvpKill message text (mob-death flavor pool selection, PvP verb
resolution, or vanilla environmental-message reconstruction), the system SHALL check whether at
least one relevant party carries a Notebook: for a non-PvP death, the dying player; for a PvP kill,
the dying player OR the killing player. When neither relevant party carries a Notebook, the system
SHALL skip message construction entirely and record nothing, avoiding wasted computation on a death
event with no Notebook anywhere to record it. When at least one relevant party carries a Notebook,
message construction proceeds exactly as it does today and is written to every Notebook the
relevant party carries.

#### Scenario: Non-PvP death with no notebook constructs no message
- **WHEN** a player who carries no Notebook dies to a creature or environmental cause
- **THEN** no Death message is constructed and no history entry is written anywhere

#### Scenario: PvP kill with neither party carrying a notebook constructs no message
- **WHEN** a player who carries no Notebook is killed by another player who also carries no
  Notebook
- **THEN** no Death or PvpKill message is constructed and no history entry is written anywhere

#### Scenario: PvP kill where only the killer carries a notebook still constructs the message
- **WHEN** a player carrying no Notebook is killed by a player who does carry a Notebook
- **THEN** the shared PvP message is constructed, a PvpKill entry is written to the killer's
  notebook(s), and no Death entry is written for the victim

#### Scenario: PvP kill where only the victim carries a notebook still constructs the message
- **WHEN** a player carrying a Notebook is killed by a player who carries no Notebook
- **THEN** the shared PvP message is constructed, a Death entry is written to the victim's
  notebook(s), and no PvpKill entry is written for the killer
