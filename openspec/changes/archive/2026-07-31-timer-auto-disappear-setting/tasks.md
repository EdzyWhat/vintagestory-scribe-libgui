## 1. Core: preference + codec

- [x] 1.1 Add `public bool TimerAutoDisappear { get; set; } = true;` to `ScribePlayerSettings` with an XML doc comment (client-local, default on, no clamp — leave `Normalized()` untouched, mirroring `MuteUiSounds`).
- [x] 1.2 Bump `TimerStore` to codec v2 per `docs/CODEC-MIGRATION.md`: add a `double FiredElapsedSeconds` property, append it after `RemainingSeconds` in `Serialize`, read it in `Deserialize`, set `Version = 2` and keep `PriorVersion = 1` accepted (a v1 blob loads with `FiredElapsedSeconds = 0`).
- [x] 1.3 Add a canonical fired auto-clear window constant (~30 s) with one home (replacing the bare `30.0` literal), referenced by the client auto-clear logic.

## 2. Core tests

- [x] 2.1 Unit-test `TimerStore` round-trip for a v2 blob (Fired with a non-zero `FiredElapsedSeconds`) in `tests/Core.Tests`.
- [x] 2.2 Unit-test that a v1 blob still deserializes (backward compat) and yields `FiredElapsedSeconds = 0`.
- [x] 2.3 Confirm `ScribePlayerSettings.Normalized()` leaves `TimerAutoDisappear` untouched and that an absent JSON key defaults to `true` (add/extend a settings test if one exists).

## 3. Server: stop auto-removing fired timers, persist Fired

- [x] 3.1 In `ScribeModSystem.OnTimerTick`, keep flipping Running→Fired but remove the `elapsed >= 30` auto-clear branch that deletes the store; the server no longer times the disappearance.
- [x] 3.2 Keep the fired-elapsed value correct on the store: write `store.FiredElapsedSeconds` (from the transition + tick accumulation) so it is available to persist. Resolved the Open Question — `TimerStore.FiredElapsedSeconds` is the single source of truth; the separate `timerFiredElapsed` dictionary is removed, and the value is carried to the client on `ScribeTimerStateMessage`.
- [x] 3.3 Update `OnGameWorldSave` to persist `Running || Fired` timers (not Running-only), and update its comment (the old comment explains why Fired was dropped — that rationale no longer holds).
- [x] 3.4 Update `OnSaveGameLoaded` to restore `Running || Fired` timers (not Running-only), preserving `FiredElapsedSeconds` so the resumed auto-clear window is `window − elapsed`, and update its comment.

## 4. Client: preference-gated auto-clear

- [x] 4.1 In the client, track a fired-elapsed accumulator that resets when `MyTimer` transitions into `Fired` (reuse `HudScribePins`'s `_lastTimerStatus` transition hook from commit 74320f9) and increments on `TimerDisplayTick`; seed it from `MyTimer.FiredElapsedSeconds` on the state push so a resumed timer continues its window.
- [x] 4.2 On each `TimerDisplayTick`, if `MyTimer.Status == Fired`, the accumulator has passed the window, AND `MySettings.TimerAutoDisappear` is on, send `ScribeClearTimerMessage` (reuse the existing packet; do not add a new one).
- [x] 4.3 Verify that turning the preference off mid-flash stops the client from ever sending the clear (the auto-clear check re-evaluates the live preference each tick), and that the fired row remains until the player clicks it or presses Stop. — Handled by re-reading `MySettings.TimerAutoDisappear` inside `OnTimerDisplayTick` each tick; a one-shot `_firedAutoClearSent` guard prevents duplicate sends during the clear round-trip. Manual in-game confirmation is tracked in task 8.2.

## 5. Settings UI

- [x] 5.1 Add a `HuggingCheckbox("settings-timerdisappear", value: settings.TimerAutoDisappear, onChanged: v => onMutate(s => s.TimerAutoDisappear = v))` to `ScribeSettingsContent.BuildModBehaviorSection`, placed with the other behavior toggles (pair it into a row per the section's existing paired-checkbox layout if a slot is free).
- [x] 5.2 Add `settings-timerdisappear` label and `settings-timerdisappear-help` tooltip keys to `assets/scribe/lang/en.json`.

## 6. In-game documentation

- [x] 6.1 Revise `handbook-scribeclockmakernotebook-timer-text` in `en.json`: state that a fired timer disappears from the HUD after ~30 seconds by default and that this can be turned off ("Timer disappears" in Scribe Settings) to keep it until clicked or stopped.

## 7. GitHub wiki (out-of-repo: EdzyWhat/vintagestory-scribe-libgui.wiki)

- [x] 7.1 Add a "Timer disappears" entry under `Scribe-Settings.md` → Mod Behavior, describing the on (default, ~30 s auto-clear) and off (stays until dismissed) behaviors. — Pushed to wiki (commit cae38db).
- [x] 7.2 Add a sentence to `Items.md`'s Clockmaker's Notebook timer coverage noting the fired-timer disappearance behavior and the setting. — `Items.md` still only documents the Lectern (no Clockmaker section exists yet); documented the fired-timer behavior on `Pinned-Task-HUD.md` instead, where the timer actually appears, linking to the new setting. Pushed (commit cae38db).

## 8. Verification

- [x] 8.1 `dotnet build` and `dotnet test` (Core suite) pass. — Mod build succeeds (0 errors; 3 pre-existing warnings unrelated to this change); Core suite 183/183 pass including the 4 new tests.
- [x] 8.2 Regenerate/append the in-game manual checks to `TESTING.md` (via the `what-to-test` skill) for: default-on 30 s disappear; off → stays until HUD click; off → stays until Stop; opening the notebook does not clear it; toggle off mid-flash cancels the pending clear; fired timer survives relog and resumes (not restarts) the window. — Added 6 items under a new `timer-auto-disappear-setting` group in TESTING.md.
