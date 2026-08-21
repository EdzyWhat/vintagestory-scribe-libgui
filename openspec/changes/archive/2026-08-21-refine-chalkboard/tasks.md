## 1. Cap the chalkboard at 10 tasks

- [x] 1.0 Add the policy seam to the block-entity base. Unlike the item hosts, the block-entity
      base uses EXPLICIT interface implementations and relies on the `IScribeDocumentHost.Policy`
      DEFAULT member, so a bare `Policy` property on a subclass would not re-map it (same hazard
      `NotebookHost.Policy` documents). In `src/Mod/BlockEntityScribeWritingStation.cs` add
      `protected virtual ScribeDocumentPolicy HostPolicy => ScribeDocumentPolicy.Unlimited;` and
      the explicit member `ScribeDocumentPolicy IScribeDocumentHost.Policy => HostPolicy;` so the
      seam actually reaches interface dispatch (Lectern/Scriptorium stay uncapped by default).
- [x] 1.1 In `src/Mod/BlockEntityScribeChalkboard.cs`, override `protected override
      ScribeDocumentPolicy HostPolicy => new() { MaxBlocks = 10 };` (MaxPins left `null` =
      uncapped, ReadOnly false). Add a doc-comment noting it deliberately does NOT reuse the
      `Tablet` preset because the chalkboard is shared and pins are per-player, so only tasks are
      capped. (Added `using Scribe.Core;` for `ScribeDocumentPolicy`.)

## 2. Correct the cap-reached notice wording

- [x] 2.1 In `src/Mod/ScribeDialogBase.cs`, add `protected virtual string TaskCapReachedLangKey
      => "scribe:tablet-full";` and change `NotifyTabletFull()` to read
      `Lang.Get(TaskCapReachedLangKey)` instead of the hardcoded `"scribe:tablet-full"`. Behavior
      for the tablet stays identical (default key unchanged).
- [x] 2.2 In `src/Mod/GuiDialogScribeChalkboard.cs`, override `protected override string
      TaskCapReachedLangKey => "scribe:chalkboard-full";`.
- [x] 2.3 In `src/Mod/assets/scribe/lang/en.json`, add `"chalkboard-full": "A chalkboard holds
      at most 10 tasks."` (confirm final wording).

## 3. Brighten the chalkboard accent (Primary +10 HSV Value)

- [x] 3.1 In `src/Mod/ScribeTheme.cs`, brighten the `ChalkAccent` (`Primary`) constant from
      `(0.17, 0.42, 0.24)` to `(0.210, 0.520, 0.297)` — a uniform ×`0.52/0.42` scale that lifts
      HSV Value by +10 points while holding hue + saturation, so the checkbox tick / caret /
      button fill / selection tint all read on the dark slate. Update its doc-comment.

## 4. Make the completion-policy picker's open menu legible on the chalkboard

- [x] 4.1 Add a `private protected virtual DropdownStyle DecoratePolicyDropdownStyle(DropdownStyle
      style) => style;` seam on `ScribeDialogBase` (in `ScribeDialogBase.Layout.cs`, beside the
      other nav/theme seams).
- [x] 4.2 Thread it through the pin tab: add a nullable `Func<DropdownStyle, DropdownStyle>`
      parameter + property to `ScribePinnedContent` (qualify as `System.Func` — VS API also
      defines `Func`), apply it to the resolved `dropdownStyle` after the font tweak, and pass
      `DecoratePolicyDropdownStyle` from `ScribeDialogBase.PinTab.cs`.
- [x] 4.3 Override it on `GuiDialogScribeChalkboard` (gated on `PixelArtDisplay`): set
      `SelectionColor` to a FULLY-OPAQUE `Primary` (alpha 1.0) and `SelectionAccentColor` to
      `OnPrimary`, so the selected row is a solid green with chalk-white text instead of
      green-on-translucent-green. (Confirmed via decompiled `Gui.dll` that `DropdownItemTile`
      reads `SelectionColor` for the selected fill and `SelectionAccentColor` for its label.)

## 5. Restore the chalkboard side-column width

- [x] 5.1 In `src/Mod/BlockEntityScribeChalkboard.cs`, revert `SideColFrac` `0.073f` → `0.078f`.

## 6. Verify

- [x] 6.1 Build (0 warnings / 0 errors).
- [x] 6.2 Run `build/restage.sh Debug` (only while the client is NOT running).
- [x] 6.3 In-game gate (SUPERSEDED by §12 — the cap now counts ALL blocks, not just tasks): add
      10 blocks of ANY MIX (tasks, notes, trackers, links, craft) to a chalkboard → the 11th add of
      any kind is refused and surfaces the chalkboard cap notice (not "tablet"). Confirm pinning is
      NOT capped. See §12.4 for the authoritative gate.
  - Superseded 2026-08-21: the authoritative cap gate is now §12.9 (counts ALL blocks, not just tasks). Cap enforcement itself is Confirmed (TESTING.md `2a03c5d1`); the mixed-block gate is tracked at 12.9.
- [x] 6.4 In-game gate: on a chalkboard (Pixel-Art Display ON), confirm the task-row checkbox
      tick reads clearly as brighter green against the slate. Confirm the tablet/lectern accents
      are unchanged (they use their own themes).
  - Obsolete 2026-08-19: TESTING.md `dff63237` — the brighter-green tick goal was superseded; §11 changes the chalkboard tick to chalk-white instead. Retest lives at 11.4.
- [x] 6.5 In-game gate: open the Pin tab's Completion Behavior dropdown on a chalkboard — the
      SELECTED row is a solid green with legible chalk-white text (not green-on-green). Confirm
      the same dropdown on the lectern (global theme) is visually unchanged.
  - Confirmed 2026-08-19: TESTING.md `b57b3d27` "Works." — selected dropdown row is solid green with legible chalk-white text; lectern unchanged.
- [x] 6.6 In-game gate: confirm the wider `SideColFrac = 0.078` nav column still centers/holds the
      nav buttons within the slate frame without clipping.
  - Confirmed 2026-08-19: TESTING.md `496bce24` "Works." — SideColFrac 0.078 holds the nav buttons within the slate frame without clipping.

## 7. Recolor the chalkboard `Secondary` (fix the amber pinned-row wash)

- [x] 7.1 In `src/Mod/ScribeTheme.cs`, replace the `StainedWood` constant `(0.42, 0.31, 0.22)` (an
      unrelated brown) with `ChalkSecondary = (0.36, 0.52, 0.40)` — a muted SAGE GREEN, a desaturated
      lighter sibling of the green `ChalkAccent` `Primary`. This follows the Notebook/Lectern (Light theme)
      pattern where `Secondary` (`#A07F4D`) is a lighter/desaturated sibling of its gold `Primary`
      (`#955F21`). `PinnedTint` derives the pinned-row wash from `Secondary` (×1.35 saturation @ 0.55 alpha),
      so the brown was reading as a discordant muddy amber over the dark slate. Update the constant's
      doc-comment and the Chalkboard theme's Secondary comment.

## 8. Unify the two input fields' caret + focus-border colors

Surfaced on the chalkboard but the fix is GLOBAL: `ScribeMultilineField` (a hand-drawn render object) and
`ScribeNumericField` (a wrapper over LibGUI's stock `TextField`) had already been reconciled on background /
text / resting-border, but diverged on the caret and the focused border because those are baked into the
stock widget.

- [x] 8.1 Caret: in `src/Mod/ScribeMultilineField.cs`, change `caretColor: colors.Primary` → `colors.OnSurface`
      on BOTH render paths (cuneiform + normal). The stock numeric field hardwires its caret to the light
      content tone and can't be themed without forking `gui`; using the text color (`OnSurface`) is the
      conventional "caret is ink" behavior and matches the numeric field on the dark chalkboard (where the
      old accent-green caret looked mismatched). Selection stays the `Primary` wash.
- [x] 8.2 Focus border: in `src/Mod/ScribeTheme.cs`, set `FocusOutlineColor` per theme (it was unset →
      LibGUI's stock gold): Light → `Accent` (Primary), Chalkboard → `ChalkboardInputFocusBorder`
      (chalk-white), and the `ClayPalette` factory → `accent`. This makes the stock numeric field's focused
      border match the multiline field's on every theme (minor residual: stock mouse-focus lerps 35% toward
      the target vs the multiline's full jump — same hue, slightly softer) and also drops the gold focus ring
      from stock checkboxes/dropdowns/radios on the dark and clay themes.

## 9. Verify (§7–§8)

- [x] 9.1 Build (0 warnings / 0 errors).
- [x] 9.2 Run `build/restage.sh Debug` (only while the client is NOT running).
- [x] 9.3 In-game gate: on a chalkboard, pin a task and confirm the pinned-row wash now reads as a soft
      SAGE GREEN over the slate (harmonious with the accent), NOT the old muddy amber, and the pinned task's
      text is still legible over it.
  - Confirmed 2026-08-19: TESTING.md `871bde9e` "Works." — pinned-row wash reads as a soft sage green.
- [x] 9.4 In-game gate: on a chalkboard, focus a multiline note field and the Tracker/Craft target-quantity
      numeric field — confirm BOTH light their focused border in the SAME chalk-white (no green on one, gold
      on the other), and both carets read as chalk-white ink (not accent-green). Confirm the +/- stepper and
      other stock controls no longer show a gold focus ring.
  - Confirmed 2026-08-19: TESTING.md `0e86ef32` "Works." — chalkboard note + target-quantity field parity.
- [x] 9.5 In-game gate: on the Lectern/Notebook (Light theme), confirm the focused numeric field's border now
      matches the multiline field's (both the parchment accent) rather than the old stock gold, and nothing
      else regressed.
  - Confirmed 2026-08-19: TESTING.md `3e0b8328` "Works." — Lectern/Notebook focused numeric border matches the multiline field (parchment accent).

## 10. Unify the Pinned-tab focus border with the Edit view (adopt the Edit pattern globally)

The Edit view and guestbook feed `focusBorderColor: style.InputFocusBorderColor` into `ScribeMultilineField`,
so their focused border reads the seeded `InputFocusBorderColor` seam (chalk-white on the chalkboard, `Primary`
elsewhere). The Pinned tab builds the SAME `ScribeMultilineField` but OMITS `focusBorderColor:`, so it falls
back to `?? colors.Primary` — which only diverges visibly where the seam ≠ `Primary` (the chalkboard: green
instead of chalk-white). §8 reconciled numeric-vs-multiline, but NOT this Pinned-path omission. (Surfaced on the
chalkboard; the fix is a one-line adoption of the pattern the Edit view already uses.)

- [x] 10.1 In `src/Mod/ScribePinnedContent.cs` (the `ScribeMultilineField` construction ~line 569-582), add
      `focusBorderColor: style.InputFocusBorderColor,`. `ScribePinnedContent` already receives the seeded
      `RowStyle` (it uses `style.FieldPadX`, `style.FontSize`, etc.), so this resolves to chalk-white on the
      chalkboard and `Primary` on every other surface by construction — matching the Edit view everywhere.
- [x] 10.2 (Optional hardening — do only if cheap and non-disruptive.) SKIPPED per its own guidance: making
      `focusBorderColor` non-optional/seam-defaulted would touch many existing call sites for a defensive-only
      gain, so 10.1 stands alone.
- [x] 10.3 Build (0 warnings / 0 errors) + `build/restage.sh Debug` (client NOT running).
- [x] 10.4 In-game gate: on the chalkboard, focus a PINNED-tab row's input and confirm its border lights in the
      SAME chalk-white as an Edit-view row (not green). Confirm the Lectern/Notebook/Tablet Pinned tab is
      visually unchanged (seam already equals `Primary` there).

## 11. Chalkboard task-row checkbox tick = chalk-white (supersedes the brighter-green tick)

Playtest verdict on §3/§6.4: the brighter-GREEN tick goal is superseded — the tick should be the SAME chalk-white
as the row text, on the CHALKBOARD ONLY (other surfaces unchanged). §3's Primary brighten stays for the caret /
button fill / selection tint; only the checkbox TICK color changes, and only on the chalkboard.

- [x] 11.1 Located: the task-row checkbox is the stock LibGUI `Checkbox` (`Gui.Widgets.Input.Checkbox`) built
      in four places (`ScribeReadContent.cs:430`, `ScribeEditorContent.cs:103` frozen + `934` live,
      `ScribePinnedContent.cs:548`), none passing a `style:`. Decompiling `Gui.dll` confirmed the tick color is
      `CheckboxStyle.CheckColor`, which defaults from `Theme.Of(context).CheckboxStyle` → the theme `Primary`
      accent (green on the chalkboard). Not hand-drawn.
- [x] 11.2 Added the chalkboard-scoped seam `CheckTickColor(ColorScheme)` on `ScribeDialogBase.Layout.cs`
      (default `null` = unchanged), seeded onto new `ScribeRowStyle.CheckTickColor`; `GuiDialogScribeChalkboard`
      overrides it (gated on `PixelArtDisplay`) to `ScribeTheme.ChalkboardInputFocusBorder` (chalk-white). A
      shared `ScribeRowControlNudge.BuildTaskCheckbox` helper applies it by copying the ambient
      `Theme.Of(context).CheckboxStyle with { CheckColor = tick }` when set (only the tick changes; box/border
      stay themed), routed through all four row sites. No white hardcoded in the shared widget.
- [x] 11.3 Build (0 warnings / 0 errors) + `build/restage.sh Debug` (client NOT running).
- [x] 11.4 In-game gate: on the chalkboard (Pixel-Art Display ON), the completed task's checkbox tick reads as
      chalk-white matching the text. Confirm the Tablet/Lectern/Notebook ticks are unchanged.

## 12. Make the cap count ALL blocks — "10 of anything" (fixes the cap-not-enforced playtest)

Playtest verdict on §1–§2: the chalkboard cap did not fire — a player reached ~50 blocks. Root cause:
the cap code (`HostPolicy` override, explicit `IScribeDocumentHost.Policy` dispatch, `CanAdd`, and the
gated add paths) is all present, correct, and staged, BUT the count it measures — `ScribeDocument.TaskCount`
— counts only `IsTask` (plain Task-kind) blocks, and the add gate only refuses kinds flagged
`CountsAgainstTaskCap`. So notes/links were never counted (by design at the time), and Tracker/Craft were
declared to count yet don't advance `TaskCount` — the effective cap was "10 plain tasks + unlimited
everything else." **Decision (user, 2026-08-19): the cap is 10 blocks of ANY kind — tasks, notes, trackers,
links, craft all count equally. `IsTask`/`CountsAgainstTaskCap` stop mattering for the cap.** This is a
GLOBAL change: it applies to the Tablet too (both share `MaxBlocks = 10`), deliberately reversing the
Tablet's old "notes/links add beyond the cap" behavior (D4) — one simple rule everywhere.

- [x] 12.1 Core — cap measure = total block count. Added a named `ScribeDocument.BlockCount => _blocks.Count`
      (Core-pure, no API) with a doc-comment marking it the cap measure, and repointed
      `ScribeDialogBase.cs` `CanAddTaskUnderPolicy()` from `doc.TaskCount` → `doc.BlockCount`. Also renamed the
      `CanAdd`/`CanHold` params (`currentBlockCount`/`blockCount`) and updated their doc-comments to "any kind".
- [x] 12.2 Core — Transcribe capacity uses the same total-block measure. Repointed `GuiDialogScribeScriptorium.cs`
      `SourceTaskCount()`→`SourceBlockCount()` and `TargetTaskBlockCount()`→`TargetBlockCount()` (both now read
      `doc.BlockCount`; `TargetTaskCount()`/`CompletableCount` for the overwrite-confirm left unchanged) and the
      two call sites, and `ScribeModSystem.Network.cs` (both copy + import capacity mirrors) `TaskCount`→
      `BlockCount`. The "too big" disabled reason reads `MaxBlocks` and still reads correctly. `CanHold`'s `<=`
      unchanged.
- [x] 12.3 Gate EVERY add path. Dropped `kind.CountsAgainstTaskCap &&` from `ScribeDialogBase.Handbook.cs`
      (`ApplyHandbookAppend`) and `ScribeDialogBase.Editor.cs` (`OnClickAdd`). Also found + fixed a bypass NOT in
      the original scope: `ApplyGuideLinkAppend` (guide-page Link append) had NO cap gate at all — added
      `if (!CanAddTaskUnderPolicy()) { NotifyTabletFull(); return; }` there too. (Task/Craft paths already gated.)
- [x] 12.4 Picker UI. In `ScribeAddKindPicker.cs` both `dim` (~195) and `primaryDim` (~276) dropped the
      `kind.CountsAgainstTaskCap &&` prefix, so Add Note / Add Link dim/disable at the cap like every kind.
- [x] 12.5 Removed the dead `CountsAgainstTaskCap` seam: the record field, its `<param>` doc, all five
      construction args, and the class/summary doc references; rewrote the explanatory comments in
      Handbook.cs, Editor.cs, and each kind's doc-comment to the "N of anything" rule. Kept `ScribeDocument.TaskCount`
      (still useful as a diagnostic tally) but rewrote its doc-comment to state it is NOT the cap measure
      (BlockCount is). Verified `grep CountsAgainstTaskCap` / `.TaskCount` return nothing in `src/`.
- [x] 12.6 Reworded both notices in `lang/en.json` to "A chalkboard/tablet holds at most 10 entries." (user
      confirmed "entries").
- [x] 12.7 Core tests: added `BlockCount_CountsEveryKind`, `BlockCount_IsZero_ForEmptyDocument`, and
      `FiniteCap_CountsMixedKinds_RefusesEleventhOfAnyKind` to `ScribeDocumentTests`; rewrote the misleading
      `TaskCount`/`CanHold` comment headers in both test files to block-count semantics. No existing "notes add
      beyond cap" assertion existed to flip (that was mod-side gate flags, now removed). `ScribeDocumentTests`
      + `ScribeDocumentPolicyTests` = 90/90 green.
- [x] 12.8 Build (0 warnings / 0 errors) + `dotnet test tests/Core.Tests` (my document/policy classes 90/90
      green; 7 pre-existing `ScribeBrightnessCurveTests`/`ScribePlayerSettingsTests` failures are unrelated —
      confirmed they fail with my changes stashed) + `build/restage.sh Debug` (client NOT running).
- [x] 12.9 In-game gate (authoritative; supersedes §6.3): on a chalkboard, add 10 blocks of a MIXED set
      (some tasks, some notes, a tracker, a link, a craft) → the "Add" affordances (including Add Note / Add
      Link) all disable at 10 and an 11th add of any kind surfaces the chalkboard cap notice with the new
      wording (not "tablet"). Pinning is still uncapped. Then repeat on a TABLET and confirm it now caps the
      same way (10 of anything, its notice reworded) — the intended global behavior change.
