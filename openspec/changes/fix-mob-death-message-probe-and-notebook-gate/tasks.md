## 1. Shared, safe pool-size discovery

- [x] 1.1 Add a shared, lazily-cached pool-size helper on the `ScribeModSystem` partial class that
      discovers the `scribe-mob-death-N` count via
      `Vintagestory.API.Config.Lang.HasTranslation(key, findWildcarded: false, logErrors: false)`
      instead of formatting the template with zero arguments. Verify by inspection that the helper
      never calls `Lang.Get`/`Format` on a key it hasn't confirmed exists.
- [x] 1.2 Point `BuildDeathMessage` (`ScribeModSystem.History.cs`) at the shared helper, removing its
      own zero-arg probe loop. Verify the method's existing signature and return behavior are
      unchanged (same random selection, same 2-arg `Lang.Get` call for the final line).
- [x] 1.3 Point `SeedMobDeathMessage` (`ScribeModSystem.DevTools.cs`) at the same shared helper,
      removing its independent copy of the probe loop. Verify the dev-content seeder still produces
      a seeded mob-death line for a sample creature code.

## 2. Notebook-presence gating in OnEntityDeath

- [x] 2.1 In `OnEntityDeath`, materialize the victim's carried-notebook list (and, when a PvP killer
      is resolved, the killer's) before any Death/PvP message is built. Verify by reading the
      updated method that message construction only proceeds when at least one of these lists is
      non-empty.
- [x] 2.2 Reuse those materialized lists for the existing write loops (replacing the second,
      redundant `FindAllCarriedNotebookRecords(sp)` walk at write time). Verify no behavior change
      for the already-covered scenarios in `notebook-history`'s spec (Death/PvpKill recorded on
      every carried notebook, including CarryOn-carried ones).

## 3. Regression coverage

- [x] 3.1 Add an Atlas integration test (`tests/Integration.Tests`, `AtlasScenarioBase`) that kills a
      notebook-carrying player with a creature and asserts: (a) the recorded Death entry's Detail is
      a non-empty, fully-substituted `scribe-mob-death-N` line naming the creature, and (b) no
      translation-format warning is logged during the death. Verify the test fails against the
      pre-fix code path and passes after the fix (e.g. by temporarily reverting 1.1-1.2 locally to
      confirm the failure mode).
- [x] 3.2 Add an Atlas integration test asserting a player with no Notebook anywhere on their person
      dies to a creature and no Death entry is written to any notebook (extends the existing
      "Death without notebook records nothing" spec scenario with the added "no message was even
      constructed" angle, verified via the absence of the warning log line from 3.1's assertion).
- [x] 3.3 Add an Atlas integration test covering the two mixed-notebook PvP scenarios from the
      updated `notebook-history` spec: only-killer-carries-a-notebook, and only-victim-carries-a-
      notebook. Verify each writes exactly the entry the spec says it should (PvpKill-only or
      Death-only) and neither throws nor logs a translation warning.

## 4. Verification

- [x] 4.1 Run the full Atlas suite locally (per `build/install-hooks.sh`'s pre-push gate) and
      confirm all new and existing tests pass, with `VINTAGE_STORY` pointed at the local install.
- [x] 4.2 Manually play-test in-game: die to at least 3 different creature types while carrying a
      Notebook, confirm the History tab still shows varied, correctly-substituted flavor lines (not
      just the vanilla fallback), and confirm the server log shows no
      "Translation string format exception" warnings during any of the deaths.
