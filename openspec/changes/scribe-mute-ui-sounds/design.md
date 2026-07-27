## Context

Adopting LibGUI brought its interaction sounds along for the ride. Players have asked to silence
Scribe's own UI clicks without touching global game audio. Scribe currently has no opt-out.

How LibGUI actually plays a click (verified in `reference/vslibgui/`):

- Exactly **one** widget plays a UI sound: `Button` (`Widgets/Basic/Button.cs:73`), which calls
  `context.GetSoundPlayer().Play("click", …)` on tap. `Checkbox`, `Dropdown`, `Slider`, and
  `RadioButton` play nothing.
- The sound player is injected once per dialog: `GuiBase` constructor calls
  `BuildOwner.SetSoundPlayer(new SoundPlayer(capi))` (`GuiBase.cs:115`). Widgets pull it via
  `BuildContext.GetSoundPlayer()` → `BuildOwner.GetSoundPlayer()` (single `_soundPlayer` field,
  `BuildOwner.cs:22`). `BuildOwner.SetSoundPlayer(ISoundPlayer)` is **public**
  (`BuildOwner.cs:79-82`), and `GuiBase.BuildOwner` is a public property (`GuiBase.cs:134`).
- There is **no** existing mute/volume toggle anywhere in LibGUI — not in `ThemeData`/`Theme.cs`,
  not in `SoundPlayer`, not in any config. `ISoundPlayer` (`Sound/ISoundPlayer.cs`) is a tiny
  two-method interface (`Play`, `Load`).

Which of Scribe's controls actually click today: only real `Button` instances — the Lectern
dialog's action buttons (`GuiDialogScribeLecternLibGui.cs:1601, 2008, 2011`) and the numeric-field
+/- steppers (`ScribeNumericField.cs:281, 315, 316`). Scribe's hand-built `ScribeRowButton` /
`ScribeRowButtonText` wrappers use a raw `GestureDetector` with no `GetSoundPlayer().Play`, so the
title/nav/pin/complete buttons are **already silent**. Scribe also leaves `OpenSound`/`CloseSound`
null, so there are no dialog open/close sounds either.

Settings model (verified): `src/Core/ScribePlayerSettings.cs` is a plain-BCL POCO of per-player,
**client-local, never-synced** preferences, persisted as `scribe-hud-config.json` via
`ScribeModSystem` (`LoadModConfig`/`StoreModConfig`). New bools append as properties (absent keys
default on load; `Normalized()` leaves bools untouched). `UpdateMySettings(Action<…>)` mutates the
singleton, persists, and fires `MyPinsChanged`, which live-rebuilds open HUD/dialogs.
(`ScribeClientConfig.cs` no longer exists — it was retired and folded into these.)

## Goals / Non-Goals

**Goals:**
- A client-local "Mute Scribe UI sounds" boolean (default off) that silences Scribe's own LibGUI
  click sounds, live, with no reopen.
- Zero edits to the LibGUI reference project — Scribe-side only.
- Scope strictly to Scribe's dialogs; leave vanilla and other-mod audio untouched.
- Surface it as a checkbox paired in a column beside the existing "Collapse the HUD" checkbox.

**Non-Goals:**
- A global/other-mod sound control, per-sound volume, or a sound-category system.
- Suppressing dialog open/close or item-slot sounds (Scribe emits none of these).
- Any server sync — this is a client-only preference.
- Modifying LibGUI's `Button`, `ThemeData`, or `SoundPlayer`.

## Decisions

### Decision 1: Suppress via a no-op `ISoundPlayer` swapped into `BuildOwner`
Implement a small Scribe-side `SilentSoundPlayer : ISoundPlayer` (empty `Play`; `Load` returns a
handle over a never-started sound). When the mute preference is on, call
`BuildOwner.SetSoundPlayer(silent)` on each Scribe dialog; when off, install the normal
`SoundPlayer(capi)`. This rides the exact injection point LibGUI already uses and needs no
reference-project change.

- **Why over a theme property:** a `ThemeData` mute flag would force edits to both `Theme.cs` and
  `Button.cs` in the reference project (cross-repo), which the project guardrails and the
  no-new-dependency posture discourage.
- **Why over intercepting `capi.World.LoadSound`:** far too broad — it would touch unrelated game
  audio, violating the Scribe-only scope.
- **Why not a per-Button flag:** none exists; `Button` always plays.

### Decision 2: Store as a client-local bool on `ScribePlayerSettings`
Add e.g. `MuteUiSounds` (default `false`) as a new property, matching the existing
`HudCollapsed`/`PixelArtDisplay` pattern. No clamp needed, so `Normalized()` is untouched. Read via
`MySettings`, written via `UpdateMySettings`. This keeps it in the one live client-config file and
inherits the safe-defaulting-on-load behavior. Core stays game-agnostic (a plain bool; no VS API).

### Decision 3: Re-apply the swap live on toggle via the existing rebuild hook
`SetSoundPlayer` is per-dialog-instance, and the settings dialog is lazily built once and reused
while the Lectern dialog is rebuilt on open — so a toggle must re-install the correct player on any
currently-open dialog. Reuse the existing `MyPinsChanged` event (already fired by
`UpdateMySettings` and already driving live HUD/dialog rebuilds) as the hook: each Scribe dialog
(re)installs the real-or-silent player when it (re)opens/rebuilds by reading `MySettings`, and the
toggle path ensures an open dialog picks up the change without a reopen. This satisfies the
spec's "writes through and takes effect immediately" requirement.

### Decision 4: Present as a paired checkbox column beside "Collapse the HUD"
In `ScribeSettingsContent.cs`, place the new checkbox in the Mod Behavior section as the second
column of a paired row with the collapsed-HUD checkbox (reuse the existing `PairedControls`/
two-column layout used elsewhere in the form), built with the same `HuggingCheckbox` helper. New
localized label + helptext keys go in `en.json`.

## Risks / Trade-offs

- **[Open dialog doesn't re-silence on toggle]** If the swap isn't re-applied to an already-open
  dialog, the change would only take effect on next open → **Mitigation:** wire the (re)install
  through the `MyPinsChanged`/rebuild path (Decision 3) and verify in-game that toggling while a
  Lectern dialog is open takes effect immediately.
- **[LibGUI adds new sound sources later]** A future LibGUI version could make Checkbox/Dropdown
  play sounds → that's *desirable* here: they'd route through the same `GetSoundPlayer()`, so the
  silent player would cover them automatically. No extra work, but note it so a future reviewer
  isn't surprised the mute suddenly covers more.
- **[`Load`-based playback path]** `SilentSoundPlayer.Load` must return a non-null, safe handle so
  any caller using the handle API (none in Scribe today, but defensively) doesn't NRE →
  **Mitigation:** return a handle wrapping a null/never-started sound rather than `null`.
- **[Two-column layout crowding]** Adding a second checkbox column could crowd narrow settings
  widths → **Mitigation:** reuse the established `PairedControls` grouping already used for other
  two-up rows so it inherits the same responsive behavior.

## Open Questions

- None blocking. Exact property name (`MuteUiSounds` vs `MuteScribeSounds`) and lang-key names are
  cosmetic and will be settled during implementation to match existing naming.
