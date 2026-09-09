## Context

See proposal.md - Why/What Changes for the root cause and the two whole-document flush
handlers involved: `BlockEntityScribeWritingStation.ApplyEdit` (Lectern) and
`ScribeModSystem.OnServerReceivedNotebookSave` (Notebook/Tablet, `src/Mod/ScribeModSystem
.Network.cs`). Both already deserialize the client's submitted `ScribeDocument` and write it
as the new authoritative copy. `NotifyAssignmentDoneChanged` (`src/Mod/ScribeModSystem
.Assignment.cs:453`) already gates purely on the canonical `ScribeAssignmentStore` record's
current state (must be `Accepted` to do anything) and is a cheap no-op otherwise — this is the
exact mechanism `fix-assignment-completion-doc-resolution` established to make the derivation
independent of whether a document/block resolves at all.

## Goals / Non-Goals

**Goals:**
- Every whole-document flush that leaves a completed, assigned task in the saved document
  results in that assignment being Completed in the canonical store, regardless of how many
  times it's saved or whether it was already Completed.
- No new call sites needed in `NotifyAssignmentDoneChanged` itself — reuse it exactly as HUD/
  Pin/Read already do.

**Non-Goals:**
- Changing how the Editor's own local mutation (`ToggleEditorTask` / `ScribeCompletion
  .ApplyLocal`) works — it keeps mutating `Done` locally and relying on the existing autosave/
  flush cadence. This change only adds a step to what the *server* does once that flush lands.
- Fixing the separate, already-documented cosmetic gap where a resolved block's own embedded
  `Assignment` object can go stale relative to the canonical store (that's `assignmentOnBlock`'s
  existing job in `NotifyAssignmentDoneChanged`, unchanged here — both flush handlers already
  have the live block/document in hand, so passing its `Assignment` through costs nothing extra).

## Decisions

### Walk the saved document's completed+assigned tasks unconditionally, rather than diffing old vs. new

Both flush handlers have a natural "old" document available before they overwrite it
(`BlockEntityScribeWritingStation.Document` on the Lectern; the `existing` document read via
`ScribeDocumentAttributes.TryReadFrom` on the Notebook/Tablet path). A tempting design is to diff
old vs. new by `TaskId` and only call `NotifyAssignmentDoneChanged` for tasks whose `Done` flips
`false -> true` in this exact save.

Rejected in favor of the simpler rule: **after deserializing the new document, iterate its
blocks and call `NotifyAssignmentDoneChanged(taskId, true, block.Assignment)` for every block
that is completable, `Done == true`, and carries a non-null `Assignment`** — with no comparison
against the prior document at all.

Rationale:
- `NotifyAssignmentDoneChanged` already gates on the canonical store record's state (must be
  `Accepted`); calling it again for a task whose assignment is already `Completed` is a cheap,
  traced no-op (one dictionary lookup, no sync push). There is no correctness or performance
  reason to avoid redundant calls.
- Diffing would require trusting the "old" document as an accurate record of what the store last
  saw — exactly the assumption the prior `fix-assignment-completion-doc-resolution` change moved
  away from (gate on the canonical store, not on local document state). A document that was
  edited by a source other than this exact save (e.g. a defensive re-registration, or a save
  whose "old" read failed) could desync from what the store thinks happened; walking the final
  document and letting the store's own gate decide is strictly more robust.
- It reuses the exact same call shape (`taskId`, `nowDone: true`, `assignmentOnBlock`) the HUD/
  Pin/Read paths already use, so no new parameters or overloads are needed on
  `NotifyAssignmentDoneChanged`.

### Shared helper lives alongside `NotifyAssignmentDoneChanged`, called from both flush handlers

Add one small helper, e.g. `NotifyDoneAssignmentsInDocument(ScribeDocument doc)`, in
`src/Mod/ScribeModSystem.Assignment.cs` next to `NotifyAssignmentDoneChanged`, that does the
walk-and-call described above. `OnServerReceivedNotebookSave` (already inside `ScribeModSystem`)
calls it directly after `ScribeDocumentAttributes.WriteTo`/`MarkDirty`.
`BlockEntityScribeWritingStation.ApplyEdit` calls it via the existing `ModSystem` field
(`ModSystem?.NotifyDoneAssignmentsInDocument(doc)`), the same pattern it already uses for
`ModSystem?.RegisterHost(this)` — no new coupling between the block entity and `ScribeModSystem`.

Alternative considered: put the walk logic in `src/Core/ScribeDocument` as a query (e.g.
`GetCompletedAssignedTaskIds()`) and keep only the `assignmentStore` call in Mod. Rejected as
unnecessary indirection for a two-line LINQ filter over `doc.Blocks` — the guardrail against
`src/Core/` referencing the VS API isn't in play here (no API type crosses that boundary either
way), so this is purely a "does this need its own seam" call, and it doesn't yet.

## Risks / Trade-offs

- **[Risk]** Every whole-document flush now does an O(document size) walk plus a store lookup
  per completed+assigned task, on every autosave tick, not just ones that actually changed a
  `Done` flag. → **Mitigation**: task lists are small (bounded by each document's capacity
  policy), and the store lookup is a dictionary read with no allocation on the no-op path — this
  mirrors the cost profile `fix-assignment-completion-doc-resolution` already accepted by making
  `NotifyAssignmentDoneChanged` unconditional at its other two call sites.
- **[Risk]** This adds a second place that can push an assignment sync from a single flush, if a
  document happens to contain more than one completed assigned task. → **Mitigation**: not a new
  risk — `PushAssignmentSyncToBothParties` was already designed to be called per-task, and each
  call targets that task's own Assigner/Assignee pair; multiple pushes to the same pair in one
  flush just re-send the same (by-then-consistent) snapshot, which the client already handles as
  an idle overwrite.
