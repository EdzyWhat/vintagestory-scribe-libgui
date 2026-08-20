## 1. Generalize the wrapping seam in the base

- [x] 1.1 In `src/Mod/ScribeDialogBase.Layout.cs`, flipped the `TitleMaxLines` default from `=> 1` to
  `=> 2` and updated its XML doc-comment to say two-line wrapping is now the shared default for every
  surface (no longer tablet-only).
- [x] 1.2 In `src/Mod/ScribeDialogBase.Layout.cs`, changed the base `BuildTitleDisplay` `RichText` from
  `maxLines: 1` to `maxLines: TitleMaxLines`, kept `overflow: TextOverflow.Ellipsis`; updated its
  doc-comment.
- [x] 1.3 Reconciled the growth-direction wording: the `BuildTitleBar` comment now describes the one
  UPWARD-growth mechanism for all surfaces (dropped the tablet-only framing); the tablet's "down" comment
  was removed with its override. No code behavior change.

## 2. Simplify the Tablet overrides

- [x] 2.1 In `src/Mod/GuiDialogScribeTablet.cs`, REMOVED the `TitleMaxLines` override entirely (base default
  is now `2`), leaving a comment. The cuneiform-ON wrapping leaves (`BuildTitleDisplay`/`BuildTitleField`
  Clip + `singleLine: false`) are untouched.
- [x] 2.2 The cuneiform-OFF Tablet now falls through to the base two-line `RichText` (its overrides already
  return `base.*` when `ActiveCuneiformBundle is null`) — no extra tablet code, and dropping the override's
  `? 2 : 1` gating is exactly what lets the cuneiform-off path wrap.

## 3. Surface the Tablet title-band width knob

- [x] 3.1 Confirmed the tablet title wrap width is `ScribeLayoutProportions.TitleBtnsWFrac * W` and that
  `TabletHost.GetLayout` (`src/Mod/TabletHost.cs`) previously inherited the shared default `0.80f`
  (did NOT override `TitleBtnsWFrac`).
- [x] 3.2 Documented the knob in-code (a doc-comment above the clay/wax `with` blocks in
  `TabletHost.GetLayout` explaining `TitleBtnsWFrac` is the tablet title wrap-width knob).
- [x] 3.3 Set the tablet `TitleBtnsWFrac` per-material (chosen by the user 2026-08-20, both wider than the
  `0.80f` default so the title wraps later): **clay = `0.86f`**, **wax = `0.82f`**, in the respective
  `with` blocks of `TabletHost.GetLayout`.

## 4. Line-height fit verification (design risk D2 / Risks)

- [ ] 4.1 Verify the readable `RichText` two-line height fits within `contentBoxH` (which reserves
  `titleLineH = titleFont * CuneiformMetrics.LineHeightRatio`); if the RichText font line height
  exceeds the reserved slack and clips line 1, derive the reserved line height from the actual font
  line height for the non-cuneiform path (or add a small headroom factor).

## 5. Build & static checks

- [x] 5.1 Built the mod (`dotnet build src/Mod/Mod.csproj -c Debug`) — 0 warnings / 0 errors.
- [x] 5.2 Confirmed `src/Core/` is untouched (`git status --porcelain src/Core` clean) and no new mod
  dependency was added — GUI-only change.
- [x] 5.3 Restaged Debug (`build/restage.sh Debug`, 138 files) with the client verified not running first.

## 6. In-game playtest verification

- [ ] 6.1 **Lectern** — set a very long title; verify the resting title wraps to two lines, chrome
  stays clear, and a title longer than two lines ends with an ellipsis on line 2.
- [ ] 6.2 **Notebook** (and Clockmaker's Notebook) — same long-title check; verify two-line wrap and
  ellipsis-on-line-2.
- [ ] 6.3 **Scriptorium** — same long-title check; verify two-line wrap and ellipsis-on-line-2.
- [ ] 6.4 **Chalkboard** — same long-title check; verify two-line wrap fits within its taller band and
  chrome stays clear.
- [ ] 6.5 **Tablet, cuneiform ON** — regression check that the proven cuneiform two-line wrap is
  unchanged (wraps, clips at line 2, no ellipsis glyph).
- [ ] 6.6 **Tablet, cuneiform OFF** — set a long title; verify the readable RichText now wraps to two
  lines (previously single-line ellipsized), on both clay and wax materials.
- [ ] 6.7 **Editing title (readable path)** — on Lectern/Notebook/Scriptorium, verify the editing field
  stays single-line while typing, Enter commits with no newline, and the resting title re-wraps to two
  lines on commit.
- [ ] 6.8 **Short-title regression** — on every surface, confirm a title that fits on one line renders
  identically to before (no band-height change, no layout shift).
- [ ] 6.9 **Width tuning (if a value was set in 3.3)** — verify the tablet title wraps at the expected
  point for the chosen `TitleBtnsWFrac`, with no effect on other surfaces.
