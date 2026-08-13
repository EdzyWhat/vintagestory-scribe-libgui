# Tasks — sync-pinned-order-per-player

> View-layer only — no `src/Core/` model / codec / persistence / network change. The single
> per-player pin list (`ScribeModSystem.MyPins`), the persisted order (`scribe:pins:v1`), the ordering
> rule (`ScribePinOrdering.ForDisplay`), and drag-reorder already exist. This change removes the HUD's
> session-only ordering overlay so the HUD and Pin Tab render one shared order, and lets un-completing a
> sunk pin return it to its prior position (see design D1/D2). Reverses the `scribe-settings-followups`
> 2.1/2.2 "sunk stays sunk in-session" behavior (no longer a live spec requirement — design D2).

## 1. Baseline & confirm the crux

- [x] 1.1 Re-read the HUD ordering as the reference: `HudScribePins.cs` `BuildOrderedRows` (~:801),
      `sunkOrder` field (~:108), its population in `OnTick` (~:570), its per-rebuild pruning (~:812), and
      `SunkForOrder` (~:608). Note the base branch (Sink/UnpinSink → `ScribePinOrdering.ForDisplay(MyPins)`,
      else raw `MyPins`) that must SURVIVE as the whole ordering, and confirm it is identical to the Pin
      Tab's `OrderedPinsForDisplay` (`ScribeDialogBase.PinTab.cs:132`).
- [x] 1.2 Confirm in code the load-bearing D3 assumption: the HUD DEFERS its network send for the undo
      window (`pendingCompletions` / `PinHudWaitMs`; the send-on-expiry in `OnTick`), so during the window
      the server's `LastKnownDone` is still false and `ForDisplay` keeps the just-checked pin in the
      not-done group (in place) with no help from `sunkOrder`. Confirm a Sink completion never permutes
      `MyPins` (`ScribeModSystem.PinOperations.cs:73` `CompleteTaskForPlayer` — only `SetPinDone` +
      document `MoveTaskToBottomFromReader`). If either is false, STOP and revisit the design.

## 2. Remove the HUD session-only ordering overlay

- [x] 2.1 In `HudScribePins.cs`, delete the `sunkOrder` field, its population in `OnTick` (the
      `sunkOrder.Add(key)` on Sink-window expiry), and its per-rebuild pruning against the live set.
- [x] 2.2 Delete `SunkForOrder` and the second not-sunk-then-sunk re-partition in `BuildOrderedRows`, so
      the method's result is exactly its base order (Sink/UnpinSink → `ForDisplay(MyPins)`, else raw
      `MyPins`) minus `awaitingRemoval`, capped by max rows. Keep `awaitingRemoval` and the in-window
      in-place hold path intact (they are orthogonal — design D3/D4).
- [x] 2.3 Remove any now-dead references, `using`s, or trace/format strings that mentioned `sunkOrder` /
      `SunkForOrder`. Confirm `BuildOrderedRows` now mirrors the Pin Tab's `OrderedPinsForDisplay` exactly
      (same base rule, no overlay) — the two surfaces agree by construction.
- [x] 2.4 Grep the file (and the rest of `src/Mod`) for any lingering `sunkOrder` / `SunkForOrder` usage;
      there should be none outside this deletion.

## 3. Verify (build + suites)

- [x] 3.1 `dotnet build src/Mod/Mod.csproj` clean (0 errors, no new warnings).
- [x] 3.2 `dotnet test tests/Core.Tests` green — no Core change expected; `ScribePinOrdering.ForDisplay`
      stable-partition coverage already exists and is unchanged. If any pure ordering assertion is worth
      adding to lock the shared-order contract, add it; otherwise note the change is GUI-layer.
- [x] 3.3 `build/verify.sh` (Core + Atlas) green; restage Debug.

## 4. In-game parity gate (the user's to run — do not skip)

- [x] 4.1 **Cross-surface, cross-document sync.** Pin tasks from several documents; open the HUD and the
      Pin Tab side by side. Confirm they render the pins in the SAME order. Reorder on the Pin Tab (drag) →
      the HUD reflects the same new order. Relog → the order persists and both surfaces still agree.
- [x] 4.2 **In-window hold still holds (D3, primary risk).** Under Sink policy, check a HUD pin → it stays
      in place during the undo window (does NOT jump to the bottom), its checkbox stays operable, then on
      window expiry it settles to the bottom — exactly as before removing `sunkOrder`.
- [x] 4.3 **Un-complete returns to prior position (the behavior change).** Under Sink, complete a pinned
      task so it sinks to the bottom (window elapsed); then un-complete it → it returns to its prior
      position among the not-completed pins on BOTH the HUD and the Pin Tab (not held at the bottom).
- [x] 4.4 **`40be9d31` Sink agreement not regressed.** Under Sink, complete a PINNED task, then pin/unpin
      others → Read view, HUD, and Pin Tab all agree on where the completed task sits and re-agree after
      pin/unpin. (This is the headline invariant; removing the overlay should strengthen it.)
- [x] 4.5 **Rapid multi-complete + edges.** Complete several pinned tasks in quick succession under Sink →
      resting order is the stable partition (done pins at the bottom in pin-list order), no row jumps or
      overlaps; unpin/delete policies still collapse-and-leave as before; a row sliding under a stationary
      cursor keeps its controls; re-pin during a collapse still revives the row.
- [x] 4.6 **Regression: unrelated ordering paths.** New pins still append to the bottom; the Sink DOCUMENT
      block-order reorder (Read/Editor drop-to-bottom) is unchanged; the Pin Tab drag-reorder still
      persists and re-syncs.

## 5. Docs & memory

- [x] 5.1 `openspec validate sync-pinned-order-per-player --strict` passes.
- [x] 5.2 Record playtest verdicts in `TESTING.md` (regenerate via the what-to-test skill) — the new
      section covers §4.1–4.6, and flag the un-complete-returns-to-prior-position behavior change for the
      ModDB changelog (user-visible).
- [x] 5.3 Note in `VSAPI-NOTES.md` / memory that the `scribe-settings-followups` "sunk stays sunk
      in-session" behavior was intentionally reversed here (single-source per-player order; resting order
      is a pure function of `LastKnownDone`), so it isn't re-introduced. Update the relevant memory files.
