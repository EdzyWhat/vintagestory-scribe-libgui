## 1. Crafting recipes (data-only)

- [x] 1.1 Author `recipes/grid/scribenotebook.json` producing one `scribe:scribenotebook` from a
      paper + leather writing set (baseline `game:paper-parchment` + `game:leather-normal-plain`);
      finalize the exact grid arrangement and any binding item.
- [x] 1.2 Add/confirm any lang keys needed for the recipe output display. (None needed — the grid
      recipe derives its output name from the existing `item-scribenotebook` key.)
- [x] 1.3 Review `recipes/grid/scribelectern.json` and `recipes/grid/scribeclockmakernotebook.json`
      for balance; adjust only if warranted and note any change. FOUND + FIXED a crash bug: the
      Clockmaker's Notebook's first variant used the non-existent `item game:metalparts-*` wildcard,
      which crashed the handbook's "Created by" renderer on open. Corrected to `block game:metal-parts`.
- [x] 1.4 Restage the mod (`build/restage.sh`) and confirm in-game: the Notebook shows a grid recipe
      in the handbook, is craftable in survival, and the full Notebook → Clockmaker's chain works.
      Confirmed in-game 2026-07-31 (user): plain Notebook survival-craftable + full upgrade craft
      completes. (TESTING.md `38e61888`.)

## 1b. Clockmaker's Notebook trait gating + bypass

- [x] 1b.1 Add `"requiresTrait": "tinkerer"` to `recipes/grid/scribeclockmakernotebook.json` (data-only;
      enforced by the vanilla `CharacterSystem` `MatchesGridRecipe` handler). Leave the plain Notebook +
      Lectern recipes ungated. (Recipe also corrected to a single 3-ingredient recipe: 1 Notebook +
      1 Temporal Gear + 1 Metal Parts.)
- [x] 1b.2 Declare the bypass worldconfig boolean in a new `worldconfig.json` at the mod root (next to
      `modinfo.json`): one `worldConfigAttributes` entry
      `{ "category": "scribe", "code": "scribeClockmakerRequiresTrait", "dataType": "bool", "default": "true", "onCustomizeScreen": false }`.
      `onCustomizeScreen: false` keeps it OUT of the world-creation/Modify GUI (it's an operator setting,
      not a worldgen option) — reached only via `/worldconfig scribeClockmakerRequiresTrait false` (needs
      `controlserver`). Because it never renders a GUI label, the `worldattribute-…` lang keys are NOT
      needed and are intentionally not shipped. `build/package.sh` also stages `worldconfig.json` into the
      release zip (it previously only copied modinfo/DLLs/assets, so the shipped mod had no WorldConfig and
      `/worldconfig` returned "No such config found" on every world).
- [x] 1b.3 In `AssetsFinalize` or `StartServerSide` (after recipe registration — NOT early
      `AssetsLoaded`), read `sapi.World.Config.GetBool("scribeClockmakerRequiresTrait", true)`; when
      false, enumerate `sapi.World.GridRecipes`, match the Clockmaker's Notebook recipe(s) by
      `Name.Path`/output code, and set `RequiresTrait = null`. Do NOT add a second `MatchesGridRecipe`
      handler (last-writer-wins semantics make it unreliable — see design 6b).
- [ ] 1b.4 Restage and confirm in-game: with the requirement ON, a clockmaker-class (tinkerer) player
      can craft the Clockmaker's Notebook and a non-tinkerer cannot; toggling the worldconfig off (new
      world or `/worldconfig`) lets a non-tinkerer craft it. Verify a classless/no-character-system
      world is not blocked.

## 2. In-game handbook (data-only)

- [x] 2.1 Add a `handbook` block with `extraSections` to `itemtypes/scribenotebook.json` referencing
      new `scribe:` lang keys (what it does, editor, how to craft). 3 sections; cross-links to Lectern
      + Clockmaker's Notebook + editor-reference.
- [x] 2.2 Add a `handbook` block with `extraSections` to `itemtypes/scribeclockmakernotebook.json`
      (function, the timer, crafting from a Notebook). 3 sections; craft section documents the
      Tinkerer-trait requirement + the worldconfig bypass (engine denial is silent).
- [x] 2.3 Refresh the two guide pages (`00-getting-started`, `01-editor-reference`) so mod-wide docs
      acknowledge the notebooks with working cross-links. (The `handbook-scribelectern-*` sections
      themselves remain Lectern-accurate and were left unchanged.)
- [x] 2.4 Restage and confirm in-game: both notebook items show the new handbook sections and the
      guide pages read coherently. Confirmed in-game 2026-07-31 (user): the new handbook extra sections
      render on the notebook items. (TESTING.md `0213647b`.)

## 3. Demo-seeding command + Clockmaker history fix (code)

- [x] 3.1 Make `NotebookHost.Flush()` public (match the existing public `FlushHistory()`).
- [x] 3.2 Widen ALL sibling-exclusion sites to `ItemScribeNotebook or ItemClockmakerNotebook`
      (in-game verification showed the bug is broader than just history): `FindNotebookInInventory`
      (history recorders), `OnServerReceivedNotebookSave` (was dropping Clockmaker task/note edits —
      confirmed data loss on dialog close), `TryResolveDocHost` inventory scan (DocId→host routing),
      and `OnActiveSlotChanged` in `GuiDialogScribeNotebook` (dialog force-closed on hotbar change).
      Leave `/scripttf` Notebook-only. `NotebookHost` works unchanged for a clockmaker stack.
- [x] 3.3 Add server-only `BlockEntityScribeLectern.SeedGuestbook(entries)` guarding
      `Api is ICoreServerAPI`, looping `TryAddEntry`/`TrySetNote`, then `MarkDirty()` (mirror
      `RecordVisitor`). Takes `(VisitorName, InGameDate, Note?)` tuples; skips empty notes.
- [x] 3.4 Add `FormatDateDaysAgo(sapi, daysAgo)` mirroring `NotebookHost.FormatDate` for varied
      in-game dates. Derives month/year/day from `TotalDays - daysAgo` (clamped ≥0); months/year
      via `DaysPerYear / DaysPerMonth` (no `MonthsPerYear` on the calendar API).
- [x] 3.5 Register the server-side `/scribe seed <what> [target]` command in `StartServerSide`:
      `WordRange` args, `.RequiresPrivilege(Privilege.controlserver)`, `.RequiresPlayer()`, plus an
      in-handler creative-mode check. Modeled as a `/scribe` root + `seed` subcommand; `target` is an
      `OptionalWordRange` defaulting to `auto`.
- [x] 3.6 Implement target resolution (`auto` → looked-at Lectern else held notebook) and the seed
      handler: 12 mixed-state tasks + 2 note sections on the document; 7-entry History spread on
      notebooks; 8 Guestbook visitors (some with short notes) on lecterns; History-on-lectern and
      Guestbook-on-notebook are errored with a clear message (never silently applied).
- [x] 3.7 Persist + resync: `NotebookHost.Flush()` for notebooks; `MarkDirty(redrawOnClient:true)`
      for lecterns. No new network message types.
- [x] 3.8 Add/confirm Core test coverage for the Clockmaker detection fix where feasible; build
      passes (`build/verify.sh`). NOT FEASIBLE at Core level — the fix is a Mod-layer type check on VS
      API sibling classes (`ItemScribeNotebook`/`ItemClockmakerNotebook`) with no Core seam. Confirmed
      the Mod build compiles and all 183 Core tests pass.
- [ ] 3.9 In a creative world, run the command against a Notebook and a Lectern; confirm tasks/notes,
      History (notebook), and Guestbook (lectern) populate, persist across save/reload, and sync to a
      second client.

## 4. Launch material (in-repo drafts)

- [x] 4.1 Update `docs/media/mod-page.txt`: fix stale LibGUI 2.0.0 → 3.1.0 (three spots: header,
      Requirements footer), add the Notebook/Clockmaker's Notebook section, and bump the roadmap
      (v0.2 marked released).
- [x] 4.2 Draft/refresh GitHub wiki pages under `docs/media/wiki/`: refreshed the 7 Lectern pages
      (LibGUI 3.1.0, roadmap, block+two-items) and added `The-Notebook`, `Clockmakers-Notebook-and-Timers`,
      `History-and-Guestbook` + a README publishing checklist. Source pages read from the sibling wiki
      clone; drafts published manually.
- [x] 4.3 Write a fresh 0.2 reddit feature-announcement draft (headline: Notebook + Clockmaker's
      Notebook) in `docs/media/reddit-announcement-0.2.md`, reusing the existing draft's voice.
- [x] 4.4 Update `docs/media/video-script.md` (0.2 outro + three new beats) and add a shot-list +
      demo-seed cheat sheet keyed to features and the `/scribe seed` command each shot needs.
- [ ] 4.5 Capture screenshots into `docs/media/screenshots/0.2/` using the seed command.

## 5. Release mechanics

- [x] 5.1 Bump `src/Mod/modinfo.json` version to 0.2.0.
- [x] 5.2 Add a `[0.2.0]` CHANGELOG entry (Notebook recipe, handbook entries, Clockmaker history
      fix, dev seed command).
- [x] 5.3 Add a 0.2.0 release-tracking section (extend `RELEASE.md` or a dedicated doc).
- [x] 5.4 Consistency check: modinfo, CHANGELOG, mod page, wiki drafts, and video script all state
      0.2.0 and LibGUI 3.1.0. Verified 2026-07-31: modinfo `0.2.0`, CHANGELOG `[0.2.0]`, and all
      launch surfaces (`mod-page.txt`, `mod-page.html`, `mod-page-inline.html`, wiki drafts, video
      script) state 0.2.0 + LibGUI 3.1.0 — zero stale `2.0.0` refs remain in `docs/media/`.

## 6. Validation

- [x] 6.1 Run `openspec validate --change scribe-0-2-0-release-content` and resolve any issues.
      (CLI flag is positional: `openspec validate scribe-0-2-0-release-content` → "is valid".)
- [x] 6.2 Cross-check the in-game test items against `what-to-test` / TESTING.md and record verdicts.
      Added the four remaining in-game-testable items (1.4, 1b.4, 2.4, 3.9) to `TESTING.md` under a new
      `scribe-0-2-0-release-content` group with computed fingerprints. Left UNCHECKED (no verdict): the
      staged build is stale (0.2.0 Mod changes uncommitted/unstaged), so none can be truthfully confirmed
      until a restage + play session — flagged in the group's note.
