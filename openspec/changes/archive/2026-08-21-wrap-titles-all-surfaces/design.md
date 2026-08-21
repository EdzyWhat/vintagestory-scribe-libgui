## Context

The title bar for every Scribe dialog is built by the shared `ScribeDialogBase.BuildTitleBar`
(`src/Mod/ScribeDialogBase.Layout.cs`). The wrapping seam already exists and was proven in-game on
the wet cuneiform Tablet:

- `TitleMaxLines` is a `private protected virtual int` returning `1` by default
  (`ScribeDialogBase.Layout.cs` ~line 349). `GuiDialogScribeTablet` overrides it to
  `ActiveCuneiformBundle is not null ? 2 : 1` (`GuiDialogScribeTablet.cs` ~line 107).
- `BuildTitleBar` (~lines 225-238) reads `TitleMaxLines` to grow the title content box:
  `contentBoxH = titleMaxLines <= 1 ? TitleBtnsH : min(TitleBarH, TitleBtnsH + (titleMaxLines-1)*titleLineH)`,
  where `titleLineH = titleFont * CuneiformMetrics.LineHeightRatio`. It also sets the title row's
  cross-axis alignment to `End` (bottom-anchored) for two lines. When `TitleMaxLines == 1` every
  value is unchanged, so the single-line surfaces are byte-identical.
- The resting title is `BuildTitleDisplay` (base, ~line 341):
  `new RichText(new TextSpan(displayTitle), titleStyle, maxLines: 1, overflow: TextOverflow.Ellipsis)`.
  The Tablet overrides it (`GuiDialogScribeTablet.cs` ~line 232) to a `Clip`-wrapped cuneiform
  renderer with `singleLine: false`.
- The editing title is `BuildTitleField` (base, ~line 358): the stock LibGUI `TextField`. The Tablet
  overrides it to `ScribeCuneiformTitleField(..., singleLine: false)`.

Only the Tablet with a cuneiform bundle active reaches the wrapping leaves; the other five surfaces
(Lectern, Notebook, Clockmaker's Notebook, Scriptorium, Chalkboard) and the cuneiform-OFF Tablet all
take the single-line ellipsized base path. None of them override `TitleMaxLines`, `BuildTitleDisplay`,
or `BuildTitleField` (the Chalkboard overrides only `TitleChromeGlyphColor`).

**LibGUI ground truth (decompiled `Gui.dll`, `Gui.Core.Basic.RenderRichText.PerformLayout`):** when
the constraint `MaxWidth` is finite, `RichText` calls `WrapRuns(maxWidth)` and lays the text out
across as many visual lines as it needs (word-break via `SKFont.BreakText`), THEN, if
`MaxLines > 0 && lines > MaxLines`, removes the surplus lines and (only for `Overflow.Ellipsis`)
appends "..." to the last kept line. Its final `Size` is `Constrain(Vector2(maxLineWidth, sumOfLineHeights))`
— i.e. **`RichText` self-measures its own height to the actual number of lines.** `TextStyle.SoftWrap`
defaults `true`. So the current single-line-ness is caused purely by `maxLines: 1`, not by any
wrap-suppression; raising it to `2` yields two-line wrapping for free, and the widget grows its own
height to two lines with no fixed-height wrapper needed.

## Goals / Non-Goals

**Goals:**
- The resting/display title on Lectern, Notebook, Clockmaker's Notebook, Scriptorium, Chalkboard, and
  the Tablet (cuneiform ON and OFF) wraps a too-long title to at most two lines, using standard fonts
  and the existing band-growth layout.
- A one-line title renders exactly as today on every surface (no layout drift).
- Generalize FROM the proven tablet-cuneiform path rather than inventing a new mechanism.
- Surface the concrete Tablet title-band width knob (name + file + current value) so the user can tune
  where the tablet title wraps.

**Non-Goals:**
- **Editing-title wrap on the readable (non-cuneiform) path.** The stock LibGUI `TextField` is
  single-line by construction (VSAPI-NOTES §LibGUI: `RenderTextField` measures one line, no
  `maxLines`/multiline flag). Wrapping the readable editing field would require a custom multi-line
  title input; out of scope. The readable editing title stays single-line (horizontal scroll within
  the field as today).
- Changing the HUD pinned-task chrome (not a `ScribeDialogBase` title bar).
- Actually changing the tablet width value — this change only surfaces/documents the knob; the user
  sets the value as a follow-up.
- Three-or-more line titles (the cap stays two).

## Decisions

### D1. Flip the base `TitleMaxLines` default to 2 (not per-surface overrides)

**Choice:** Change `ScribeDialogBase.TitleMaxLines` from `=> 1` to `=> 2`. Then every surface that
takes the base path (all five block/item dialogs and the cuneiform-OFF Tablet) wraps at once. Simplify
the Tablet override to unconditional `2` — or delete it entirely, since the base default now matches
(the Tablet no longer needs a special case).

**Why over per-surface overrides:** The user's intent is "two-line wrapping everywhere with standard
layout." Flipping the shared default expresses that in one line and keeps the surfaces from drifting;
adding a `TitleMaxLines => 2` override to each of five dialogs would be repetitive and easy to miss on
a future sixth surface. The band-growth math in `BuildTitleBar` is already surface-agnostic and
capped at the band height (`min(TitleBarH, …)`), so it is safe to enable globally. Any future surface
that wants single-line can still override back to `1`.

**Alternative considered:** keep base `1` and add per-surface `=> 2` overrides. Rejected as
higher-maintenance and against the "generalize" goal.

### D2. Base `BuildTitleDisplay` wraps `RichText` to `TitleMaxLines`; no fixed height needed

**Choice:** Change the base leaf to
`new RichText(new TextSpan(displayTitle), titleStyle, maxLines: TitleMaxLines, overflow: TextOverflow.Ellipsis)`.
Keep `Overflow.Ellipsis` so a title longer than two lines shows "..." on the second line (readable
fonts have a "…"/"..." glyph, unlike cuneiform).

**Why no fixed two-line height (unlike the tablet):** `RenderRichText` self-measures to the actual
line count (D-context ground truth), so it naturally occupies one or two lines and the enclosing
`Expanded`/`Row` places it. The Tablet's cuneiform path needs an explicit `Clip` sized by the slot
only because `ScribeCuneiformFieldRenderWidget` is a custom renderer that fills its parent rather than
self-measuring; the stock `RichText` does not have that problem. So the two rendering leaves stay
different by necessity — the base leaf is a one-argument change, the cuneiform leaf keeps its Clip.

**Cross-axis fit:** the title lives in `Expanded(titleSlot)` inside a `Row` whose cross-axis (vertical)
constraint is loose, so `RichText` may size up to two line-heights and is bottom-anchored via the
`CrossAxisAlignment.End` that `BuildTitleBar` already sets for `TitleMaxLines > 1`. `contentBoxH`
reserves `TitleBtnsH + titleLineH` (capped at `TitleBarH`) for the second line.

### D3. The Tablet cuneiform-OFF path inherits the base two-line RichText automatically

With D1 (Tablet `TitleMaxLines` now `2` unconditionally) and D2 (base RichText wraps), a Tablet with
cuneiform OFF falls through `BuildTitleDisplay`/`BuildTitleField` to the base (its cuneiform overrides
already return `base.*` when `ActiveCuneiformBundle is null`). So the readable Tablet wraps its resting
title exactly like the Lectern/Notebook — no Tablet-specific work beyond simplifying the `TitleMaxLines`
override. The cuneiform-ON path is untouched (its dedicated wrapping leaves already ship).

### D4. Editing-title wrap scope (readable path stays single-line)

The readable editing title keeps the stock single-line `TextField` (Non-Goal above). Only the resting
title wraps on the readable path. This is an intentional asymmetry: on the readable surfaces the title
is edited rarely and briefly, and the single-line field with horizontal scroll is the standard LibGUI
affordance. The cuneiform Tablet already wraps its editing field (`ScribeCuneiformTitleField
singleLine:false`); the user noted that path is "a bit janky when super long" but acceptable, so no
change there. If readable editing-wrap is wanted later, it is a separate change (custom multi-line
title input).

### D5. Tablet title-band width knob (the value the user will tune)

> **DECIDED 2026-08-20 — the user chose `TitleBtnsWFrac = 0.86f` for the tablet** (wider than the `0.80f`
> default, so the title wraps later). Apply it to BOTH the clay and wax `with` blocks in
> `TabletHost.GetLayout` (task 3.3). The rest of this section is the analysis behind the knob.

**The knob:** `ScribeLayoutProportions.TitleBtnsWFrac` — the fraction of the window width `W` used by
the bottom-anchored title+buttons row (`ScribeLayout.TitleBtnsW => TitleBtnsWFrac * W`, which sizes the
`Row` that contains the `Expanded` title slot; the title's effective wrap width is that row width minus
the `10 + 0.04·W` left / `0.04·W` right padding and the trailing pencil·grip·close button group).

- **Definition:** `src/Mod/IScribeDocumentHost.cs` — field at line ~16, default assigned at line ~24:
  `TitleBtnsWFrac = 0.80f;`
- **Current effective value for the Tablet:** **`0.80f`** — the Tablet does NOT override it.
  `TabletHost.GetLayout` (`src/Mod/TabletHost.cs` ~lines 80-96) only overrides `TitleBarFrac`,
  `InnerHFrac`, and `SideColFrac` in its clay and wax `with` blocks; `TitleBtnsWFrac` falls through to
  the shared `ScribeLayoutProportions.Default` of `0.80f`.
- **How to tune ONLY the tablet's wrap point:** add `TitleBtnsWFrac = <value>` to the clay and/or wax
  `with` blocks in `TabletHost.GetLayout`. Raising it (e.g. `0.86f`) widens the title row so more text
  fits per line and the title wraps later; lowering it wraps sooner. (For reference, the Chalkboard
  sets `TitleBtnsWFrac = 0.82f` in `BlockEntityScribeChalkboard.cs` ~line 80; the shared default that
  every other surface — including the tablet — uses is `0.80f`.)

**Prompt-ready statement for the user:** "The tablet title wraps at the width of the title+buttons
row, governed by `ScribeLayoutProportions.TitleBtnsWFrac`, which the tablet currently inherits at its
default `0.80f` (defined in `src/Mod/IScribeDocumentHost.cs`). To move where the tablet title wraps,
set `TitleBtnsWFrac = <new value>` in the clay/wax `with` blocks of `TabletHost.GetLayout`
(`src/Mod/TabletHost.cs`). What value do you want?"

## Risks / Trade-offs

- **[Line-height mismatch: `BuildTitleBar` reserves the second line using
  `titleLineH = titleFont * CuneiformMetrics.LineHeightRatio`, but the readable `RichText` measures
  its own line height via `TextLayoutHelper`/`GetLineHeight`.]** If the RichText line height exceeds
  the reserved slack, the two lines can exceed `contentBoxH` and clip the top of line 1 (the Expanded
  cross-axis is loose, so RichText can overflow the reserved box). → Mitigation: verify in-game on
  each surface; if it clips, derive `titleLineH` for the non-cuneiform path from the actual font line
  height (or add a small headroom factor) rather than the cuneiform ratio. Measure, don't assume the
  ratio matches.
- **[Short title bands clip a two-line title.]** `contentBoxH` is capped at `TitleBarH`. The clay
  Tablet has the shortest band (`TitleBarFrac = 0.11`) yet is the PROVEN two-line surface, so the
  taller bands (Lectern `0.13`, Chalkboard/wax `0.15`) have strictly more slack — low risk, but
  confirm each surface visually (this is the single biggest thing to watch).
- **[Growth direction wording.]** The base code comment says the wrapped line grows "UPWARD into the
  band's slack" while the Tablet override comment says "DOWN into the band's slack." They describe the
  same proven mechanism (content bottom-anchored via `Align.BottomCenter` + `CrossAxisAlignment.End`;
  the extra line occupies the slack above the buttons). No behavior change — just reconcile the comment
  wording when editing so it is not confusing.
- **[Editing/display asymmetry on the readable path.]** The resting title wraps but the editing field
  does not, so a long title "unwraps" to single-line-with-scroll while being edited, then re-wraps on
  commit. Accepted (D4); the two-line resting view is the common case.

## Migration Plan

Client-side render/layout only — no data, network, or persistence migration. Rollout is a mod rebuild
+ restage; rollback is reverting the `TitleMaxLines` default and the `BuildTitleDisplay` line.

## Open Questions

- **Reconciliation with `wrap-tablet-title-band` (unarchived).** That change ADDs a `tablet-dialog`
  requirement stating wrapping is "scoped to the Tablet" and other surfaces "SHALL remain single-line
  as today" — which this change directly supersedes. Because that requirement is not yet in the base
  spec, this change introduces the generalized behavior as a NEW capability (`dialog-title-wrapping`)
  rather than MODIFYING a requirement that does not yet exist (avoiding the known archive-order
  header-drift trap — see project memory). Recommended archive order: archive `wrap-tablet-title-band`
  first (already working in-game), then this change; when this change archives, narrow the
  `tablet-dialog` requirement's "scoped to the Tablet / others single-line" clause so the two specs do
  not contradict. Confirm this ordering before implementing.
- **The tablet width value.** Deferred to the user (D5) — the change ships the generalization; the
  `TitleBtnsWFrac` value is set once the user picks it.
