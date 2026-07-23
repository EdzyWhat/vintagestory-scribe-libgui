## 1. Config knobs (ScribeClientConfig.cs)

- [x] 1.1 Add pressed-overlay knobs (`AffordancePressedR/G/B` ~0.8 light tone, `AffordancePressedA`
      ~0.10) alongside the existing `Affordance*` set, hand-editable flat JSON.
- [x] 1.2 Add `MinAffordanceButtonSize` (unscaled px floor for the square pin/delete buttons, sized so
      they stay legible at min text-size). (No `AffordanceDividerThickness` — the divider forms from
      the two grouped buttons' shared outlines, so no extra knob is needed.)
- [x] 1.3 Add a pinned-indicator selector: a `PinnedIndicatorMode` knob (None / RowAccent /
      AlwaysShowButton / Both) plus `PinnedAccent*` color + size knobs for the row-accent variant.

## 2. Chrome-less grip + pressed state (ScribeBlockRowCell.cs — ScribeHoverIconButton)

- [x] 2.1 Add a `drawChrome` flag to the ctor; when false, `BakeButton` skips the fill + outline and
      draws only the icon (icon fills the full square so the bare grip glyph is ≥ checkbox height).
- [x] 2.2 Add pressed-state support: bake a translucent overlay texture (`AffordancePressed*`, clipped
      to the button's rounded rect) and, in `RenderInteractiveElements`, blit it when the left mouse
      button is held AND the pointer is inside the button's bounds. (Computed statelessly from
      `api.Input.MouseButton.Left` rather than via mouse-event overrides — avoids fighting the base
      toggle's `OnMouseUp` and self-clears on release/leave; recorded in VSAPI-NOTES.md.)
- [x] 2.3 Add an "always visible when pinned" path (`AlwaysVisible` flag): a pinned row's pin button
      bypasses the `HoverRegion` early-out so it renders in its filled "on" look without hovering, gated
      by `PinnedIndicatorMode` (AlwaysShowButton/Both). Unpinned buttons stay hover-gated.

## 3. Grouped, square pin/delete geometry (RowTextLayout.cs + GuiDialogScribeLectern.cs)

- [x] 3.1 Add `ScribeRowElement.AffordanceButtonSizeFixed` = `max(MinAffordanceButtonSize,
      singleLineHeight)` and thread it into `RowTextLayout.For` (new `affordanceSize` param) as BOTH the
      pin and delete width; anchor delete flush-right and pin immediately left of it (abutted, no gap).
      Updated the doc-comments.
- [x] 3.2 In `GuiDialogScribeLectern.cs`, set pin/delete bounds to the square size (width = height =
      affordanceSize); pass `drawChrome: false` for the grip; keep grip at `layout.DragHandleX`.
- [x] 3.3 Group the pair via `AffordanceGroupSide` (pin rounds left corners, delete rounds right; the
      shared straight edge reads as the divider) baked in `GroupedRect`. `IsInIconGutter` passes the
      same measured `affordanceSize` so the per-icon yield boundary tracks the real cluster.

## 4. Ruling padding (ScribeClientConfig.cs + ScribeRowElement.cs)

- [x] 4.1 Default `RulingPadding` to 0 so the line hugs the content, keeping it a tunable knob
      (documented why on the field). `TopPadFixed`/`BottomOverheadFixed` read it unchanged.
- [x] 4.2 `BottomOverheadBandFixed` still includes the ruling thickness and feeds the floating input's
      height, so the focus highlight keeps a margin above the line even at padding 0 (documented on the
      field). No code change needed there.

## 5. Real pin persistence + sync (Core reuse + Mod)

- [x] 5.1 Editor view: replaced the `OnEditViewTogglePin` stub body with
      `scratchDocument?.TogglePinned(index); isDirty = true; RequestRecompose();` (mirrors
      `OnEditViewToggleTask`). The pin button's `On` is re-seeded from the now-updated `block.Pinned`
      on recompose, so it survives.
- [x] 5.2 **Not needed — no `ScribeTogglePinMessage`.** Pin is an editor-only control (there is no
      read-view pin control). The editor autosave already serializes `Pinned` (codec v3) and the
      server's `MarkDirty(redrawOnClient: true)` re-syncs it to other clients' read view — the same
      whole-document path the done-toggle rides. A dedicated lock-free message is only needed for a
      read-view action with no editor to autosave (e.g. `ScribeToggleTaskMessage`). Adding one here
      would be dead code. (Rationale recorded in VSAPI-NOTES.md.)
- [x] 5.3 **Not applicable** — the read view has no pin control to wire (see 5.2). Multiplayer sync of
      a pin toggle is delivered by the editor autosave → `MarkDirty` → `FromTreeAttributes` →
      `RefreshReadView` path already in place.

## 6. Pinned indicator (ScribeRowElement.cs)

- [x] 6.1 Row-level accent: `ScribeRowElement` takes a `pinned` flag (passed at both read- and
      editor-view row composition) and `DrawPinnedAccent` bakes a small ink dot at the row's top-right
      into the row texture (visible both views, scrolls/clips with the row), gated by
      `PinnedIndicatorMode` (RowAccent/Both).
- [x] 6.2 Both variants switch via `PinnedIndicatorMode`: RowAccent (dot), AlwaysShowButton
      (`AlwaysVisible` pin button), Both, or None — chosen in-game by editing the config knob.

## 7. Core tests

- [x] 7.1 Confirmed existing coverage is complete — `ScribeDocumentTests` already covers
      `TogglePinned` (unpinned→pinned, pinned→unpinned, text-section fails, invalid index fails safely)
      and `ScribeDocumentCodecTests.RoundTrip_PreservesPinnedAndAssignedToUid` covers the mixed-state
      round-trip. No new tests needed; suite green (37 passed).

## 8. Build, test, playtest

- [x] 8.1 `dotnet build src/Mod/Mod.csproj -c Release` — clean (0 warnings/errors); `dotnet test
      tests/Core.Tests` — green (37 passed).
- [x] 8.2 Playtested (report 2026-07-22T15-27-35, verdicts in TESTING.md). **Confirmed:** grouped
      pin+delete pill (1), square + min-size (4). **Came back needing fixes:** grip too short/off-center
      (2), pressed overlay invisible/confused with pinned-fill (3), ruling gap persists from the box
      model not RulingPadding (5), pinned indicator (dot) unnoticeable → blocked persistence check (6,7).
      Follow-ups in task group 10 below.

## 9. Docs

- [x] 9.1 Recorded in `VSAPI-NOTES.md` ("Custom button pressed-state and stateful toggles"): the
      render-time stateless pressed-overlay approach (vs. mouse-event overrides fighting the base
      toggle's `OnMouseUp`), and when an editor autosave suffices vs. needing a lock-free toggle message.

## 10. Round-3 fixes (from playtest 2026-07-22T15-27-35)

- [x] 10.1 **Pressed state → darken whole button.** Repoint `AffordancePressed*` from a ~10% white
      wash (invisible on parchment; only lightened the ink glyph) to dark `0,0,0 @ 0.18`.
      `BakePressedOverlay` already fills the whole (grouped) rounded rect, so only the color changed.
- [x] 10.2 **Grip taller + centered on checkbox.** Added `ScribeRowElement.CheckboxGlyphMetricsFixed`
      (checkbox glyph center-Y + size in fixed units, mirroring the draw math). Grip glyph sized to
      1.1× the checkbox glyph and vertically centered on the checkbox midline (notes with no checkbox
      keep the column-fill fallback). Grip stays chrome-less.
- [x] 10.3 **Pinned indicator → row-background tint.** Replaced the top-right dot with a whole-row
      tint filled first (under checkbox/text/ruling) in `ScribeRowElement.ComposeElements`, gated by
      `PinnedIndicatorMode.RowTint`/`Both` (now the default). Config `PinnedAccent*`+size dropped for
      `PinnedRowTint*` RGBA. Kept the pin button's filled `showActiveState` look on hover ("keep both").
- [x] 10.4 **Ruling gap — box-model writeup (deferred, no code change).** Documented the row's vertical
      band stack in `VSAPI-NOTES.md` ("Editor row vertical box model…"): why a gap remains at
      `RulingPadding = 0` (the input height subtracts the full `BottomOverheadFixed`, which still holds
      the ruling thickness + the input's own text centering), and the levers to close it. Await the
      user's target spacing before editing.
- [x] 10.5 Retested (report 2026-07-22T16-21-57). **Confirmed:** grip (2) and pressed-darken (3).
      **Still open:** pinned tint (6,7) not visibly landing; ruling (5) target now defined; NEW general
      note — button group looks anchored to the ruling, wants ~85% height matching the input. Also
      surfaced the config-drift trap (stale on-disk JSON shadowed the dark-press default) → added a
      guard to `build/restage.sh` + `what-to-test` skill step 0b. Follow-ups in group 11.

## 11. Round-4 fixes (from playtest 2026-07-22T16-21-57)

- [x] 11.1 **Pinned tint wasn't visible → go loud.** Verified the tint render path is correct (draws
      first, under content), so the cause was subtlety (alpha 0.12) + the stale on-disk config. Set
      `PinnedRowTint*` to an unmistakable amber (`0.95/0.75/0.20 @ 0.35`) in the code default AND
      reconciled the on-disk config, to prove the path renders in-game before dialing back.
- [x] 11.2 **Button group height → single-row input (~85%), centered.** Added
      `AffordanceButtonSizeFactor` (default 0.85); `AffordanceButtonSizeFixed` now returns
      `SingleLineRowHeight * factor` (floored at `MinAffordanceButtonSize`), so the square pin/delete
      shrink together. Vertically center the group on the row's first text line (checkbox midline for a
      task; single-line-height center for a note) instead of top-aligning, so it lines up with the input
      rather than hugging the ruling.
- [x] 11.3 **Ruling spacing — debug aids (measurement pass, spacing not yet changed).** Added TEMPORARY
      visuals: stark magenta outline on the focused input (`ScribeRowTextInput`), and green (top pad) /
      cyan (bottom overhead) band fills on edit-mode rows (`ScribeRowElement`). Target: a single line
      vertically centered between the rulings. Await the user's measured adjustment, set the spacing,
      then REMOVE these debug colors.
- [x] 11.4 **Config-drift enforcement.** Added a non-fatal config-drift warning to `build/restage.sh`
      (fires when the on-disk `scribe-client-config.json` predates a `ScribeClientConfig.cs` change or
      there are uncommitted default edits) and a matching step 0b to the `what-to-test` skill. This is
      the root-cause guard for the "dark press still showed white" gotcha this round.
- [x] 11.6 **Pin/delete buttons did nothing (loop-capture bug).** The editor compose loop is a
      `for (int i = ...)` (`GuiDialogScribeLectern.cs:621`); the pin/delete click lambdas closed over the
      shared `i` (`_ => OnEditViewTogglePin(i)` / `OnEditViewDeleteRow(i)`), which is `blocks.Count` after
      the loop — so every button called its handler with an out-of-range index → `IsValidIndex` fail →
      silent no-op (blocked all of round-4's pin-persist/tint verification). Fixed by snapshotting a
      per-iteration local (`int rowIndex = i;`) and capturing THAT in both lambdas. The row-element
      checkbox was immune (routes through the element's own `blockIndex` field, not a closure); grip is a
      no-op callback; the post-Compose pin-seed loop uses `i` immediately (not in a closure) so it was
      fine. Build clean.

- [ ] 11.5 Restage Debug + fully relaunch, then: (a) pin a task → whole row obviously amber-tinted at
      rest (both views), persists across recompose + save/reload → then dial the tint back to a tasteful
      wash; (b) confirm the pin/delete group height matches the single-row input and is centered on the
      line, not the ruling; (c) with the magenta/green/cyan debug aids, report the ruling adjustment so
      the single line centers between rulings → then remove the debug colors.
