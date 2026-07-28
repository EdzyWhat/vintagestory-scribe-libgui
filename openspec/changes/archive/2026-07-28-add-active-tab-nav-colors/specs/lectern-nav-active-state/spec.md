## ADDED Requirements

### Requirement: The active nav button shows its thematic color

Each of the Lectern's four sidebar nav buttons SHALL display a distinct thematic color when its
target view/surface is the currently active one, and SHALL keep the existing neutral resting style
when it is not. When active, the button's box SHALL fill with the thematic color and its glyph SHALL
switch to a cream tone (`#eae6dd`) for contrast. At most one nav button SHALL be shown active for the
lectern's own view at a time (the Settings button is governed separately, below).

The thematic colors SHALL be: Read `#465481`, Edit `#9d4b44`, Pinned `#6b8257`, Settings `#746f66`.

#### Scenario: The current view's button is highlighted

- **WHEN** the lectern is showing the Read view
- **THEN** the Read nav button's box is filled with `#465481` and its glyph is `#eae6dd`, while the
  Edit and Pinned buttons remain in the neutral resting style

#### Scenario: Switching views moves the highlight

- **WHEN** the player switches from Read to the Editor view
- **THEN** the Edit button becomes the highlighted one (`#9d4b44` fill, cream glyph) and the Read
  button returns to the neutral resting style

### Requirement: The Settings button reflects the standalone settings window

The Settings nav button SHALL show its active thematic color whenever the standalone settings window
is open, and return to the neutral resting style when it is closed. Because the settings window is a
separate dialog that can be open alongside the lectern (not a lectern view), the lectern SHALL update
the Settings button's appearance in response to the settings window opening or closing, without
requiring the player to interact with the lectern.

#### Scenario: Opening settings highlights the gear

- **WHEN** the settings window is opened (from the lectern gear or the HUD gear) while the lectern is
  visible
- **THEN** the Settings nav button shows its active color (`#746f66` fill, cream glyph) even though
  the lectern's own view (Read/Edit/Pinned) is unchanged

#### Scenario: Closing settings restores the gear

- **WHEN** the open settings window is closed
- **THEN** the Settings nav button returns to the neutral resting style

### Requirement: Hovering the active button brightens it

Hovering the currently-active nav button SHALL brighten its thematic fill by 10 HSV Brightness points
(so the active button still gives interactive feedback distinct from its resting active state).
Inactive buttons SHALL retain their existing neutral hover behavior.

#### Scenario: Hover brightens the active fill

- **WHEN** the pointer hovers the active nav button
- **THEN** its fill is the thematic color brightened by +10 HSV Brightness points

#### Scenario: Inactive hover is unchanged

- **WHEN** the pointer hovers a nav button that is not active
- **THEN** it uses the existing neutral hover style (no thematic color)
