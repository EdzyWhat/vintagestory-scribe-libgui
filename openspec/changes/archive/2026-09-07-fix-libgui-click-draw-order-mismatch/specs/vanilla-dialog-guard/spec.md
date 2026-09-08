## Purpose

Lets any Scribe surface check whether a vanilla (non-LibGUI) dialog is currently open, so it can
decline to consume a click or open a new modal-style surface rather than contest a click it can never
correctly win against something it will always paint underneath.

## ADDED Requirements

### Requirement: A reusable check reports whether a vanilla dialog is currently open
The system SHALL provide a single reusable check, callable from any Scribe surface (a dialog or a HUD
element alike), that reports true when at least one currently-open game dialog is neither a LibGUI
(`Gui.GuiBase`) window nor a HUD-type overlay (`DialogType == HUD`) — i.e. a vanilla Cairo/GL dialog
such as the base-game Handbook, Inventory, a chest, or a foreign mod's own dialog (e.g. Progression
Framework's Ledger). The check SHALL be read-only: it SHALL NOT close, focus, or otherwise mutate any
dialog it inspects.

#### Scenario: No vanilla dialog open reports false
- **WHEN** no vanilla dialog is currently open (only Scribe/LibGUI windows and/or HUD overlays, or
  nothing at all)
- **THEN** the check reports false

#### Scenario: The base-game Handbook open reports true
- **WHEN** the base-game Handbook is open
- **THEN** the check reports true

#### Scenario: A foreign mod's vanilla dialog open reports true
- **WHEN** a foreign mod's own vanilla (non-LibGUI) dialog is open (e.g. Progression Framework's
  Ledger)
- **THEN** the check reports true

#### Scenario: Another LibGUI window open does not report true on its own
- **WHEN** only another LibGUI window (e.g. PlayerInvUI's inventory) is open, with no vanilla dialog
  open
- **THEN** the check reports false

#### Scenario: A HUD overlay open does not report true on its own
- **WHEN** only always-on HUD overlays (e.g. the pinned-task HUD, minimap) are open, with no vanilla
  dialog open
- **THEN** the check reports false

### Requirement: The guard does not alter click dispatch or rendering
This check is advisory only — callers decide what to do with it (e.g. decline to open a new surface).
It SHALL NOT change `DrawOrder`, patch the engine's click-dispatch loop, or otherwise attempt to make
Scribe correctly win or lose an already-in-flight click against a vanilla dialog. Perfect click/paint
z-order arbitration against vanilla dialogs remains a known, deliberately parked limitation.

#### Scenario: Using the guard does not change existing click behavior
- **WHEN** a Scribe surface consults the guard before deciding to open a new modal-style surface
- **THEN** no existing dialog's `DrawOrder`, focus behavior, or click handling changes as a result
