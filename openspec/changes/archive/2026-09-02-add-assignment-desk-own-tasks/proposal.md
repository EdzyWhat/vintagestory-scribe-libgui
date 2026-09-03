## Why

The Assignment Desk's Create Assignments tab can only stage tasks from an item placed in its one
Scribe-items-only slot — there's no way to author tasks directly at the Desk itself, even though the
Desk's block entity (`BlockEntityAssignmentDesk : BlockEntityScribeWritingStation`) already carries its
own `ScribeDocument` like every other writing station; the dialog just never exposes it. A player who
wants to hand out a batch of tasks they haven't already written onto some other Scribe item today has to
go author them somewhere else first, then carry that item to the Desk. Exposing the Desk's own document
lets it work as a standalone assignment-drafting surface, not only a staging pass-through.

## What Changes

- Add **Read** and **Editor** nav tabs to the Assignment Desk, giving full read/write access to the
  Desk's own document — same server-lock-gated editor access, same checkbox/pin/delete/reorder/tracker
  affordances as every other writing station's Read/Editor views. No Pinned tab (the Desk's own document
  isn't a personal pin target).
- Reorder the Desk's nav column to: **Create Assignments** (default view, unchanged) → **Sent History**
  → **Inbox** → **Read** → **Editor** → **Settings**.
- Add a button to the Create Assignments tab's empty-state (below the existing
  `scribe-assignment-stage-empty` hint), shown only when the staging slot is empty AND the Desk's own
  document has at least one eligible row. Clicking it switches the tab's task source to the Desk's own
  document — from then on the list live-tracks that document (editing it via the new Editor tab updates
  the list immediately), exactly like the existing item-staged case tracks a staged item's document live.
- Placing an item in the staging slot always takes priority: whenever the slot holds an item, its
  document's rows show, regardless of whether "pull from Desk" was previously activated. Removing the
  item reveals the Desk's own rows again (if the pulled-from-Desk source is still active and the Desk
  document still has rows).
- The existing "Delete from source on send" toggle applies identically when the source is the Desk's own
  document: sending removes the sent rows from the Desk's document, the same way it already removes them
  from a staged item's document. **BREAKING (wire protocol)**: `ScribeSendAssignmentBatchMessage` gains a
  field identifying which source a batch was drawn from, since the server-side removal path needs to know
  whether to mutate the slotted item's embedded document or the Desk block entity's own document.
- Parent/subtask selection cascade rules (design.md D11: selecting a parent row also selects its
  immediately-following subtask rows once) apply identically regardless of source.

### Capabilities

#### New Capabilities
- `assignment-desk-own-document`: the Assignment Desk's own document becomes a full read/write/task-source
  surface — Read/Editor tab access, and its rows can be pulled into the Create Assignments tab as an
  alternate (live-tracked) task source when the staging slot is empty.

#### Modified Capabilities
- `assignment-desk-block`: the "Craftable, placeable Assignment Desk block" requirement's tab list is
  stale (it still says "two tabs: Assignment and Inbox," predating the Sent History tab this change's
  predecessor added) and now grows two more (Read, Editor) — updated to the current+new full nav list and
  order.

The existing staging-slot-based Create Assignments flow (its own requirements aren't tracked under a
separate `specs/` capability today — see `assignment-desk-block`) is otherwise unchanged in its own
right; this adds a second, independent task source alongside it rather than altering that behavior.

## Impact

- **Mod-layer UI**: `GuiDialogScribeAssignmentDesk.cs` (nav column rebuild, view-mode wiring, new
  "pull from Desk" state), `ScribeAssignmentFormContent.cs` / `ScribeAssignmentStageRow.cs` (empty-state
  button), `ScribeDialogBase.*` (Read/Editor view plumbing this dialog currently opts out of — see
  `EnterGrantedView`/`BuildRightColNav` overrides).
- **Wire protocol**: `ScribeSendAssignmentBatchMessage` (new source-identifying field),
  `ScribeModSystem.Assignment.cs` (`OnServerReceivedSendAssignmentBatch`/`TryRemoveStagedRows` need a
  second removal path for the Desk's own document).
- **No Core changes** — this is entirely a Mod-layer UI/wire-protocol change; `src/Core/` stays untouched
  since the Desk's own document already reuses the exact same `ScribeDocument` model as any other writing
  station.
- **Lang**: one or two new keys for the empty-state button label and any Read/Editor tab tooltip text.
