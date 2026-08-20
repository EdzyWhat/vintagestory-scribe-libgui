## 0. Base placement seams (D6)

- [x] 0.1 In `BlockScribeWritingStation.cs`, add `protected virtual bool RequiresSolidGround
      => true;` and guard the below-floor check in `CanPlaceBlock` with it (skip the check when
      false). Keep the current behavior for the Lectern/Scriptorium (default true).
- [x] 0.2 In `BlockScribeWritingStation.cs`, add `protected virtual bool
      OrientTowardPlayerOnPlace => true;` and guard the `MeshAngleRad` orientation block in
      `TryPlaceBlock` with it (skip when false). Default true keeps Lectern/Scriptorium unchanged.

## 1. C# subclasses (mirror the Lectern trio)

- [x] 1.1 Add `src/Mod/BlockScribeChalkboard.cs` — `sealed class BlockScribeChalkboard :
      BlockScribeWritingStation`, overriding `InteractionsCacheKey`
      (`"scribeChalkboardBlockInteractions"`), `OpenHintLangCode`
      (`"scribe:blockhelp-scribechalkboard-open"`), `EditHintLangCode`
      (`"scribe:blockhelp-scribechalkboard-edit"`), plus `RequiresSolidGround => false` and
      `OrientTowardPlayerOnPlace => false` (D6 wall-mount opt-out). Mirror `BlockScribeLectern.cs`.
- [x] 1.2 Add `src/Mod/BlockEntityScribeChalkboard.cs` — `sealed class
      BlockEntityScribeChalkboard : BlockEntityScribeWritingStation`, overriding
      `PageBackdrop => ScribeBackdrops.ChalkboardPage`, `PageAspect => 145f / 128f`,
      `DefaultDocumentTitleKey => "scribe:doctitle-chalkboard"`, `MeshCacheKeyPrefix =>
      "scribechalkboardmesh"`, and `CreateDialog => new GuiDialogScribeChalkboard(...)`.
      Mirror `BlockEntityScribeLectern.cs`.
- [x] 1.3 Add `src/Mod/GuiDialogScribeChalkboard.cs` — `sealed class GuiDialogScribeChalkboard
      : ScribeDialogBase`, ctor mirroring the Lectern dialog, `EditorAccessIsAsync => true`,
      the guestbook nav button in `GetExtraNavButtons()` (per D3 open question 3, default
      include), and `protected override ThemeData ResolveTheme(bool pixelArt) =>
      ScribeTheme.Chalkboard` (added in task 2.1).

## 2. Theme + backdrop plumbing

- [x] 2.1 Add a `ScribeTheme.Chalkboard` `ThemeData` (dark-slate surface roles, chalk-light
      text/on-surface), authored the same mechanical way as `ScribeTheme.Light` (17 ColorScheme
      roles; keep the raised/recessed Surface/SurfaceHigh/SurfaceLow ordering correct).
- [x] 2.2 Add `ScribeBackdrops.ChalkboardPage` in `ScribeBackdrop.cs` pointing at
      `new AssetLocation("scribe", "textures/gui/scribe-chalkboard.png")` (no tint), with a
      doc comment noting the 128×145 source and aspect 145/128.

## 3. Register the classes

- [x] 3.1 In `ScribeModSystem.cs` (near lines 218–221), add
      `api.RegisterBlockClass("BlockChalkboard", typeof(BlockScribeChalkboard));` and
      `api.RegisterBlockEntityClass("Chalkboard", typeof(BlockEntityScribeChalkboard));`
      — the exact string names the committed `chalkboard.json` references.

## 4. Fix the malformed committed assets

- [x] 4.1 Rewrite `assets/scribe/blocktypes/chalkboard.json`: drop the Scriptorium comments;
      fix the texture-dict key typo `clate` → `slate`; keep `class`/`entityclass`/`shape` as-is
      (they now resolve). Add the painting wall-mount characteristics (D6): a `side` variant
      group (`loadFromProperties: "abstract/horizontalorientation"`), the `HorizontalAttachable`
      behavior, `rotateYByType` on `shapebytype` (north:180/east:90/south:0/west:270 or as the
      model requires), `replaceable`/`rainPermeable`/`materialDensity`, and painting-style
      `guiTransform`/`groundTransform`/`tpHandTransform`. Set `creativeinventory` to the
      `chalkboard-north` variant.
- [x] 4.2 Reconcile the `.bbmodel` texture keys with the blocktype `textures` dict: the model
      carries mixed keys (`chalk`, `slate`, `wood-h`, `wood-v` plus leftover Scriptorium
      `glass`/`material`/`lining`/`material-deco`/`material-grid`/`scribe-wax-32`). Ensure every
      key a face uses is declared in the blocktype `textures` dict (remap or remove unused
      Scriptorium keys) so no face renders untextured.
      **RESOLVED 2026-08-19:** the model was collapsed to 33 faces, all on the four authored keys
      (`wood-h`×21, `chalk`×6, `wood-v`×4, `slate`×2); no face references any clutter key. The
      five placeholder clutter mappings were removed from the blocktype `textures` dict so it now
      declares only the four authored keys. (Six unused texture *slots* still linger in the
      `.bbmodel` textures list — harmless, nothing renders them; a Blockbench-only tidy.) In-game
      renders fully textured (6.4 PASS).
- [x] 4.3 Set the collision/selection boxes to painting-style wall boxes: `collisionbox: null`
      (or a thin box), and a thin `selectionbox` against the wall with `rotateYByType` per side,
      right-sized to the chalkboard model's actual depth (the committed boxes are the
      free-standing Scriptorium's).
- [x] 4.4 Replace the borrowed Scriptorium `handbook.extraSections` with chalkboard-specific
      section title/text lang keys.

## 5. Lang + obtainability

- [x] 5.1 Add lang strings to `lang/en.json`: `blockhelp-scribechalkboard-open`,
      `blockhelp-scribechalkboard-edit`, `doctitle-chalkboard`, the block display name
      (`block-chalkboard`), and the handbook section keys referenced in 4.4.
- [x] 5.2 Add a crafting recipe JSON (and creative-inventory entry is already in the blocktype)
      so the chalkboard is obtainable in survival — mirror a simple Lectern-class recipe
      (D3 open question 4 default: planks + pigment/charcoal).

## 6. Build, restage, verify

- [x] 6.1 `dotnet build src/Mod/Mod.csproj` — 0 errors, 0 warnings.
- [x] 6.2 `dotnet test tests/Core.Tests` — no new failures (Core untouched; pre-existing
      brightness-curve failures unrelated).
- [x] 6.3 `bash build/restage.sh Debug` (only while the client is quit).
- [x] 6.4 In-game: the chalkboard is craftable/spawnable, mounts on a wall facing outward
      (all four sides), requires no floor, breaks when its wall is removed, and renders fully
      textured (no untextured faces) with its own model. Confirm the Lectern/Scriptorium still
      place floor-only facing the player (seams didn't regress them). **PASS 2026-08-19.**
- [x] 6.5 In-game: opening the chalkboard shows the Scribe document dialog with its own GUI
      background and theme; all task kinds, tabs, and the guestbook work exactly as the Lectern.
      **PASS 2026-08-19.**
- [x] 6.6 In-game: editing takes the server-lock round-trip, persists, and syncs across a
      re-open / second client — confirming the inherited writing-station path is intact.
      **PASS 2026-08-19** (full chalkboard contents persist in all scenarios).
- [x] 6.7 In-game: confirm the player's global Light/Default theme preference and the Lectern's
      appearance are unchanged (theme override is chalkboard-scoped). **PASS 2026-08-19.**
- [x] 6.8 In-game: handbook entry reads as chalkboard-specific (not Scriptorium); interaction
      hints and default document title are chalkboard copy. **PASS 2026-08-19.** (Follow-up: the
      chalkboard Handbook entry reads notably cleaner than the others — spun into a separate
      handbook-restructuring proposal, NOT a delayed task here.)
