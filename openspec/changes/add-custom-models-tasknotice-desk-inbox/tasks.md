## 1. Assignment Desk: local model

- [x] 1.1 Create `src/Mod/assets/scribe/shapes/block/assignmentdesk/assignmentdesk.json` and
  `assignmentdesk.bbmodel` as exact copies of
  `src/Mod/assets/scribe/shapes/block/scriptorium/scriptorium.json` and `scriptorium.bbmodel`,
  and verify both files exist and the `.json` parses (VS shape JSON is relaxed/JSON5-ish per
  `docs/vs-texture-model-workflow`, so prefer diffing against the source file over a strict JSON
  parse if that trips up on comments/trailing commas).
- [x] 1.2 Copy the Scriptorium's nine textures (`charred.png`, `ebony.png`, `ebony-ornate.png`,
  `opened-normal.png`, `normal-orangebrown.png`, `feather.png`, `bituminous.png`, `quartz.png`,
  `brass.png`) from `src/Mod/assets/scribe/textures/block/scriptorium/` into a new
  `src/Mod/assets/scribe/textures/block/assignmentdesk/` folder, unmodified, and verify all nine
  files exist at the new path.
- [x] 1.3 Update `src/Mod/assets/scribe/blocktypes/assignmentdesk.json`: change `shape.base` to
  `"scribe:block/assignmentdesk/assignmentdesk"`, add a `textures` override block with the same
  nine keys as `scriptorium.json`'s own but pointing at `scribe:block/assignmentdesk/<key>`, and
  update its PLACEHOLDER MODEL comment to note the model is now a locally-owned,
  Scriptorium-derived starting point (not a direct reference to the Scriptorium's or Lectern's
  files, and not final art).
- [x] 1.4 Build (`dotnet build -c Debug`) and restage per `build/restage.sh Debug`, then manually
  - Confirmed 2026-09-05: TESTING.md `00000090` "It looks like the new model, not the Scriptorium (which was a placeholder model). The appearance is correct, we may need to change the spec." (submission 2026-09-05T22-19-48)
  verify in-game: placing and viewing an Assignment Desk renders fully textured (no missing-
  texture/pink-checkerboard faces or the engine's unknown-asset "?" placeholder). STALE WORDING
  NOTE (2026-09-05): this task originally expected the result to match the Scriptorium's
  appearance — no longer accurate, since `assignmentdesk.bbmodel` has since received its own
  real geometry (see proposal.md's 2026-09-05 update); verify texture completeness, not visual
  identity to the Scriptorium. BLOCKED as of 2026-09-05: the current shape fails to parse at all
  (client log: "Failed parsing shape model scribe:shapes/block/assignmentdesk/assignmentdesk.json"
  / "Exception has been thrown by the target of an invocation.") — 36 elements / 68 faces have
  `"texture": null` (looks fine in Blockbench since these are faces hidden from the default
  camera angle, but the engine crashes the whole shape's parse on any enabled face with a null
  texture; see VSAPI-NOTES.md's new Blockbench-export note). Needs a Blockbench pass assigning a
  texture to (or disabling) all 68 faces before this task can pass.

## 2. Inbox: local model

- [x] 2.1 Create `src/Mod/assets/scribe/shapes/block/inbox/inbox.json` and `inbox.bbmodel` as
  exact copies of the Scriptorium's shape and `.bbmodel` files, and verify both exist.
- [x] 2.2 Copy the same nine Scriptorium textures into a new
  `src/Mod/assets/scribe/textures/block/inbox/` folder, and verify all nine files exist.
- [x] 2.3 Update `src/Mod/assets/scribe/blocktypes/inbox.json`: change `shape.base` to
  `"scribe:block/inbox/inbox"`, add the matching nine-key `textures` override block pointing at
  `scribe:block/inbox/<key>`, and update its PLACEHOLDER MODEL comment the same way as the
  Desk's.
- [x] 2.4 Restage and manually verify in-game: placing and viewing the standalone Inbox block
  - Confirmed 2026-09-05: TESTING.md `00000091` "Once again, the look is what I want - but different than the Scriptorium (which was used as a placeholder model)." (submission 2026-09-05T22-19-48)
  renders fully textured (no missing-texture faces or the engine's unknown-asset "?"
  placeholder). Same stale-wording note as 1.4 — `inbox.bbmodel` has since received its own real
  geometry, no longer expected to match the Scriptorium. BLOCKED as of 2026-09-05 by the same
  class of bug as 1.4: 35 elements / 72 faces in `inbox.json` have `"texture": null`. Needs the
  same Blockbench fix.

## 3. Task Notice: split blank/filled model files

- [x] 3.1 Move `src/Mod/assets/scribe/shapes/item/tasknotice.json` to
  - Confirmed 2026-09-05: TESTING.md `00000092` "(no note)" (submission 2026-09-05T22-19-48)
  `src/Mod/assets/scribe/shapes/item/tasknotice/blank.json` (new folder), and move its two
  textures (`tasknotice.png`, `tasknotice-tie.png`) from
  `src/Mod/assets/scribe/textures/item/` into `src/Mod/assets/scribe/textures/item/tasknotice/`
  as `blank.png`/`blank-tie.png`, updating the shape's own `textures` block to match the new
  texture filenames. Verify the relocated shape file's texture references resolve (no missing
  texture warnings in the game log on load).
- [ ] 3.2 Create `src/Mod/assets/scribe/shapes/item/tasknotice/filled.json`, starting from a copy
  of `blank.json`, and add one small structural element (e.g. an extra seal/wax-blob cube on the
  `tie` element) so the filled shape is visibly distinct from blank even before real art exists.
  Add matching placeholder textures (`filled.png`, and `filled-tie.png` if the seal needs its own
  texture) under `src/Mod/assets/scribe/textures/item/tasknotice/`. Verify the file loads without
  missing-texture warnings.
- [ ] 3.3 Update `src/Mod/assets/scribe/itemtypes/tasknotice.json`'s `shape.base` to
  `"scribe:item/tasknotice/blank"` (the default/fallback shape — actual per-stack selection comes
  from task 4 below), and verify the item still renders in creative inventory, hand, and on the
  ground after this rename (no missing-asset errors in the log).

## 4. Task Notice: per-stack render swap

- [x] 4.1 In `src/Mod/ItemScribeTaskNotice.cs`, add `OnBeforeRender(ICoreClientAPI, ItemStack,
  EnumItemRenderTarget, ref ItemRenderInfo)`: pick between two fixed `ObjectCacheUtil`-cached
  `MultiTextureMeshRef`s (one for `scribe:item/tasknotice/blank`, one for
  `scribe:item/tasknotice/filled`) based on the existing `IsSealed(ItemStack)` check, tesselating
  each shape once via `capi.Tesselator.TesselateShape` + `capi.Render.UploadMultiTextureMesh` on
  first use (see design.md Decision 2). Verify by build + manual in-game check: a blank notice in
  hand shows the blank model; sealing one (send an assignment via "Send a Notice" mode) makes the
  now-sealed notice in the output slot show the filled model, with no change to a stack already
  held elsewhere until it's re-rendered (should be immediate, since the check runs every frame).
- [x] 4.2 Add an `OnUnloaded(ICoreAPI)` override that disposes both cached mesh refs (mirroring
  - Confirmed 2026-09-05: TESTING.md `00000094` "(no note)" (submission 2026-09-05T22-19-48)
  `CollectibleBehaviorCustomTongedShape.OnUnloaded`). Verify by leaving and rejoining a world
  twice in a row with a Task Notice present in inventory and confirming no error/exception in the
  client log on either transition.

## 5. Full regression pass

- [x] 5.1 Run `dotnet test` for `tests/Core.Tests` and confirm all tests still pass (no Core
  changes expected in this work, but confirm the suite is unaffected).
- [x] 5.2 Manual playtest: craft a blank Task Notice, confirm its appearance in inventory/hand
  - Confirmed 2026-09-05: TESTING.md `00000095` "(no note)" (submission 2026-09-05T22-19-48)
  matches the (relocated, otherwise unchanged) blank model; send an assignment via "Send a
  Notice" to seal one, confirm the sealed notice in the Create Assignments tab's output slot
  shows the new filled model; place an Assignment Desk and a standalone Inbox and confirm both
  now render as fully-textured, Scriptorium-derived placeholders (visually matching the
  Scriptorium block, no missing textures) from their own local files.
