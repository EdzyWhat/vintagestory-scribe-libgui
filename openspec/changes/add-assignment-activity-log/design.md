## Context

`ScribeAssignment` (`src/Core/ScribeAssignment.cs`) is a small, game-agnostic record:
`AssignerUid`, `TargetPlayerUid`, `State` (`ScribeAssignmentState`), `AssignedDate` (a
pre-formatted display string, not a `DateTime`/calendar value), `Seen`. It persists through
two independent hand-rolled binary codecs, not the vanilla Sign block tree-attribute
pattern:

- `ScribeAssignmentStore.WriteRecordList`/`TryReadRecordList` (`src/Core/
  ScribeAssignmentStore.cs:208-278`) — the server's canonical in-memory store, serialized
  into the savegame blob and into per-player sync blobs (`SerializeList`/
  `TryDeserializeList`).
- `ScribeDocumentCodec.Serialize`/deserialize (`src/Core/ScribeDocumentCodec.cs:135-145`) —
  the placed copy riding inside a player's own document (a `ScribeBlock.Assignment` clone),
  currently at codec version 11 (assignment fields were v9/v10 additions).

Both codecs write the same five fields in the same order and must both gain any new field,
version-bumped per `docs/CODEC-MIGRATION.md`'s append-only convention.

State transitions funnel through exactly two choke points in `src/Mod/
ScribeModSystem.Assignment.cs`:

- `OnServerReceivedAssignmentAction` (lines 58-90) — the single handler for the four
  explicit `ScribeAssignmentAction` values (`Accept`, `Decline`, `Cancel`, `Discard`; no
  `Complete` member — see below), calling `ScribeAssignmentStore.TryApplyAction`
  (`src/Core/ScribeAssignmentStore.cs:89-103`, pure/game-agnostic) and then, on a
  successful `Accept`, `TryPlaceAcceptedAssignment` (lines 115-147).
- `NotifyAssignmentDoneChanged` (lines 158-168) — the derived **Completed** transition,
  fired from the task's own Done-flag toggle (`ScribeAssignmentTransitions.TryMarkCompleted`,
  `ScribeAssignment.cs:102-107`), applied to *both* the store's canonical record and the
  placed block's cloned copy separately.
- `NotifyAssignmentDiscardOnDelete` (lines 174-181) — a **second, distinct Discard path**:
  deleting an Accepted assigned block drives the same Discard transition outside the Inbox
  "Discard" button.

`ScribeInboxRowData` (`src/Mod/ScribeInboxContent.cs:25-27`) is a per-render value snapshot
built fresh from `ScribeBlock`/`ScribeAssignment` at two call sites in `src/Mod/
ScribeDialogBase.ViewSwitching.cs` (`BuildInboxContent`, `BuildAssignmentContent`) — it is
not itself persisted. `ScribeInboxContent.BuildExpandedDetail` (`ScribeInboxContent.cs:294-
299`) renders the current single "Assigned by X — <date>" line via one `Lang.Get` call
against `data.AssignedDate`; there is no shared date-formatting helper beyond `NotebookHost
.FormatDate`/`FormatCalendarDate` (`src/Mod/NotebookHost.cs:292-309`), which mints the VS
in-game-calendar display string once, at the moment of the event, on the Mod layer (which
holds `sapi`).

## Goals / Non-Goals

**Goals:**
- Every assignment carries an ordered, server-authoritative log of dated lifecycle events:
  Accepted (with the item/slot it landed on), Completed, Declined, Cancelled, Discarded.
- The log survives both existing codecs (store blob + document blob) and both existing
  transports (per-player sync push + document save/load), so the Assigner's Sent view and
  the Assignee's own placed copy agree.
- The expanded inbox row renders the log in order, underneath the existing "Assigned by"
  line, using the same per-line `Lang.Get` convention already used for that line and for
  Accept-candidate labels (`scribe:scribe-assignment-candidate-label`, `<Type> "<Title>"`).

**Non-Goals:**
- No change to `ScribeAssignmentState`/`ScribeAssignmentAction`'s transition legality —
  the log only observes transitions that already happen; it never gates or alters one.
- No sortable/re-derivable timestamp. Consistent with `AssignedDate` and `HistoryEntry
  .InGameDate`, each log entry's date is a frozen display string minted once, not a
  `DateTime` — log order is list order, not a re-sort key.
- No standalone log viewer/filter UI. It renders inline in the expanded row only.
- No retroactive backfill for assignments that already exist in a save when this ships —
  see Migration Plan.

## Decisions

### 1. New type: `ScribeAssignmentLogEntry` (Core), modeled on `HistoryEntry`

Add a small, game-agnostic record in `src/Core/`, alongside `ScribeAssignment.cs`:

```csharp
public enum ScribeAssignmentLogKind : byte
{
    Accepted = 0, Completed = 1, Declined = 2, Cancelled = 3, Discarded = 4,
}

public sealed class ScribeAssignmentLogEntry
{
    public ScribeAssignmentLogKind Kind { get; }
    public string Date { get; }           // pre-formatted, e.g. "14 Rain, Year 3"
    public string? Detail { get; }        // Accepted-only: "<Type> \"<Title>\""; null otherwise

    public ScribeAssignmentLogEntry(ScribeAssignmentLogKind kind, string date, string? detail = null) { ... }
}
```

`ScribeAssignment` gains `IReadOnlyList<ScribeAssignmentLogEntry> LogEntries { get; }` plus
an internal append method (`AppendLogEntry`) used only by the Mod-layer choke points below;
`Clone()` deep-copies the list (entries themselves are immutable, so a shallow list copy is
enough — matching how `Clone()` already copies every other field by value).

**Alternative considered**: reuse `HistoryEntry` directly instead of a new type. Rejected —
`HistoryEntry` carries `ActorName`/`EntryId`/kind-specific sliding-window caps
(`HistoryStore.cs`) sized for a *document's* long-running chronicle across many tasks; an
assignment's log is short (at most 5 entries: one Accept + one terminal event), has no
actor-name field (the viewer already knows who they are relative to `ViewerRole`), and
needs no cap or store-level pruning. A dedicated, smaller type avoids dragging in machinery
sized for a different problem, while still following the *same structural pattern*
(`HistoryEntry`/`HistoryStore`'s "append-only, dated, own-tiny-codec" shape) rather than
inventing a new one.

### 2. Where each transition appends its entry

All four log-append calls happen in `src/Mod/ScribeModSystem.Assignment.cs`, which already
holds `sapi` (needed for `NotebookHost.FormatDate(sapi)`) at every relevant site — never in
`src/Core/`, which has no calendar access and must stay game-agnostic:

- **Accepted** — in `TryPlaceAcceptedAssignment`, right after the slot/doc are resolved
  (the same point `stack.GetName()` and `doc.Title` are already read for
  `AppendAssignedBlock`). Build the detail string mirroring `FormatCandidateLabel`'s
  `<Type> "<Title>"` shape (reusing its lang key, `scribe:scribe-assignment-candidate-
  label`, computed server-side from the same two values) and append
  `Accepted(date, detail)` to the record *before* cloning it into the placed block, so the
  clone carries the entry too.
  - **Decision**: append the Accepted entry even when placement fails (the two early-return
    branches at lines 119-123/128-132) is explicitly **not** done — those branches mean the
    action never actually completed (no slot resolved / not writeable / doc full), so
    `TryApplyAction`'s `Accept` transition itself should be treated as not-yet-applied in
    that case. This matches current behavior: today those branches leave `State` at
    whatever `TryApplyAction` already set it to only once placement is confirmed. Cross-
    check against the actual call order in `OnServerReceivedAssignmentAction` during
    implementation (task list) rather than assuming here — if `TryApplyAction` already runs
    before placement is attempted, the log-append must sit at the same point `State` is
    already committed, not before it, to avoid a log entry for a transition that visibly
    didn't happen.
- **Completed** — in `NotifyAssignmentDoneChanged`, applied to both the canonical store
  record and the placed clone (mirroring how `TryMarkCompleted` itself is already called
  twice there).
- **Declined / Cancelled / Discarded** — in `OnServerReceivedAssignmentAction`, right after
  `TryApplyAction` returns success, branching on `action` (Decline/Cancel/Discard map 1:1 to
  their log kinds; `Accept` is handled separately per above since it needs placement data
  `TryApplyAction`'s boolean result alone doesn't carry).
- **Discarded (delete path)** — `NotifyAssignmentDiscardOnDelete` needs its own identical
  append call; it is a genuinely separate call site from the Inbox Discard button and would
  silently miss the log without this.

### 3. Wire format: extend the existing blobs, no new ProtoMember

The log rides inside the already-existing `SentBytes`/`ReceivedBytes` blobs on
`ScribeAssignmentSyncMessage` (`src/Mod/ScribeAssignmentSyncMessage.cs`) — those are
opaque `byte[]` produced by `ScribeAssignmentStore.SerializeList`/`WriteRecordList`, so
adding fields there requires no message-schema change, only a codec version bump. Same
reasoning for `ScribeDocumentCodec` (next version past 11).

### 4. Rendering: extend `ScribeInboxRowData`, append `Text` widgets in order

`ScribeInboxRowData` (`ScribeInboxContent.cs:25-27`) gains
`IReadOnlyList<ScribeAssignmentLogEntry> LogEntries`, populated identically at both existing
construction sites (`ScribeDialogBase.ViewSwitching.cs`'s `BuildInboxContent`/
`BuildAssignmentContent`) from `b.Assignment.LogEntries`. `BuildExpandedDetail` renders one
additional `Text` widget per entry, in list order, directly below the existing `meta` line,
each via its own `Lang.Get` call keyed by `Kind` (new lang keys per kind, following the
existing `"scribe-assignment-assigned-by": "Assigned by {0} — {1}"` em-dash convention,
e.g. `"scribe-assignment-log-completed": "Completed — {0}"`, and an Accepted-specific
template taking the detail string as well).

## Risks / Trade-offs

- **[Risk] Missing a choke point silently drops a log entry** (there are four distinct
  places state can change: the action message handler, the Done-flag completion path, and
  the delete-triggered discard path — one omission means a transition that changed `State`
  correctly but produced no log line) → **Mitigation**: tasks.md enumerates all four sites
  explicitly by file:line (from this design's research); a manual playtest pass exercises
  each of the five terminal outcomes (Accept, Complete, Decline, Cancel, Discard-via-button,
  Discard-via-delete) and checks the expanded row after each.
- **[Risk] Codec version drift between the store blob and the document blob** (two
  independent `Write`/`Read` implementations for the same conceptual field, per §1b of the
  research — a mismatch between them would desync the Assigner's Sent view from the
  Assignee's own document) → **Mitigation**: bump and test both codecs together in the same
  task; add a round-trip unit test per codec (Core.Tests already covers `ScribeDocumentCodec`
  and should gain assignment-log-specific cases) rather than relying on manual play alone.
- **[Trade-off] No sortable timestamp** — matches existing precedent (`AssignedDate`,
  `HistoryEntry.InGameDate`) but means a log can never be re-ordered or filtered by real
  time, only displayed in append order. Accepted as consistent with how the rest of the mod
  already handles in-game dates.

## Migration Plan

- Additive-only codec change (new field appended after existing ones in both binary
  formats), per `docs/CODEC-MIGRATION.md`'s convention — old save data deserializes with an
  empty `LogEntries` list for any assignment that predates this change (no crash, no
  required migration step).
- **Backfill is explicitly out of scope** (Non-Goals): an assignment that was, say, already
  Accepted before this ships will show no "Accepted onto..." line retroactively — only
  entries appended after this change ships exist. This is a one-time, cosmetically-thin gap
  (a pre-existing assignment's expanded row just has fewer log lines than a new one) judged
  not worth a synthetic backfill entry with a fabricated or missing date.
- No rollback concerns beyond a normal revert — the new field is additive and nothing
  downstream depends on its presence.

## Open Questions

- Exact call-order confirmation for Accept: does `TryApplyAction`'s `Accept` transition
  commit `State = Accepted` *before* `TryPlaceAcceptedAssignment` resolves the slot, or only
  once placement succeeds? This determines whether the Accepted log entry can be appended
  unconditionally after a successful `TryApplyAction` call, or must be gated on
  `TryPlaceAcceptedAssignment`'s own success path. Resolve during implementation by reading
  `OnServerReceivedAssignmentAction`'s exact call order (tasks.md item references the exact
  lines to check) rather than guessing here.
- Exact lang-key names/wording for the four new per-kind log lines and the Accepted detail
  template — left to implementation to match the project's existing em-dash convention and
  existing `en.json` key-naming style (`scribe-assignment-log-<kind>`), not specified further
  here since it's copy, not a design decision.
