## ADDED Requirements

### Requirement: A preference toggles the storm-corruption effect

The settings surface SHALL provide a labeled, localized-helptext control that toggles the temporal
storm-corruption HUD effect (both the text corruption and the storm title swap). The control SHALL
default to on. When off, the HUD SHALL never corrupt its text or swap its title regardless of storm
or stability state. The setting SHALL be client-local (a display/behavior preference), consistent
with the other Scribe client preferences, and SHALL write through immediately.

#### Scenario: Disabling the effect stops corruption immediately

- **WHEN** the player turns the storm-corruption setting off while a storm is active
- **THEN** the HUD immediately renders normal, uncorrupted text with the normal "Pinned" title

#### Scenario: Default is on

- **WHEN** a player has never changed the setting
- **THEN** the storm-corruption effect is active by default
