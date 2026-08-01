## Context

`add-settings-tab` (archived 2026-07-25) shipped the deferred-send HUD completion model: checking a
pinned row on the HUD records a `PendingCompletion { policy, expiry }` held for `PinHudWaitMs` (1500ms),
flips optimistically, and only sends `ScribeCompleteTaskMessage` when the window elapses (`OnTick`).
Unchecking inside the window is a true undo (nothing sent). Playtest confirmed all four policies work,
but flagged two behaviors that were knowingly deferred, plus settings-form layout polish.

Current mechanics (in `src/Mod/HudScribePins.cs`):
- **Fade**: `AnimatedOpacity` sets the text to a *static* `FadingOutOpacity` (0.15) for a
  destructive-pending (Unpin/Delete) row. Because the target is a single fixed value, it reads as an
  instant jump to ~15–40%, not a ramp — there is no time-varying opacity.
- **Order**: `SunkForOrder(pin)` returns true only when `DisplayedDone(pin)` is true AND policy is
  `Sink` AND the window has elapsed. `Build` partitions `!SunkForOrder` above `SunkForOrder`. Because
  the predicate reads live `DisplayedDone`, unchecking a sunk row makes it not-done → not-sunk → it
  jumps back to its prior slot. There is no durable "this completed, it belongs at the bottom now"
  state. The Core `ScribePinOrdering.ForDisplay` (pure, unit-tested) partitions purely on
  `LastKnownDone` and is the resting-order rule the HUD overlays its undo-awareness on.

The settings form (`src/Mod/ScribeSettingsContent.cs`) currently stacks every control in a single
`Column`, so paired numeric controls each take a full-width row and the collapse `Checkbox` stretches
edge-to-edge. HUD anchor labels come from the localized enum labels in `en.json`.

## Goals / Non-Goals

**Goals:**
- Unpin/Delete pending rows fade their text opacity *linearly* 100%→0% across the `PinHudWaitMs` window
  (a visible countdown), with the checkbox staying opaque/clickable for undo.
- A Sink/Keep completion reorders the row to the END of the pin list on window expiry, and it STAYS
  there even if the player later unchecks it — a durable, client-local resting position.
- Settings-form polish: two paired two-column rows, a label-hugging collapse checkbox, arrow-key
  numeric stepping, renamed mid-edge anchor labels, and a right-sized HUD gear.

**Non-Goals:**
- No change to server-authoritative completion, persistence format, or the network protocol — the
  durable sink position is a client-local HUD ordering concern, not synced state.
- No custom SVG checkbox / toggle animation (still its own future change).
- No slide/translation animation of the row across its reorder — LibGUI's implicit pixel-offset
  `AnimatedSlide` can't animate a `Column` reorder (a zero offset animates nothing), which is exactly
  why this was deferred. The reorder is a discrete re-partition; the existing mute-fade remains the
  settle cue.

## Decisions

**D1 — Time-varying fade via a per-frame opacity, not a static target.**
Replace the fixed `FadingOutOpacity` target with a value computed from the pending window's remaining
fraction each build/tick: `opacity = clamp01(remainingMs / PinHudWaitMs)` for a destructive-pending
row (1.0 at check → 0.0 at expiry). The HUD already runs `OnTick` at `TickIntervalMs` and rebuilds;
drive the opacity off `elapsedMs` so it ramps smoothly. *Alternative considered:* wrapping in an
`AnimatedOpacity` with a 1.5s curve triggered on state entry — rejected because the undo path must be
able to *cancel* the animation mid-ramp cleanly, and recomputing from the live window remaining is
simpler and already synchronized with the send/undo timing than managing an animation controller's
lifecycle across undo.

**D2 — Durable client-local "sunk" set, decoupled from done-state.**
Introduce a client-local set of pin identities `(DocId, TaskId)` that have completed-and-settled under
a Sink policy this session (call it `sunkOrder`, populated in `OnTick` when a `Sink` pending window
expires). `SunkForOrder` returns true if the pin is in that set — regardless of current
`DisplayedDone`. Unchecking a sunk row clears its optimistic-done and may re-send state to the server,
but does NOT remove it from `sunkOrder`, so it holds its bottom position. *Alternative considered:*
adding a `SunkAt` timestamp field to `ScribePinnedRef`/Core and persisting it — rejected as
over-reach: the resting order is a HUD presentation concern, the ref snapshot is server-synced state,
and persisting per-session sink order across relog wasn't requested. Keep Core's `ForDisplay` as the
pure resting rule and keep the session overlay in the Mod layer where the undo window already lives.

**D3 — "Stays at end even after uncheck" scope: session-local.**
The durable position lasts for the HUD's lifetime (session), not across relog. This matches how the
undo window and optimistic state already live only in the Mod layer, and avoids a persistence-format
change. If cross-session sink order is later wanted, it becomes its own change (it would need a synced
or client-persisted `SunkAt`).

**D4 — Settings two-column rows reuse LibGUI `Row` with `Expanded` children.**
Wrap each pair (max-rows + row-width; HUD text size + window text size) in a `Row` of two `Expanded`
cells so they split the width evenly, instead of two full-width `Column` entries. The collapse
`Checkbox` is placed in a `Row` with `MainAxisAlignment.Start` so it hugs its label rather than
stretching. This is pure layout, no behavior change.

**D5 — Arrow-key stepping in the numeric field.**
Handle Up/Down in the numeric field's key handler: Up = `value + step`, Down = `value - step`, each
clamped to the field's range (reusing the same clamp the +/- buttons use). This is additive to the
existing `NumericField`; keep the typed-entry clamp behavior unchanged (the +/- and now arrow keys are
the clean stepping path).

**D6 — Anchor label rename is lang-only.**
Rename the presented labels for the two mid-edge anchors in `en.json` (`Left` → `Mid-Left`,
`Right` → `Mid-Right`). The enum values and code keys are unchanged — only the displayed strings.

**D7 — HUD gear size is a render constant.**
The pinned-list HUD gear is drawn ~25% too large; reduce its size constant in `HudScribePins` to bring
it into proportion with the collapse chevron beside it. Pure sizing tweak.

## Risks / Trade-offs

- **[Fade ramp vs. rebuild cadence]** If opacity is only recomputed on the coarse `OnTick` interval,
  the ramp could look steppy. → Compute opacity from the live window-remaining fraction on every
  `Build`, and if the tick interval is too coarse for a smooth ramp, drive a lightweight per-frame
  rebuild only while a destructive window is active (bounded to the ≤1.5s window).
- **[Durable sink + uncheck confusion]** A sunk-then-unchecked row sitting at the bottom while showing
  as not-done could confuse ("why is my active task at the bottom?"). → This is the explicitly
  requested behavior; the resting-at-bottom is the intended signal that it was completed once. Verify
  it reads acceptably in playtest; the `sunkOrder` set is trivially clearable if the verdict is bad.
- **[Session-only durability]** Unchecking-stays-at-bottom is lost on relog (pin returns to done/undone
  partition order). → Acceptable per D3; called out so it isn't mistaken for a bug at retest.
- **[Two-column narrow window]** Paired controls could crowd at the smallest window font scale. →
  `Expanded` splits available width; if a numeric field's +/- buttons crowd, fall back to stacking
  that specific pair (revisit only if playtest shows crowding).

## Migration Plan

No data migration — no persisted format changes. Ships as a normal restage; fully relaunch the client
(lang label rename loads at boot). Rollback is a code revert; no on-disk state to unwind.

## Open Questions

- Should the linear fade also apply a matching mute-ramp to the Sink/Keep settle (currently a static
  `SunkOpacity` mute), for consistency? Leaning no (keep scope tight; the user only called out the
  destructive fade), but easy to add if the retest asks.
