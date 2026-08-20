## Context

`ScribeDialogBase.BuildRightColNav` (in `ScribeDialogBase.Layout.cs`) builds the shared
right-hand nav column (Read, Edit, Pinned, [extras], Settings) inside a `SizedBox` of width
`layout.SideColW`. During `add-chalkboard-block`, an adaptive horizontal-placement rule was
added inline: it computes `sideColW = host.GetLayout(PixelArtSize).SideColW` and
`navBoxW = NavButtonSize − ScribeRowButton.BoxShrink`, then sets
`navAlign = navBoxW > sideColW ? End : Center` and passes it as the Column's
`crossAxisAlignment`. That works for the chalkboard but was applied to every dialog.

Five dialogs subclass `ScribeDialogBase`: `GuiDialogScribeLecternLibGui`,
`GuiDialogScribeNotebook` (base of `GuiDialogClockmakerNotebook`), `GuiDialogScribeScriptorium`,
`GuiDialogScribeTablet`, and `GuiDialogScribeChalkboard`. The Lectern, Notebook, Scriptorium,
and Clockmaker's Notebook are the paper/margin **Pages group** whose art was tuned for the
original left-aligned (`Start`) nav column — centering them is a visual regression. The
Chalkboard and Tablet are the framed **Hard Border group**; the Chalkboard wants the adaptive
rule, and the Tablet — although Hard Border by intent — overrides `BuildRightColNav` to return
an empty column (`new SizedBox()`), so it renders no nav buttons and the placement seam never
fires for it.

LibGUI `RenderFlex` cross-axis offset for a vertical `Column` is `num5 = Size.X − child.Size.X`;
`Start`/default → 0 (overflow spills right, off-window → clipped), `Center` → `num5/2`,
`End` → `num5` (child right edge pinned to column right; when `num5 < 0` the overflow spills
left, inward). This is why `End` is the correct choice for a narrow column and `Start` clips.

## Goals / Non-Goals

**Goals:**
- Restore the original `Start` (left-align) nav placement for the Pages group.
- Keep the adaptive center/end placement for the Hard Border group (Chalkboard).
- Make placement a per-dialog seam so future surfaces opt into a family, defaulting to Pages.

**Non-Goals:**
- No change to `SideColW`, the three-column skeleton, button size/count/order, shadows,
  tooltips, or active-state coloring.
- No Core changes, no new dependency, no asset/persistence change.
- No new formal "group" type/enum in code — the two groups are expressed purely by whether a
  dialog overrides the seam (default = Pages; override = its own rule).

## Decisions

**Decision: A `private protected virtual` placement seam on `ScribeDialogBase` that maps
`(sideColW, navBoxW)` → `CrossAxisAlignment`, defaulting to `Start`.**
Signature (final name to be confirmed in tasks): `private protected virtual CrossAxisAlignment
NavButtonAlignment(float sideColW, float navBoxW) => CrossAxisAlignment.Start;`. `BuildRightColNav`
still computes `sideColW`/`navBoxW` (the base owns the layout math) and calls the seam for the
mapping only. The chalkboard overrides it with the adaptive rule
`navBoxW > sideColW ? CrossAxisAlignment.End : CrossAxisAlignment.Center`.
- *Why over a bool/enum property (e.g. `bool CenterNavButtons`)*: the Hard Border rule is not a
  single alignment, it's a *function* of column vs. button width; a plain property can't express
  the center↔end switch. Passing the two measurements to the seam keeps the decision where the
  numbers already are and mirrors the mod's existing "dedicated override seam" pattern
  (`ResolveTheme`, `NavIconColor`, `TitleChromeGlyphColor`, `InputFocusBorderColor`,
  `DecorateRowStyle`).
- *Why `private protected`*: matches the visibility of the sibling nav seams (`NavIconColor`,
  `TitleChromeGlyphColor`, `InputFocusBorderColor`) — mod-internal, subclass-overridable.

**Decision: Default = Pages behavior; the chalkboard overrides; the Tablet is Hard Border by
classification but needs no override.**
The seam defaults to `Start` (Pages). The Chalkboard overrides it to the adaptive rule. The
Tablet is classified Hard Border per the author, but `GuiDialogScribeTablet.BuildRightColNav`
returns an empty `SizedBox()` — it renders no nav buttons — so the base's seam call site never
executes for it and no override is warranted (adding one would be dead code). Its group
membership is documented in the spec/comments as intent, in case a future refinement gives the
tablet a nav column. Any other future surface inherits the Pages layout for free.

**Decision: Move the adaptive logic verbatim into the chalkboard override; delete the inline
mod-wide computation from the base.**
The base's `navAlign` line and its explanatory comment move to `GuiDialogScribeChalkboard`; the
base call site becomes `crossAxisAlignment: NavButtonAlignment(sideColW, navBoxW)`. No math
changes — the chalkboard's rendered result is byte-identical to today; only the other four
dialogs revert to `Start`.

## Risks / Trade-offs

- [The comment currently attributes nav-centering to `add-chalkboard-block`] → Update the moved
  comment to reference this change (`refine-nav-button-placement`) so the history stays honest.
- [A future Hard Border surface would duplicate the chalkboard's override] → Acceptable now
  (one member, ~2 lines). If a second Hard Border surface appears, promote the adaptive rule to
  a shared protected helper the override can call — noted, not built.
- [Visual regression risk is asymmetric] → The Pages group is the one changing (back to
  `Start`); the chalkboard is unchanged. The playtest gate only needs to confirm the four Pages
  surfaces look right at large and small `PixelArtSize` and the chalkboard is unaffected.

## Open Questions

- Final seam name: `NavButtonAlignment` vs. `ResolveNavButtonAlignment` — cosmetic, decided in
  tasks to match the nearest sibling seam's naming.
