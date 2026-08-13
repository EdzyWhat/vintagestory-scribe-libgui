## Why

The pinned-task HUD and the Notebook Pin Tab are meant to be two views of one thing — the
player's pins — but their row order drifts apart. Pin across several documents, or across
sessions, and the two surfaces disagree about where a pin sits. This is confusing precisely
where the mod is supposed to be reassuring ("these are my goals, in this order").

The drift is not two data sources. Both surfaces already read the **same** server-authoritative,
per-player pin list (`ScribeModSystem.MyPins`), whose order is already persisted (savegame key
`scribe:pins:v1` and per-player network push) and already writable (drag-reorder on the Pin Tab
permutes it). The single divergence is a **client-local, session-only display overlay on the HUD**
(`sunkOrder` / `SunkForOrder` in `HudScribePins.cs`) that the Pin Tab has no equivalent of: it
holds a completed pin at the bottom for the rest of the session even after it is un-completed.
Because that overlay is recomputed per-client and never shared, the two surfaces compute
different orders. This overlay is also the exact source of the ordering friction that the
`40be9d31` cross-surface Sink work had to fight to a draw.

## What Changes

- **Remove the HUD's session-only sink overlay.** Delete `sunkOrder`, `SunkForOrder`, and the
  render-time re-partition in `HudScribePins.BuildOrderedRows` that applies it. The HUD then
  orders pins with exactly the rule the Pin Tab already uses — `ScribePinOrdering.ForDisplay(MyPins)`
  gated on the Sink/UnpinSink policy — so **both surfaces render the one persisted per-player order
  identically, agreeing by construction** rather than by two independently-maintained derivations.
- **Un-completing a sunk pin returns it to its prior position** (a deliberate behavior change).
  `ForDisplay` is a stable partition: not-done pins keep their persisted order, done pins fall to
  the bottom keeping theirs. A Sink completion only sets the pin's persisted `LastKnownDone`; it
  never permutes the pin list. So once the overlay is gone, un-checking a pin flips `LastKnownDone`
  back and the stable partition returns the pin to its exact persisted slot — no bookkeeping needed.
  This **reverses** the earlier `scribe-settings-followups` "sunk stays sunk for the session even
  after un-check" decision (which now survives only in code — the live spec requirement was already
  reworded to drop it).
- **The in-undo-window "hold in place" behavior is preserved unchanged.** The HUD defers its network
  send for the undo window, so during the window the server's `LastKnownDone` stays false and
  `ForDisplay` naturally keeps the just-checked pin in the not-done group (in place) until the window
  expires — the same effect the overlay used to give, now falling out of the shared rule for free.
- **No change to:** new pins appending to the bottom, Pin Tab drag-reorder, the Sink *document*
  block-order reorder (a separate, shared, server-side reorder of the task within its document),
  persistence, sync, or the Core model. This is a view-layer removal.

## Capabilities

### New Capabilities
<!-- none — the single-source-of-truth pin list already exists; this change removes the divergence. -->

### Modified Capabilities
- `pinned-task-hud`: the HUD's automatic ordering requirement changes from "sink completed tasks
  (with a session-durable resting position)" to "render the shared per-player pin order using the
  same ordering rule as the Pin Tab, with no HUD-only session overlay"; un-completing a pin returns
  it to its prior position.
- `player-pins`: the HUD⇄Pinned-view agreement is strengthened from "agree on sink order" to "render
  one and the same persisted per-player order," and the HUD undo-window requirement's post-window
  behavior is clarified (un-completing after the window returns a pin to its prior position, not the
  bottom).

## Impact

- **Code:** `src/Mod/HudScribePins.cs` only — delete `sunkOrder` (field + population in `OnTick` +
  pruning), `SunkForOrder`, and the overlay re-partition in `BuildOrderedRows`; the HUD's base-order
  branch (Sink/UnpinSink → `ForDisplay`, else raw `MyPins`) becomes the whole ordering, matching the
  Pin Tab's `OrderedPinsForDisplay`.
- **No Core change:** `MyPins`, `LastKnownDone`, and `ScribePinOrdering.ForDisplay` already exist and
  are unit-tested; the ordering rule is unchanged.
- **No persistence/sync/network change.**
- **Invariant to protect:** must NOT regress the `40be9d31` cross-surface Sink agreement (Read / HUD /
  Pin Tab agree) confirmed 2026-08-11 — removing the overlay should *strengthen* it, but it is the
  primary regression to re-verify.
- **Primary risk to verify (code + in-game):** the in-undo-window "hold in place" behavior surviving
  the overlay removal (hypothesis above); and the reversed un-complete-returns-to-prior-position
  behavior being the intended feel.
- **Docs:** TESTING.md gets the parity/behavior checklist; `VSAPI-NOTES.md` / memory note the reversal
  of the session-durable-sink decision.
