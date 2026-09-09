## 1. Foundation: lang keys, shared header helper, title bar padding

- [x] 1.1 Add new descriptor lang keys to `src/Mod/assets/scribe/lang/en.json` for all nine tabs
  (`scribe-gui-subtitle-read`, `scribe-gui-subtitle-edit`, `scribe-gui-subtitle-pinned`,
  `scribe-gui-subtitle-history`, `scribe-tab-subtitle-guestbook`, `scribe-tab-subtitle-inbox`,
  `scribe-tab-subtitle-senthistory`, `scribe-tab-subtitle-assignment`,
  `scribe-tab-subtitle-transcribe`), using the wording agreed during exploration, and verify the
  file still parses as valid JSON.
- [x] 1.2 Add a shared header-building helper to `ScribeDialogBase.Layout.cs` that renders the Row
  2 subtitle (small-caps label + italic descriptor, or the uppercase/reduced-size fallback if
  small-caps isn't renderable — see design.md), an optional Row 3 content slot, an optional
  Guest-Book-only leading divider, and the durable divider at the fixed 8-units-above /
  4-units-below spacing. Verify `dotnet build` succeeds.
- [x] 1.3 Tighten Row 1's padding in `BuildTitleBar`. Verify by rebuilding and opening any dialog:
  the title bar is visibly tighter and the drag handle, title text, and edit/close buttons are
  still fully visible and clickable (no clipping).

## 2. Migrate list-based tabs onto the shared header

- [x] 2.1 `ScribeReadContent.cs` — replace the `Column(spacing: 8)` + inline pill padding +
  `Divider` with a call to the new shared header helper (label `scribe-gui-nav-read`, descriptor
  `scribe-gui-subtitle-read`, Row 3 = the filter-pill row). Verify the Read view shows the
  subtitle above the pills and exactly one divider directly below the pills.
- [x] 2.2 `ScribeEditorContent.cs` — same replacement with no Row 3 content. Verify the Editor view
  now shows a subtitle above the divider where none existed before, and the editor's row list and
  footer (Add task / Done editing) still function.
- [x] 2.3 `ScribePinnedContent.cs` — same replacement with the completion-policy picker as Row 3.
  Verify the Pinned view shows its subtitle above the picker and the picker's dropdown still
  functions.
- [x] 2.4 `GuiDialogScribeNotebook.cs` (`BuildHistoryContent`) — same replacement with no Row 3
  content. Verify the History tab shows its new subtitle above the divider and the "Add Entry"
  footer still functions.
- [x] 2.5 `ScribeInboxContent.cs` — insert the new Row 2 subtitle above the existing filter-pill
  Row 3 (already on the target divider spacing), taking the label/descriptor as parameters so
  callers can supply distinct text. Verify the tab still filters and lists rows exactly as before.
- [x] 2.6 `ScribeDialogBase.ViewSwitching.cs` — update the `BuildInboxContent` and
  `BuildSentAssignmentHistoryContent` call sites to pass distinct label/descriptor pairs
  (`scribe-tab-inbox`/`scribe-tab-subtitle-inbox` vs.
  `scribe-tab-senthistory`/`scribe-tab-subtitle-senthistory`) into `ScribeInboxContent`. Verify by
  opening the Assignment Inbox and Sent Assignment History tabs side by side and confirming their
  subtitles differ while everything else renders identically.

## 3. Guest Book (ledger exception)

- [x] 3.1 `ScribeDialogBase.Guestbook.cs` — replace the existing divider-header-divider sandwich
  with a call to the shared header helper, passing the leading-divider flag and the "Visitor /
  Note" column-header row as Row 3. Verify the Guest Book tab still shows a divider directly above
  the column headers and the durable divider below them (two total), and that no other tab shows
  two dividers.

## 4. Form-shaped tabs

- [x] 4.1 `ScribeAssignmentFormContent.cs` — wrap the existing heading, staging area,
  delete-checkbox row, delivery row, and send-to row as Row 3 content passed into the shared
  header helper. Verify the Create Assignments tab shows its new subtitle and its first-ever
  divider directly above the stage tray, and that drafting, staging, and sending an assignment
  still work end to end.
- [x] 4.2 `GuiDialogScribeScriptorium.cs` (`BuildInventoryContent`) — move the durable divider to
  sit directly after the new subtitle row instead of directly after the title bar; keep the
  existing divider between the Copy/Seal zone and the Import/Export zone as an internal content
  separator. Verify the Transcribe tab shows both dividers in their new positions and that copy,
  seal, import, and export all still function.

## 5. Playtest bug fixes on the shipped subtitle row

- [x] 5.1 `ScribeDialogBase.Layout.cs` (`ScribeTabHeader.Build`) — split the label into a
  full-cap-size first-letter span and a ~75%-size remaining-letters span (both uppercase, same
  family/weight/color) instead of a uniform ALL-CAPS block, for a genuine small-caps look. Verify
  `dotnet build` succeeds.
- [ ] 5.2 `ScribeModSystem.Assets.cs` (`RegisterCustomFonts`) — bundle Caudex's real italic cut
  (`textures/fonts/caudex-italic.ttf`) and register it under the "Caudex" family's `Italic` weight
  slot only, leaving Normal/SemiBold/Bold on the existing bold cut. `dotnet build` verified; still
  needs the in-game check — after restaging, confirm the lectern title still renders bold (the
  historical Normal-vs-Bold resolver quirk this risks recurring — see design.md) and the subtitle
  descriptor renders genuinely slanted, not upright bold.
- [x] 5.3 (2026-09-08 playtest) `ScribeTabHeader.Build` — extend 5.1's small-caps split to EVERY
  word in the label (not just the first), and baseline-correct every text run (cap letters,
  small-cap tails, colon, descriptor) via a `WidgetSpan`+computed-font-metrics `BaselineRun`
  helper, fixing the smaller runs floating above the line's baseline instead of sitting on it.
  `dotnet build` and `dotnet test tests/Core.Tests` verified.

## 6. Extend the shared header to the two missed tabs

- [x] 6.1 Add new descriptor lang keys for the Inbox Inventory and Timer tabs (reusing
  `scribe-tab-inbox-inventory` / `scribe-gui-nav-timer` as labels), following the existing
  `scribe-tab-subtitle-*` / `scribe-gui-subtitle-*` naming convention. `en.json` verified to still
  parse as valid JSON.
- [ ] 6.2 `GuiDialogScribeInbox.cs` (`BuildInboxInventoryContent`) — call `ScribeTabHeader.Build`
  with the new subtitle, treating the existing 3-row slot grid as Row 3. `dotnet build` verified;
  still needs the in-game check — after restaging, confirm the Inbox Inventory tab shows its
  subtitle above the slot grid and exactly one durable divider below it.
- [ ] 6.3 `GuiDialogClockmakerNotebook.cs` (`BuildTimerContent`) — call `ScribeTabHeader.Build`
  with the new subtitle and no Row 3, replacing the existing bare `new Divider()`. `dotnet build`
  verified; still needs the in-game check — after restaging, confirm the Timer tab shows its
  subtitle above the divider and the gearworks/countdown/form content below still function in all
  three timer states (Idle, Running, Fired).

## 7. Divider/padding rule: flush divider, top padding moves inside the scroll region

- [x] 7.1 Audit every existing `ScribeTabHeader.Build` call site for the caller-owned fixed
  `Padding(EdgeInsets.Only(top: 4f))` sitting outside the scroll region (the idiom the helper's own
  doc-comment names, e.g. `ScribeReadContent.cs`'s `Expanded(Padding(top:4, rowList))`). Found:
  `ScribeReadContent.cs`, `ScribeEditorContent.cs`, `ScribePinnedContent.cs`,
  `ScribeInboxContent.cs`, `ScribeDialogBase.Guestbook.cs`, `GuiDialogScribeScriptorium.cs`,
  `GuiDialogScribeNotebook.cs` (History), `ScribeAssignmentFormContent.cs`,
  `GuiDialogClockmakerNotebook.cs` (Timer, added new in §6.3) — every non-tablet tab.
- [ ] 7.2 For each call site found in 7.1, remove that outer padding and add the equivalent 4-unit
  top padding to the `Column`/content living inside the tab's `SingleChildScrollView` instead (or
  the equivalent non-scrolling content wrapper for the two form-shaped tabs). `dotnet build`
  verified; still needs the in-game check — after restaging, confirm each affected tab shows the
  divider flush against the viewport at rest, and the same visual gap at the top of the content
  that scrolls away once you scroll down.

## 8. Row 1 drag-grip relocation

- [x] 8.1 `ScribeDialogBase.Layout.cs` (`BuildTitleBar`) — move the drag-grip's
  `Tooltip`/`GestureDetector`/glyph into a new leading slot before `titleSlot` in `titleRow`,
  removing it from the trailing group. Keep its existing `OnGripDragStart`/`OnGripDragMove`/
  `OnGripDragEnd` wiring and tooltip text unchanged — this is a reposition, not a new drag
  mechanism.
- [ ] 8.2 Reduce the title row's left inset from `10 + 0.04·W` to `0.04·W`, matching the right
  inset, now that the grip itself (plus its own spacing) occupies the leading space. `dotnet
  build` verified; still needs the in-game check — after restaging, confirm the title text still
  isn't clipped and the grip glyph has visible spacing from both the panel edge and the title text
  at default and narrow window sizes.
- [ ] 8.4 (2026-09-08 playtest) Halve both the title's own inner left padding
  (`titleBtnSpacing * 1.5f` → `* 0.75f`) and the outer left inset a second time (`0.04·W` →
  `0.02·W`, right inset unchanged), and vertically align the grip to the title's baseline via a
  bottom-`Padding` computed from the title font's own `descent + leading` metrics. `dotnet build`
  verified; still needs the in-game check.
- [ ] 8.3 Verify by dragging the window from the new grip position, from elsewhere in the title
  band (unaffected band-drag), and from the tooltip's hover state — all three still behave exactly
  as before the move — on at least one dialog with a two-line-wrapped title (bottom-anchored Row 1)
  to confirm the grip still aligns sensibly there too.

## 9. Row1-row2 gap tightened 6px, all ten tabs

- [x] 9.1 (2026-09-08 playtest) Change each of the ten tabs' outer content `Padding` top inset
  from `10` to `4` (left/right/bottom stay `10`):
  `ScribeReadContent.cs`, `ScribeEditorContent.cs`, `ScribePinnedContent.cs`,
  `ScribeInboxContent.cs`, `ScribeDialogBase.Guestbook.cs`, `GuiDialogScribeScriptorium.cs`,
  `GuiDialogScribeNotebook.cs` (History), `ScribeAssignmentFormContent.cs`,
  `GuiDialogClockmakerNotebook.cs` (Timer), and `GuiDialogScribeInbox.cs`'s Inbox Inventory tab
  (which previously had no outer padding wrapper at all — added one). `dotnet build` and `dotnet
  test tests/Core.Tests` verified.

## 10. Tablet renders neither Row 2 nor Row 3

- [x] 10.1 (2026-09-08 playtest, reverses an earlier pass) Add `SupportsTabHeader` (default
  `true`) to `ScribeDialogBase.Layout.cs`, mirroring `SupportsFilterPills`; override it `false` on
  `GuiDialogScribeTablet.cs`.
- [x] 10.2 Thread `supportsTabHeader: SupportsTabHeader` into `BuildReadContent()`'s
  `ScribeReadContent` construction and `BuildEditorContent()`'s `ScribeEditorContent`
  construction; add the ctor parameter + property to both widgets.
- [x] 10.3 In `ScribeReadContent`/`ScribeEditorContent`, skip the `ScribeTabHeader.Build` call
  entirely when `SupportsTabHeader` is false (rather than passing empty Row 2/Row 3 content), and
  revert the surrounding padding to its pre-header values on that path (outer top inset stays
  `10`; the §7 inner 4px scroll-content top padding is skipped). `dotnet build` and `dotnet test
  tests/Core.Tests` verified.

## 12. Create Assignments tab rearrangement (Round 3, 2026-09-09 playtest)

- [x] 12.1 `ScribeAssignmentFormContent.cs` — reduce Row 3 to ONLY the send-to row (player picker +
  Send button); move the staging-slot hint, delete-checkbox row, delivery toggle, and notice
  slots back down into the scrollable/general content below the divider (the previous pass had
  moved the whole drafting form into Row 3, which read as too much header chrome). `dotnet build`
  verified; still needs the in-game check.
- [x] 12.2 Delete the "Assign Tasks" heading `Text` row entirely and its
  `scribe-assignment-form-heading` lang key from `en.json`. `en.json` verified to still parse as
  valid JSON.
- [x] 12.3 Rename the two notice-slot hint strings to just the item names: `en.json`'s
  `scribe-delivery-notice-supply-hint` → "Task Notice", `scribe-delivery-notice-output-hint` →
  "Assigned Notice".
- [x] 12.4 Move the "Local Inboxes" / "Send a Notice" delivery-toggle row to sit BELOW the
  scrollable stage tray in the content area, instead of bundled with the other form controls
  above it. `dotnet build` and `dotnet test tests/Core.Tests` verified; still needs the in-game
  check (draft/stage/send an assignment end to end with both delivery choices).

## 13. Round 3 polish (2026-09-09 playtest)

- [x] 13.1 `GuiDialogScribeInbox.cs` (`BuildInboxInventoryContent`) — stop passing the slot grid as
  Row 3 content (which put the durable divider AFTER it, at the tab's very bottom); call
  `ScribeTabHeader.Build` with no Row 3 so the divider sits directly under the subtitle like every
  other tab, then place the slot grid as general content after the header. Establishes the
  general rule: any tab sharing the Row 1/Row 2 header anatomy always shows its divider right
  there, never deferred behind unrelated content. `dotnet build` verified; still needs the in-game
  check.
- [x] 13.2 `ScribeDialogBase.Layout.cs` (`ScribeTabHeader.Build`) — increase the small-caps size
  from 75% to 87.5% of the cap-letter size (halfway between the old 75% and the full cap size).
  `dotnet build` verified; still needs the in-game check.
- [x] 13.3 `ScribeDialogBase.Layout.cs` (`BuildTitleBar`) — halve the drag-grip's baseline-nudge
  (from the full descent+leading correction to half of it), since the prior fix (unify-tab-
  header-layout 8.4) overshot and read as sitting too high relative to the title's visual center.
  `dotnet build` verified; still needs the in-game check.
- [x] 13.4 Double the established "padding-top before the first element in scrollable/general
  content" idiom (unify-tab-header-layout §7) from 4 units to 8, across every call site:
  `ScribeReadContent.cs`, `ScribeEditorContent.cs`, `ScribePinnedContent.cs`,
  `ScribeInboxContent.cs`, `ScribeDialogBase.Guestbook.cs`, `GuiDialogScribeScriptorium.cs`,
  `GuiDialogScribeNotebook.cs` (History), plus the equivalent gap in
  `ScribeAssignmentFormContent.cs`'s restructured content block (12.1) and
  `GuiDialogScribeInbox.cs`'s slot grid (13.1). `dotnet build` and `dotnet test tests/Core.Tests`
  verified; still needs the in-game check.
- [x] 13.5 `ScribeDialogBase.Layout.cs` (`BuildTitleField`) — set the title `TextField`'s
  `TextFieldStyle.Height` explicitly to the title font's own line height instead of the struct's
  fixed 40px default, which visibly grew the title band the instant editing started. Border was
  already 0. The stock `TextField`'s hardcoded 10px horizontal text inset (baked into
  `RenderTextField.PaintInternal`, not exposed on `TextFieldStyle`) can't be zeroed without
  forking `gui` — documented as a known residual gap in `VSAPI-NOTES.md` rather than silently
  left unmentioned. `dotnet build` verified; still needs the in-game check.

## 14. Verification

- [ ] 14.1 Rebuild the mod (`build/restage.sh Debug`, client not running) and manually open every
  affected tab in-game — Read, Edit, Pinned, History, Guest Book, Inbox, Sent Assignment History,
  Create Assignments, Transcribe, Inbox Inventory, Timer — confirming each shows a distinct,
  correctly-styled (per-word small-caps label, italic descriptor, baseline-aligned) subtitle and
  exactly one durable divider flush against its scroll content (two for Guest Book only), with no
  clipped or overlapping header content at default window size, that the drag-grip has moved to
  the left of every dialog's title and aligns with the title baseline, and that the Row1-row2 gap
  reads visibly tighter than before. Also confirm the Tablet's Read View/Editor show NEITHER Row 2
  nor Row 3 — no subtitle line, no extra divider beyond its own title-bar chrome.
- [ ] 14.2 Manually verify the Round 3 items in-game: Create Assignments' new layout (Row 3 = only
  send-to; hint/checkbox/notice-slots/delivery-toggle all below the divider, delivery toggle below
  the scrollable stage tray) end to end for both delivery choices; Inbox Inventory's divider now
  sits under its subtitle; the small-caps size reads bigger; the grip no longer sits too high; the
  content top-padding reads visibly roomier; and clicking the title edit-pencil no longer grows
  the title band.
- [x] 14.3 Add or update this change's manual-verification entries in `TESTING.md` per the
  `what-to-test` workflow, confirming every affected tab/behavior is represented on the checklist.

## 16. Round 4 polish (2026-09-10 playtest)

- [x] 16.1 `ScribeDialogBase.Layout.cs` (`ScribeTabHeader.Build`) — pull the small-caps size back
  from 87.5% to 82.5% of the cap-letter size (87.5% read as too close to the cap size). `dotnet
  build` verified; still needs the in-game check.
- [x] 16.2 `ScribeDialogBase.Layout.cs` (`BuildTitleBar`) — replace the static
  `TitleMaxLines`-reserved band/content-box height with one driven by the CURRENT title's actual
  wrapped line count (new `CountWrappedLines` helper, mirroring `ScribeMultilineField`'s own
  greedy word-wrap), so a short title's band is unchanged and a genuinely-wrapping title grows the
  band downward only, pushing Row 2/content down with it instead of the old static reservation
  reading as growth in both directions. The title-edit `TextField` has no wrap support at all, so
  the measurement is skipped while `_isTitleEditing` (band stays single-line-sized during an
  edit). `dotnet build` verified; still needs the in-game check — after restaging, confirm a
  short title's band/Row 2 position is unchanged, and a long enough title to force a 2nd line
  pushes Row 2 and the rest of the tab's content down with no upward shift.
- [x] 16.3 `ScribeAssignmentFormContent.cs` (`BuildDeliveryRow`) — size the info `ScribeRowButton`
  from the same font metrics + `ButtonStyle.Default` padding/border that determine the Local
  Inboxes/Send a Notice `Button`s' own height beside it, with a solved-for `IconScale` so growth
  is mostly padding (icon only ~15% bigger than its old rendering). `dotnet build` verified; still
  needs the in-game check — after restaging, confirm the info button's box now visually matches
  the height of the two buttons beside it, with the icon only slightly larger than before.
- [x] 16.4 `ScribeAssignmentFormContent.cs` (`Build`) — move `noticeSlotsRow` to sit directly
  above `deliveryRow` (previously above the scrollable stage tray), and merge
  `deleteFromSourceRow` into the same `Row` as `deliveryRow` (`MainAxisAlignment.SpaceBetween`,
  checkbox+label on the trailing side), falling back to a standalone row only when `deliveryRow`
  is null (non-Hybrid delivery mode, no toggle to share with). `dotnet build` and `dotnet test
  tests/Core.Tests` verified; still needs the in-game check — after restaging, confirm the notice
  slots sit just above the Local Inboxes/Send a Notice group, the delete-checkbox reads on the
  same line trailing that group in Hybrid mode, and the checkbox still renders on its own line in
  a non-Hybrid delivery mode.
- [x] 16.5 Update this change's `TESTING.md` entries for the Round 4 items above per the
  `what-to-test` workflow.

## 17. Round 5 polish (2026-09-10, 2nd playtest pass)

- [x] 17.1 `ScribeAssignmentFormContent.cs` (`BuildDeliveryRow`) — reduce the info button's
  `infoSize` by 2px (1px/side), re-solving `IconScale` against the smaller size so the icon's
  target size stays unchanged and the reduction comes entirely out of padding. `dotnet build`
  verified; still needs the in-game check — after restaging, confirm the info button now reads
  slightly smaller than the two buttons beside it (not dead-on matching their height, per Round 4).
- [x] 17.2 `ScribeAssignmentFormContent.cs` (`Build`) — un-merge `deleteFromSourceRow` from
  `deliveryRow` (removing Round 4's `deliveryAndDeleteRow`/non-Hybrid-fallback construction
  entirely) and place the standalone checkbox row at the very TOP of the content section, above
  `stagingArea`. `dotnet build` and `dotnet test tests/Core.Tests` verified; still needs the
  in-game check — after restaging, confirm the delete-checkbox renders on its own line at the top
  of the content area (above the staging slot), in both Hybrid and non-Hybrid delivery modes.
- [x] 17.3 `ScribeDialogBase.Layout.cs` (`BuildTitleBar`) — fix Round 4's band-height formula: grow
  `bandH` by the same `extraLines * titleLineH` delta as `contentBoxH` (both from their
  single-line baselines) instead of `Math.Max(layout.TitleBarH, contentBoxH)`, which almost never
  actually grows since `TitleBarH` already has slack over `TitleBtnsH`. `dotnet build` verified;
  still needs the in-game check — after restaging, confirm a title wrapping to a 2nd line no
  longer visibly slides upward, and single-line titles are pixel-identical to before.
- [x] 17.4 Update this change's `TESTING.md` entries for the Round 5 items above per the
  `what-to-test` workflow.

## 18. Round 6 polish (2026-09-10, 3rd playtest pass)

- [x] 18.1 `ScribeDialogBase.Layout.cs` (`BuildTitleBar`) — replace the two nested `SizedBox`es
  (exact, precomputed `bandH`/`contentBoxH` heights) with `ConstrainedBox`es carrying only a
  MINIMUM height (`layout.TitleBarH`/`layout.TitleBtnsH`) and an unbounded maximum, so the real
  measured `titleRow` decides its own height instead of a `titleLineH` estimate. Remove
  `titleLineH`/`contentBoxH`/`bandH`/`extraLines` entirely; keep `CountWrappedLines`/
  `titleAvailableW` only to choose `titleCrossAlign` (cosmetic, no longer sizing-critical). `dotnet
  build` and `dotnet test tests/Core.Tests` verified; still needs the in-game check — after
  restaging, confirm a title wrapping to a 2nd line grows smoothly downward with NO discrete jump
  (in either direction), and a single-line title's band is pixel-identical to before.
- [x] 18.2 `ScribeDialogBase.Layout.cs` (`BuildTitleBar`) — reaffirm (comment-only, no logic
  change) that the title-edit pencil's `scratch is not null` gate is already exactly the Edit-tab
  gate for every tabbed dialog (traced through `LeaveEditorMode`), and that the Tablet's single
  always-edit view correctly counts as "the Edit tab" too. `dotnet build` verified — no in-game
  check needed (no behavior changed).
- [x] 18.3 Update this change's `TESTING.md` entries for the Round 6 items above per the
  `what-to-test` workflow.

## 19. Round 6 correction (2026-09-10, 4th playtest pass — regression fix)

- [x] 19.1 `ScribeDialogBase.Layout.cs` (`BuildTitleBar`) — 18.1's `Align(Alignment.BottomCenter)`
  wrappers regressed to collapsing the ENTIRE dialog against the bottom of the window (reported by
  the user immediately after restaging): this subtree is a non-`Expanded` `Column` child pinned
  inside a fixed-height `SizedBox`, so the "unbounded" `maxHeight: float.PositiveInfinity` was
  actually clamped down to the dialog's fixed total height, and `Align` fills whatever space it's
  handed rather than passively respecting a floor. Remove both `Align`s; replace with a fixed top
  `Padding(EdgeInsets.Only(top: layout.TitleBarH - layout.TitleBtnsH))` reproducing the same
  historical gap directly, wrapping a single min-only `ConstrainedBox` around ordinary
  intrinsic-sizing widgets (no fill-to-max behavior anywhere in the remaining chain). `dotnet build`
  and `dotnet test tests/Core.Tests` (753/753) verified; still needs the in-game check — after
  restaging, confirm the dialog renders normally again (title/Row 2/Row 3/content all back in their
  normal positions) AND re-verify the original Round 6 ask (title wraps to a 2nd line growing
  smoothly downward, no jump, single-line pixel-identical to before).
- [x] 19.2 Update this change's `TESTING.md` `000000da` entry to also cover this regression
  (confirm the whole-dialog-collapse is gone, not just the wrap-growth behavior).

## 20. Round 7 — revert Round 6 + its correction entirely (2026-09-10, 5th playtest pass)

- [x] 20.1 `ScribeDialogBase.Layout.cs` (`BuildTitleBar`) — 19.1's `Align` removal also deleted the
  only step that horizontally CENTERED the title row within the full-width band (`Align(BottomCenter)`
  centers on both axes, not just bottom-anchors), regressing the whole grip+title+trailing-buttons
  group flush against the left edge (reported by the user: "ruined the orientation left and right").
  Reverted Round 6 (18.1) and Round 6 correction (19.1) wholesale back to Round 5's structure: two
  nested, exactly-sized `SizedBox`es (`W × bandH` outer, `TitleBtnsW × contentBoxH` inner) with
  `Align(Alignment.BottomCenter)` centering the inner box — restoring centering, and keeping the
  outer box's height explicit/bounded so `Align` only ever fills that bounded height, never the
  whole dialog (the bottom-collapse bug can't recur once the outer box isn't unbounded). To avoid
  reintroducing Round 5's own disclosed "jump on wrap" bug, fixed `titleLineH` to derive from the
  title's real font metrics (`titleFontMetrics`, already computed for the grip-baseline nudge)
  instead of the mismatched `CuneiformMetrics.LineHeightRatio`. `dotnet build` and
  `dotnet test tests/Core.Tests` (753/753) verified; still needs the in-game check — after
  restaging, confirm: (a) the title row is horizontally centered again (not flush-left), (b) the
  whole-dialog bottom-collapse stays gone, and (c) a title wrapping to a 2nd line still grows
  smoothly downward with no jump, single-line pixel-identical to before.
- [x] 20.2 Update this change's `TESTING.md` `000000da` entry to also cover this second regression.
