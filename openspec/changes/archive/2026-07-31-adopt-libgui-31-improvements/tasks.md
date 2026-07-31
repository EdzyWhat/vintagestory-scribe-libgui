## 1. Foundation: shared resolver + ancestor helper

- [x] 1.1 Add a helper that resolves the player's Task Text Font family and window-scaled base size
  into a single `TextStyle` carrying ONLY `FontFamily` (+ base `FontSize` if scaling there), leaving
  every other field at default (per the Merge-semantics rule in design.md). Reuse the existing
  `ScribeTaskFont.Resolve` / `ScribeRowConstants` scale logic; do not duplicate it.
- [x] 1.2 Add a small `DefaultTextStyle` wrapper the tabs call to root their subtree with that style.

## 2. Convert tabs one at a time (each individually verified before moving on)

- [x] 2.1 Read tab (`ScribeReadContent.cs`): wrap subtree in `DefaultTextStyle`; remove redundant
  `FontFamily = taskFont` and redundant base `FontSize`; keep color/weight/align/`SoftWrap` overrides.
  Verify in-game across the font/size matrix. *(code done; in-game verify batched into 4.2/4.3)*
- [x] 2.2 Edit tab (`ScribeEditorContent.cs`): same treatment; pay attention to the empty-hint
  centered placeholder (it sets `Align`/`SoftWrap` — keep those explicit). *(code done; ghost row now
  inherits task font too; ScribeMultilineField keeps explicit font — custom RenderBox, doesn't inherit)*
- [x] 2.3 Pinned tab (`ScribePinnedContent.cs`): same; confirm the Completion-policy picker label +
  dropdown text still follow the Task Text Font (this session's fix must survive the sweep). *(code
  done: caption inherits; tooltip + dropdown keep EXPLICIT taskFont — they render in a global overlay
  outside the subtree, task 3.1; pin-row field keeps explicit font — custom RenderBox)*
- [x] 2.4 Timer tab (`GuiDialogClockmakerNotebook.cs`): same; confirm the mode radios' labels still
  use the Task/Button font and the countdown text is unaffected. *(code done: wrapped per user choice;
  bodyStyle KEEPS explicit family — threaded into non-inheriting TextField/numeric steppers; big/label
  inherit; small chrome labels now follow task font (approved change); radios/buttons keep Caudex;
  countdown/blink inherit the same task font they had. NOTE: Merge compares vs `new TextStyle()`
  property-initialized defaults, NOT `default(TextStyle)` — design doc's landmine list is inverted;
  corrected in 5.2.)*
- [x] 2.5 Settings tab (`ScribeSettingsContent.cs`): same treatment. *(NO-OP by design: this tab has
  ZERO `FontFamily = taskFont` threadings — settings chrome is deliberately the neutral default
  sans-serif, sized off `BaseSettingsFontSize`, NOT the task font. Nothing to remove, and it is
  deliberately NOT wrapped: a task-font ancestor would regress every label to the player's task font.
  Left exactly as-is; noted as a deliberate survivor in 5.1.)*
- [x] 2.6 Notebook/shell shared text (`GuiDialogScribeNotebook.cs`, `ScribeDialogBase.cs`,
  `ScribeNumericField.cs`): convert remaining tab-scoped `TextStyle` sites. *(code done: History tab +
  Guestbook tab + both base empty-placeholders wrapped (user approved History/Guestbook metadata
  following the task font). Guestbook headers keep Caudex — non-default family wins under Merge. Shell
  title (Caudex, outside tabs) + WithTooltip (global overlay) are deliberate survivors. ScribeNumericField
  left untouched — shared by the unwrapped Settings tab; its +/- label Text inherits harmlessly under the
  Timer wrap, its numeric TextField is non-inheriting.)*

## 3. Handle the non-tab and overlay cases explicitly

- [x] 3.1 Audit `useGlobalOverlay: true` tooltips/overlays (e.g. the Completion-policy tooltip): they
  render outside the tab subtree, so either give them their own `DefaultTextStyle` or keep an
  explicit font. Verify none silently fall back to the default font. *(4 sites audited, all safe:
  (1) ScribePinnedContent:195 policy tooltip — kept EXPLICIT taskFont in 2.3 (the one real risk, a
  task-font element hoisted to overlay); (2)(3) ScribeSettingsContent:368/404 — neutral sans-serif,
  matches the unwrapped Settings tab, no fallback; (4) ScribeDialogBase:1837 WithTooltip — neutral,
  labels shell chrome that isn't task-font. Plus the Pinned dropdown menu (also global overlay) keeps
  explicit taskFont. No silent default-font fallback anywhere.)*
- [x] 3.2 Decide the HUD (`HudScribePins.cs`): give it its own `DefaultTextStyle` root or leave
  explicit styles. Whichever — verify the pin/timer text still uses the chosen font/size. *(DECISION:
  leave explicit styles, NO wrap. The HUD never uses the task font — it's deliberately neutral near-white
  sans-serif over the dark game world, with per-widget glow/color/size that are all genuine non-default
  overrides. Zero `FontFamily = taskFont` threading exists here, so the missing-thread bug class this
  change targets is absent; a task-font wrap would INTRODUCE an unwanted font change. Deliberate
  survivor, noted in 5.1.)*

## 4. Verify behavior preservation

- [x] 4.1 Build clean; run `dotnet test tests/Core.Tests` (should be unaffected — no Core change).
  *(full solution builds clean, 0 warnings; Core.Tests 179/179 pass.)*
- [x] 4.2 In-game: for each tab, walk the full Task Text Font × window-text-size matrix and confirm
  no font/size regression vs. the pre-change build. *(verified in-game — looks good.)*
- [x] 4.3 In-game: change the Task Text Font and window text size with a dialog open; confirm every
  label live-updates (no label stuck on the old font because it bypassed inheritance). *(verified
  in-game — every label live-updates.)*

## 5. Close out

- [x] 5.1 Grep for any remaining `FontFamily = taskFont` / `FontFamily = ScribeTaskFont.Resolve`
  threadings; confirm each survivor is deliberate (overlay/HUD) and noted. *(17 survivors, all
  deliberate: Caudex `ButtonFamily` on buttons/radios (ScribeRead:79, Editor:250, Clockmaker:151/230/447);
  custom-RenderBox `ScribeMultilineField` fields, non-inheriting (DialogBase:2045, Pinned:370, Editor:495);
  global-overlay (Pinned:194 tooltip, Pinned:202 dropdown); non-inheriting inputs (Clockmaker:105/112
  bodyStyle→TextField/steppers); and the resolver itself (ScribeTextDefaults). No redundant threading
  left on any plain Text inside a wrapped subtree. HUD + Settings have ZERO threadings by design.)*
- [x] 5.2 Append a short note to `VSAPI-NOTES.md` (§ LibGUI) recording the `DefaultTextStyle` +
  `TextStyle.Merge` default-inherit semantics, so it isn't re-derived. *(done — and CORRECTED the
  semantics: Merge's sentinel is `new TextStyle()` (property-initialized: sans-serif/14/white/SoftWrap=
  true/Left), NOT `default(TextStyle)`. Fixed the same inversion in this change's design.md.)*
