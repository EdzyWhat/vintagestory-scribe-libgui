## 1. Codec docs match v8 / pin v5

- [x] 1.1 Fix stale `[MinVersion=5, Version=7]` (and any leftover “current v7”) wording in `src/Core/ScribeDocumentCodec.cs` method comments so they name `[5, 8]` / Version 8. Do not change serialize/deserialize logic.
- [x] 1.2 Fix the same stale window wording in `tests/Core.Tests/ScribeDocumentCodecTests.cs` comments. Add `TryDeserialize_V7Bytes_Succeeds_AndDefaultsRecipeSignature` if a dedicated v7 older-blob test is missing (assert empty `RecipeSignature`; not merely `ok == true`).
- [x] 1.3 Update `docs/CODEC-MIGRATION.md`: add a v7→v8 worked note (`RecipeSignature` per block) and pin v4→v5 (`Depth`); set the summary table to document codec **v8** / window **v5–v8** and pin codec **v5**.
- [x] 1.4 `dotnet test tests/Core.Tests` — green, including the v7 older-blob test.



## 2. In-game handbook tells the 1.3 truth

- [x] 2.1 Update `craftinginfo-scribe-getting-started-text`: add the Chalkboard to the craft list (working `handbook://` link); name Crafting Tasks in the task-types paragraph; fix `featues` and the incomplete “enrich your experiences with other”; mention Chalkboard among placed Guest Book surfaces.
- [x] 2.2 Update `craftinginfo-scribe-task-types-title` and `-text`: title names Item Trackers, Links, **and Crafting Tasks**; body explains Craft (handbook page, recipe variants, ingredient subtasks, litre liquids) and no longer claims only two item-bound types.
- [x] 2.3 Update `craftinginfo-scribe-editor-reference-text` “Adding tasks”: Crafting Tasks sit with Trackers/Links as handbook-created; one sentence on grip-tap subtask indent.
- [x] 2.4 Update `craftinginfo-scribe-pinned-hud-text`: drop “Item Item Tracker”; note that Crafting Tasks can be pinned.
- [x] 2.5 Leave `pt-br.json` untouched (English fallback). Restage (`build/restage.sh`) so the lang reload is in the staged mod.



## 3. ROADMAP + public version surfaces

- [x] 3.1 `ROADMAP.md`: v1.2 Scriptorium cluster **shipped**; v1.3 = Crafting Tasks + Chalkboard (tablet readability) **shipped**; assignment (Assign & History / Inbox) **later**; wall Chalkboard ≠ v6 drawable board.
- [x] 3.2 `README.md`: status line → v1.3.0; feature bullets include Chalkboard and Crafting Tasks (Scriptorium is already there).
- [x] 3.3 `docs/media/mod-page.txt`, `mod-page.html`, and `mod-page-inline.html`: bump to 1.3.0; add Chalkboard + Crafting Tasks; tick v1.2 and v1.3 released; remove the “v1.2 Writing Desk planned” row.



## 4. Wiki drafts

- [x] 4.1 `docs/media/wiki/Home.md`: intro + nav + roadmap tick v1.2 and v1.3 shipped; link new Scriptorium and Chalkboard pages; assignment later.
- [x] 4.2 `docs/media/wiki/Items.md`: add Scriptorium and Chalkboard sections (uniqueness-first, Lectern-length).
- [x] 4.3 `docs/media/wiki/Crafting-the-Lectern.md`: Scriptorium recipe (same writing kit, eight planks) and Chalkboard recipe (planks + charcoal + nails, no ink kit).
- [x] 4.4 New `docs/media/wiki/Scriptorium.md` (Transcribe / copy / import-export) and `docs/media/wiki/Chalkboard.md` (wall-mount, 10-task cap, not the drawable v6 board).
- [x] 4.5 Note in `docs/media/wiki/README.md` that these drafts are for **1.3.0** (publishing to the GitHub wiki clone stays a manual post-tag step).



## 5. Version freeze + CHANGELOG

- [x] 5.1 Set `src/Mod/modinfo.json` `"version"` to `"1.3.0"` (deps unchanged).
- [x] 5.2 Write `CHANGELOG.md` `## [1.3.0] - <today>` from the D1 audit (player-facing only; save-compat note for codec v8 / pin v5). Add footer compare links `[1.3.0]` (`v1.2.1...v1.3.0`) and the missing `[1.2.1]` (`v1.2.0...v1.2.1`).
- [x] 5.3 Grep the version surfaces in §3–5: every one says **1.3.0**, none still advertise v1.1.0 or a planned Writing Desk.



## 6. Verify

- [x] 6.1 In-game smoke (restaged 1.3.0): Getting Started lists Chalkboard + Crafting Tasks; task-types explainer describes Craft; Chalkboard handbook entry still uniqueness-first. (Author: glance Craft-from-handbook, chalkboard place/open, wet→hard→fired tablet, Transcribe stamp+sound — not a full TESTING.md sweep.)
- [x] 6.2 `openspec validate cut-v1-3-0` passes.

> After this change lands, the author tags `v1.3.0`, builds the zip, and uploads (GitHub Release + mod DB). That ship step is **not** a task here. Wiki publish from `docs/media/wiki/` is the same manual copy as 1.0.

