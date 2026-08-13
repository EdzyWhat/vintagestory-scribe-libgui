## Context

Both pinned-task surfaces already read one shared source of truth:

- **Store:** `ScribePinStore` holds `Dictionary<playerUid, List<ScribePinnedRef>>` — per-player,
  server-authoritative (modeled on vanilla `WaypointMapLayer`). Order is list position.
- **Persistence:** `ScribePinCodec` writes/reads the list sequentially, so list position IS the
  persisted order, both to the savegame (`scribe:pins:v1`) and the per-player network push
  (`ScribePinnedSetMessage`). New pins append to the back (`ScribePinStore.SetPin` → `list.Add`).
  Drag-reorder permutes the list server-side (`ScribeReorderPinsMessage` → `ReorderPins`).
- **Client mirror:** `ScribeModSystem.MyPins` is that pushed list, order preserved.

The two surfaces derive their display order from `MyPins`:

- **Pin Tab** (`ScribeDialogBase.PinTab.cs:132` `OrderedPinsForDisplay`): under a sinking policy,
  `ScribePinOrdering.ForDisplay(MyPins)`; otherwise raw `MyPins`. No session overlay.
- **HUD** (`HudScribePins.cs:801` `BuildOrderedRows`): the *same* base branch, PLUS a HUD-only,
  client-local, session-only overlay — `sunkOrder` (`:108`, a `HashSet<(Guid,Guid)>` populated in
  `OnTick` `:570` when a Sink window expires, pruned to the live set each rebuild) applied via
  `SunkForOrder` (`:608`) as a second not-sunk-then-sunk partition (`:830`).

`ScribePinOrdering.ForDisplay` (Core, pure) is a **stable partition**: all `!LastKnownDone` pins in
input order, then all done pins in input order.

The verified crux (`ScribeModSystem.PinOperations.cs:73` `CompleteTaskForPlayer`): a Sink completion
flips only the pin's persisted `LastKnownDone` (`SetPinDone`) and reorders the *document's* block
order (`MoveTaskToBottomFromReader`). It **never permutes the `MyPins` list**. So pin-list order is
changed only by explicit drag-reorder — the done/not-done sink is a display-time partition over the
persisted list, not a rewrite of it.

The `sunkOrder` overlay is therefore the *only* thing that makes the HUD and Pin Tab disagree, and the
only thing that keeps an un-completed pin pinned to the bottom. Removing it makes the two surfaces
agree by construction and makes un-complete return-to-prior-position fall out for free.

## Goals / Non-Goals

**Goals:**
- The HUD and Pin Tab render one and the same per-player order, agreeing by construction (one shared
  ordering rule over one shared list, no surface-only overlay), across documents and sessions.
- Un-completing a sunk pin returns it to its prior (persisted) position.
- Preserve the in-undo-window "hold in place" feel and all animation behavior (fade, collapse, sink
  settle, re-pin revive).
- Remove code, not add a second sync path.

**Non-Goals:**
- No change to the Sink *document* block-order reorder (`MoveTaskToBottomFromReader`) — a separate,
  shared, server-side reorder of the task within its document; out of scope and untouched.
- No new persisted "sink order" or `SunkAt` field (the earlier `scribe-settings-followups` design
  explicitly rejected persisting sink order; this change makes the resting order a pure function of
  the already-persisted `LastKnownDone`, so nothing new is persisted).
- No Core model / codec / network / persistence change.
- No change to new-pin-appends-to-bottom or Pin Tab drag-reorder.

## Decisions

**D1 — Delete the HUD `sunkOrder` / `SunkForOrder` overlay; the base order becomes the whole order.**
`BuildOrderedRows` keeps its Sink/UnpinSink → `ForDisplay(MyPins)` else raw-`MyPins` base branch
(identical to the Pin Tab's `OrderedPinsForDisplay`) and drops the second `SunkForOrder` partition.
Delete the `sunkOrder` field, its population in `OnTick`, and its per-rebuild pruning.
*Alternative considered:* give the Pin Tab an equivalent `sunkOrder` overlay so both diverge
identically. Rejected — it keeps two derivations, has no cross-session durability, adds code, and
still can't survive relog. Removing the overlay is strictly simpler and gives the durable, synced
behavior the user asked for.

**D2 — Un-complete-returns-to-prior-position is achieved by the stable partition, with no new state.**
Because `ForDisplay` is stable and Sink never permutes `MyPins`, flipping `LastKnownDone` false again
re-lands the pin in its persisted slot among the not-done group. This deliberately reverses the
`scribe-settings-followups` 2.1/2.2 "sunk stays sunk for the session" decision. That decision no
longer has a live spec requirement (its sentence was reworded out of `player-pins` by the reconcile
work — verified: removed in commit `8fe7207`), so this is not contradicting a live SHALL; it is
realigning code with the current spec and adding a scenario that states the chosen behavior.

**D3 — The in-undo-window "hold in place" survives the overlay removal, for free.** The HUD defers the
network send for the undo window (`pendingCompletions` / `PinHudWaitMs`); it does not call
`CompleteTaskForPlayer` until the window expires. So during the window the server's `LastKnownDone`
stays false, `MyPins` is unchanged, and `ForDisplay` keeps the just-checked pin in the not-done group
— i.e. in place. The visual "settle toward sunk" during the window is the row's own animated feedback,
independent of list order. This is the primary assumption to confirm in code review + playtest.

**D4 — `awaitingRemoval` (post-send collapse) is unaffected.** It keys destructive completions leaving
the set at send-time and is orthogonal to `sunkOrder` (which only reordered kept pins). Sink is a kept
policy — it never enters `awaitingRemoval` — so removing the overlay changes nothing there.

## Risks / Trade-offs

- **[In-window hold breaks once `sunkOrder` is gone]** → D3 argues it holds because the send is
  deferred; verify by reading the `OnTick`/`pendingCompletions` send path and by the in-game gate
  (check a HUD pin under Sink → it must stay in place during the window, then settle to the bottom on
  expiry, exactly as before).
- **[Regress the `40be9d31` cross-surface Sink agreement]** → removing the sole divergent overlay
  should *strengthen* agreement, but it is the headline regression to re-verify: Read / HUD / Pin Tab
  must agree under Sink after completing a pinned task and after pin/unpin, across documents.
- **[Behavior-change surprise]** un-complete now moves a row back up rather than holding it at the
  bottom. This is the user's explicit choice (return to prior position). Trade-off accepted; noted for
  the changelog and TESTING.md so it isn't mistaken for a regression.
- **[Rapid multi-complete ordering]** with several Sink completions settling in quick succession, the
  resting order must still be the stable partition (done pins at the bottom in their pin-list order).
  Since order now comes purely from `ForDisplay`, this is deterministic; verify in-game that no row
  jumps or overlaps.

## Migration Plan

No data migration. `sunkOrder` was session-only client state — nothing persisted, nothing to migrate.
Deploy is a code-only view-layer change; rollback is reverting the commit. Existing pins load and
render identically except for the intended un-complete-returns-to-prior-position behavior.

## Open Questions

None blocking. The single load-bearing assumption (D3, in-window hold) is a code-verifiable +
playtest-verifiable claim rather than an open design question.
