## MODIFIED Requirements

### Requirement: The HUD's screen position is configurable

The system SHALL anchor the HUD to one of seven screen positions — top-left, top-middle, top-right,
middle-left, middle-right, bottom-left, bottom-right — defaulting to **top-left**, as a client-local
per-player preference. Each anchor SHALL support a configurable pixel X/Y offset so the HUD can be
nudged clear of other on-screen overlays (the minimap, coordinate overlay, and block-info overlay).
The top-right anchor SHALL be pre-offset far enough to the left that the HUD does not render
underneath the default top-right minimap **at any GUI Scale setting the player has chosen** — the
clearance SHALL be a small, consistent visual gap, not an amount that only happens to be correct at
one particular GUI Scale value and drifts (overlapping, or sitting an excessive/inconsistent distance
away) at others. The HUD's task-row area SHALL be a fixed width. Selecting the anchor and offsets
from an in-mod settings UI is out of scope for this change (the values are config-editable now; the
UI is a later change).

#### Scenario: The default position clears the minimap

- **WHEN** the player has pins and has not changed the HUD position preference
- **THEN** the HUD renders anchored top-left, with no minimap-clearance pre-offset applied (the
  default minimap is top-right, so there is nothing to clear)

#### Scenario: The top-right anchor clears the minimap at any GUI Scale

- **WHEN** the player selects the top-right anchor, the minimap is shown, and the player's GUI Scale
  setting is not the game's reference value
- **THEN** the HUD still renders clear of the minimap by a small, consistent gap — neither
  overlapping it nor sitting a noticeably larger gap away from it than at the reference GUI Scale

#### Scenario: Changing the anchor is honored

- **WHEN** the player changes the HUD anchor preference (e.g. to bottom-right) and reloads
- **THEN** the HUD renders at the new anchor on its next show

#### Scenario: An offset nudges the HUD clear of an overlay

- **WHEN** the player sets a nonzero X/Y offset for the active anchor
- **THEN** the HUD is displaced by that offset from the anchored corner/edge

#### Scenario: An existing saved top-right preference is unaffected by the default change

- **WHEN** a player's client-local config already has an explicit `TopRight` anchor preference saved
  (whether they chose it deliberately or it was written while top-right was still the default)
- **THEN** their HUD continues to render top-right, with the corrected (GUI-Scale-consistent) minimap
  clearance from this change, and is not silently moved to top-left
