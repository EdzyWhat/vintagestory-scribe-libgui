## 1. Foundation: shared resolver + ancestor helper

- [ ] 1.1 Add a helper that resolves the player's Task Text Font family and window-scaled base size
  into a single `TextStyle` carrying ONLY `FontFamily` (+ base `FontSize` if scaling there), leaving
  every other field at default (per the Merge-semantics rule in design.md). Reuse the existing
  `ScribeTaskFont.Resolve` / `ScribeRowConstants` scale logic; do not duplicate it.
- [ ] 1.2 Add a small `DefaultTextStyle` wrapper the tabs call to root their subtree with that style.

## 2. Convert tabs one at a time (each individually verified before moving on)

- [ ] 2.1 Read tab (`ScribeReadContent.cs`): wrap subtree in `DefaultTextStyle`; remove redundant
  `FontFamily = taskFont` and redundant base `FontSize`; keep color/weight/align/`SoftWrap` overrides.
  Verify in-game across the font/size matrix.
- [ ] 2.2 Edit tab (`ScribeEditorContent.cs`): same treatment; pay attention to the empty-hint
  centered placeholder (it sets `Align`/`SoftWrap` — keep those explicit).
- [ ] 2.3 Pinned tab (`ScribePinnedContent.cs`): same; confirm the Completion-policy picker label +
  dropdown text still follow the Task Text Font (this session's fix must survive the sweep).
- [ ] 2.4 Timer tab (`GuiDialogClockmakerNotebook.cs`): same; confirm the mode radios' labels still
  use the Task/Button font and the countdown text is unaffected.
- [ ] 2.5 Settings tab (`ScribeSettingsContent.cs`): same treatment.
- [ ] 2.6 Notebook/shell shared text (`GuiDialogScribeNotebook.cs`, `ScribeDialogBase.cs`,
  `ScribeNumericField.cs`): convert remaining tab-scoped `TextStyle` sites.

## 3. Handle the non-tab and overlay cases explicitly

- [ ] 3.1 Audit `useGlobalOverlay: true` tooltips/overlays (e.g. the Completion-policy tooltip): they
  render outside the tab subtree, so either give them their own `DefaultTextStyle` or keep an
  explicit font. Verify none silently fall back to the default font.
- [ ] 3.2 Decide the HUD (`HudScribePins.cs`): give it its own `DefaultTextStyle` root or leave
  explicit styles. Whichever — verify the pin/timer text still uses the chosen font/size.

## 4. Verify behavior preservation

- [ ] 4.1 Build clean; run `dotnet test tests/Core.Tests` (should be unaffected — no Core change).
- [ ] 4.2 In-game: for each tab, walk the full Task Text Font × window-text-size matrix and confirm
  no font/size regression vs. the pre-change build.
- [ ] 4.3 In-game: change the Task Text Font and window text size with a dialog open; confirm every
  label live-updates (no label stuck on the old font because it bypassed inheritance).

## 5. Close out

- [ ] 5.1 Grep for any remaining `FontFamily = taskFont` / `FontFamily = ScribeTaskFont.Resolve`
  threadings; confirm each survivor is deliberate (overlay/HUD) and noted.
- [ ] 5.2 Append a short note to `VSAPI-NOTES.md` (§ LibGUI) recording the `DefaultTextStyle` +
  `TextStyle.Merge` default-inherit semantics, so it isn't re-derived.
