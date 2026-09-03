## Why

When an Assignee accepts a task, the Inbox already shows *when* it was accepted (the
"Accepted — <date>" line, refine-assignment-desk-inbox-ux §15.4) but not *where* — which
Notebook, Lectern, or Tablet the task actually landed in. A player juggling several Scribe
items has no way to tell, after the fact, which one holds a given accepted assignment
without opening each one and searching.

## What Changes

- The server now computes a short destination label (`<Type> "<Title>"`, e.g. `Notebook
  "Book of Nick"`, falling back to the bare item name when the document has no title) at
  the moment it places an accepted task into the resolved surface/inventory/block-target,
  reusing the same naming rule the client's placement-candidate picker already uses today.
- `ScribeAssignment` gains a new optional `AcceptedIntoLabel` field, persisted additively
  (store version 4 → 5, backward-compatible: pre-existing Accepted assignments simply have
  no label and the date-only line renders exactly as it does today).
- The Inbox's expanded-row "Accepted — <date>" line becomes "Accepted into <label> —
  <date>" whenever a label is present; unchanged when it isn't (older data, or a legacy
  path with no resolvable destination).
- Scope is the Inbox row only — the Read/Editor-row tooltip and Pin Tab snapshot keep
  showing date-only, matching their current (smaller) footprint for this info.

## Capabilities

### New Capabilities
(none — this extends existing Accept/Inbox behavior, no new standalone capability)

### Modified Capabilities
- `assignment-state-machine`: the Accept transition's placement step now also records a
  destination label alongside the existing accepted-date stamp.
- `inbox-tab`: the expanded-row date line now includes the destination label when one was
  recorded.

## Impact

- `src/Core/ScribeAssignment.cs` — new field + `Clone()` update.
- `src/Core/ScribeAssignmentStore.cs` — version bump 4 → 5, additive read/write.
- `src/Mod/ScribeModSystem.Assignment.cs` — compute + stamp the label in
  `TryPlaceAcceptedAssignment`.
- A destination-label-formatting helper currently living client-side in
  `ScribeDialogBase.ViewSwitching.cs` (`FormatCandidateLabel`) needs a server-reachable
  counterpart (shared static in `src/Mod`, since it touches `ItemStack`/document types that
  are Mod-layer, not Core-eligible).
- `src/Mod/ScribeInboxContent.cs` + `src/Mod/ScribeDialogBase.ViewSwitching.cs` — thread the
  new field into `ScribeInboxRowData` and render it.
- `src/Mod/assets/scribe/lang/en.json` — new lang key for the combined line.
- `tests/Core.Tests/ScribeAssignmentStoreTests.cs` — coverage for the v5 field
  (round-trip + pre-v5 backward-compat default).
