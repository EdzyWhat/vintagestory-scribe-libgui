## Why

When a Clockmaker's Notebook timer reaches zero it flashes on the Pinned Task HUD and is
then auto-cleared after 30 seconds — server-side and unconditional. A player who steps away,
is mid-fight, or simply misses the flash loses the notification entirely with no way to opt
out. Some players want a timer they set (a kiln, a cooking pot, a "log off at" reminder) to
stay put until they personally acknowledge it. This makes the 30-second auto-disappear a
per-player preference instead of a hardcoded behavior.

## What Changes

- Add a per-player **"Timer disappears"** preference (default **on**, preserving today's
  behavior) to the Scribe Settings **Mod Behavior** section, as a labeled checkbox with
  hover helptext beside the existing Collapse-the-HUD / Mute-UI-sounds toggles.
- **On (default):** a fired timer auto-clears ~30 seconds after firing, exactly as today.
- **Off:** a fired timer stays on the HUD (and the Notebook's Timer tab) indefinitely until
  the player dismisses it — by clicking the fired HUD timer row or pressing **Stop Timer** in
  the Clockmaker's Notebook. No new dismissal trigger is introduced; opening the Notebook does
  *not* clear it on its own.
- Move the 30-second auto-clear off the server tick and onto the **client**, driven by the
  player's own preference, so the setting takes effect live for the currently-running timer
  with no world round-trip and no server knowledge of a client-local preference.
- **Persist the Fired state server-side** so an unacknowledged fired timer survives logout /
  rejoin (today only Running timers are saved, so a fired-but-unclicked timer is lost on
  relog). This is required for the Off mode to be meaningful across a session boundary.
- Update the in-game documentation: a new settings Tooltip for the toggle, and a revision to
  the Clockmaker's Notebook **"The Timer"** handbook entry (which currently states the fired
  timer is cleared by click/Stop and omits the auto-disappear behavior).
- Update the GitHub wiki: a new entry under **Scribe-Settings → Mod Behavior**, and a note in
  the Clockmaker's Notebook timer coverage.

## Capabilities

### New Capabilities
- `timer-lifecycle`: The lifecycle of a Clockmaker's Notebook countdown timer once it fires —
  how a fired timer is displayed, dismissed, auto-cleared (or not) under the player's
  preference, and persisted across a session. Captures behavior that exists in the code today
  but has never had a spec, plus the new preference-gated auto-disappear.

### Modified Capabilities
- `settings-tab`: Adds the "Timer disappears" preference to the Mod Behavior section — a new
  labeled boolean control with localized helptext, following the same write-through-immediately
  and per-control-helptext requirements the section already defines.

## Impact

- **Code (`src/Core/`):** new `bool TimerAutoDisappear { get; set; } = true;` on
  `ScribePlayerSettings` (a plain bool, no clamp — `Normalized()` untouched, matching
  `MuteUiSounds`). Optionally a named constant for the 30 s window if one is introduced.
- **Code (`src/Mod/`):**
  - `ScribeModSystem.OnTimerTick` — stop the server-side 30 s auto-clear (server keeps
    tracking `Fired` but no longer removes the store on its own).
  - Client-side — drive the auto-clear from the existing `TimerDisplayTick` (or the timer
    state change), sending the existing `ScribeClearTimerMessage` after 30 s **only when
    `TimerAutoDisappear` is on**.
  - `ScribeSettingsContent.BuildModBehaviorSection` — add the checkbox.
  - Timer save/load (`OnSaveGameLoaded` / save path) — persist `Fired` timers, not just
    `Running`, including the elapsed-since-fired counter needed to resume the countdown.
- **Assets:** `lang/en.json` — `settings-timerdisappear` + `settings-timerdisappear-help`
  keys; revise `handbook-scribeclockmakernotebook-timer-text`.
- **Docs (GitHub wiki, out-of-repo):** `Scribe-Settings.md` (Mod Behavior), `Items.md`
  (Clockmaker's Notebook timer note).
- **No new dependencies.** Client-local preference — never server-synced. No codec version
  bump for `ScribePlayerSettings` (JSON defaults an absent key). The `TimerStore` codec **may**
  need a version bump if the persisted Fired-elapsed value is added to its serialized form.
