## 1. Extract the shared writing-station base (behavior-preserving refactor)

- [x] 1.1 Create `abstract BlockEntityScribeWritingStation : BlockEntity, IRotatable, IScribeDocumentHost` and move the generic logic up from `BlockEntityScribeLectern`: document field, mesh-angle placement/`RotatedBox`, break/pick carry-over hooks, pin-store registration, editor-lock state machine, guestbook, server reply routing, `ToTreeAttributes`/`FromTreeAttributes`, tesselation. Leave abstract config members for the subclass: `PageBackdrop`, `PageAspect`, `DefaultDocumentTitleKey`, `MeshCacheKeyPrefix`, and `CreateDialog`. (Also renamed the dormant `LecternAccessMode` → `ScribeAccessMode` since it is now shared.)
- [x] 1.2 Generalize the cached-mesh key so it is keyed per block code (never shared between Lectern and Scriptorium) — `MeshCacheKeyPrefix + "-" + Block.Code + "-" + angle`.
- [x] 1.3 Create `abstract BlockScribeWritingStation : Block` and move the generic block logic up from `BlockScribeLectern`: floor-only `CanPlaceBlock`, face-player `TryPlaceBlock`, `GetCollisionBoxes`/`GetSelectionBoxes`, `GetDrops`/`OnPickBlock` document stamping, `GetPlacedBlockInfo`/`GetHeldItemInfo` title line, and `OnBlockInteractStart` (shift = quick-add). Leave abstract members for the interaction-hint lang keys + cache key.
- [x] 1.4 Refactor `BlockScribeLectern`/`BlockEntityScribeLectern` to derive from the bases and supply only Lectern config (`LecternPage`, aspect 1160/1024, `doctitle-lectern`, `blockhelp-scribelectern-*`, mesh-key prefix, dialog). Widened the 7 `ScribeModSystem.Network.cs` routing casts to `BlockEntityScribeWritingStation` so both blocks route.
- [x] 1.5 Run the Core unit suite (`dotnet test`) and confirm green — 358 passed.
- [x] 1.6 Run the local Atlas integration suite and confirm Lectern parity is unchanged (pure-refactor gate) — Core 358 + Atlas 25 green via `build/verify.sh --no-restage`.

## 2. Scriptorium block and block entity

- [x] 2.1 Add `BlockEntityScriptorium : BlockEntityScribeWritingStation` supplying Scriptorium config: `PageBackdrop` (reuse `LecternPage` placeholder), `PageAspect`, `DefaultDocumentTitleKey` = `"scribe:doctitle-scriptorium"`, a distinct mesh-key prefix, and its own `GuiDialogScribeScriptorium` dialog.
- [x] 2.2 Add `BlockScriptorium : BlockScribeWritingStation` supplying the `blockhelp-scriptorium-open`/`-edit` interaction-hint lang keys + cache key. (Also added `GuiDialogScribeScriptorium` as the v1.3 nav-button attachment point.)
- [x] 2.3 Register both in `ScribeModSystem`: `RegisterBlockClass("BlockScriptorium", …)` and `RegisterBlockEntityClass("Scriptorium", …)`.

## 3. Assets and content

- [x] 3.1 Add `assets/scribe/blocktypes/scriptorium.json` (entity class `Scriptorium`, block class `BlockScriptorium`, floor placement, collision/selection box, shape reference), using a stand-in/derived shape until the final model lands. Modeled on `lectern.json`; documents the JSON-only art-swap path (Decision 2).
- [x] 3.2 Stand-in shape/textures: reuses the existing Lectern shape `scribe:block/lectern/lectern` and its textures as the documented placeholder (Decision 2) — no new binary art committed. Final dedicated model/textures are the tracked 5.3 follow-up.
- [x] 3.3 Add the grid recipe `assets/scribe/recipes/grid/scriptorium.json`: plank-heavy `PFP,PNP,PPP` (7 planks + feather + ordinary metal nails, wildcard metal so not iron-gated). Kept the ink-fill (`liquidContainerProps` black dye) requirement for tier consistency with the Lectern.
- [x] 3.4 GUI backdrop: reuses the Lectern's `ScribeBackdrops.LecternPage` via `BlockEntityScriptorium.PageBackdrop` (Decision 3) — no new texture entry needed; dedicated `ScriptoriumPage` is the 5.3 follow-up.
- [x] 3.5 Add `lang/en.json` entries: `block-scriptorium`, `blockhelp-scriptorium-open`, `blockhelp-scriptorium-edit`, `doctitle-scriptorium`. (No recipe-name key — the Lectern has none; the grid `name` is a variant-grouping id, not a displayed string.)
- [x] 3.6 Add the handbook entry: `handbook-scriptorium-about-*` + `-views-*` sections wired via the blocktype `attributes.handbook.extraSections`, plus a Scriptorium line in the getting-started overview.

## 4. In-game verification

- [x] 4.1 Craft, place, and break the Scriptorium; confirm it returns to inventory and is visibly distinct from the Lectern. — Confirmed 2026-08-14 via playtest.
- [x] 4.2 Right-click opens the dialog in Read view; shift+right-click opens Edit view with a fresh focused task (quick-add). — Confirmed 2026-08-14 via playtest.
- [x] 4.3 Add/edit/complete/delete/reorder tasks; confirm edits persist across a save/reload. — Confirmed 2026-08-14 via playtest.
- [x] 4.4 Confirm floor-only placement (rejected on non-floor with the "requires solid ground" message) and face-the-player orientation that survives reload. — Confirmed 2026-08-14 via playtest.
- [x] 4.5 Break a Scriptorium holding a document and re-place the item; confirm document content, `DocId`/`TaskId`s, and a pinned task all survive. — Confirmed 2026-08-14 via playtest.
- [x] 4.6 Confirm the tooltip title line (placed + inventory) and that no burn/combustion lines appear. — Confirmed 2026-08-14 via playtest.
- [x] 4.7 Place both a Lectern and a Scriptorium; confirm distinct meshes/facings (no mesh-cache collision) and independent documents. — Confirmed 2026-08-14 via playtest.
- [x] 4.8 Multiplayer: confirm one-editor-at-a-time locking and cross-client sync work on the Scriptorium (or note as covered by the shared base + Atlas). — Confirmed 2026-08-14 via playtest.

## 5. Wrap-up

- [x] 5.1 Regenerate `TESTING.md` from the Group 4 items via the `what-to-test` skill — done 2026-08-14 (added the `add-scriptorium-block` group; all 8 items now carry Confirmed verdicts). Also retired 8 all-terminal archived-change groups into `playtest-history/TESTING-archive.md`.
- [x] 5.2 Update `CHANGELOG.md` and `ROADMAP.md` (mark the Scriptorium block landed; note provisional art/backdrop). Added a CHANGELOG `[Unreleased]` entry and re-scoped the ROADMAP organization tier (v4→v7) to the shared Scriptorium with the block foundation landed.
- [ ] 5.3 (Follow-up, tracked not done here) Swap in the final Blockbench model + textures (JSON-only) and decide on a dedicated `ScribeBackdrops.ScriptoriumPage`.
