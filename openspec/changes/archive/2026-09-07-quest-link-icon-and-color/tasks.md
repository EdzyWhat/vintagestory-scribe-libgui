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
- [x] 2.4 (design.md D6 — added after 2026-09-06 playtest, value corrected twice same day) Add a
  `HudQuestLinkAccent` constant to `src/Mod/ScribeTheme.cs`, next to `QuestLinkAccent`, at the
  playtester-finalized value `rgb(172,207,255)`. Document it as HUD-only and already finalized (not
  a placeholder), unlike the other constants in Task 2.2.
  - Done 2026-09-06: added; a first pass at this task never actually landed (retest `000000aa`
    caught it still reading the old shared `QuestLinkAccent`). Value then went through two more
    same-day corrections: `rgb(122,176,255)` when the fix landed, brightened once more to
    `rgb(172,207,255)` on a third pass.

## 3. Shared leading-slot helper

- [x] 3.1 Add `ScribeRowControlNudge.BuildLeadingControl(context, style, linkTarget, done,
  onChanged)` to `src/Mod/ScribeRowWidgets.cs`: returns a `ScribeVsIconGlyph("scribequest", ...)`
  sized to the checkbox's footprint and colored via `style.QuestLinkColor ?? QuestLinkAccent` when
  `ScribeLinkTarget.IsQuest(linkTarget)` is true, otherwise returns the existing
  `BuildTaskCheckbox(...)` result unchanged. Verify by reading the method that the non-quest branch
  is byte-identical to today's `BuildTaskCheckbox` call.
- [x] 3.2 (design.md D7 — added after 2026-09-06 playtest) Give
  `ScribeRowControlNudge.CheckboxAndGripTop` a quest-aware branch: when computing the leading-slot
  control's top offset for an item row that is a Quest Link (`ScribeLinkTarget.IsQuest(linkTarget)`),
  center against the plain `SingleLineInputHeight`-derived band (the same formula the non-`itemRow`
  branch already uses) instead of the tall `iconBand` formula, while still applying
  `ItemControlOpticalNudgeEm` (not the task-row nudge). Add a `linkTarget` parameter (or an
  `isQuestLink` bool) to the method's signature. Verify by reading the diff that the quest branch's
  formula matches the non-`itemRow` branch's centering term (no existing automated coverage of this
  helper's pixel math — verified in-game per Task 8, same as the rest of this change).
  - Done 2026-09-06: added a third branch (checked after the `!itemRow` case, before the `iconBand`
    case) that reuses the exact `SingleLineInputHeight`-derived centering term but keeps
    `ItemControlOpticalNudgeEm`. Added an optional `linkTarget` parameter (default `null`, so every
    existing non-quest call site is unaffected).

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
- [x] 4.4 (design.md D7 — added after 2026-09-06 playtest; 4.3 only fixed the text side) Update this
  file's `CheckboxAndGripTop(style, Widget.Data.IsItemKind)` call for the row's leading-slot control
  (not the invisible grip spacer) to pass the Quest Link check added in Task 3.2, so the icon's
  top-offset uses the same short band the text already uses. Verify in-game: a Quest Link row's
  icon and text now align at the same vertical center; a non-quest item row (Tracker/Craft/plain
  Link) is visually unchanged.
  - Done 2026-09-06: `CheckboxAndGripTop` call in `ScribeReadRowState.Build` now passes
    `Widget.Data.LinkTarget`. Pending in-game retest.

## 5. Editor view (`src/Mod/ScribeEditorContent.cs`)

- [x] 5.1 Replace the `BuildTaskCheckbox(...)` call inside the `Completable`-gated block (around
  line 1098) with `BuildLeadingControl(...)`.
- [x] 5.2 Guard the inline `ScribeLinkIcon.Build(...)` → `rowChildren.Add(...)` step (around line
  984) with `!ScribeLinkTarget.IsQuest(Widget.Data.LinkTarget)`, and use `questLinkColor` in place
  of `style.LinkColor ?? colors.Primary` for the item-name hyperlink color (around line 1001) when
  the row is a quest link.
- [x] 5.3 Same `bandHeight` re-derivation check as Task 4.3, verified in-game (Task 8).
- [x] 5.4 Same `CheckboxAndGripTop` call-site update as Task 4.4, for this file's leading-slot
  call. Verify in-game as in 4.4.
  - Done 2026-09-06: `CheckboxAndGripTop` call in the live editor row now passes
    `Widget.Data.LinkTarget`. Pending in-game retest.

## 6. Pinned view (`src/Mod/ScribePinnedContent.cs`)

- [x] 6.1 Replace the `BuildTaskCheckbox(...)` call inside the `Completable`-gated block (around
  line 605) with `BuildLeadingControl(...)`.
- [x] 6.2 In `BuildItemContent`, guard the `rowChildren.Add(icon)` step for the `IsLink` branch
  (around line 529) the same way as Task 4.2, using `questLinkColor` for `nameLink`.
- [x] 6.3 Same `bandHeight` re-derivation check, verified in-game (Task 8).
- [x] 6.4 Same `CheckboxAndGripTop` call-site update as Task 4.4, for this file's leading-slot
  call. Verify in-game as in 4.4.
  - Done 2026-09-06: `CheckboxAndGripTop` call in the pinned row now passes `data.LinkTarget`.
    Pending in-game retest.

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
- [x] 7.3 (design.md D6 — added after 2026-09-06 playtest) In `HudScribePins.cs`, replace both
  `ScribeTheme.QuestLinkAccent` reads (the leading-slot `ScribeVsIconGlyph("scribequest", ...)` and
  `BuildHudItemContent`'s `textStyle.Color` override) with `ScribeTheme.HudQuestLinkAccent` (Task
  2.4). Verify in-game: a Quest Link pinned to the HUD reads at the new blue, while the same Quest
  Link on Read/Editor/Pinned/Assignment-stage is visibly unchanged (still the shared
  `QuestLinkAccent`).
  - Done 2026-09-06: both reads swapped. Pending in-game retest (TESTING.md `000000aa`, tasks.md
    8.3a) — this task's first landing attempt is what `000000aa` caught as missing.

## 8. Manual verification

- [x] 8.1 Build the mod (`dotnet build`) and confirm it compiles with no new warnings.
- [x] 8.2 Restage Debug (`build/restage.sh Debug`) with the game client closed, then relaunch.
- [x] 8.3 On the parchment (Light) theme, view a Quest Link row on Read, Editor, Pinned, and the
  - Confirmed 2026-09-06: TESTING.md `000000a9` "(no note)" (submission 2026-09-06T18-08-19) —
    narrowed same day to the icon-swap/checkbox/color scope below; the alignment claim this task
    originally also carried was NOT actually verified (3.2/4.4/5.4/6.4 were never implemented) and
    is split out as 8.3b, per direct user correction.
  Assignment-stage picker (HUD is covered separately by 8.3a) alongside a plain Link row; confirm
  the quest row shows the exclamation-in-a-circle icon in the checkbox's former slot (or, on the
  Assignment-stage row, alongside its still-functioning selection checkbox), shows no completion
  checkbox on the three completion surfaces (Read/Editor/Pinned), and reads in the steel-blue
  accent while the plain Link stays its normal color.
- [x] 8.3a (added after 2026-09-06 playtest; value corrected twice same day, see Task 2.4) On the
  HUD specifically, confirm a pinned Quest Link renders at the finalized `rgb(172,207,255)` (Task
  2.4/7.3), not the shared steel-blue accent the other four surfaces use.
  - Confirmed 2026-09-06: TESTING.md `000000aa` — "the new HUD color is good."
- [x] 8.3b (split out of 8.3 on 2026-09-06 — that task was incorrectly marked confirmed for this
  part) On the same four surfaces (Read, Editor, Pinned, Assignment-stage), confirm the icon and
  item name are vertically centered together (Task 3.2/4.4/5.4/6.4's fix). A 2026-09-06 playtest
  found the text rendering noticeably higher than the icon; 3.2/4.4/5.4/6.4 have since landed
  (`CheckboxAndGripTop`'s quest-aware branch + its Read/Editor/Pinned call-site wiring) — this task
  is the pending in-game retest of that fix.
  - **2026-09-07 correction (tablet-only):** a fresh screenshot showed the quest icon still pinned
    to the row's top edge on the tablet specifically (Notebook/Lectern were fine). Root cause: the
    3.2/4.4/5.4/6.4 landing used `ScribeRowControlNudge.TextLineHeight(style.FontSize)` (the plain
    Latin line height) for the quest branch's band at all four call sites — correct on
    Lectern/Notebook but far too short on the tablet, where the real cuneiform text line is
    `FontSize * LineHeightRatio * GlyphDrawScale` (nearly double). Fixed by routing all four
    through the cuneiform-aware `ItemNameLineHeight(style)` helper instead (same helper the text
    band itself already used indirectly), and adding the missing `GlyphDrawScale` factor to that
    helper (it predates that constant). See design.md's D7 correction. Pending in-game retest on
    the tablet specifically, alongside the original four-surface retest above.
  - **Second correction, same day:** a follow-up screenshot showed the icon/text now aligned, but
    the grip handle (drag-dots left of the checkbox) still sat higher — `GripInsets` never threaded
    `linkTarget` through its own `CheckboxAndGripTop` call, so it kept using the generic tall
    icon-band formula for every row kind, quest included. Fixed by adding an optional `linkTarget`
    parameter to `GripInsets` and passing it through at all four call sites (Read, Pinned, Editor's
    live row, Editor's frozen ghost row). See design.md's second correction. Pending in-game retest.
- [x] 8.4 Repeat the glance-check on the chalkboard and on all four tablet clay variants
  - Confirmed 2026-09-06: TESTING.md `000000ab` "(no note)" (submission 2026-09-06T18-08-19)
  (clay-fire, clay-red, clay-blue, wax); confirm each surface's quest color is legible against its
  own backdrop and, on the blue-clay tablet specifically, confirm the quest color is visibly
  distinct from that tablet's own (already-blue) Primary/link color. Adjust the placeholder values
  from Task 2.2 if any surface reads poorly, and record the final values. (The HUD's value is
  already finalized per 8.3a — this task covers only chalkboard/tablet.)
- [x] 8.5 Confirm a Quest Link's completion count is unaffected: check that a document containing a
  - Confirmed 2026-09-06: TESTING.md `000000ac` "(no note)" (submission 2026-09-06T18-08-19)
  Quest Link still reports the same "N of M tasks done" total as before this change, and that its
  `Done` value round-trips through the existing completion toggle path used elsewhere (e.g. via
  TSV export/import) even though its checkbox is no longer shown.
