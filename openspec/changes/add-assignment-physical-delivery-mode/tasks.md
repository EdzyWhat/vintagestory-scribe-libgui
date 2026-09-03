## 1. Core: DeliveryMode setting and range check

- [x] 1.1 Add a `DeliveryMode` enum (`AlwaysInstant` / `AlwaysPhysical` / `Hybrid`) to `src/Core/`
- [x] 1.2 Add an admin-configurable radius value (default 200 blocks) alongside `DeliveryMode`
- [x] 1.3 Implement the one-time range-check function in `src/Core/` (position A, position B,
      radius → in-range bool), with no Vintage Story API reference
- [x] 1.4 Add a last-known-position field to the per-player persisted state in `src/Core/`
- [x] 1.5 Add an `OnPlayerDisconnect` hook in `src/Mod/` that persists the player's current
      position into that field, following the existing Sign `ToTreeAttributes`/`MarkDirty`
      persistence pattern

## 2. Core: Task Notice document and assignment integration

- [x] 2.1 Confirm the Task Notice's document payload reuses `ScribeDocumentAttributes.WriteTo`/
      `TryReadFrom` unchanged (no new serialization code needed in `src/Core/`)
- [x] 2.2 Implement the Accept-time path: creating a `ScribeAssignmentStore` record directly in the
      Accepted state (never Unaccepted) via the existing `AcceptedIntoLabel` placement mechanism
- [x] 2.3 Implement the Decline-time path: consuming the notice with no record created and no
      notification to the Assigner
- [x] 2.4 Add `Core.Tests` coverage: range-check boundary cases (exactly at radius, offline vs.
      online target), `DeliveryMode` gating logic, accept-creates-at-Accepted, decline-creates-
      nothing

## 3. Mod: Task Notice item, recipe, and model

- [x] 3.1 Register the Task Notice `CollectibleObject` implementing the existing
      `IScribeDocumentItem` pattern (blank = stackable/no data, sealed = non-stackable/unique data)
- [x] 3.2 Add the crafting recipe JSON: knife (tool) + parchment + reed → 8 blank Task Notices
- [x] 3.3 Wire the item's model/texture to the existing placeholder scroll asset
  - **Fix 2026-09-02:** was actually wired to `game:item/utility/schematic-glider` (a leftover
    from early prototyping); a 2026-09-02 playtest caught it. Corrected to `game:item/lore/scroll`
    with that shape's `lore-scroll` gui/ground/tpHand transforms (`tasknotice.json`). Also fixed a
    second, related bug found in the same report: the Accept dialog's "who assigned it and when"
    line called a lang key (`scribe:scribe-tasknotice-from`) that was never added to `en.json`, so
    the raw key rendered instead of text — repointed at the existing `scribe-assignment-assigned-by`
    key (the exact Sent History wording) and added a flavor line (`GuiDialogTaskNotice.cs`).
- [x] 3.4 Implement the held-item right-click handler that opens the existing Scribe document
      dialog in locked/read-only mode with Accept and Decline buttons, reusing the Notebook/Tablet
      open path

## 4. Mod: Create Assignments tab UI

- [x] 4.1 Add the "Local Inboxes" / "Send a Notice" toggle to the Create Assignments tab, shown
      only when `DeliveryMode` is `Hybrid`
- [x] 4.2 Wire the toggle's pre-selected position to the Core range-check result for the currently
      selected target, remaining freely overridable with no blocked/grayed state
- [x] 4.3 Add the info (ⓘ) button beside the toggle and its longer-form explanation dialog
- [x] 4.4 Add the blank-notice supply slot (stacking) and output slot (non-stacking), shown only
      when "Send a Notice" is selected
- [x] 4.5 Block the Send control with a clear message when the supply slot is empty in "Send a
      Notice" mode; on a successful send, consume one blank notice and populate the output slot
- [x] 4.6 Ensure `AlwaysInstant`/`AlwaysPhysical` modes never show the toggle, and show the
      Task Notice slots unconditionally (AlwaysPhysical) or never (AlwaysInstant)

## 5. Mod: Proximity discovery for an at-rest Task Notice

- [x] 5.1 Extend the existing `OnStormTick`-style heartbeat with a per-player scan for players who
      have at least one outstanding sealed Task Notice addressed to them
- [x] 5.2 Add the chunk-boundary movement gate so a stationary player's scan is skipped between
      chunk crossings
- [x] 5.3 Implement the scan itself: `IWorldChunk.BlockEntities` walk (container contents) +
      `GetEntitiesAround` (dropped items) for a matching Scribe-tagged stack within 10-15 blocks
- [x] 5.4 Spawn the existing ambient particle/badge effect at the found position, client-local to
      that player only

## 6. Server admin setting

- [x] 6.1 Expose `DeliveryMode` and the radius value via this project's existing server-config
      mechanism
- [x] 6.2 Document the new setting (what each `DeliveryMode` value does, the radius default) in
      the appropriate project docs

## 7. Verification

- [x] 7.1 Manual playtest: Hybrid in-range send (Local Inboxes default), out-of-range send (Send a
  - Confirmed 2026-09-02: TESTING.md `00000087` "(no note)" (submission 2026-09-02T20-53-17)
      Notice default), offline-target range check, toggle override in both directions, decline-
      with-no-notification, accept-then-complete/discard syncing identically to an in-range
      assignment
- [x] 7.2 Add the new manual test cases to `TESTING.md`
- [x] 7.3 Record any newly-learned API mechanism or gotcha in `VSAPI-NOTES.md`

## 8. Optional / stretch (not required for apply-readiness)

- [ ] 8.1 Crafting-grid merge of two same-recipient Task Notices into one, via
      `CollectibleObject.OnCreatedByCrafting` (mechanism verified feasible against the
      `BlockPie.OnCreatedByCrafting` precedent; not committed to this change)
