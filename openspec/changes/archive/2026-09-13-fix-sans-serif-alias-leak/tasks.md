## 1. Font registration change

- [x] 1.1 In `ScribeModSystem.Assets.cs`'s `RegisterCustomFonts()`, remove the
  `FontRegistry.RegisterFontAlias("sans-serif", family)` call and its associated log line. Verify by
  grepping the file: no remaining call to `RegisterFontAlias`.
- [x] 1.2 Keep the existing fallback-chain loop (`registeredFamilies` walk over Noto Sans → Noto
  Serif → Scapholene → La Belle Aurore → Caudex) but have it select and return/store the chosen
  family name (or `"sans-serif"` if none registered) instead of aliasing it. Verify by reading the
  method: the loop's `break`-selected family is captured into a value passed forward, not discarded.
- [x] 1.3 Thread that resolved family into `ScribeTaskFont` (mirroring how `caudexRegistered` is
  already passed to `BuildMetrics`) and change `ScribeTaskFont.DefaultFamily` in
  `ScribeRowConstants.cs` from the literal constant `"sans-serif"` to the resolved value. Verify by
  reading `ScribeRowConstants.cs`: `DefaultFamily` is no longer a compile-time `"sans-serif"` literal
  (or is set from the resolved value at the same client-init call site as `BuildMetrics`).

## 2. Verification

- [x] 2.1 Run the Core test suite (`dotnet test tests/Core.Tests`) and confirm it still passes
  unchanged (this change touches only `src/Mod`).
- [x] 2.2 Restage a Debug build (`build/restage.sh Debug`) per [[restage-before-handoff-to-testing]]
  and confirm the client log shows the new "[scribe] default family resolved to '<family>' (no global
  sans-serif alias)"-style notification (or equivalent) instead of the old "aliased to bundled font"
  line, and shows no `RegisterFontAlias`-related warning. Confirmed 2026-09-12: `client-main.log`
  shows `[Notification] [scribe] default family resolved to 'Noto Sans' (no global sans-serif alias)`
  at 19:02:21, inside the session started right after the 19:01 restage build — the mechanism works.
  Note for next time: this is a `Notification`-level line, not a `Warning`, and Notification-level
  entries don't surface in the in-game chat/console overlay by default — check the log file, not
  in-game chat.

- [x] 2.6 (found during 2.3/2.4 manual review, not originally scoped) `ScribeTextDefaults.WrapSettingsChrome`
  and `ScribeSettingsContent.Pegged` both read `ScribeTaskFont.DefaultFamily` for Settings chrome's
  "follow LibGUI's own default, not the task font" `FontFamily` — safe only while `DefaultFamily` WAS
  the literal `"sans-serif"` string. Task 1.3 repointed `DefaultFamily` to a concrete bundled family,
  which silently rebranded Settings chrome to Scribe's bundled font too, contradicting both call
  sites' own doc comments and the "leave HUD + Settings unwrapped" design decision. First fix: added
  `ScribeTaskFont.LibGuiDefaultFamily = "sans-serif"` and repointed both call sites to it — SUPERSEDED
  by 2.7 below.
- [x] 2.7 (author direction, 2026-09-12, after seeing 2.6's fix in-game) Reversed the "leave HUD +
  Settings on LibGUI's raw default" decision entirely: HUD chrome, Settings chrome, the dev-tuning
  dialogs (`ScribeGearTuningDialog`/`ScribeBoxTuningDialog`), and the Task Notice/quest-prompt popups
  (`GuiDialogTaskNotice`/`GuiDialogTaskNoticeRedirectConfirm`/`GuiDialogScribeQuestPrompt`) now all
  root their `Build()` in a new `ScribeTextDefaults.WrapChrome` ancestor naming
  `ScribeTaskFont.DefaultFamily` explicitly, so every Scribe-owned surface renders in the same bundled
  face regardless of Task Text Font setting. Removed the now-unused `LibGuiDefaultFamily` constant.
  Audited every `: GuiBase` dialog root in `src/Mod` to confirm none are left unwrapped.
- [x] 2.3 In-game smoke test (Scribe only, no HudUI needed for this check): open the Lectern/Notebook
  and the pinned-task HUD; confirm Scribe's own default-family text (Settings chrome, HUD chrome,
  task rows on the Default font choice) still renders in a bundled face, not a garbled/missing-glyph
  fallback. Confirmed 2026-09-12.
- [x] 2.4 In-game smoke test with HudUI installed alongside Scribe: confirm HudUI's own UI text (in
  particular the temporal-stability stat) renders in whatever font it used before Scribe was
  installed, not Scribe's bundled Noto Sans — the direct regression check for the reported bug.
  **CLOSED — not a Scribe bug.** "100" still wraps onto two lines after the alias removal; traced to
  HudUI's own `EdgeBarWidgetState.Build` (decompiled `HudUI.dll` — not open-source): a fixed 20px
  `SizedBox` + default `SoftWrap = true` + no `FontFamily` override on the stat's `Text`, which will
  wrap a 3-digit number under any wide-enough font, including LibGUI's own raw, unaliased
  `"sans-serif"` resolution. **Confirmed via A/B test 2026-09-12: Scribe pulled entirely from the Mods
  folder, client relaunched, `client-main.log`'s mod list has no `scribe` entry — "100" still wraps.**
  This mechanism has zero Scribe involvement, past or present; the original ModDB report's "started
  after installing Scribe" was a coincidental mis-attribution. This change is otherwise complete —
  the residual HudUI display bug is out of scope; report it upstream to HudUI's author instead. See
  [[hudui-edgebar-fixed-width-wrap-mechanism]].
- [x] 2.5 Update `CHANGELOG.md` noting the fix; corrected 2.7's reversal into the changelog wording too
  (no more "follow the player's own OS/LibGUI default font again" claim).
