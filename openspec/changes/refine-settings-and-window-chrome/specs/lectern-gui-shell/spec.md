## ADDED Requirements

### Requirement: The title bar shows a drag-grip affordance
The Lectern dialog's title-bar button row SHALL include a drag-grip icon (the mod's registered
`scribegrip` SVG) positioned to the LEFT of the close button, so the fully-draggable title-bar band is
visually discoverable. The grip SHALL be a passive affordance marking the drag zone — it SHALL be tinted
as a non-primary control and SHALL provide a localized tooltip indicating the band can be dragged to move
the window. The window's drag behavior SHALL remain owned by the title-bar band itself (the grip does not
need its own drag gesture), so dragging works anywhere in the band, not only on the grip.

#### Scenario: The drag grip appears left of the close button
- **WHEN** the Lectern dialog is open
- **THEN** a drag-grip icon (the `scribegrip` SVG) is shown immediately to the left of the title bar's
  close button, and hovering it shows a tooltip indicating the title bar can be dragged to move the window

#### Scenario: Dragging still works across the whole band
- **WHEN** the player click-drags anywhere within the title-bar band (not only on the grip icon)
- **THEN** the window moves, since the drag zone is the whole band and the grip is only a discoverability cue
