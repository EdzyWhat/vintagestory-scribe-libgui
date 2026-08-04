## Why

The Notebook and Clockmaker's Notebook are fully built in the mod, but 0.2.0 cannot ship
truthfully until the *content and launch material* around them exist: the plain Notebook has no
crafting recipe (it is creative-only today, yet it is a required ingredient of the Clockmaker's
Notebook recipe), neither notebook item has an in-game handbook entry, and every external
surface (mod page, GitHub wiki, reddit, video script) still describes the v0.1 Lectern-only
release. We also have no way to produce believable demo content for screenshots/video — the
append-only History and Guestbook logs in particular cannot be hand-authored in a saved world.

## What Changes

- **Add the missing plain-Notebook grid recipe** (`scribe:scribenotebook`) from a paper + leather
  writing set, matching the existing Lectern recipe's ingredient vocabulary; review the existing
  Lectern and Clockmaker's Notebook recipes for balance while we are here.
- **Add in-game handbook coverage** for both notebook items (`handbook` `extraSections`) and
  refresh the Lectern handbook sections + the two guide pages so Scribe reads coherently as a
  whole now that the notebook exists.
- **Add a dev/creative-gated `/scribe seed` command** that populates realistic demo content —
  ≥12 tasks (mixed done/undone), a few note sections, a varied notebook History spread, and
  fictional Lectern Guestbook visitors — persisted server-authoritatively for screenshot/video
  capture. History seeds only the Notebook; Guestbook seeds only the Lectern (they are hosted
  asymmetrically).
- **Gate the Clockmaker's Notebook craft behind the `tinkerer` trait** (granted by the vanilla
  `clockmaker` character class) using the recipe's native `requiresTrait` field — data-only, enforced
  by the game's own `CharacterSystem`. Add a server-side worldconfig toggle (default: requirement ON)
  that, when disabled, clears `requiresTrait` at server startup so anyone can craft it.
- **Fix a latent bug**: live history recorders (deaths, temporal storms, boss kills) never record
  into a held Clockmaker's Notebook because inventory detection matches only `ItemScribeNotebook`.
  Widen detection to include `ItemClockmakerNotebook`.
- **Refresh all launch material in-repo**: update `docs/media/mod-page.txt` (fix stale LibGUI
  2.0.0 → 3.1.0, add the notebook/clockmaker section, bump the roadmap), draft refreshed/expanded
  GitHub wiki pages under `docs/media/wiki/`, write a fresh 0.2 reddit feature-announcement,
  update the video script, and add a light screenshot/video shot-list keyed to the demo seeds.
- **Release mechanics**: bump `src/Mod/modinfo.json` to 0.2.0, add a `[0.2.0]` CHANGELOG entry,
  and add a 0.2.0 release-tracking section.

## Capabilities

### New Capabilities
- `notebook-crafting`: a survival grid recipe that produces one `scribe:scribenotebook` from a
  paper + leather writing set, authored as a data-only asset (no C# registration), while keeping
  the Notebook available in creative.
- `item-handbook-entries`: in-game handbook coverage for the Notebook and Clockmaker's Notebook
  (item `extraSections`) plus refreshed Lectern sections and guide pages, so every Scribe item
  and the mod-wide guides describe current behavior.
- `dev-content-seeding`: a dev/creative-gated command that seeds a target Notebook or looked-at
  Lectern with sample tasks, notes, and — for capture of otherwise un-fakeable logs — programmatic
  History (notebook) and Guestbook (lectern) entries, persisted through the normal
  server-authoritative flow.
- `clockmaker-trait-gating`: the Clockmaker's Notebook recipe requires the `tinkerer` trait
  (data-only `requiresTrait`), with a server worldconfig toggle to disable the requirement
  world-wide (clears `requiresTrait` at startup).

### Modified Capabilities
- `notebook-item`: history events SHALL record into a held Clockmaker's Notebook as well as the
  plain Notebook (inventory detection widened to both sibling item classes).

## Impact

- **Assets (data-only):** new `recipes/grid/scribenotebook.json`; `requiresTrait: "tinkerer"` added
  to `recipes/grid/scribeclockmakernotebook.json` (also corrected to a single 3-ingredient recipe and
  a valid `game:metal-parts` block code); possible balance tweaks to `recipes/grid/scribelectern.json`; `handbook` blocks in
  `itemtypes/{scribenotebook,scribeclockmakernotebook}.json`; refreshed
  `config/handbook/{00-getting-started,01-editor-reference}.json`; new lang keys in `lang/en.json`.
- **Code (src/Mod):** new server-side `/scribe seed` command registered in `StartServerSide`
  (`ScribeModSystem.cs`); `NotebookHost.Flush()` made public; widened notebook detection in
  `FindNotebookInInventory`; new server-only `BlockEntityScribeLectern.SeedGuestbook(...)`; a
  server-startup worldconfig read that clears `RequiresTrait` on the Clockmaker's Notebook recipe(s)
  when the bypass toggle is on. No new network message types; no `src/Core` API dependency added
  (Core seeding uses existing `AddTask`/`AddTextSection`/`TryAddEntry`).
- **Docs/media:** `docs/media/mod-page.txt`, `docs/media/wiki/*` (new drafts), a new 0.2 reddit
  post, `docs/media/video-script.md`, `docs/media/screenshots/0.2/` (capture target).
- **Release:** `src/Mod/modinfo.json` version, `CHANGELOG.md`, release tracker.
- **Dependencies:** none added; vanilla `VintagestoryAPI` only. Deps stay `game 1.22.x`,
  `gui 3.1.0`.
