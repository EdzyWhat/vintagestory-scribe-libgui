## Context

`BlockEntityAssignmentDesk` already extends `BlockEntityScribeWritingStation`, so the Desk already owns a
full `ScribeDocument` — the same server-authoritative document every Notebook/Lectern/Tablet uses,
persisted and synced through the exact same `IScribeDocumentHost` machinery. `GuiDialogScribeAssignmentDesk`
(a thin `ScribeDialogBase` subclass) has never exposed it: it deliberately replaces the base's
Read/Editor/Pinned nav column with its own Assignment/Sent-History/Inbox trio
(`BuildRightColNav`/`EnterGrantedView` overrides), and its one authoring surface — the Create Assignments
tab (`ScribeAssignmentFormContent`/`ScribeAssignmentStageRow`) — stages tasks exclusively from an item
placed in the Desk's one Scribe-items-only inventory slot (`BlockEntityAssignmentDesk.Inventory`,
resolved fresh every rebuild in `GuiDialogScribeAssignmentDesk.BuildAssignmentContent`).

The dialog already anticipates exposing editor access later: `EditorAccessIsAsync => true` is set today
with a comment noting it's "retained even though no nav button here currently opens the editor view."
This change is that follow-through — plus a second task source (the Desk's own document) for the Create
Assignments tab, selectable via a button under the existing `scribe-assignment-stage-empty` hint.

## Goals / Non-Goals

**Goals:**
- Add Read + Editor nav tabs to the Desk with full parity to every other writing station's views
  (checkbox/pin/delete/reorder/tracker affordances, server-lock-gated editor access) — no new view code,
  just wiring this dialog into the base class's existing `BuildReadContent`/`BuildEditorContent`.
- Reorder the nav column to Create Assignments (default) → Sent History → Inbox → Read → Editor → Settings.
- Let the Create Assignments tab pull its task list from the Desk's own document as an alternate source,
  live-tracked exactly like the existing staged-item source, activated by an explicit button click, and
  always superseded by a staged item when one is present.
- Extend "Delete from source on send" to work symmetrically when the source is the Desk's own document.

**Non-Goals:**
- No Pinned tab for the Desk (its own document isn't a personal pin target — pins are per-player, and
  "my pins" pointing at a shared block's document would be a different, unrequested feature).
- No change to the existing staged-item flow's own behavior or requirements
  (`assignment-multi-item-creation` stays as specified).
- No attempt to let the Assignment/Inbox/Sent-History tabs and the Read/Editor tabs show *different*
  documents simultaneously, or any kind of split-view — this dialog still shows exactly one tab at a time,
  same as every other Scribe dialog.
- No UI affordance to "un-pull" back to the empty state once the Desk source is active (see Decision 4)
  — it naturally reverts on its own once the Desk document has nothing left to offer.

## Decisions

### D1: Full Read/Editor parity, reusing `ScribeDialogBase.BuildReadContent`/`BuildEditorContent` unchanged
The base class already parameterizes these two builders as `protected` precisely so a surface with
different nav (the tablet, the chalkboard) can call into them without forking. The Desk needs zero new
view code — only `BuildRightColNav` grows two more `TitleButton`s wired to the base's existing
`EnterReadMode`/`EnterEditMode`-style dispatch, mirroring how e.g. `GuiDialogScribeTablet` already wires a
non-default nav layout onto the same base views.
**Alternative considered**: a lighter, Editor-only affordance (skip Read, since Editor already shows
everything). Rejected per explicit user decision — Read and Editor are both wanted, with full normal
affordances on each, not a stripped-down variant.

### D2: Nav order — Create Assignments, Sent History, Inbox, Read, Editor, Settings
`BuildRightColNav`'s returned `Column`'s `children` array order directly controls the visual stacking
order (top to bottom) already (see the existing `{ assignmentBtn, sentHistoryBtn, inboxBtn, settingsBtn }`
array) — this is a pure reordering + insertion, no layout mechanism changes. `DefaultToAssignmentView()`
(called from the constructor) and `EnterGrantedView()`'s override (landing back on whichever
Assignment/Inbox/Sent-History/Read/Editor tab was last active, never a nonexistent view) are both
untouched: Create Assignments stays the default view on open.

### D3: "Pull from Desk" is an explicit, sticky opt-in — a session boolean, not automatic
A new `private bool deskSourceActive` field on `GuiDialogScribeAssignmentDesk`, in the same family as the
existing UI-only session fields (`selectedTaskIds`, `deleteFromSource`) — reset to `false` in
`OnGuiClosed`/never persisted. `BuildAssignmentContent` resolves the active source with this priority
each rebuild:
1. Slot holds an item → that item's document rows (**unchanged** existing behavior; always wins).
2. Slot is empty AND `deskSourceActive` AND the Desk's own document has ≥1 eligible row → the Desk's own
   document's rows.
3. Otherwise → empty list (the existing `scribe-assignment-stage-empty` hint renders, plus the new
   "pull from Desk" button IF the Desk's own document has ≥1 eligible row to offer and `deskSourceActive`
   is still false).
Because this re-resolves fresh on every rebuild (exactly like the existing slot-item path already does,
per `BuildAssignmentContent`'s own doc-comment: "resolves the staged item's rows fresh on every build...
rather than caching them"), reading the Desk's own document instead of the slot's item document is a
same-shape, same-cost swap — no new caching or diffing.
**Alternative considered**: no explicit gesture — auto-show the Desk's own tasks whenever the slot is
empty and the Desk has tasks, no button. Rejected: the user explicitly asked for a click-to-fill button,
and auto-showing would make the empty-state hint's "place an item here" instruction contradict what's
literally on screen (a populated list) the moment the Desk had any of its own tasks.
**Alternative considered**: a toggle to explicitly go back to the plain empty state. Rejected — once the
Desk's own document is emptied (its rows sent, or deleted via the Editor), source resolution above falls
through to the empty state on its own; there is nothing further to "undo."

### D4: Live-tracking reads the Desk's *persisted* document, not an in-progress Editor scratch buffer
`BuildAssignmentContent` will read the Desk's own document via the same `host.Document` access
`BuildReadContent` already uses — never the Editor tab's in-progress `scratch` buffer, which only flushes
to `host.Document` on commit/blur. This matches the existing Read-view convention (Read always shows the
last-committed document, never live keystrokes) and needs no new synchronization: switching tabs to
Assignment after editing on the Editor tab sees whatever was last committed, same as switching to Read
would.

### D5: Selection cascade rules are source-agnostic (no change needed)
`OnToggleStagedRowSelected`'s parent→subtask cascade-once-on-select logic already operates purely on
`stagedRowsCache` (a flat `List<ScribeReadRowData>`) with no awareness of where those rows came from. It
needs no changes — swapping which document populates `stagedRowsCache` is invisible to it.

### D6: Wire protocol — `ScribeSendAssignmentBatchMessage` gains a source discriminator
Server-side removal (`TryRemoveStagedRows`, called when `DeleteFromSource` is set) currently hard-codes
"read the block position's staging slot's ItemStack-embedded document." When the source is the Desk's own
document there is no ItemStack to mutate — the rows to remove live in the Desk's own persisted document
(the same one `IScribeDocumentHost`/`BlockEntityScribeWritingStation`'s normal editor-save path already
mutates). Add a `bool SourceIsDeskDocument` field to the message; `OnServerReceivedSendAssignmentBatch`
branches on it to call one of two sibling removal methods:
- `TryRemoveStagedRows` (existing, unchanged) — mutates the slot's ItemStack-embedded document.
- `TryRemoveDeskOwnRows` (new) — mutates `desk.Document` (or whatever the writing-station base's document
  accessor is named) directly, then persists + syncs exactly the way a normal editor save already does for
  this block entity (so this reuses existing persistence, not a new mechanism).
Both mirror the same best-effort semantics: match-by-TaskId against the CURRENT document state at removal
time, remove only matches, silently no-op anything since-changed — never treat a stale/missing source as
an error, since the assignments were already created server-authoritatively regardless (matches
`TryRemoveStagedRows`'s existing doc-comment reasoning).
**Alternative considered**: a single unified removal method taking a resolved `IScribeDocumentHost` +
save-back delegate. Rejected for this change — the two persistence shapes (ItemStack tree attribute vs.
block entity's own tree attribute) are different enough that forcing one signature to cover both adds an
abstraction with exactly two callers; two small sibling methods stay clearer (per the project's
own "no premature abstraction" guardrail).

### D7: Empty-state button — new data threaded through the existing content-widget chain
`ScribeAssignmentStageContent`'s empty-state branch (`Rows.Count == 0`) grows a conditional button below
the existing hint `Text`, shown when the dialog reports a Desk-own-document pull is available. Threaded
the same way every other cross-cutting flag already flows through this chain (`GuiDialogScribeAssignmentDesk`
→ `ScribeAssignmentFormContent` → `ScribeAssignmentStageContent`): two new plain values,
`bool canPullFromDesk` and `Action onPullFromDesk`, no new widget classes needed beyond the button itself.

## Risks / Trade-offs

- **[Risk]** `ScribeSendAssignmentBatchMessage`'s new required field is a **breaking wire-protocol
  change** — a client and server on mismatched builds would misread the packet.
  → **Mitigation**: this mod already requires matching client/server builds for every wire message (no
  server→client mod version negotiation exists in VS — see `VSAPI-NOTES.md`); this is consistent with
  every prior wire-protocol change in this codebase, not a new risk class. Note it in `CHANGELOG.md` under
  the same "restage both sides" guidance already given for prior message-shape changes.
- **[Risk]** Concurrent multiplayer edits: another player edits the Desk's own document (via the new
  Editor tab) between this player loading the Assignment tab's row list and clicking Send.
  → **Mitigation**: `TryRemoveDeskOwnRows` re-reads the current document server-side at removal time and
  matches by TaskId (same as the existing slot-item path) — a since-changed/removed row is silently
  skipped rather than erroring; the assignments themselves were already created from the client's
  snapshot regardless, so the send itself is unaffected by the race, only the source-cleanup step.
- **[Risk]** Exposing Editor access on a new surface could surface a previously-moot lock-contention edge
  case (two players opening the Desk's Editor at once).
  → **Mitigation**: `EditorAccessIsAsync => true` and the underlying lock round-trip are pre-existing,
  shared machinery every other writing station already exercises daily; this dialog already opts into it
  today even with no button reaching it. Low incremental risk.
- **[Trade-off]** A player can leave tasks "orphaned" on the Desk's own document (e.g. remove a staged
  item, forget the Desk still holds its own drafted tasks). Accepted as inherent to giving the Desk real
  document semantics — no UI guard added in this change; a future polish pass could add a subtle indicator
  if this proves confusing in practice.

## Migration Plan

No data migration: the Desk's own document already exists in every save (inherited from
`BlockEntityScribeWritingStation`), just unread by the dialog until now — nothing to backfill. The one
compatibility-sensitive piece is the wire-message shape change (D6): ship it as a normal mod version bump,
matching this project's existing "both sides restage together" practice for every prior assignment-message
change (see e.g. the `BatchId` field's introduction).
