## MODIFIED Requirements

### Requirement: Settings are grouped into Behavior and Appearance sections
The settings surface SHALL present its controls in three labeled sections separated by horizontal
dividers: a Mod Behavior section, a Window Appearance section, and a HUD Appearance section. The Mod
Behavior section SHALL contain the
completion policy and the mute-UI-sounds toggle. The Window Appearance section SHALL contain the window
font-size scale, the Pixel Art Display toggle, the Pixel Art Size, and the **task font selector**. The
HUD Appearance section SHALL contain the HUD anchor, HUD maximum rows, HUD row width, HUD horizontal
and vertical offsets, the HUD font-size scale, and the collapsed-HUD toggle.

#### Scenario: Font selector appears under Window Appearance
- **WHEN** the settings surface is shown
- **THEN** the task font selector control is present in the Window Appearance section, alongside the
  window font-size scale and Pixel Art controls

#### Scenario: Controls appear under their section
- **WHEN** the settings surface is shown
- **THEN** the completion-policy and mute-sounds controls appear under Mod Behavior; the window
  font-scale, pixel art, and font-selector controls appear under Window Appearance; and the HUD
  position/size and HUD font-scale controls appear under HUD Appearance

#### Scenario: Sections are visually separated
- **WHEN** the settings surface is shown
- **THEN** a horizontal divider separates each of the three sections from the next
