# Tasks — add-tablet-state-dev-command

## 1. Shared swap seam on ItemScribeTablet (D2)

- [x] 1.1 Add `public static ItemStack? BuildStateVariant(ItemStack current, TabletState target,
      IWorldAccessor world)` to `ItemScribeTablet`: resolve the base material via `ResolveMaterialState`,
      map `target` → sibling variant suffix (`Wet` → bare `clay-<color>`/`wax`; `Hard` →
      `clay-<color>-hard`; `Fired` → `clay-<color>-fired`), `world.GetItem(CodeWithVariant("material", …))`,
      and `CarryStackData` the document + history across. Return `null` when the sibling variant does not
      exist (wax `Hard`/`Fired`, or an unregistered variant).
      — DONE: `BuildStateVariant` added; state-agnostic (permanence rule stays in `Soften`'s guard),
      returns null on a missing sibling (the wax-cannot case).
- [x] 1.2 Refactor `Soften` to call `BuildStateVariant(stack, TabletState.Wet, world)` (keeping its
      hard-only guard), so the natural and dev paths share one swap+carry implementation. Confirm
      `OnTransitionNow`/`DoSmelt` still pass (they build the sibling via the engine, then `CarryStackData`;
      leave them unless the seam cleanly subsumes them without behavior change).
      — DONE: `Soften` now guards hard-only then delegates to the seam; `OnTransitionNow`/`DoSmelt` left as-is
      (engine builds their sibling, so the seam does not subsume them without behavior change).

## 2. The `/scribe tablet <state>` sub-command (D1, D3, D4, D5)

- [x] 2.1 In `ScribeModSystem.DevTools.cs`, add a `BeginSubCommand("tablet")` under the existing
      `/scribe` root with `parsers.WordRange("state", "wet", "hard", "fired")` and a `HandleWith`
      handler. Keep the root's `controlserver` privilege; check creative mode in-handler (mirror
      `OnSeedCommand`).
      — DONE: `tablet` subcommand chained onto the existing `/scribe` builder next to `seed`; `OnTabletCommand`
      mirrors `OnSeedCommand`'s server/player/creative gates.
- [x] 2.2 Resolve the calling player's held Scribe Tablet (active hotbar slot, else scan hands); clean
      "no held tablet" error if none (D4).
      — DONE: `FindHeldTablet` checks `ActiveHotbarSlot` then the offhand (`Entity.LeftHandItemSlot`);
      `cmd-tablet-no-tablet` on miss.
- [x] 2.3 Map `wet`/`hard`/`fired` → `TabletState`; call `BuildStateVariant`. On `null`, report the
      material can't reach that state (wax hard/fired). On success, write `slot.Itemstack = swapped;
      slot.MarkDirty()` (server-authoritative) and report the resulting state.
      — DONE: exactly this; `null` → `cmd-tablet-wax-cannot`, success writes the slot + `MarkDirty`.
- [x] 2.4 For a `fired` source going to `wet`/`hard`, append a "(testing override)" note to the success
      message (D3) so the deliberate bypass of the permanent-fired rule is visible.
      — DONE: `wasFired` captured pre-swap; a fired→non-fired result reports `cmd-tablet-set-override`.

## 3. Strings

- [x] 3.1 Add the command's `WithDescription` text and result/error `Lang` strings to
      `src/Mod/assets/scribe/lang/en.json` (usage line, success, wax-cannot, no-tablet, override note),
      consistent with the `seed` sub-command's wording.
      — DONE: five `cmd-tablet-*` keys in `en.json` (creative-only, no-tablet, wax-cannot, set, set-override).
      The `WithDescription` usage line stays inline English, matching `seed` exactly.

## 4. Build + verify

- [x] 4.1 `dotnet build src/Mod/Mod.csproj -c Debug` clean (0 warnings / 0 errors).
      — DONE: Build succeeded, 0 Warning(s) / 0 Error(s).
- [x] 4.2 `dotnet test tests/Core.Tests/Core.Tests.csproj` green (no Core change expected).
      — DONE: no Core change (Mod-only). 7 pre-existing failures in `ScribeBrightnessCurveTests` /
      `ScribePlayerSettingsTests` (illumination-floor/curve drift) are UNRELATED — flagged in
      `fix-recipe-variant-identity` 5.2; the 474 other tests pass.
- [x] 4.3 Restage (Debug) — never while the client is running.
      — DONE: client confirmed not running; `build/restage.sh Debug` staged 138 files.

## 5. In-game verification (playtest gate)

- [x] 5.1 Creative mode: hold a wet `clay-red` tablet with a written document, run `/scribe tablet fired`
      → the held tablet becomes read-only fired, document + history intact, success reported.
  - Confirmed 2026-08-21 (playtest verdict on file in TESTING.md).
- [x] 5.2 Run `/scribe tablet hard` then `/scribe tablet wet` on the same tablet → transitions apply and
      the document survives each swap.
  - Confirmed 2026-08-21 (playtest verdict on file in TESTING.md).
- [x] 5.3 Hold a `wax` tablet, run `/scribe tablet hard` → clean "wax never hardens" error, no swap.
  - Confirmed 2026-08-21 (playtest verdict on file in TESTING.md).
- [x] 5.4 On a fired tablet run `/scribe tablet wet` → resets to wet with the "(testing override)" note.
  - Confirmed 2026-08-21 (playtest verdict on file in TESTING.md).
- [x] 5.5 Not holding a tablet → "no held tablet" error; in survival mode → refused by the creative gate.
  - Confirmed 2026-08-21 (playtest verdict on file in TESTING.md).
- [x] 5.6 Re-run `TESTING.md` `00000016` (fired/hardened cuneiform readability) now that the state is
      reachable; record the verdict there.
      — Done: re-run via `/scribe tablet fired|hard` (submission 2026-08-20T21-32-19) and the verdict recorded
      on `00000016` — outcome was Still broken (the wet-tuned dark glow hurts legibility on the darker
      fired/hardened backdrops). The FIX for that legibility is now owned by `add-tablet-state-glow-modifier`;
      this task's obligation (re-run + record) is complete.
