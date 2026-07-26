## 1. Core: consolidated settings + font scales

- [x] 1.1 Add `HudFontScale` and `WindowFontScale` (default `1.0`) to `ScribePlayerSettings` as 5-notch
  scales `{0.8, 0.9, 1.0, 1.1, 1.2}` (-20%/-10%/Default/+10%/+20%); add `MinFontScale=0.8`/`MaxFontScale=1.2`
  consts and a `ClampFontScale` helper that clamps to `[0.8,1.2]` and snaps to the nearest 0.1; apply both
  in `Normalized()`. Also add `MinHudOffset=-100`/`MaxHudOffset=100` consts and clamp `HudOffsetX`/
  `HudOffsetY` to `[-100,100]` in `Normalized()`.
- [x] 1.2 Decide the base window font size to migrate from `ScribeClientConfig.RowFontSize` (15) and
  represent it as a Core or Mod constant per design D1 (font = base × `WindowFontScale`). → Mod constants
  (`ScribeRowConstants.BaseWindowFontSize` = 15, `BaseHudFontSize` = 16); see task 2.1.
- [x] 1.3 Unit tests (tests/Core.Tests): defaults are `1.0`; out-of-range scales clamp to `[0.8,1.2]` and
  snap to the nearest notch; unknown anchor/policy still normalize (regression), covering the delta spec's
  normalization scenario.

## 2. Mod: retire ScribeClientConfig, retarget row style

- [x] 2.1 Move the still-live layout knobs (row vertical/horizontal padding, checkbox size, field pad
  X/Y) into descriptively-named code constants in `src/Mod` (e.g. a small consts holder); confirm values
  match today's `ScribeClientConfig` defaults so scale 1.0 is pixel-identical. Name the base font sizes
  `BaseWindowFontSize` (15) and `BaseHudFontSize` (16). Do NOT inline the pinned tint (see 2.5).
  → `ScribeRowConstants`.
- [x] 2.2 Retarget `ScribeRowStyle.FromConfig` to take `ScribePlayerSettings`: `FontSize =
  BaseWindowFontSize × WindowFontScale`, other fields from the constants in 2.1; drop `PinnedTint` from
  `ScribeRowStyle` (moved to build-time theme derivation, task 2.5). → renamed `FromSettings`.
- [x] 2.3 Delete `ScribeClientConfig.cs` (all fields, including the ~25 dead ones) and remove every
  reference to it and `ClientConfigFileName` where it only fed row styling. Also deleted the stale
  `configlib-patches.json` manifest (the retired ConfigLib row-sizing surface).
- [x] 2.4 Verify the build has no dangling references; run the Core suite.
- [x] 2.5 Derive the resting pinned-row tint at build time from `Theme.Of(context).ColorScheme.Primary`
  at a low alpha, in the read/editor row widgets (design D1b); remove the `PinnedRowTint{R,G,B,A}` fields
  with the rest of `ScribeClientConfig`. → `ScribeRowConstants.PinnedTint(colors)`.

## 3. Mod: live row style + computed centering

- [x] 3.1 In `GuiDialogScribeLecternLibGui`, stop caching `rowStyle` in the ctor; derive it per-`Build()`
  from `modSystem.MySettings` so a font change repaints the open dialog (design D4). → `RowStyle` prop.
- [x] 3.2 Confirm a `WindowFontScale` change mid-edit preserves the caret via the existing
  `autoFocusRowOnRebuild`/`focusedEditIndex` path; extend it if the font-change rebuild needs it. → the
  existing `OnMyPinsChanged` re-arms `autoFocusRowOnRebuild` in editor mode, and `UpdateMySettings` fires
  `MyPinsChanged`, so a font change while editing preserves the caret via the same path (no change needed).
- [x] 3.3 Replace `ScribeRowControlNudge` font-15 constants with offsets computed from measured
  input/control heights (reuse `TextLayoutHelper.MeasureText`) so the checkbox/grip stay centered at any
  scale.

## 4. Mod: settings widget

- [x] 4.1 Create `ScribeSettingsContent` (host-agnostic LibGUI widget) taking a settings snapshot + an
  onChange callback, wrapped in `SingleChildScrollView` + `Scrollbar`.
- [x] 4.2 Behavior section: `Dropdown<ScribeCompletionPolicy>` (policy) + `Checkbox` (HudCollapsed), each
  with a `Tooltip` helptext.
- [x] 4.3 Appearance section: `Dropdown<ScribeHudAnchor>`, `Slider` (HudMaxRows, HudRowWidth), two
  `NumericField` (HudOffsetX/Y, pixel nudges clamped to ±100) on one `Row`, and a 5-notch discrete control ×2 (HudFontScale,
  WindowFontScale) labeled -20%/-10%/Default/+10%/+20%; slider/numeric ranges mirror the Core clamps; each
  control has a `Tooltip` helptext. → font scales use a 4-division Slider over [0.8,1.2] with a
  percentage/Default value label.
- [x] 4.4 Wire every control's change to `modSystem.UpdateMySettings(...)`; persist sliders on
  `OnChangeEnd` (local echo during drag only if flicker/IO shows in testing).

## 5. Mod: gear entry points + view switch

- [x] 5.1 Add a gear control to the lectern `WindowFrame` chrome, present in read and editor views. →
  `WindowFrame` has no trailing-action slot, so the gear sits in a slim `ScribeGearHeader` row just under
  the title bar in both views.
- [x] 5.2 Add an `isSettingsMode` third central-region state; the gear switches to it and `ForceRebuild`s;
  add a back affordance to the prior view. → `ScribeSettingsView` Back button.
- [x] 5.3 Entering settings from the editor commits the pending edit and releases the lock first (reuse
  the `OnClickSwitchToRead` sequencing) so settings is lock-free. → `OnClickOpenSettings`; Back re-requests
  editor access when it was the prior view.
- [x] 5.4 Add a gear on the HUD next to the collapse chevron in `HudPinsContent`; wire it to open a minimal
  standalone LibGUI dialog hosting `ScribeSettingsContent` (design D2 HUD-gear target). → `ScribeSettingsDialog`.
- [x] 5.5 Make the HUD honor `HudFontScale` (replace the hardcoded row font size with base × scale).

## 6. Assets: icon + localization

- [x] 6.1 Add `assets/scribe/textures/icons/gear.svg` from the source at `~/Downloads/gear.svg` (Bootstrap
  `bi-gear`, two `currentColor` paths), flattened to a single `#000000` shape to match the existing
  pin/grip/close/edit icons; register it as `scribegear` in `ScribeModSystem.RegisterCustomIcons` (via the
  self-healing `RegisterSvgIcon` path, rendered with `VsIcon`, NOT LibGUI's SVG-by-path `Icon`).
- [x] 6.2 Add all settings keys to `assets/scribe/lang/en.json`: section titles, each field label, each
  field `-help` helptext, and localized enum option labels for policy and anchor, plus gear/back labels.

## 7. In-game verification

- [x] 7.1 (Backlogged test) Switch CompletionPolicy at runtime; each of Sink/Unpin/Delete behaves on the
  next completion. → Confirmed 2026-07-25 playtest.
- [x] 7.2 (Backlogged test) Pick each non-default HUD anchor, non-zero offsets, and a non-default row
  width; the HUD repositions/wraps correctly and persists across a restart. → Confirmed 2026-07-25 playtest.
- [x] 7.3 HUD font scale and window font scale change live (HUD instant; open lectern instant); a
  mid-edit window-font change preserves the caret. → Confirmed 2026-07-25 playtest.
- [x] 7.4 Gear swaps the central region in both read and editor; entering from the editor commits +
  releases the lock; back returns to the prior view; HUD gear opens settings. → Confirmed 2026-07-25 playtest.
- [x] 7.5 Tooltips show helptext; all strings resolve via lang (no raw keys); scale 1.0 reproduces
  today's row layout. → Confirmed 2026-07-25 playtest.
- [x] 7.6 Confirm an existing `scribe-client-config.json` on disk causes no load error and is ignored.
  → Confirmed 2026-07-25 playtest.

## 8. Follow-up refinements (playtest round 1)

- [x] 8.1 Core: add `ScribeCompletionPolicy.Keep` (=3) — completing keeps the pin and does NOT sink it;
  server-side treat it like `Sink` in `ScribeModSystem.CompleteTaskForPlayer` (no removal). Widen
  `MinHudOffset`/`MaxHudOffset` to ±300. Change `ClampFontScale` to snap to the nearest 0.05 (still clamp
  `[0.8,1.2]`).
- [x] 8.2 Core tests: update `ScribePlayerSettingsTests` for the 0.05 snap (e.g. 0.83→0.85, 0.86→0.85,
  1.16→1.15), the ±300 offset clamp, and a `Keep` policy normalization case; run the Core suite.
- [x] 8.3 HUD: replace `UndoWindowMs` with a shared `PinHudWaitMs` (1500). Defer the completion send —
  `OnToggleRow` records a pending `{policy, expiry}` (generalize `sinkExpiryMs` → `pendingCompletion`),
  flips optimistically, and rebuilds; unchecking within the window cancels it (true undo, nothing sent);
  `OnTick` sends the `ScribeCompleteTaskMessage` (with its stored policy) on expiry. Read-view checkbox
  stays immediate.
- [x] 8.4 HUD: policy-aware ordering (`SunkForOrder` never sinks a `Keep` pin); animate the pending
  window — `AnimatedOpacity` fade of the row TEXT toward ~0.15 for unpin/delete (checkbox stays opaque for
  undo), and the existing mute-fade for sink. The `AnimatedSlide` for sink was intentionally NOT added:
  sinking is a `Column` reorder, and LibGUI's implicit pixel-offset `AnimatedSlide` can't animate a row
  across a position change (a zero offset animates nothing), so a slide here would be a no-op. The
  mute-fade remains the sink cue; a real reorder animation is left to the custom-checkbox/animation change.
- [x] 8.5 HUD: scale the row checkbox with `HudFontScale` (base × scale) instead of the hardcoded 20.
- [x] 8.6 Settings form: convert the font scale (percent 80–120 step 5), HUD row width (100–1000 step 5),
  and HUD max rows (1–20 step 1) from sliders to `NumericField`, each clamped in `onChanged` and keyed by
  a `ValueKey` of its current value so a clamp re-displays the clamped result; offsets ±300 step 5.
- [x] 8.7 Settings form: scale the form's own `Text`/`Checkbox` with `WindowFontScale` (× base) so it
  re-renders live on the write-through rebuild.
- [x] 8.8 HUD `ApplyAnchor`: interpret `HudOffsetX/Y` as relative to the anchor's pre-baked offset
  (`prebaked(anchor) + userOffset`), dropping the "apply minimap clearance only when offset==0" case.
- [x] 8.9 Chrome: `WindowFrame` title = "Scribe Settings" while `isSettingsMode`. Replace `gear.svg` with
  the flattened `~/Downloads/gear-filled.svg` (single path, `fill="#000000"`); code `scribegear` unchanged.
- [x] 8.10 Multiplayer Back safety: in `BlockEntityScribeLectern.HandleServerReply`, an editor-access
  denial while the dialog is open (and not the save-failed recovery) falls back to `EnterReadMode()`.
- [x] 8.11 Lang: add `scribe-completion-keep`; update `settings-hudoffset-help` (relative, ±300); adjust
  any label wording for the percent/numeric-field change.

## 9. In-game verification (round 1 refinements)

- [x] 9.1 Complete a HUD task under each policy: the ~1.5s window shows the pending animation (fade for
  unpin/delete, settle for sink); unchecking within the window fully undoes it (task stays); letting it
  elapse applies the completion. `Keep` keeps the checked task in place (no sink).
- [x] 9.2 Font size, HUD row width, and HUD max rows are numeric fields (no slider); each clamps to its
  range, steps by its increment, and never hijacks scrolling. Font entered as a percent, 5% steps.
- [x] 9.3 HUD X/Y offsets are relative to the anchor's built-in position (0 = default, clear of the
  minimap on TopRight); values up to ±300 apply and persist.
- [x] 9.4 Changing window font size live-rescales the settings form's own text + checkboxes; HUD font size
  live-rescales the HUD checkbox.
- [x] 9.5 The window title reads "Scribe Settings" in the settings view; the filled gear icon renders.
- [ ] 9.6 (Multiplayer) Enter the editor, open settings (releasing the lock), have a second player grab
  the editor, then hit Back — you land in the read view, not a stuck settings frame.
  → Backlogged 2026-07-25 (playtest submission 2026-07-25T13-50-39): deferred pending a two-client setup.
  Not a defect — the Back-loses-lock → read-view fallback code path (8.10) is in place; archived with this
  one verification parked.

> Round-1 verification (9.1–9.5) confirmed in playtest 2026-07-25 (see root TESTING.md). New-scope
> follow-ups the user raised on confirmed items — gradual unpin/delete fade, sink-reorder-and-stay,
> settings-form two-column layout, HUD-gear sizing, arrow-key numeric stepping, label renames — are NOT
> part of this change; they are carried into the `scribe-settings-followups` change.
