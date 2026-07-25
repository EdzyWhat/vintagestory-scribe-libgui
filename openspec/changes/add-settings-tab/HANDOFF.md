# add-settings-tab — handoff (round 1 follow-ups implemented, awaiting playtest)

## Where things stand
The `add-settings-tab` change is fully implemented, including the playtest **round-1 follow-ups**.
Everything below is **uncommitted** on branch `add-pinned-task-foundation` (see `git status`). Build is
clean, Core tests pass (102), `openspec validate add-settings-tab` passes, and the Debug build is
**staged** to the game's Mods folder.

Original section-7 in-game items were confirmed in playtest (2026-07-25). Round-1 items (`9.1`–`9.6` in
`openspec/changes/add-settings-tab/tasks.md`, mirrored in root `TESTING.md`) are **implemented but not yet
playtested**.

## What round 1 added (see design.md D7/D8/D9)
- Deferred-send HUD completion: 1.5s undoable window (`PinHudWaitMs`); unpin/delete fade text, sink mutes.
- New `ScribeCompletionPolicy.Keep` (=3): completes in place, no sink (server treats like Sink).
- Settings form: sliders → numeric fields (font as %, row width, max rows); offsets ±300 relative to the
  anchor's pre-baked offset; form text + checkboxes live-rescale with window font size; HUD checkbox
  scales with HUD font size.
- Chrome: "Scribe Settings" window title in settings view; filled gear icon; multiplayer Back-loses-lock
  falls back to read view.
- Deferred to its OWN future change (do NOT build here): custom SVG check+box with toggle animation, and a
  real row-reorder (sink) animation — LibGUI's pixel-offset AnimatedSlide can't do the Column reorder.

## How the user starts testing/validation
1. **Fully quit and relaunch the Vintage Story client** (assets/lang load once at boot — a world reload is
   NOT enough).
2. Open a lectern → click the filled gear (top of the central region) to reach Settings; the HUD gear
   (next to the collapse chevron) opens the standalone settings window.
3. Work the six round-1 items in `TESTING.md` under `## add-settings-tab` (codes `1b57beda`, `df9b1b06`,
   `27c0af03`, `ac377d10`, `52cfbc4e`, `a581fcab`). Submit results from the checklist app, or just report
   back.
4. Known rough edge to watch on `df9b1b06`: `NumericField` is uncontrolled/unclamped, so **typing** a value
   whose prefix is below the min can clamp mid-keystroke — the **+/- buttons are the clean path**. If the
   mid-type clamp feels bad, that's a candidate fix (special-case typed entry), not a bug in the plan.

## If a rebuild/restage is needed after any code edit
```
export VINTAGE_STORY="/Applications/Vintage Story.app"
dotnet build src/Mod/Mod.csproj          # or: dotnet test tests/Core.Tests/Core.Tests.csproj
bash build/restage.sh Debug              # then fully relaunch the client
```
Config-only note: an old `scribe-client-config.json` may still sit in ModConfig — it's intentionally
vestigial now (retired file), causes no error, and is fine to leave (that was test 7.6).

## Recording results / next steps
- Verdicts go in `TESTING.md` under each item (Confirmed/Still broken/Backlogged/Obsolete) via the
  `what-to-test` skill flow — the checkbox alone is not authoritative.
- Once round-1 items are confirmed, this change is ready to **commit** and then **archive**
  (`openspec-archive-change`). Nothing is committed yet — the user asked to review first.
