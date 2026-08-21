# tablet-state-dev-command Specification

## Purpose
TBD - created by archiving change add-tablet-state-dev-command. Update Purpose after archive.
## Requirements
### Requirement: Dev command sets a held tablet's life-cycle state

The mod SHALL provide a server dev sub-command `/scribe tablet <state>` where `<state>` is one of
`wet`, `hard`, or `fired`, which sets the calling player's currently held Scribe Tablet to that
life-cycle state by swapping its `material` variant to the corresponding sibling (`clay-<color>` /
`clay-<color>-hard` / `clay-<color>-fired`) and carrying the tablet's stored document and history
onto the swapped stack. The command SHALL be gated identically to `/scribe seed`
(`controlserver` privilege plus an in-handler creative-mode check) and SHALL run server-side so the
swapped stack syncs authoritatively. It SHALL NOT alter the natural `Harden` transition or firepit
firing paths, and SHALL NOT change any persistence format.

#### Scenario: Fire a wet clay tablet for testing

- **WHEN** a creative-mode player holding a wet `clay-red` Scribe Tablet runs `/scribe tablet fired`
- **THEN** the held stack becomes the `clay-red-fired` tablet, read-only, with its document and
  history preserved, and the command reports success

#### Scenario: Document survives the swap

- **WHEN** the held tablet carries a written document and the player runs `/scribe tablet hard`
- **THEN** the resulting hardened tablet contains the same document and history (no content loss),
  matching the carry-across behavior of the natural dry/fire transitions

#### Scenario: Wax cannot harden or fire

- **WHEN** the player holds a `wax` tablet and runs `/scribe tablet hard` (or `fired`)
- **THEN** no swap occurs and the command reports that wax tablets never harden/fire; `wet` on a wax
  tablet is a no-op success

#### Scenario: Fired can be reset to wet as a deliberate dev override

- **WHEN** the player holds a `fired` clay tablet and runs `/scribe tablet wet`
- **THEN** the held stack becomes the wet `clay-<color>` tablet (document preserved) and the command
  reports that it applied a testing override of the normally-permanent fired state

#### Scenario: No held tablet

- **WHEN** the player runs `/scribe tablet fired` while not holding a Scribe Tablet
- **THEN** the command reports that no held tablet was found and makes no change

#### Scenario: Gating

- **WHEN** a player without `controlserver` privilege, or in survival mode, runs `/scribe tablet <state>`
- **THEN** the command is refused (privilege/creative gate), exactly as `/scribe seed` is

