## Why

The 1.3.0 feature work is archived and the working tree is clean, but the **release surfaces still describe 1.1/1.2**. Shipping the zip as-is would tag a codec-v8 cut whose `modinfo` says 1.2.1, whose CHANGELOG stops at 1.2.1, whose README still says v1.1.0, and whose in-game handbook still claims there are only two item-bound task types. The features are done; the gate is a truthful cut.

## What Changes

- **Version freeze at 1.3.0.** Bump `src/Mod/modinfo.json`; add a Keep-a-Changelog `[1.3.0]` section (plus the missing `[1.2.1]` compare-link footer); sync every version surface that the 1.1/1.2 cuts already treat as required: README status line, `docs/media/mod-page.{txt,html,inline.html}`, wiki `Home.md`.
- **Reframe the map.** `ROADMAP.md` currently calls v1.3 the assignment system and v1.2 “in progress.” This cut is Crafting Tasks + Chalkboard + tablet readability. Assignment (Assign & History / Inbox) moves to **later**; v1.2 is marked shipped.
- **Handbook tells the truth about 1.3.** Getting Started, the task-types explainer, and the editor-reference currently describe Tracker + Link only (“two item-bound types,” “can’t be added from the Add button”) and never mention the Chalkboard. Update them so Crafting Tasks and the Chalkboard are documented, and clean leftover copy bugs (`featues`, “Item Item Tracker”, incomplete “enrich your experiences with other”).
- **Wiki drafts catch up.** `docs/media/wiki/` is still the 1.0.0 set (no Scriptorium, no Chalkboard, no Craft, roadmap still “v1.2 Writing Desk planned”). Refresh Home / Items / Crafting and add the missing pages so a published wiki doesn’t contradict the mod DB.
- **Codec docs match the code.** Document codec is v8 (`RecipeSignature`); pin codec is v5 (`Depth`); reader window is `[5, 8]`. `docs/CODEC-MIGRATION.md` and a few comments/tests still say the window ends at v7. Bring them current and state the one-way write (1.2.x worlds open; 1.3 writes cannot be read by 1.2 clients — VS version-matching makes mixed MP a non-issue).
- **Out of scope (explicit):** stamp-sound credit (`stamp.ogg` is author-recorded); GitHub tag / zip / mod-DB upload (author’s manual ship step, same as 1.2); pt-br catch-up; parked bugs (`fix-dialog-open-white-flash`, HUD blank-checkbox, MP watcher stamp); LibGUI decoupling; Scriptorium dedicated backdrop art.

No **BREAKING** player behavior. The v8 write is one-way, same class as the v6/v7 bumps already shipped.

## Capabilities

### New Capabilities
- `v1-3-release-cut`: the 1.3.0 version freeze — `modinfo` 1.3.0, CHANGELOG `[1.3.0]` from a per-change audit of everything since `v1.2.1`, compare-link footers, and every public version surface agreeing on 1.3.0. ROADMAP reframes this cut as Crafting Tasks + Chalkboard (assignment deferred). Wiki drafts in `docs/media/wiki/` describe the surfaces that actually ship.

### Modified Capabilities
- `item-handbook-entries`: Getting Started and the shared guide pages SHALL mention the Chalkboard as a placed surface and Crafting Tasks as a third item-bound type, not a Lectern/Notebook/Tracker-Link-only mod.
- `handbook-scribe-entry`: the task-types explainer SHALL describe Crafting Tasks alongside Item Trackers and Links (how they are created, that they generate ingredient subtasks, that they come from an item’s handbook page).
- `codec-migration`: the documented accepted window SHALL be `[MinVersion=5, Version=8]` (not v7); `docs/CODEC-MIGRATION.md` and codec comments/tests SHALL name the current version and the v8 `RecipeSignature` / pin-v5 `Depth` fields.

## Impact

- **Release files:** `src/Mod/modinfo.json`, `CHANGELOG.md`, `README.md`, `ROADMAP.md`, `docs/media/mod-page.txt` / `.html` / `mod-page-inline.html`, `docs/media/wiki/*`.
- **Handbook:** `src/Mod/assets/scribe/lang/en.json` (Getting Started, task-types, editor-reference, HUD copy as needed). `pt-br.json` is not translated here — new/changed keys fall back to English, same as 1.2.
- **Codec docs:** `docs/CODEC-MIGRATION.md`; comment drift in `src/Core/ScribeDocumentCodec.cs` and `tests/Core.Tests/ScribeDocumentCodecTests.cs`. No codec *behavior* change.
- **No `src/Core/` logic, no new dependency, no codec bump.** Deps stay `game 1.22.0`, `gui 3.1.0`.
- **Not this change:** tagging `v1.3.0`, building the zip, GitHub Release, mod-DB upload.
