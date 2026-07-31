## Context

The Clockmaker's Notebook timer is player-owned server state (`TimerStore`, keyed by player UID,
persisted in the savegame under `scribe:timer:v1`). It counts down on a 1 Hz server tick
(`ScribeModSystem.OnTimerTick`). When it hits zero it flips to `Fired`; the server then counts
`timerFiredElapsed[uid]` up and, at ≥30 s, removes the store and pushes an empty state to the client —
which is what makes the fired HUD row disappear (`ScribeModSystem.cs:1277-1297`).

Two facts constrain the design:

1. **`ScribePlayerSettings` is client-local and never server-synced** (see its class doc). The 30 s
   auto-clear currently lives on the server, which has no knowledge of a per-client preference. So the
   preference cannot simply be read inside `OnTimerTick`.
2. **Fired timers are deliberately NOT persisted today.** Both the save and load paths filter to
   `Running` only, with an explicit comment (`ScribeModSystem.cs:596-599`, `615-619`): a resurrected
   Fired timer would "restart the 30 s flash from zero" on rejoin — the "flashing on rejoin with no
   active timer" bug. The `timerFiredElapsed` counter that drives the flash is a separate un-persisted
   server-side dictionary, not part of `TimerStore`.

The confirmed product decisions: the preference is a **client-local bool** (`TimerAutoDisappear`,
default on); OFF keeps a fired timer up until the player clicks the HUD row or presses Stop (both
existing clear paths — no new dismissal trigger); and a fired-but-undismissed timer **persists across
relog**.

## Goals / Non-Goals

**Goals:**
- Make the 30 s auto-disappear a per-player, client-local preference that takes effect live, including
  for a timer that is already fired.
- Preserve today's exact behavior when the preference is on (default).
- Persist a fired timer across logout so an unacknowledged notification is not silently lost, resuming
  (not restarting) the auto-clear window.
- Keep the invariant that `src/Core/` never references the VS API, and add no new mod dependency.

**Non-Goals:**
- No new dismissal trigger (opening the notebook does not clear the timer).
- No server-side or world-config knowledge of the preference; not server-synced.
- No change to Running-timer countdown, the Idle→Running→Fired transitions, or the notebook Timer tab's
  set/stop controls beyond what auto-clear removal requires.
- No multi-timer support; still one timer slot per player.

## Decisions

### D1: Client drives the auto-clear; server stops auto-removing

Move the 30 s auto-clear off `OnTimerTick`. The server keeps decrementing Running timers and flipping
them to `Fired`, but **no longer removes a Fired store on its own**. Instead the **client** decides:
while its cached `MyTimer.Status == Fired`, the client accumulates fired-elapsed time on the existing
`TimerDisplayTick` (the shared 1 Hz client tick from commit 74320f9) and, once it passes the window,
sends the existing `ScribeClearTimerMessage` — but **only when `MySettings.TimerAutoDisappear` is on**.

- **Why:** the preference is client-local; the client is the only place that knows it. Reusing
  `ScribeClearTimerMessage` means no new packet type and the server stays a dumb authority (it already
  clears on that message from both the HUD Stop and the notebook Stop).
- **Live effect:** because the decision is re-evaluated every client tick against the current
  preference, turning the setting off mid-flash simply stops the client from ever sending the clear —
  the spec's "changing the preference affects the current fired timer live" scenario falls out for free.
- **Alternative considered — carry the preference to the server with the timer-set message** (like
  `CompletionPolicy` travels with the completion request). Rejected: it wouldn't affect an
  already-running/fired timer live, and it leaks a client display preference into server state for no
  benefit.
- **Alternative considered — keep auto-clear on the server, gated by a synced copy of the preference.**
  Rejected: violates the "never server-synced" invariant for `ScribePlayerSettings` and adds a sync
  path for a purely cosmetic client behavior.

### D2: Track fired-elapsed on the client

The client needs its own fired-elapsed accumulator (the server's `timerFiredElapsed` is no longer the
driver). Accumulate a client-side counter that resets when `MyTimer` transitions into `Fired` and ticks
up on `TimerDisplayTick`. The existing `_lastTimerStatus` transition detection in `HudScribePins`
(added in 74320f9) is the natural hook for the reset.

- Use one canonical window constant shared by client logic (and referenced by the spec's "~30 seconds").
  Introduce a named constant rather than the current bare `30.0` literal so the value has one home.

### D3: Persist Fired timers WITH their fired-elapsed, resuming the window

To satisfy "survives logout" and "window resumes rather than restarts", the save/load filter changes
from `Running`-only to `Running || Fired`, and the **fired-elapsed value must travel with the store** so
the resumed window is `30 − elapsed`, not a fresh 30.

The fired-elapsed is currently server-side (`timerFiredElapsed`). Two ways to persist it:

- **D3a (chosen): add `FiredElapsedSeconds` to `TimerStore` (codec v2).** Bump `TimerStore` to version 2
  per `docs/CODEC-MIGRATION.md`: append a `double FiredElapsedSeconds` after `RemainingSeconds`, keep
  `PriorVersion = 1` readable (a v1 blob deserializes with `FiredElapsedSeconds = 0`). The server writes
  `store.FiredElapsedSeconds` from its `timerFiredElapsed[uid]` at save time (or maintains it directly on
  the store). On load, a Fired store seeds both the client flash and — if we keep a server mirror — the
  server counter. This keeps all timer persistence in one versioned codec.
- **D3b (rejected): persist a parallel dictionary alongside the store blob.** Rejected: splits timer
  state across two serialized structures with no version discipline, exactly the kind of ad-hoc
  persistence `docs/CODEC-MIGRATION.md` and the Sign-pattern guardrail steer away from.

The original "flashing on rejoin with no active timer" bug (the reason Fired was dropped) is now
**desired behavior when the preference is off** and **correctly time-boxed when on** (the resumed window
means an on-preference player who was 25 s in clears ~5 s after rejoin instead of flashing a full 30 s).

### D4: The preference is a plain client-local bool

Add `public bool TimerAutoDisappear { get; set; } = true;` to `ScribePlayerSettings` — no clamp, so
`Normalized()` is untouched (mirrors `MuteUiSounds`). Absent JSON keys default to `true`, so existing
config files and existing players keep today's behavior with no migration. Surface it in
`ScribeSettingsContent.BuildModBehaviorSection` as a `HuggingCheckbox` with `settings-timerdisappear`
label + `-help` tooltip, written through the existing `UpdateMySettings` path.

- **Default polarity:** the control reads "Timer disappears" = on by default. This keeps the checkbox's
  checked state aligned with the current/expected behavior (checked = disappears), avoiding a
  double-negative ("don't keep timer").

### D5: Documentation

- **Tooltip (primary in-game doc):** new `settings-timerdisappear` / `settings-timerdisappear-help` lang
  keys — settings have no dedicated handbook page, only per-control tooltips.
- **Handbook:** revise `handbook-scribeclockmakernotebook-timer-text`, whose last line currently states
  the fired timer is cleared by click/Stop and omits auto-disappear. Mention the default 30 s
  disappearance and that it can be turned off in Scribe Settings.
- **GitHub wiki (out-of-repo, `EdzyWhat/vintagestory-scribe-libgui.wiki`):** add a "Timer disappears"
  entry under `Scribe-Settings.md` → Mod Behavior, and a sentence in `Items.md`'s Clockmaker's Notebook
  timer coverage. These are applied to the wiki repo, not this source tree.

## Risks / Trade-offs

- **[Client stops sending the clear → timer lingers unexpectedly]** If a client with the preference *on*
  disconnects the instant its timer fires, the server no longer auto-clears, so the fired timer persists
  until that player next logs in and its client sends the clear (or they dismiss it). → Acceptable and
  arguably better: the notification is preserved for the player who set it rather than silently expiring
  while they're away; the resumed window means it clears shortly after they return.
- **[Codec bump risk]** Getting the `TimerStore` v2 append wrong could drop timers. → Follow
  `docs/CODEC-MIGRATION.md` exactly: append-only, keep `PriorVersion = 1` accepted, unit-test round-trip
  of both v1 (no fired-elapsed) and v2 blobs in `tests/Core.Tests`.
- **[Two fired-elapsed counters could diverge]** If both a server `timerFiredElapsed` and a persisted
  `FiredElapsedSeconds` exist, they must agree. → Prefer a single source: let the store's
  `FiredElapsedSeconds` be the persisted truth and derive the flash from `MyTimer` on the client; the
  server only needs it to write the correct value at save time (it no longer times the auto-clear).
- **[Server never garbage-collects an abandoned fired timer]** With server auto-clear removed, a fired
  timer for a player who never returns stays in `timerStores`. → Same footprint as a Running timer today
  (one slot per player, already persisted); no unbounded growth.

## Migration Plan

- Additive, no data loss for existing saves: v1 timer blobs still load (as Running-only, exactly as
  today, since a v1 save never persisted Fired). New saves may contain Fired timers (v2).
- Rollback: reverting the code leaves a v2 blob on disk; the v1 reader rejects an unknown version and
  returns a default (Idle) store, so a downgrade harmlessly forgets an in-flight fired timer rather than
  crashing. Confirm this by testing a v2 blob against the pre-change `Deserialize`.
- No world-config, no server op action, no player action required. Existing players default to on.

## Open Questions

- Should the server keep a mirror of fired-elapsed at all, or persist it purely as a field the client
  reads on the next state push? (Leaning: store field is the truth; server sets it at save time, does not
  time anything.) Resolve during implementation once the client-tick accumulator is in place.
- Exact window constant home: a `const double` in the Mod layer vs. a Core constant on
  `ScribePlayerSettings`/`TimerStore`. (Leaning: Mod layer, since it's a client display timing, unless a
  Core home aids a unit test.)
