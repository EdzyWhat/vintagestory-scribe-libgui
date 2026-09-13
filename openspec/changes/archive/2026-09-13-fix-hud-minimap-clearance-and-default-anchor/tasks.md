## 1. Default anchor change

- [x] 1.1 In `ScribePlayerSettings.cs`, change `HudAnchor`'s default from `ScribeHudAnchor.TopRight` to
  `ScribeHudAnchor.TopLeft`. Verify by reading the property initializer.
- [x] 1.2 In `ScribePlayerSettings.cs`'s `NormalizeAnchor`, change the fallback-for-unrecognized-value
  from `ScribeHudAnchor.TopRight` to `ScribeHudAnchor.TopLeft`. Verify by reading the method and by a
  quick `Core.Tests` case (if one already covers `NormalizeAnchor`) asserting an out-of-range value
  normalizes to `TopLeft`.

## 2. Minimap-clearance fix

- [x] 2.1 Add a `<Reference Include="VSEssentials">` entry to `src/Mod/Mod.csproj` (mirroring the
  existing `VSSurvivalMod` reference's `HintPath`/pattern) so `Vintagestory.GameContent.GuiDialogWorldMap`
  is resolvable. Verify by building: the new reference resolves against the local game install
  without a missing-assembly error.
- [x] 2.2 In `HudScribePins.ApplyAnchor()`, replace the hardcoded `DefaultTopRightMinimapClearanceX`
  usage with a read of the live minimap HUD dialog's actual rendered width (via
  `capi.Gui.OpenedGuis.OfType<GuiDialogWorldMap>()`, mirroring the existing
  `DialogHeldTrackerDocs()` lookup pattern), falling back to the current constant when that dialog
  isn't resolvable. Verify by reading the diff: the fallback path is unchanged behavior, the live path
  is new.
- [x] 2.3 Extend `AnchorInputs` (the `ApplyAnchor` cache key) to include whatever value now drives the
  clearance (e.g. the observed minimap width) so a change in it is still detected and triggers a
  recompute, consistent with `hud-anchor-optimization`'s existing per-frame gating contract. Verify by
  reading the updated record and its comparison.
- [x] 2.4 In-game smoke test at the default GUI Scale (10): confirm the top-right anchor still clears
  the minimap with the same visual gap as before this change (no regression at the value that already
  worked).
- [x] 2.5 In-game smoke test at GUI Scale 8 (the author's setting) and at least one other non-default
  value (e.g. 12): confirm the top-right anchor clears the minimap with a consistent gap, matching
  task 2.4's gap rather than sitting noticeably closer/farther. (Tested at 10, 8, and 11 — confirmed
  consistent. Along the way, found and fixed a pre-existing, previously-tracked bug — `AnchorInputs`
  omitted `WindowSize` from its cache key, so a shrink-wrap settle after the frame-1 size estimate
  never invalidated the cache; this also likely explains the older
  `hud-pin-width-worldload-race-investigation` ModDB report.)

## 3. Verification

- [x] 3.1 Run the Core test suite (`dotnet test tests/Core.Tests`) and confirm it passes — this change
  touches `ScribePlayerSettings.cs` (in `src/Core`), so existing `NormalizeAnchor`/default-value tests
  must still reflect the new default (update any test that asserted the old `TopRight` default).
- [x] 3.2 Restage a Debug build (`build/restage.sh Debug`) per [[restage-before-handoff-to-testing]]
  and confirm no build warnings from the new `VSEssentials` reference.
- [x] 3.3 In-game smoke test on a brand-new world/save (no existing Scribe client-config file): pin a
  task, confirm the HUD defaults to top-left with no minimap-clearance offset applied.
- [x] 3.4 In-game smoke test on an existing save with a previously-saved config: confirm the HUD stays
  at whatever anchor was already saved (including a saved `TopRight`), demonstrating the default
  change doesn't silently move existing players.
- [x] 3.5 Update `TESTING.md`/`CHANGELOG.md` noting both changes (default anchor flip, and the
  minimap-clearance fix now holding across GUI Scale settings). `CHANGELOG.md` updated (also notes
  the `AnchorInputs`/`WindowSize` cache fix found along the way). `TESTING.md` has no existing
  entries for this change to update — all manual verification happened directly in this session
  rather than via the `/what-to-test` checklist flow.
