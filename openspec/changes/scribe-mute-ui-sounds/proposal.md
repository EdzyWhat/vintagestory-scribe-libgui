## Why

Adopting LibGUI brought along its built-in interaction sounds (clicks on buttons, checkboxes,
and similar controls). Players have told us they find Scribe's UI noise unwanted and would like
to silence just this mod's sounds without touching their global game audio. Scribe has no way to
opt out today, so the sounds are effectively mandatory.

## What Changes

- Add a new **client-local** boolean preference — "Mute Scribe UI sounds" (default off, i.e.
  sounds on) — that suppresses the click/interaction sounds LibGUI plays for Scribe's own
  dialogs and HUD, without affecting any other mod's or the vanilla game's audio.
- Surface the toggle as a checkbox in the settings surface's **Mod Behavior** section, laid out
  as a second column beside the existing "Collapse the HUD" checkbox (a paired two-column row).
- Honor the preference live: toggling it takes effect immediately for Scribe's UI, with no
  reopen or apply step, consistent with the surface's existing write-through behavior.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `settings-tab`: adds a new UI-sound-mute preference to the settings surface — a new checkbox
  control in the Mod Behavior section, paired in a column beside the collapsed-HUD checkbox, that
  writes through immediately like every other control.

## Impact

- **Settings model**: a new client-local boolean preference (stored alongside the other
  client-side settings — this is a client-only display/audio preference, not server-authoritative
  document state).
- **Settings UI** (`src/Mod/ScribeSettingsContent.cs`): a new paired checkbox column in Mod
  Behavior; new localized label + helptext keys in `en.json`.
- **Sound suppression mechanism** (`src/Mod/`): a hook that makes Scribe's LibGUI widgets honor
  the mute flag when they would otherwise play a sound (exact mechanism resolved in design.md
  against the LibGUI sound API).
- No Core/game-agnostic behavior changes beyond possibly holding the new preference field; no new
  mod dependencies; no server sync (client-local).
