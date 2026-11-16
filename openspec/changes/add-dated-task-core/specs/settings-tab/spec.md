## MODIFIED Requirements

### Requirement: Settings are grouped into Behavior and Appearance sections
The settings surface SHALL present its controls in three labeled sections separated by horizontal
dividers: a Mod Behavior section, a Window Appearance section, and a HUD Appearance section. The Mod
Behavior section SHALL contain task-policy dropdowns (completion, tracker completion, subtask,
new-task insert), the mute-UI-sounds toggle, and Clockmaker timer prefs (Timer disappears and Alarm
Volume) — always shown, not gated on the Clockmaker trait. The Window Appearance section SHALL contain
the Pixel Art Display toggle, Pixel Art Size, window font-size scale, the **task font selector**, the
**Date order** dropdown, and the cuneiform tablets / press-in toggles. The HUD Appearance section SHALL contain the collapsed-HUD
toggle, storm-corruption toggle, HUD icons, HUD settings-gear toggle, HUD anchor, HUD maximum rows,
HUD row width, HUD horizontal and vertical offsets, and the HUD font-size scale.

#### Scenario: Font selector appears under Window Appearance
- **WHEN** the settings surface is shown
- **THEN** the task font selector control is present in the Window Appearance section, alongside the
  window font-size scale and Pixel Art controls

#### Scenario: Controls appear under their section
- **WHEN** the settings surface is shown
- **THEN** the completion-policy, mute-sounds, and timer controls appear under Mod Behavior; the window
  font-scale, pixel art, font-selector, and cuneiform controls appear under Window Appearance; and the
  collapsed-HUD, storm-corruption, and HUD position/size/font-scale controls appear under HUD Appearance

#### Scenario: Sections are visually separated
- **WHEN** the settings surface is shown
- **THEN** a horizontal divider separates each of the three sections from the next

#### Scenario: Date order appears under Window Appearance
- **WHEN** the settings surface is shown
- **THEN** the Date order dropdown is present in the Window Appearance section, with its localized
  label, helptext and the options Language default, Day-Month-Year, Month-Day-Year and Year-Month-Day

#### Scenario: Changing Date order updates open views
- **WHEN** a player changes Date order while a History, Guest Book, Inbox or Pin Tab view is open in
  another Scribe dialog
- **THEN** that view's timestamp-built dates show the new order without reopening the dialog
