## 1. Crafting recipes (data-only)

- [ ] 1.1 Author `recipes/grid/scribenotebook.json` producing one `scribe:scribenotebook` from a
      paper + leather writing set (baseline `game:paper-parchment` + `game:leather-normal-plain`);
      finalize the exact grid arrangement and any binding item.
- [ ] 1.2 Add/confirm any lang keys needed for the recipe output display.
- [ ] 1.3 Review `recipes/grid/scribelectern.json` and `recipes/grid/scribeclockmakernotebook.json`
      for balance; adjust only if warranted and note any change.
- [ ] 1.4 Restage the mod (`build/restage.sh`) and confirm in-game: the Notebook shows a grid recipe
      in the handbook, is craftable in survival, and the full Notebook → Clockmaker's chain works.

## 2. In-game handbook (data-only)

- [ ] 2.1 Add a `handbook` block with `extraSections` to `itemtypes/scribenotebook.json` referencing
      new `scribe:` lang keys (what it does, editor, how to craft).
- [ ] 2.2 Add a `handbook` block with `extraSections` to `itemtypes/scribeclockmakernotebook.json`
      (function, the timer, crafting from a Notebook).
- [ ] 2.3 Refresh `handbook-scribelectern-*` sections and the two guide pages
      (`config/handbook/00-getting-started.json`, `01-editor-reference.json`) so mod-wide docs
      acknowledge the notebook; add/adjust lang keys in `lang/en.json` with working cross-links.
- [ ] 2.4 Restage and confirm in-game: both notebook items show the new handbook sections and the
      guide pages read coherently.

## 3. Demo-seeding command + Clockmaker history fix (code)

- [ ] 3.1 Make `NotebookHost.Flush()` public (match the existing public `FlushHistory()`).
- [ ] 3.2 Widen `FindNotebookInInventory` (`ScribeModSystem.cs:1257`) to match
      `ItemScribeNotebook or ItemClockmakerNotebook` so live history records into a held Clockmaker's
      Notebook; verify `NotebookHost` works unchanged for a clockmaker stack.
- [ ] 3.3 Add server-only `BlockEntityScribeLectern.SeedGuestbook(entries)` guarding
      `Api is ICoreServerAPI`, looping `TryAddEntry`/`TrySetNote`, then `MarkDirty()` (mirror
      `RecordVisitor`).
- [ ] 3.4 Add `FormatDateDaysAgo(sapi, daysAgo)` mirroring `NotebookHost.FormatDate` for varied
      in-game dates.
- [ ] 3.5 Register the server-side `/scribe seed <what> [target]` command in `StartServerSide`:
      `WordRange` args, `.RequiresPrivilege(Privilege.controlserver)`, `.RequiresPlayer()`, plus an
      in-handler creative-mode check.
- [ ] 3.6 Implement target resolution (`auto` → looked-at Lectern else held notebook) and the seed
      handler: ≥12 mixed-state tasks + a few note sections on the document; History spread on
      notebooks; Guestbook visitors (some with short notes) on lecterns; skip inapplicable content
      types and report them.
- [ ] 3.7 Persist + resync: `NotebookHost.Flush()` for notebooks; `MarkDirty(redrawOnClient:true)`
      for lecterns. No new network message types.
- [ ] 3.8 Add/confirm Core test coverage for the Clockmaker detection fix where feasible; build
      passes (`build/verify.sh`).
- [ ] 3.9 In a creative world, run the command against a Notebook and a Lectern; confirm tasks/notes,
      History (notebook), and Guestbook (lectern) populate, persist across save/reload, and sync to a
      second client.

## 4. Launch material (in-repo drafts)

- [ ] 4.1 Update `docs/media/mod-page.txt`: fix stale LibGUI 2.0.0 → 3.1.0, add the
      Notebook/Clockmaker's Notebook section, and bump the roadmap for 0.2.0.
- [ ] 4.2 Draft/refresh GitHub wiki pages under `docs/media/wiki/`: refresh the existing 7 Lectern
      pages and add coverage for the Notebook, Clockmaker's Notebook & Timers, History, and
      Guestbook.
- [ ] 4.3 Write a fresh 0.2 reddit feature-announcement draft (headline: Notebook + Clockmaker's
      Notebook) in `docs/media/`, reusing the existing draft's voice.
- [ ] 4.4 Update `docs/media/video-script.md` (0.2 outro + beats) and add a light screenshot/video
      shot-list keyed to features and the demo-seed each shot needs.
- [ ] 4.5 Capture screenshots into `docs/media/screenshots/0.2/` using the seed command.

## 5. Release mechanics

- [ ] 5.1 Bump `src/Mod/modinfo.json` version to 0.2.0.
- [ ] 5.2 Add a `[0.2.0]` CHANGELOG entry (Notebook recipe, handbook entries, Clockmaker history
      fix, dev seed command).
- [ ] 5.3 Add a 0.2.0 release-tracking section (extend `RELEASE.md` or a dedicated doc).
- [ ] 5.4 Consistency check: modinfo, CHANGELOG, mod page, wiki drafts, and video script all state
      0.2.0 and LibGUI 3.1.0.

## 6. Validation

- [ ] 6.1 Run `openspec validate --change scribe-0-2-0-release-content` and resolve any issues.
- [ ] 6.2 Cross-check the in-game test items against `what-to-test` / TESTING.md and record verdicts.
