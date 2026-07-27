## 1. Bundle the font asset and clear its license

- [x] 1.1 Add the Caudex `.ttf` under the Scribe mod's assets. **Placed at `src/Mod/assets/scribe/textures/fonts/caudex-regular.ttf`** (lowercase — `LoadFont` lowercases the path). NOT a bare `fonts/` folder: `fonts` is not a scanned `AssetCategory` (only 16 exist; confirmed by decompiling `Vintagestory.API.Common.AssetCategory`) — LibGUI only loads its own `assets/gui/fonts/` via an extra `AddModOrigin("gui","fonts")` + `Assets.Reload` dance. Filing under the already-scanned `textures` category (same as the SVG icons) avoids that. Included automatically via the csproj `assets/**` glob.
- [x] 1.2 Ship Caudex's `OFL.txt` alongside the `.ttf` (at `.../textures/fonts/OFL.txt`; files unmodified).
- [x] 1.3 Create a `CREDITS` file at the repo root crediting Caudex and its SIL OFL 1.1 license.

## 2. Load and register the face at client init

- [x] 2.1 In `ScribeModSystem`, added `RegisterCustomFonts(api)` (called from `StartClientSide` right after `RegisterCustomIcons`): loads via `new SkiaAssetLoader(api).LoadFont("scribe", "textures/fonts/caudex-regular.ttf")` and registers with `FontRegistry.RegisterCustomFont("Caudex", FontWeight.Normal, typeface)`. Added `using Gui.Rendering;` + `using Gui.Rendering.Text;`.
- [x] 2.2 Null-case guarded: on `LoadFont` returning null, logs a warning and returns (text falls back to a system face via `TextLayoutHelper`); no crash. No dispose hook added — the shared LibGUI registry owns the typeface lifetime. No `loadAsset:true` re-fetch needed: `LoadFont` reads bytes into the `SKTypeface` at init, before `UnloadAssets`.
- [x] 2.3 Registration runs in `StartClientSide`, before any Scribe dialog opens, so first layout resolves the family.

## 3. Route the row text at the registered family

- [x] 3.1 Established a single source of truth: added `internal const string RowFontFamily = "Caudex"` to `ScribeRowControlNudge` in `GuiDialogScribeLecternLibGui.cs`, and repointed that class's own `FontFamily` measurement const at it. **Correction to design:** the read view does NOT use a `FontFamily` const — it relies on `TextStyle`'s *default* family (two sites, ~lines 1708 & 1801), so there are FOUR coupling points, not two. Using one shared const prevents the drift the design flagged.
- [x] 3.2 Repointed `ScribeMultilineField.cs`'s `FontFamily` const at `ScribeRowControlNudge.RowFontFamily` (was `"sans-serif"`), so editor draw/measure track the shared family.
- [x] 3.3 Set `FontFamily = ScribeRowControlNudge.RowFontFamily` on both read-view `TextStyle` initializers (`ScribeReadRowState.Build` and the collapsing-row `Build`) in `GuiDialogScribeLecternLibGui.cs`, applied after the background §8-10 agent released the file (verified my `RowFontFamily` const and the agent's `GripInsets` coexist in `ScribeRowControlNudge`). All four coupling points now resolve the one const. `TextStyle` size/color untouched.

## 4. Build and prove on the author's Apple Silicon Mac

- [x] 4.1 `dotnet build src/Mod/Mod.csproj --nologo` clean (0 warnings, 0 errors); Core suite green (128/128). No `src/Core/` change from the font work (the `MaxHudMaxRows` Core change in the tree is the separate refine-settings §10.3, not this spike).
- [ ] 4.2 Restage (`bash build/restage.sh Debug`) and fully relaunch the client on the Mac.
- [ ] 4.3 (arm64 render) Open the lectern and confirm the row text renders in Caudex with no crash, error, or garbled glyphs.
- [ ] 4.4 (mod-scoping) Confirm other in-game GUI text (menus, tooltips, other dialogs, the standalone settings window) is unchanged — only the lectern row text uses Caudex.
- [ ] 4.5 (measure/draw lockstep) Confirm read view and editor field render the row text at consistent size and line height (no clipping or baseline drift from a family/measure mismatch).

## 5. Record findings and correct the docs

- [ ] 5.1 Record the outcome (registered-face path works on arm64 macOS via LibGUI's Skia `FontRegistry`; scoping via `TextStyle.FontFamily`) in `VSAPI-NOTES.md` — replacing/annotating the old "Custom TTF fonts in the GUI" Cairo/FreeType note, which described the retired native path.
- [ ] 5.2 Correct `docs/specs/presentation-and-fonts.md`: the Cairo `FreeTypeFontFace` / `SetContextFontFace` mechanism no longer describes this repo; the LibGUI/Skia `FontRegistry.RegisterCustomFont` + `TextStyle.FontFamily` path is the current mechanism.
- [ ] 5.3 Remove or correct the stale `GuiStyle.StandardFontName` reference (it belongs to the native Cairo path and is irrelevant to LibGUI's text rendering).
- [ ] 5.4 Note in the findings whether the parent font work should keep bundling its own faces (proven here) or instead reference LibGUI's already-bundled serifs (Playfair Display / Cormorant Unicase) with zero new assets — the open question from design.md.
