All GUI/HUD work is in `src/Mod/`. Line numbers are from the research pass and may drift — locate by
symbol. Source items: playtest submission `2026-07-27T10-16-26`.

## 1. Editor hotkey trap (v1 blocker — `696dd143`)

- [x] 1.1 Gate `CaptureAllInputs()` (`GuiDialogScribeLecternLibGui.cs:330`) on a field actually holding
  focus, not on `isEditorMode`: return true only when an editor field is focused (`focusedEditIndex is
  not null`) or a Pin Tab field is focused (`focusedPinTaskId is not null`), else false. Confirm the
  macOS Cmd-translation in `OnKeyDown` still guards correctly (it already checks `isEditorMode` and only
  rewrites keys a focused field would consume).
  - Shipped 2026-07-27. Still broken on retest: `OnRowFocusChanged` fires only on focus-gained, so
    `focusedEditIndex` stays non-null after click-away. Fix needed: live `HasFocus` check in `CaptureAllInputs`.
- [x] 1.2 (second pass) Add a live `FocusNode.HasFocus` guard: `CaptureAllInputs()` checks
  `editorFocusNodes[idx].HasFocus` (and `pinFocusNodes[pinId].HasFocus` for Pin Tab) so capture drops
  the moment the active focus token leaves the field, regardless of whether `focusedEditIndex` has been
  cleared. Build + test clean; restaged Debug 2026-07-27.
- [x] 1.3 Manually test in-game: open the editor, add a task via "New Task", click away to unfocus — the
  Handbook key (H) and other global hotkeys now fire. Then click into a row and type movement/hotbar keys
  — they still edit the field and do NOT leak to the game (no player move, no hotbar change).
  Also confirm in Pin Tab: click away from a pin field — hotkeys fire; type in a focused pin field — no leak.
  - **Confirmed 2026-07-27** (playtest submission 2026-07-27T18-22-10): "Fully fixed. I can now open the
    Handbook" — second-pass live-`HasFocus` guard holds after relaunch. TESTING.md `696dd143`/`e6b5148e`.

## 2. Sink reorders every surface (`0c09d185`)

- [x] 2.1 Pinned view: order rows with the HUD's sink rule. In `BuildPinnedContent`
  (`GuiDialogScribeLecternLibGui.cs:~1460`), render `modSystem.MyPins` through the same completed-sinks-
  below-not-completed ordering the HUD uses (`ScribePinOrdering.ForDisplay`) instead of raw pin-list
  order.
- [x] 2.2 Decide (design Open Question) whether the Pinned view also needs the HUD's undo-window "stay
  then sink" overlay (`HudScribePins.sunkOrder`/`SinksForOrder`). If yes, factor that overlay into a small
  shared helper both surfaces call; if no, apply the plain Core resting order and accept an immediate
  sink. Record which was chosen.
- [x] 2.3 Read/Edit views reflect the owner's sink reorder promptly: the server Sink completion already
  calls `ScribeDocument.MoveTaskToBottom` on the shared doc, so ensure the acting player's open Read view
  repaints from the resync (and the editor's scratch reflects the move — already handled by
  scribe-lectern-view-consistency's editor Sink branch). Do NOT invent a new reorder path; wire the
  existing reorder to refresh the surface.
- [x] 2.4 Add/confirm Core coverage: `ScribePinOrdering.ForDisplay` sink ordering is already unit-tested;
  add a test only if 2.2 introduces a new shared ordering helper.
- [x] 2.5 Manually test in-game: with policy *sink*, complete a pinned task from the Pinned view → it sinks
  to the bottom of the Pinned list (not just the HUD). Complete an owned task under *sink* from the Read
  and Edit views → it moves to the bottom there too, and the HUD agrees.
  - **Confirmed 2026-07-27** (playtest submission 2026-07-27T15-22-22): "Works."

## 3. Read-view pin keeps scroll (`32f807d9`)

- [x] 3.1 In `OnReadViewTogglePinned` (`GuiDialogScribeLecternLibGui.cs:1441`), call
  `CaptureScrollForRestore()` (`:471`) before `SendSetPin`, so the pending `OnMyPinsChanged` →
  `ForceRebuild` has an offset for the existing `OnRenderGUI` re-apply loop (`:~1060`) to restore. Guard so
  it only arms in the read view (editor/Pin Tab keep their own focus-restore paths).
  - Shipped 2026-07-27. Still broken on retest: pre-send capture was too early — the restore loop expired
    before the async `OnMyPinsChanged` callback arrived. Fix: move capture into `OnMyPinsChanged` itself.
- [x] 3.2 (second pass) Move `CaptureScrollForRestore()` into `OnMyPinsChanged`, called immediately before
  `ForceRebuild`, guarded to non-Pinned views. Remove the pre-send call from `OnReadViewTogglePinned`.
  The Pinned view uses a non-virtualized Column (content height exact from frame-1) and doesn't need it;
  Read and Editor views use virtualized ListView / SCSV that re-clamp on rebuild. Build clean; restaged Debug 2026-07-27.
- [x] 3.3 Manually test in-game: scroll the read view down, pin then unpin a task → the list stays at the
  scrolled position instead of jumping to the top. Also test pinning from the editor view. The Pinned
  view itself should still stay put (wasn't broken, confirm not regressed).
  - **Confirmed 2026-07-27** (playtest submission 2026-07-27T18-22-10): "Works." Second-pass capture in
    `OnMyPinsChanged` survives the async round-trip. TESTING.md `ed0a4f7e`/`32f807d9`.

## 4. Polish (general notes — visual/layout only)

- [x] 4.1 HUD legibility (`HudScribePins.cs`): nudge the pinned-task row text toward white (not fully
  white); slightly darken the outer text glow and slightly reduce its range (e.g. ~5px → ~4px). Pick exact
  values by eye in-game.
- [x] 4.2 Lectern title padding: give the title text ("Lectern") 10px of `padding-left` in the title-bar
  band build (supersedes the earlier 4px value).
- [x] 4.3 Settings layout (`ScribeSettingsContent.cs`, HUD Appearance section): place HUD Text Size
  (`hudfontscale`) in a column beside the HUD position (offsets) row, reusing the `PairedControls`
  two-column helper.
- [x] 4.4 Manually test in-game: HUD text/glow read better without washing out; the Lectern title has a
  clear 10px left gap; HUD Text Size sits beside HUD position in Settings.
  - **Confirmed 2026-07-27** (playtest submission 2026-07-27T15-22-22): "Works."

## 5. Polish round 2 (general notes from 2026-07-27T15-22-22)

- [x] 5.1 Title bar font: increase the Lectern title text font size relative to the window body text size.
  The ask moved 50% → 100% (playtest 2026-07-27T18-22-10) → then back down a relative 25% to **50% larger**
  after `× 2.0` read too large in-game. In `BuildTitleBar`, the `titleFont` factor is now `× 1.5` (was
  `× 1.1`). Build clean; restaged Debug 2026-07-27.
- [x] 5.2 Completion policy order: reorder the policy options in the UI picker (Settings + Pinned view
  picker) to: 1. Keep (stay in place), 2. Keep (sink to bottom), 3. Unpin, 4. Delete. Both pickers now use
  an explicit item order (`ScribeSettingsContent` + the Pinned-view picker in `GuiDialogScribeLecternLibGui`)
  instead of the enum/alphabetical default. Build clean; restaged Debug 2026-07-27.
- [x] 5.3 HUD text alignment: make the HUD header ("Pinned" + gear) and footer ("+N more") align left
  when the HUD Position is a Left anchor (TopLeft, MiddleLeft, BottomLeft), and right when it is a Right
  anchor (TopRight, MiddleRight, BottomRight). `HudPinsContent` takes a `leftAligned` flag (from the new
  Core `ScribeHudAnchor.IsLeftAnchored()` helper) that drives the outer Column's `crossAxisAlignment`
  (Start vs End); `UpdateMySettings` → `MyPinsChanged` → `ForceRebuild` re-reads it live. Core test added
  (`IsLeftAnchored_ClassifiesHorizontalSide`, 7 cases). Build clean; restaged Debug 2026-07-27.
- [~] 5.5 Bundle a real Caudex Bold cut so the Bold dialog title renders in the designed bold weight rather
  than a Skia-synthesized fake-bold of the regular. **OBSOLETE 2026-07-27 (user decision) — abandoned, does
  not work.** Shipped `caudex-bold.ttf` (verified weight-700 bold via name table) and tried both registering
  it under Bold/SemiBold only, then under ALL weights (the approach that made the regular face "stick"
  before) — the title still rendered regular in-game. Diagnostic confirmed both faces load distinctly, so
  the mismatch is in the shipped `gui` mod's font resolution, not our assets; not worth pursuing for a
  title-weight nicety. Current state (harmless): only `caudex-bold.ttf` ships, registered under every
  weight; title renders in Caudex at 1.5× size, just not visibly bolder. TESTING.md `686f45ae` obsoleted.
  (Added per playtest 2026-07-27T18-22-10 general note.)
- [x] 5.6 Sidebar nav buttons bigger + spacing + shadow: enlarge the Read/Edit/Pinned/Settings buttons in
  the Lectern sidebar (`BuildRightColNav`, SectionRightCol) — BOTH the button box and the inscribed SVG —
  by scaling the shared `size` local. Landed at `RowCheckboxSize × 1.7` after ×1.5/×1.8 read too large/small
  in-game; `ScribeRowButton` derives both box and glyph size from that one value. Also set the nav Column's
  `spacing: 16` and `crossAxisAlignment: CrossAxisAlignment.Start`, and gave each button a drop shadow
  (`navShadow`: black @ 0.35, offset (2,2), blur 4) via a new `boxShadows` param threaded
  `TitleButton` → `ScribeRowButton` → `BoxStyle.BoxShadows`. Build clean; restaged Debug 2026-07-27.
  (Added per user request this session; values iterated live.)
- [ ] 5.4 Manually test in-game: title bar text is noticeably larger than the row text; policy picker
  shows the new order in both Settings and the Pinned view; HUD header/footer text aligns left on a
  Left-anchored HUD and right on a Right-anchored HUD; the Read/Edit/Pinned/Settings sidebar buttons are
  50% larger (box + glyph) — note how the enlarged buttons read against the SideColW column bounds.

## 6. HUD-Delete refreshes open editor (`80777b7b`)

- [x] 6.1 In `RefreshReadView` (`GuiDialogScribeLecternLibGui.cs`): when in editor mode, compare
  scratch tasks against `lectern.Document` and call `DeleteEditorBlock` for any task absent from the
  server doc. This reconciles HUD-Delete (and any external deletion) into the live scratch without
  overwriting in-progress text on other rows. Build clean; restaged Debug 2026-07-27.
- [x] 6.2 Manually test in-game: open the editor, switch to the HUD, complete a task under Delete
  policy — the row should vanish from the open editor view immediately (with its collapse animation),
  not linger until a view-swap. Other rows' in-progress text should be undisturbed.
  - **Confirmed 2026-07-27** (playtest submission 2026-07-27T18-22-10): "Works." Open editor drops a
    HUD-deleted row immediately via the `RefreshReadView` reconcile. TESTING.md `80777b7b`/`22412531`.

## 7. Build, test, restage, verify (round 2)

- [x] 7.1 `dotnet build src/Mod/Mod.csproj --nologo` clean; `dotnet test tests/Core.Tests/Core.Tests.csproj`
  green (133/133).
- [x] 7.2 Restage (`bash build/restage.sh Debug`) and fully relaunch the client.
- [x] 7.3 Update `TESTING.md` with second-pass fix notes and new items.

## 8. Polish round 3 — Lectern visual pass (this session, not previously specced)

Retroactively captured: these are playtest-driven, visual-only tweaks to the Lectern surface (layout,
theme-derived color, drag-state signalling). No behavior/persistence/API change, so no spec delta —
recorded here as tasks because §5 is the standing "polish" home and 5.6 already lives here. All in
`src/Mod/` (`GuiDialogScribeLecternLibGui.cs` + `ScribeRowConstants.cs`); values iterated live in-game.

- [x] 8.1 `LecternLayout` column retune: rework the proportional column widths so the three columns
  (`2·SideColW + TasksColW`) sum to `InnerW` exactly at `InnerW = 1.0·W`, keeping `TasksColW = 0.795·W`
  and growing `SideColW` to `0.1025·W` to absorb the remainder (to fit the enlarged §5.6 nav buttons).
  `TitleBtnsW = 0.795·W` set independently (not aliased to `TasksColW`). Invariant preserved: the three
  columns sum to `InnerW`.
- [x] 8.2 Pixel-Art-OFF colored backgrounds: when Pixel Art Display is OFF, paint a themed surface behind
  the Lectern's central region and title row (previously transparent), matching the Scribe Settings panel.
  Added a `FlatPanel(Widget)` helper that wraps its child in a `Container` filled with
  `ThemeData.Default.ColorScheme.Surface` when Pixel Art is off (pass-through when on), applied to
  `BuildCentralRegion()` and the title row — NOT the whole window. Title row padding changed to
  `left: 10 + 0.04·W`, `right: 0.04·W`.
- [x] 8.3 Theme-aware drag highlights (Edit + Pinned tabs): replace the flat white/black drag washes with
  theme-derived color. New `ScribeRowConstants.ShiftBrightness(Vector4, float)` shifts a color's HSV
  Brightness by ±N points (SkiaSharp `ToHsv`/`FromHsv`, hue/sat and float alpha preserved). Source row =
  `Primary` brightened +20, drop target = `Primary` darkened −20; fill at 0.4 alpha with a 1px border of
  the same shifted color at 0.5 alpha; source wins on overlap. Applied in both `ScribeEditRowState.Build`
  and `ScribePinRowState.Build` (the Pinned tab gained the `isDragSource` plumbing to match the Edit view,
  replacing its lone `StateSelected` drop-target fill).
- [x] 8.4 Pinned-row resting tint stronger: bump `ScribeRowConstants.PinnedTintAlpha` 0.22 → 0.33 (the
  theme-`Primary`-derived resting wash for pinned tasks in the read + editor views).
- [x] 8.5 `dotnet build` / `restage.sh Debug` clean (0 warn / 0 err); Core suite green.
- [ ] 8.6 Manually test in-game: (a) enlarged sidebar buttons sit correctly within the retuned `SideColW`;
  (b) with Pixel Art OFF, the central region + title row show the themed surface fill (not a transparent
  gap), title text keeps its left gap; (c) Edit view AND Pinned tab drags show a lighter theme wash on the
  grabbed row and a darker one on the hover target, each with a crisp 1px border, source winning on
  overlap — verify it reads well against BOTH the pixel-art light theme and the global dark theme; (d)
  pinned rows show a slightly stronger resting tint in read/editor views.
