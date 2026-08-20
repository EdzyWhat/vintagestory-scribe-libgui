## 1. Add the placement seam to the base

- [x] 1.1 In `src/Mod/ScribeDialogBase.Layout.cs`, add a `private protected virtual
      CrossAxisAlignment NavButtonAlignment(float sideColW, float navBoxW) =>
      CrossAxisAlignment.Start;` near the other nav seams (`NavIconColor`,
      `TitleChromeGlyphColor`, `InputFocusBorderColor`), with a doc-comment stating the default
      is the Pages-group left-align and a Hard Border-group subclass overrides it.
- [x] 1.2 In `BuildRightColNav`, keep the `sideColW`/`navBoxW` computation but replace the inline
      `navAlign = navBoxW > sideColW ? End : Center` with `CrossAxisAlignment navAlign =
      NavButtonAlignment(sideColW, navBoxW);` and pass it to the Column's `crossAxisAlignment`
      (already wired). Update the comment there to reference this change instead of
      `add-chalkboard-block nav-centering` and to explain the default is now `Start`.

## 2. Restore the Hard Border rule on the chalkboard

- [x] 2.1 In `src/Mod/GuiDialogScribeChalkboard.cs`, override
      `private protected override CrossAxisAlignment NavButtonAlignment(float sideColW, float
      navBoxW) => navBoxW > sideColW ? CrossAxisAlignment.End : CrossAxisAlignment.Center;` with
      a doc-comment carrying over the RenderFlex spill-left rationale (End pins the right edge so
      overflow spills inward at small `PixelArtSize`; Center when the column has slack).
      (The Tablet is Hard Border by intent but renders no nav column, so it needs no override.)

## 3. Verify

- [x] 3.1 Build (0 warnings / 0 errors).
- [x] 3.2 Run `build/restage.sh Debug` (only while the client is NOT running).
- [x] 3.3 In-game gate: at large and small `PixelArtSize`, confirm the four Pages-group surfaces
      (Lectern, Notebook, Scriptorium, Clockmaker's Notebook) left-align their nav buttons as
      before, the Tablet is unchanged (it has no nav column — Hard Border by intent only), and
      the Chalkboard still centers when roomy / pins-right + spills-left when narrow with no
      clipping.
