## ADDED Requirements

### Requirement: Pinned notes appear on the Pin Tab and can be unpinned there
The Pin Tab SHALL list pinned Text notes. A note row SHALL offer unpin (and delete, following the
existing delete-from-Pin-Tab path). The row SHALL NOT complete the note (notes have no done flag).
Ticking a note's Pin Tab checkbox, if one is shown, SHALL unpin the note rather than marking it done.

#### Scenario: Unpin a note from the Pin Tab
- **WHEN** the player unpins a Text note from the Pin Tab
- **THEN** the note leaves the pin set and the HUD, and the source document still contains the note
