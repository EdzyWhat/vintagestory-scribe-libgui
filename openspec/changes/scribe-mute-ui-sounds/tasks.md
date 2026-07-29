## 1. Add the client-local preference

- [x] 1.1 In `src/Core/ScribePlayerSettings.cs`, add a `bool MuteUiSounds` property defaulting to
  `false` (sounds on), following the existing `HudCollapsed`/`PixelArtDisplay` bool pattern. Confirm
  `Normalized()` needs no change (bools aren't clamped) and add a brief doc note like the
  `PixelArtDisplay` one.
- [x] 1.2 Add a Core unit test in `tests/Core.Tests` asserting the new bool defaults to `false` and
  round-trips through serialization / `Normalized()` unchanged (mirror an existing bool-preference
  test if one exists).

## 2. Silent sound player + injection

- [x] 2.1 Add a Scribe-side `SilentSoundPlayer : ISoundPlayer` in `src/Mod/` implementing the
  `ISoundPlayer` interface (`reference/vslibgui/Gui/Gui/Sound/ISoundPlayer.cs`): `Play(...)` is a
  no-op; `Load(...)` returns a non-null `SoundHandle` over a null/never-started sound (never return
  `null`).
- [x] 2.2 In each Scribe `GuiBase` dialog (`GuiDialogScribeLecternLibGui`, `ScribeSettingsDialog`),
  after `base(capi)` runs, install the correct player onto `BuildOwner` based on
  `MySettings.MuteUiSounds` — `BuildOwner.SetSoundPlayer(muted ? silent : new SoundPlayer(capi))`.
  Do this where the dialog is built/opened so a rebuild re-reads the current preference. Keep a
  single shared `SilentSoundPlayer` instance (it's stateless) rather than allocating per rebuild.
- [x] 2.3 Ensure a live toggle re-applies to an already-open dialog: hook the swap through the
  existing `MyPinsChanged`/rebuild path fired by `UpdateMySettings` (`ScribeModSystem.cs`), so
  flipping the setting while a Lectern dialog is open takes effect without reopening.

## 3. Settings UI control

- [x] 3.1 In `src/Mod/ScribeSettingsContent.cs`, add a `HuggingCheckbox` for `MuteUiSounds` in the
  **Mod Behavior** section, reading `settings.MuteUiSounds` and writing via
  `onMutate(s => s.MuteUiSounds = v)` (i.e. `UpdateMySettings`).
- [x] 3.2 Lay it out as a second column paired beside the existing "Collapse the HUD" checkbox on
  one row, reusing the existing `PairedControls`/two-column grouping helper used elsewhere in the
  form.
- [x] 3.3 In `src/Mod/assets/scribe/lang/en.json`, add the localized label and helptext keys for the
  mute control (following the existing `settings-*` key naming), and wire the helptext through the
  same tooltip path the other settings controls use.

## 4. Build, test, restage, verify

- [x] 4.1 `dotnet build src/Mod/Mod.csproj --nologo` clean; run
  `dotnet test tests/Core.Tests/Core.Tests.csproj` green.
- [x] 4.2 Restage (`bash build/restage.sh Debug`) and fully relaunch the client.
- [x] 4.3 In-game: with mute OFF (default), Scribe's action/stepper buttons still click; enable the
  toggle and confirm those buttons go silent immediately (no reopen). Toggle it back and confirm the
  click returns. — confirmed 2026-07-29
- [x] 4.4 In-game: confirm the two checkboxes ("Collapse the HUD" + "Mute Scribe UI sounds") sit on
  one row as two columns in Mod Behavior, both labeled with working helptext. — confirmed 2026-07-29
- [x] 4.5 In-game: confirm the preference persists across a relog and that vanilla/other-mod sounds
  (e.g. block break, inventory) are unaffected while Scribe is muted. — confirmed 2026-07-29
- [x] 4.6 Update `TESTING.md` with the new in-game items.
