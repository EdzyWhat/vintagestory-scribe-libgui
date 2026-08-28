## ADDED Requirements

### Requirement: New Task Insert dropdown
The Mod Behavior section SHALL include a **New Task Insert** dropdown with two localized values,
**Top** (default) and **Bottom**. Changing it SHALL persist on `ScribePlayerSettings` immediately
(same write-through as other Behavior dropdowns). Helptext SHALL state that it applies to the
footer Add control, Shift+right-click quick-add, and Handbook Add to Scribe.

#### Scenario: Dropdown is in Mod Behavior
- **WHEN** the settings surface is shown
- **THEN** a New Task Insert dropdown listing Top and Bottom is present in the Mod Behavior section

#### Scenario: Choosing Bottom persists
- **WHEN** the player selects Bottom
- **THEN** subsequent document-level creates append until they change the setting again
