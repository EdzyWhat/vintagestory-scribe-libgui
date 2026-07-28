## 1. Pin Tab — add to TESTING.md and verify in-game

- [x] 1.1 Transcribe scribe-pin-editor tasks 7.1–7.11 into TESTING.md under a new `## scribe-pin-editor`
      section. Include a preamble describing the Pin Tab (all pins across documents, editable rows,
      policy picker). Run `what-to-test` or write items manually. DO NOT mark any item done yet.
      - Done 2026-07-27: added the `## scribe-pin-editor` section with a preamble + 10 unchecked items
        (codes 691ef3c9, 8e914c42, 639b2da7, 85592294, e152d0e1, 12ca42f8, 85e92e9b, a1e8e10e, 45bb88ed,
        58328d3f) covering §1.3–1.12. No verdicts yet — awaiting the in-game pass.
- [ ] 1.2 Build Debug, restage (`bash build/restage.sh Debug`), and fully relaunch the client.
- [ ] 1.3 In-game: open the Lectern; the `scribepin` nav button switches the central region to the
      Pin Tab, listing all pins with no row cap. Navigate back and forth with read/editor.
- [ ] 1.4 In-game: confirm rows are editable by default — text field, checkbox, delete, unpin, and
      reorder grip are all present and act on the right pin.
- [ ] 1.5 In-game: complete a task from the Pin Tab → applies immediately with NO undo delay; confirm
      the HUD updates in lockstep.
- [ ] 1.6 In-game: edit a pin whose source Lectern IS loaded → source doc text updates and persists
      (reopen the Lectern to verify).
- [ ] 1.7 In-game: edit/delete a pin whose source Lectern IS NOT loaded → pin snapshot/removal
      updates; no crash; source doc unchanged.
- [ ] 1.8 In-game: unpin removes the pin only (task survives in the source doc); delete removes the
      task from the source doc.
- [ ] 1.9 In-game: reorder pins → order persists per-player across relog (under `scribe:pins:v1`);
      the corner HUD reflects the new order.
- [ ] 1.10 In-game: confirm blank/whitespace-only inline edit is rejected and leaves the task text
       unchanged.
- [ ] 1.11 In-game: change the completion policy from the Pin Tab picker → the Settings window
       reflects the same value; completing a task follows the new policy.
- [ ] 1.12 In-game: confirm the Pin Tab respects the Lectern-dialog theme/size (`PixelArtDisplay`,
       `WindowFontScale`, `PixelArtSize`) — not HUD-prefixed settings.

## 2. Multiplayer pass (A4) — second machine

- [ ] 2.1 Start a local headless server on the second machine:
      `dotnet ".../VintagestoryServer.dll" --dataPath ~/vsdata` with the scribe mod in the Mods folder.
      Confirm the server starts and loads the mod without errors.
- [ ] 2.2 Connect a second client (the second machine). Place two separate Lecterns. Edit each from its
      respective session; confirm the documents are independent and don't bleed into each other.
- [ ] 2.3 From session A, open Lectern 1 in read view. From session B, edit that same Lectern.
      Confirm session A's read view updates live (server syncs the edit).
- [ ] 2.4 From session A, open Lectern 1 in EDITOR view. From session B, try to open the same Lectern
      in editor (shift+right-click) → B is refused with the "one person at a time" message but can
      still open it in READ view normally.
- [ ] 2.5 From session A's editor: drag-reorder a row, use the tool panel, adjust text-size slider.
      Reopen from the same session → changes persist. Confirm B's read view reflects the same order.
- [ ] 2.6 Record verdicts in TESTING.md for items c127b9ad (7.5), 2a105a38 (7.6), and the
      reorder/settings item (7.7).

## 3. Survival pass (A5) + recipe handbook entry

- [ ] 3.1 Start or load a survival world (not Creative). Confirm the Lectern does NOT appear in the
      creative inventory in creative mode (it should — but confirm it also works in survival reach).
- [ ] 3.2 Gather the recipe ingredients in survival: planks, nails, parchment, feather, leather, and
      a fired bowl containing black dye (≥1 L). Confirm all items are obtainable without Creative.
- [ ] 3.3 Craft the Lectern at a crafting grid using the `add-lectern-recipe` change's recipe.
      Confirm the recipe registers and produces one Lectern block.
- [ ] 3.4 Place and use the crafted Lectern in survival: open read view, open editor, add a task, pin
      it to HUD. Confirm no Creative-reach quirks or crashes.
- [ ] 3.5 Confirm the Lectern's in-game handbook entry shows the crafting recipe (add-lectern-recipe
      task 3.2). Confirm the entry is reachable via the H handbook.
- [ ] 3.6 Record verdicts for the survival pass items in TESTING.md (RELEASE.md A5).

## 4. Sidebar nav buttons retest (923a395a)

- [ ] 4.1 Build and restage Debug (the ×1.5 fix is already staged from v1-playtest-fixes 5.6).
      Confirm the DLL is fresh.
- [ ] 4.2 In-game: open the Lectern and check the Read/Edit/Pinned/Settings nav buttons in the
      right sidebar — confirm they are visibly larger (×1.5) and sit correctly within the column
      without breaking layout. Record the verdict in TESTING.md for `923a395a`.

## 5. v3-blob codec test

- [x] 5.1 In `tests/Core.Tests/`, add a new test method (or test class `ScribeDocumentCodecV3Tests`)
      that hand-crafts a valid v3-format byte array: the magic bytes, version byte = 3, block count,
      and one task block with known text. Call `ScribeDocumentCodec.TryDeserialize`. Assert:
      (a) returns `true`, (b) `document.DocId` is a non-empty Guid (freshly generated on migration),
      (c) the task text matches the source content.
      - Already covered 2026-07-27: `ScribeDocumentCodecTests.TryDeserialize_V3Bytes_Succeeds_AndSurfacesLegacyPinnedIds`
        hand-builds a v3 byte array (magic `SCRB`, version byte 3, block count, task blocks with known text)
        and asserts (a) `TryDeserialize` returns true, (b) `DocId` != `Guid.Empty` (fresh on migration) +
        fresh per-block `TaskId`s, (c) block text matches ("Find copper"/"Find tin"), plus the legacy-pinned
        id surfacing. The proposal's "zero test coverage" premise was stale — no new test needed; a second
        one would duplicate this. (If a dedicated single-task fixture is still wanted, flag it.)
- [x] 5.2 Run `dotnet test tests/Core.Tests/Core.Tests.csproj` — confirm the new test passes alongside
      all existing tests with no game install. If CI is currently green, push and confirm CI stays green.
      - Verified 2026-07-27: `dotnet test tests/Core.Tests/Core.Tests.csproj` → 140/140 passed (incl. the v3
        codec test), no game install.

## 6. Font selector — decide lineup and bundle faces

- [x] 6.1 Decide the font lineup (design.md open question #2). Confirm Scapholene is in. Choose up to
      3 additional faces from: the existing LibGUI-bundled serifs (Playfair Display / Cormorant
      Unicase — zero new assets), or other faces with verified permissive licenses. Document the
      decision here (edit this task with a note once decided).
      - Decided 2026-07-27 (user): lineup = Default (built-in body font) + **Scapholene, Caudex, La Belle
        Aurore, Noto Sans, Noto Serif** (bundled) + **Playfair Display, Cormorant Unicase** (zero-asset,
        from the LibGUI `gui` dep). 7 named faces + default. Codified in `ScribePlayerSettings.KnownTaskFonts`.
- [x] 6.2 Decide the button font (design.md open question #1). Either: keep the current default for
      buttons and only change task text, or choose a button face. Document the decision.
      - Decided 2026-07-27 (user): the in-Lectern TEXT buttons (Edit / New Task / Done Editing) use a
        FIXED **Caudex** face (`ScribeTaskFont.ButtonFamily`), independent of the task-text selector; the
        font selector governs task/note row text ONLY.
- [x] 6.3 Download and verify the license for each new font to be bundled (Scapholene +  any others).
      Confirm license allows mod redistribution.
      - Verified 2026-07-27: Scapholène v1.0 (redistribution permitted with license+credit retained, no
        modification — bundled unmodified, fine); La Belle Aurore / Noto Sans / Noto Serif all SIL OFL 1.1.
        License files shipped alongside the TTFs; all recorded in CREDITS.
- [x] 6.4 Add new TTF file(s) to `src/Mod/assets/scribe/textures/fonts/`. Add OFL or license
      text files alongside them if required by the license.
      - Done 2026-07-27: added scapholene-regular.ttf, labelleaurore-regular.ttf, notosans-regular.ttf,
        notoserif-regular.ttf + LICENSE_Scapholene.txt, OFL-LaBelleAurore.txt, OFL-NotoSans.txt,
        OFL-NotoSerif.txt (~2.3 MB total).
- [x] 6.5 In `ScribeModSystem.StartClientSide`, register each new face via
      `FontRegistry.RegisterCustomFont` — follow the exact Caudex pattern (`caudex-regular.ttf`
      registration). Verify each face loads without error on a Debug build (`bash build/restage.sh Debug`
      + relaunch; watch for font-load warnings in the log).
      - Done 2026-07-27: `RegisterCustomFonts` now loads the 4 bundled faces (each under all weights, per
        the Caudex pattern) with a per-face load-failure warning; Playfair/Cormorant come from LibGUI so
        aren't re-registered. Build clean; in-game font-load-log check is part of 6.9.
- [x] 6.6 Add `TaskFontFamily` (string, defaults to `""` = system default) to `ScribePlayerSettings`.
      Wire it through `UpdateMySettings` / `ApplySettings` like the other settings fields.
      - Done 2026-07-27. NOTE: `ScribePlayerSettings` lives in `src/Core/`, not Mod (design D1's
        "Mod-side only" was inaccurate). Added `TaskFontFamily` (string, default "") + `KnownTaskFonts`
        allowlist + `NormalizeTaskFontFamily` (falls unknown→default) there — a plain POCO string, no VS
        API, so the Core-unit-testable invariant holds. Normalized() clamps it. User approved this location.
- [x] 6.7 Add a font selector control to `ScribeSettingsContent` in the Window Appearance section,
      following the existing `PairedControls` / dropdown pattern. Add the `settings-taskfont` and
      option-label lang keys to `lang/en.json`.
      - Done 2026-07-27: `Dropdown<string>` (Default + KnownTaskFonts, labeled by family name) in the
        Window Appearance section; `settings-taskfont`/`-help`/`-default` lang keys added.
- [x] 6.8 In `GuiDialogScribeLecternLibGui`, apply `TaskFontFamily` to the `TextStyle` used for task
      row text. Confirm the font updates live when `ApplySettings` fires (no dialog restart needed).
      - Done 2026-07-27: threaded through `ScribeRowStyle.TaskFontFamily` (re-derived per build) →
        read-row `TextStyle.FontFamily` and the editor/pin `ScribeMultilineField` (new `fontFamily` param,
        used for BOTH measure + draw so read/edit line metrics stay identical). `ScribeTaskFont.Resolve`
        maps ""→"sans-serif". Live-update confirmation is part of the in-game 6.9.
- [ ] 6.9 In-game: open Settings → confirm the font selector is present in Window Appearance with all
      configured options. Switch fonts → task rows update immediately. Close and reopen the Lectern →
      font persists. Relog → font still persists.
- [ ] 6.10 Test the fallback: temporarily rename a bundled TTF so it can't load → confirm a single
       warning is logged and the selector falls back to the default font without crashing.

## 7. Credits and CHANGELOG

- [x] 7.1 Update `CREDITS` at the repo root: add an entry for every newly bundled font (name, author
      URL, license name). Add an entry crediting JeanPierre and Wanderer's Sketchbook as an inspiration
      for the task-pinning concept.
      - Done 2026-07-27: CREDITS now lists Caudex, Scapholène (+ Artekuno suggestion), La Belle Aurore,
        Noto Sans, Noto Serif with license + bundled-file paths, notes Playfair/Cormorant come from LibGUI,
        and adds a Wanderer's Sketchbook / JeanPierre inspiration entry. Also fixed the stale
        caudex-regular.ttf reference → caudex-bold.ttf (the file we actually ship).
- [x] 7.2 Create `CHANGELOG.md` at the repo root. Add the `## [0.1.0]` section with:
      - **Added**: Lectern block (place, edit tasks/notes, multiplayer-safe), pinned-task HUD
        (always-on, rebindable P hotkey, completion policies), Scribe Settings (all preferences),
        font selector for task text (Scapholene + [other faces]), survival grid recipe.
      - **Dependencies**: `game 1.22.0`, `gui 2.0.0 (LibGUI)`.
      - Date: fill in on the day of the tag.
      - Done 2026-07-27: created CHANGELOG.md (Keep a Changelog format) with the `## [0.1.0] - Unreleased`
        entry — Added (Lectern, recipe, HUD, Pin Tab, Settings, font selector) + Dependencies. Date to be
        filled on tag day (§8.4).

## 8. Scope freeze and ship (RELEASE.md A6 + Track G)

- [ ] 8.1 Confirm all items in tasks 1–7 above are done. Confirm no known-broken TESTING.md items
      remain. Confirm `modinfo.json` version = `"0.1.0"` and dep versions are current.
- [ ] 8.2 Build Release: `dotnet build -c Release`. Run `dotnet test`. Confirm 0 failures.
- [ ] 8.3 Package: `./build/package.sh` → `Releases/scribe_0.1.0.zip`.
- [ ] 8.4 Tag and release: `git tag v0.1.0 && git push origin v0.1.0` → `release.yml` creates the
      GitHub Release. `gh release upload v0.1.0 Releases/scribe_0.1.0.zip`.
- [ ] 8.5 Publish the VS mod DB page with the zip/release link.

## 9. Post-release (not ship gates — do after 8.5)

- [ ] 9.1 Draft and post the reddit release post. Must answer: recipe (planks + nails + parchment +
      feather + leather + ink bowl), no temporal gear, LibGUI required (no ImGui), multiplayer
      confirmed, sound toggle exists, timers deferred to Clockmaker's Notebook (v2), Scapholene
      credit to Artekuno's suggestion. Reference the teaser thread.
- [ ] 9.2 Capture B2 feature screenshots: HUD in-world with active pins, Settings window, notebook
      backdrop, task checklist in the editor. Store under `docs/media/` (per RELEASE.md press-media
      decision — separate from `screenshots/` debug shots).
- [ ] 9.3 Produce the 60–90s feature showcase video: place lectern → add/check tasks → pin to HUD →
      HUD in-world → settings/themes → outro/download link. Script draft at `docs/media/video-script.md`.
- [ ] 9.4 Reach out to the LibGUI author with a courtesy message (attribution, feedback, and optionally
      a link to the released mod).
- [ ] 9.5 Add Tab/Shift+Tab navigation tooltips to the task editor rows (discoverability — surfaced by
      thepeebrain's teaser comment about keyboard-driven use). Promote to its own small change if
      non-trivial.
- [ ] 9.6 Run `/simplify` code quality pass on `src/Mod/GuiDialogScribeLecternLibGui.cs` and the most
      recently touched files.
- [ ] 9.7 Iterate on the VS mod DB page based on early download/comment feedback.
