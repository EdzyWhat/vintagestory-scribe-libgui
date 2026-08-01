## MODIFIED Requirements

### Requirement: Death event recorded when holder dies while carrying notebook
The system SHALL record a `Death` entry on the server when a player dies while holding a
Notebook in their active hotbar. The Detail field SHALL contain the reconstructed vanilla
death message for that player and damage source. When the killing damage was dealt by another
player (resolved via the damage source's cause entity, which covers both melee and projectile
attacks), the Detail SHALL instead be a mod-owned PvP death message that names the killer, the
victim, and a kill verb, rather than falling back to an unattributed "died" message.

#### Scenario: Death while holding records entry
- **WHEN** a player holding a Notebook in their hotbar dies from any cause
- **THEN** a Death entry is added with their name, the reconstructed death message, and
  the in-game date

#### Scenario: Death by another player names the killer with a weapon-aware verb
- **WHEN** a player holding a Notebook is killed by another player, whether by melee or
  projectile
- **THEN** the Death entry's message names the killer and uses a kill verb chosen from the
  killer's weapon category when available (e.g. a bow → "shot", a sword → "slashed"), and does
  NOT fall back to the generic "<victim> died." message

#### Scenario: Kill verb degrades gracefully for unknown weapons
- **WHEN** the killer's weapon has no recognized tool category but the damage carries a known
  damage type (e.g. a modded weapon dealing piercing damage)
- **THEN** the kill verb is derived from the damage type; and when neither a tool category nor a
  damage-type mapping is available, a generic kill verb is used with no immediate repeat across
  successive kills recorded on the same notebook

#### Scenario: Death without notebook records nothing
- **WHEN** a player dies while NOT holding a Notebook
- **THEN** no Death entry is added to any Notebook

### Requirement: PvpKill event recorded when holder kills another player
The system SHALL record a `PvpKill` entry on the server when the holder of a Notebook
delivers the killing blow to another player. The killer SHALL be resolved from the damage
source's cause entity so that melee kills are attributed, not only projectile kills.

#### Scenario: Killing another player records entry
- **WHEN** a player holding a Notebook kills another player
- **THEN** a PvpKill entry is added to the killer's notebook naming the victim and using the same
  weapon-aware kill verb as the victim's Death entry

#### Scenario: Melee kill is attributed
- **WHEN** a player holding a Notebook kills another player with a melee weapon (a case where
  the damage source's direct source entity is null)
- **THEN** a PvpKill entry is still added to the killer's notebook naming the victim

#### Scenario: Self-inflicted or environmental death records no PvpKill
- **WHEN** a notebook holder dies to a non-player cause, or by their own hand
- **THEN** no PvpKill entry is added to any notebook
