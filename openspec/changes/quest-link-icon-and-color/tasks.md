## 1. Icon asset

- [x] 1.1 Register the existing `src/Mod/assets/scribe/textures/icons/quest.svg` as `"scribequest"`
  in `src/Mod/ScribeModSystem.Assets.cs`, next to the other `RegisterSvgIcon` calls (e.g. beside
  `"scribebook"`), and verify the mod builds with no asset-loading warnings.

## 2. Color seam

- [x] 2.1 Add a nullable `QuestLinkColor` field to `src/Mod/ScribeRowStyle.cs`, immediately next to
  the existing `LinkColor` field, following the same doc-comment style.
- [x] 2.2 In `src/Mod/ScribeTheme.cs`, add a `QuestLinkAccent` constant (steel-blue,
  `rgb(66,107,183)`) as the default/parchment value, a `ChalkboardQuestLinkText` constant (a
  lightened variant of `QuestLinkAccent`, mirroring how `ChalkboardLinkText` lightens
  `ChalkAccent`), and a `ForTabletQuestLink(material, state)` function mirroring `ForTabletLink`:
  a darkened/saturated variant of `QuestLinkAccent` for `clay-fire`/`clay-red`/`wax`, and a
  distinct warm amber/gold (NOT a blue) for `clay-blue`. Document each as a playtest-tunable
  starting default, per design.md D4.
- [x] 2.3 Bake `QuestLinkColor` explicitly wherever `LinkColor` is already explicitly baked: the
  tablet's `DecorateRowStyle` (using `ForTabletQuestLink`) and the chalkboard's `DecorateRowStyle`
  (using `ChalkboardQuestLinkText`). Verify by reading both call sites that `QuestLinkColor` is set
  alongside `LinkColor` in the same place, not left null.

## 3. Shared leading-slot helper

- [x] 3.1 Add `ScribeRowControlNudge.BuildLeadingControl(context, style, linkTarget, done,
  onChanged)` to `src/Mod/ScribeRowWidgets.cs`: returns a `ScribeVsIconGlyph("scribequest", ...)`
  sized to the checkbox's footprint and colored via `style.QuestLinkColor ?? QuestLinkAccent` when
  `ScribeLinkTarget.IsQuest(linkTarget)` is true, otherwise returns the existing
  `BuildTaskCheckbox(...)` result unchanged. Verify by reading the method that the non-quest branch
  is byte-identical to today's `BuildTaskCheckbox` call.

## 4. Read view (`src/Mod/ScribeReadContent.cs`)

- [x] 4.1 Replace the `BuildTaskCheckbox(...)` call inside the `Completable`-gated `Padding` (around
  line 499) with `BuildLeadingControl(...)`, keeping the surrounding `Padding`/top-nudge unchanged.
- [x] 4.2 In `BuildItemContent`, guard the `rowChildren.Add(icon)` step for the `IsLink` branch
  (around line 407) with `!ScribeLinkTarget.IsQuest(Widget.Data.LinkTarget)`, and resolve a
  `questLinkColor` the same way `linkColor` is resolved (`style.QuestLinkColor ?? QuestLinkAccent`)
  for use on `nameLink`'s text color when the row is a quest link, in place of `linkColor`.
- [x] 4.3 If the item name's vertical centering (`ScribeCenterIfShort.Name`, fed by `bandHeight`)
  visibly misaligns for a quest row now that no inline icon feeds `bandHeight`, re-derive
  `bandHeight` for that row from plain `lineHeight` instead. Verify in-game (Task 8) rather than by
  inspection alone.

## 5. Editor view (`src/Mod/ScribeEditorContent.cs`)

- [x] 5.1 Replace the `BuildTaskCheckbox(...)` call inside the `Completable`-gated block (around
  line 1098) with `BuildLeadingControl(...)`.
- [x] 5.2 Guard the inline `ScribeLinkIcon.Build(...)` → `rowChildren.Add(...)` step (around line
  984) with `!ScribeLinkTarget.IsQuest(Widget.Data.LinkTarget)`, and use `questLinkColor` in place
  of `style.LinkColor ?? colors.Primary` for the item-name hyperlink color (around line 1001) when
  the row is a quest link.
- [x] 5.3 Same `bandHeight` re-derivation check as Task 4.3, verified in-game (Task 8).

## 6. Pinned view (`src/Mod/ScribePinnedContent.cs`)

- [x] 6.1 Replace the `BuildTaskCheckbox(...)` call inside the `Completable`-gated block (around
  line 605) with `BuildLeadingControl(...)`.
- [x] 6.2 In `BuildItemContent`, guard the `rowChildren.Add(icon)` step for the `IsLink` branch
  (around line 529) the same way as Task 4.2, using `questLinkColor` for `nameLink`.
- [x] 6.3 Same `bandHeight` re-derivation check, verified in-game (Task 8).

## 7. HUD pins (`src/Mod/HudScribePins.cs`) and Assignment-stage row (`src/Mod/ScribeAssignmentStageRow.cs`)

- [x] 7.1 In `HudScribePins.cs`, swap the leading-slot control for a Quest Link row (its checkbox is
  a bespoke grayscale `CheckboxStyle`, not `BuildTaskCheckbox`, so the swap is inlined rather than
  routed through `BuildLeadingControl` — see design.md's amended D1 note — to avoid silently
  restyling every non-quest row's checkbox), and in `BuildHudItemContent` skip building the inline
  `icon` widget and use `ScribeTheme.QuestLinkAccent` for the name's `textStyle.Color` on that row.
- [x] 7.2 In `ScribeAssignmentStageRow.cs`, leave the existing `BuildTaskCheckbox(...)` call
  UNCHANGED (design.md D1a — that checkbox is a row-selection control, not a completion toggle, and
  must keep working for a Quest Link row). Since that leading slot isn't available for the quest
  icon here, SWAP the inline icon (around line 52) to the quest-marker glyph instead of skipping it
  (design.md D2 exception), using `questLinkColor` for both the icon and `nameLabel`.

## 8. Manual verification

- [x] 8.1 Build the mod (`dotnet build`) and confirm it compiles with no new warnings.
- [x] 8.2 Restage Debug (`build/restage.sh Debug`) with the game client closed, then relaunch.
- [ ] 8.3 On the parchment (Light) theme, view a Quest Link row on each surface (Read, Editor,
  Pinned, HUD, and the Assignment-stage picker) alongside a plain Link row; confirm the quest row
  shows the exclamation-in-a-circle icon in the checkbox's former slot (or, on the Assignment-stage
  row, alongside its still-functioning selection checkbox), shows no completion checkbox on the
  four completion surfaces, and reads in the steel-blue accent while the plain Link stays its
  normal color. Confirm no vertical misalignment on the item name.
- [ ] 8.4 Repeat the glance-check on the chalkboard and on all four tablet clay variants
  (clay-fire, clay-red, clay-blue, wax); confirm each surface's quest color is legible against its
  own backdrop and, on the blue-clay tablet specifically, confirm the quest color is visibly
  distinct from that tablet's own (already-blue) Primary/link color. Adjust the placeholder values
  from Task 2.2 if any surface reads poorly, and record the final values.
- [ ] 8.5 Confirm a Quest Link's completion count is unaffected: check that a document containing a
  Quest Link still reports the same "N of M tasks done" total as before this change, and that its
  `Done` value round-trips through the existing completion toggle path used elsewhere (e.g. via
  TSV export/import) even though its checkbox is no longer shown.
