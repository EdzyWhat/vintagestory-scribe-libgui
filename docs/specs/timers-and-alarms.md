# Timers & Alarms (Clockmaker feature, on the Notebook)

> Exploration/design spec (not an OpenSpec change, not implemented code). Input to a future
> `openspec-propose`. Follows `docs/specs/README.md`. Captures the 2026-07-26 feasibility +
> architecture exploration and four user scoping decisions. **Deferred to the v2 Notebook tier** —
> nothing user-facing ships before then; this file exists so the research and decisions survive
> until v2 is picked up.

## Summary

A player can set **timers and alarms** from the Notebook: real-time ("in 20 real minutes", a wall-clock
time) and in-game-time ("on day 42", "at hour 6:00", "in 3 in-game hours"). When one elapses it fires a
non-blocking alert (an on-screen toast + a short sound), and optionally shows a live countdown on the HUD
next to the pinned tasks. It is a **Clockmaker class perk** — the timer-capable notebook is gated to the
clockmaker's `tinkerer` trait — which fits the mod's tech-tree-progression theme (a clockmaker builds
timepieces) and the "reminders are a portability feature" framing of the held tiers.

Four settled decisions (user, 2026-07-26) define the scope:

1. **Surface = the Notebook (v2), not the Lectern.** The Lectern stays task/note-only. The timer page
   lives on the future leather notebook item, so this feature **hard-depends on the v2 tier**.
2. **Both real-time and in-game-time** alarms in the first cut.
3. **Client-local firing** — timer state is per-player **client JSON** (the `ScribePlayerSettings`
   model), polled by a single **client-side** listener. NOT server-authoritative like pins. Simpler,
   uses the player's own clock for real-time, single-machine (won't sync cross-device, won't fire while
   the game is closed — accepted trade-off for a personal reminder tool).
4. **Clockmaker-gated** via the `tinkerer` trait (see below).

Why this doesn't contradict `ROADMAP.md`'s "discipline reminder" (resist due-dates/priority as structured
`ScribeBlock` fields): that guidance is about **not bolting scheduling metadata onto tasks** in the core
document model. Timers here are a **separate, opt-in, class-gated feature with its own state** that may
*optionally reference* a task by id — tasks themselves stay plain text with no due-date field. The
document codec is untouched.

## VS API hooks

Confirmed by decompiling the installed `VintagestoryAPI.dll` / `VintagestoryLib.dll` /
`VSSurvivalMod.dll` (v1.22.x), 2026-07-26. The in-game-calendar convention ("store numeric
`TotalDays`/`TotalHours` in Core, format in Mod") is already established in **`VSAPI-NOTES.md` →
"Calendar, player events, per-player storage, and survival-mod systems"** — cited, not re-derived here.

### Reading time

- **In-game:** `api.World.Calendar` (`IGameCalendar`) — `TotalHours`/`TotalDays` (monotonic `double`,
  the alarm key), `HourOfDay` (wraps 0→24), `Year`/`DayOfYear`, and the world-configurable
  `CalendarSpeedMul`/`HoursPerDay`/`DaysPerMonth`. Default day = 48 real minutes, **but per-world
  configurable**, so "in N in-game hours" must be computed from live `TotalHours`, never a hardcoded
  minute count. `Calendar` is **null on the server until run stage `LoadGamePre`** (VSAPI-NOTES) — the
  client-side poller here is less exposed, but still guard `if (Calendar == null) return;`.
- **Real-time:** BCL `System.DateTime.UtcNow` (the mod targets `net10.0`; nothing blocks it — same
  precedent as the `HttpClient` note in VSAPI-NOTES). This is the **only** true wall-clock source.

### Scheduling — no native scheduler; poll

- **Confirmed absent:** `RegisterAlarm`/`OnCalendar`/calendar `Schedule` — there is no calendar-native
  scheduler. Alarms are built by polling, the same way vanilla `SystemTemporalStability` (temporal
  storms) and `BlockEntityFarmland` (crop growth) do — both store a `double` target
  (`nextStormTotalDays` / `totalHoursForNextStage`) and compare it in a periodic tick.
- **`IEventAPI.RegisterGameTickListener(Action<float>, int intervalMs)`** — the poller. The interval is
  the engine's in-world stopwatch (NOT scaled by game speed), but **it pauses when the game is paused**.
  Register **one** listener (~1000 ms) that walks the whole timer list — not one per timer.
- **Comparison rule:** always `target <= Calendar.TotalHours` (in-game) or `target <= DateTime.UtcNow`
  (real-time) against a **monotonic** stored value. Never compare the wrapping `HourOfDay`, never use
  equality — a coarse poll or a sleep/time-skip must not step over the target.
- **Real-time + pause:** because the tick listener freezes on pause, do NOT accumulate elapsed `dt` for a
  wall-clock timer. Store the **absolute** `DateTime` target so the OS-clock comparison fires correctly
  after an unpause/alt-tab.

### Firing an alert (all client-side)

- **`ICoreClientAPI.TriggerIngameError(object sender, string code, string text)`** — the vibrating
  on-screen notice; the visible "alarm!" toast. `TriggerIngameDiscovery(...)` is the calmer-styled
  sibling. Feed a `Lang.Get(...)` string.
- **`capi.World.PlaySoundAt(AssetLocation, Entity atEntity, ...)`** at the player's own entity — a short
  audible ding.

### Clockmaker class gate

- **Gate on the `tinkerer` trait, not the class string.** `characterclasses.json` (clockmaker, code
  `"clockmaker"`, traits include `tinkerer`) + `traits.json` (`tinkerer` = a `positive` trait with empty
  `attributes: {}` — a pure marker, unique to the clockmaker in vanilla, exactly meant for gating).
  Trait-gating is the engine-idiomatic path and is robust to class-def changes / mods granting `tinkerer`
  via `extraTraits`; a hardcoded `== "clockmaker"` is not.
- **Crafting gate = pure JSON, zero code:** `requiresTrait: "tinkerer"` on the timer-notebook's grid
  recipe. Enforced by `CharacterSystem` via `Event.MatchesGridRecipe`/`MatchesRecipe`
  (`IRecipeBase.RequiresTrait`). Precedent: `recipes/grid/sewingkit.json` → `requiresTrait: "clothier"`.
  Non-tinkerers can't craft it (fails silently — no crafting-path toast).
- **Use gate = optional code** (clean feedback + covers trade/admin-give/class-change): in
  `OnHeldInteractStart`, `api.ModLoader.GetModSystem<CharacterSystem>().HasTrait(player, "tinkerer")`; if
  false → `TriggerIngameError(this, "requirestrait", Lang.Get("scribe:requires-clockmaker"))` (guard
  `World.Side == Client`) + `handling = EnumHandHandling.PreventDefault` (server) to block. Direct class
  read if ever needed: `player.Entity.WatchedAttributes.GetString("characterClass", null)`.

## C# data structures

Respect the `src/Core` (NO VS API) vs `src/Mod` split. **Core holds durations/deadlines as plain
numbers; all game/real-time reading lives in Mod** — precedent: `ScribePinnedRef.PinnedAtTotalHours` is a
plain `double` the Mod layer feeds from `Calendar.TotalHours`.

**Core (`src/Core/`), game-agnostic:**

```
enum ScribeTimerKind { RealTime, GameTime }          // which clock the target is measured against

sealed record ScribeTimer(
    Guid Id,
    ScribeTimerKind Kind,
    double Target,        // GameTime: absolute Calendar.TotalHours. RealTime: DateTimeOffset.UtcTicks
                          //           (or Unix ms) as a plain number — never a VS/DateTime type in Core.
    string Label,         // free text ("check the kiln")
    bool Recurring,       // repeat (e.g. every in-game 06:00) — see open question
    double? RecurEvery,   // interval in the same unit as Target, when Recurring
    Guid? DocId,          // optional: alarm tied to a task's document
    Guid? TaskId);        // optional: the task within it

// A small ScribeTimerList + a Normalized()/clamp discipline mirroring ScribePlayerSettings:
//   clamp Label length, drop malformed/expired-recurring, cap count.
```

**Mod (`src/Mod/`), the adapter:**
- Persist the list to a client config (`scribe-timers.json`) via `api.LoadModConfig`/`StoreModConfig`,
  exactly as `ScribePlayerSettings` persists `scribe-hud-config.json`.
- A single client-side poller registered in `ScribeModSystem.StartClientSide`:
  `capi.Event.RegisterGameTickListener(OnTimerTick, 1000)`. `OnTimerTick` reads
  `capi.World.Calendar.TotalHours` and `DateTime.UtcNow`, fires+removes/reschedules any elapsed timer,
  persists on change, and raises a `MyTimersChanged` event (mirror `MyPinsChanged`) so the HUD/notebook
  page rebuild. Catch-up: on load, fire (or coalesce into one "N reminders elapsed" notice) any timer
  whose stored target already passed.

## Implementation spec

Ordering (all inside the v2 Notebook change, or a sub-change that lands with it):

1. **Core model + tests.** `ScribeTimer`, `ScribeTimerKind`, list container, `Normalized()`/clamps.
   Unit-test the pure logic: relative→absolute target math, `>=` firing, recurring reschedule, catch-up
   selection — no game install needed (the whole point of Core).
2. **Client persistence + poller** (`ScribeModSystem`): load/store `scribe-timers.json`; one
   `RegisterGameTickListener`; `MyTimersChanged`; fire via `TriggerIngameError` + `PlaySoundAt`; catch-up
   on load. Guard `Calendar == null`.
3. **Notebook timers page.** Reuse the Lectern's view-switch pattern (`GuiDialogScribeLecternLibGui`:
   promote the `bool isEditorMode` to a small view enum, `BuildCentralRegion()` switch; the **stubbed
   `scribepin` nav slot** shows exactly where a new page hooks in) — but on the **v2 notebook dialog**,
   not the lectern. Add a `scribeclock` SVG icon registered in `ScribeModSystem.RegisterCustomIcons`.
   A create-timer form (kind toggle, target picker, label) + a list of active timers. A live countdown
   label MUST use the **`ScribeFadeText` self-ticking `StatefulWidget` precedent** (`HudScribePins.cs`) so
   it repaints across `ForceRebuild` without the host rebuilding each frame.
4. **Optional HUD-alongside-pins.** Extend `HudPinsContent` (`HudScribePins.cs`) with a timers list + a
   second row-block after a `Divider`; feed from a `modSystem.MyTimers` cache; gate behind a
   `bool ShowTimersOnHud` preference (a no-clamp toggle like `PixelArtDisplay`, added via the
   `ScribePlayerSettings` + `ScribeSettingsContent` pattern). Same self-ticking-widget caveat for
   countdowns.
5. **Clockmaker gate.** `requiresTrait: "tinkerer"` on the timer-notebook recipe (JSON). Optional
   use-gate in the notebook item's `OnHeldInteractStart` with a `scribe:requires-clockmaker` lang string.

## Dependencies & sequencing

- **Hard dependency: the v2 Notebook tier** (`docs/specs/v2-notebook.md`) — the surface. No timer UI can
  ship before the notebook item + held-item GUI exist.
- The **state layer + client poller (steps 1–2) could be built earlier** than v2 (they're self-contained
  and don't need the notebook), but they ship **no user-facing feature** until a surface exists, so there's
  little value in landing them ahead of v2 unless a non-notebook surface is wanted.
- Fits the mod's **portability** theme (v5 backpack/HUD is the other "tasks that follow you" tier); the
  HUD-alongside-pins piece (step 4) overlaps v5's pinned-task HUD and should be sequenced with it.
- No new mod dependencies. `CharacterSystem` is part of VSSurvivalMod (vanilla), reached via
  `GetModSystem<CharacterSystem>()` — degrade gracefully (fail-open, matching `HasTrait`'s own null-class
  behavior) if it's somehow absent.

## Open questions

- **Notebook-in-hand vs always-on firing.** Recommend the client poller fires **regardless of whether the
  notebook is currently held** — it's a player-level mod-system poller, and the item only gates *creating*
  timers, not their running. (This is precisely why the "stops when you walk away from the lectern" fear
  does not apply: a client mod-system poller is not tied to any block/item being loaded.) Confirm at
  v2-scoping.
- **Clockmaker-gate scope fork.** Does gating apply to (a) ALL notebooks (only clockmakers get a notebook
  at all) or (b) a distinct *timer-capable* notebook variant, with a plain notebook for everyone?
  **Recommend (b)** — a plain leather notebook is the v2 core tier for all players; timers are a
  clockmaker perk (either a separate "chronometer's notebook" recipe, or the timer page unlocked on the
  standard notebook only for `tinkerer`s via the use-gate). Avoids walling off v2's core note-taking
  behind a class. Decide with the user at v2 scoping.
- **Recurring vs one-shot**, and **snooze/dismiss** UX on a fired alarm — first cut could be one-shot only.
- **Real-time clock ownership in multiplayer** — client-local means the player's machine clock, which is
  what a personal "remind me" means; no server involvement needed. Confirm no world/shared-alarm use case
  is wanted (that would need the server-authoritative model instead).
