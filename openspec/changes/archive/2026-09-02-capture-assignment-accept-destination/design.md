## Context

`ScribeAssignment` (`src/Core/ScribeAssignment.cs`) already carries five nullable
transition-date strings (`AcceptedDate`/`DeclinedDate`/`CancelledDate`/`DiscardedDate`/
`CompletedDate`), stamped by the Mod layer immediately after a transition
(`ScribeModSystem.Assignment.cs:169`, `StampTransitionDate`) and rendered on the Inbox's
expanded row (`ScribeInboxContent.cs:374-375`). `ScribeAssignmentStore`'s binary codec is
additively versioned (v1→v4), each bump adding one optional field with a documented,
backward-compatible default for older blobs.

Separately, the client already knows how to label a Scribe item nicely for a human:
`FormatCandidateLabel` (private, `ScribeDialogBase.ViewSwitching.cs:519-528`) turns an
`ItemStack` into `<Type> "<Title>"` (e.g. `Notebook "Book of Nick"`), falling back to the
bare item name when the document has no real title. It's used today only to label the
Accept-placement candidate picker (`ScribeAcceptCandidate.Label`) — a client-side,
display-only string that is never sent to the server or persisted.

The actual Accept-time placement is server-authoritative: `TryPlaceAcceptedAssignment`
(`ScribeModSystem.Assignment.cs:201-249`) resolves the real target `slot`/`item`/`doc`
and writes the task into it. That's the one place that knows, with certainty, where a
task ended up — so it's the right place to mint the label, rather than trusting whatever
string the client might send.

## Goals / Non-Goals

**Goals:**
- Capture, server-side, a short display label for the Scribe item an accepted assignment
  was placed into, at the moment it's placed.
- Persist it on the store record so it survives a restart/resync (matching the other
  transition-date fields).
- Show it folded into the Inbox's existing "Accepted — <date>" line, becoming "Accepted
  into <label> — <date>" whenever a label is present.
- Reuse the existing `<Type> "<Title>"` naming rule verbatim (no new labeling scheme).

**Non-Goals:**
- No change to the Read/Editor-row tooltip or Pin Tab — they keep showing date-only (an
  explicit scope decision; both already have a smaller footprint for assignment info than
  the Inbox's expanded row).
- No retroactive backfill for assignments already Accepted before this ships — they keep
  rendering the date-only line, identical to today.
- No change to *how* placement is resolved (candidate computation, target picking,
  capacity checks) — purely additive metadata alongside it.

## Decisions

**1. Compute the label server-side, in `TryPlaceAcceptedAssignment`, not client-side.**
The server already resolves the authoritative `slot`/`doc` right before this point
(`ScribeModSystem.Assignment.cs:204-212`) — reusing that resolution avoids trusting a
client-sent string for something that will be persisted and shown to other players (e.g.
the Assigner, via Sent Assignment History, if this is ever mirrored there later). The
client's own `ComputeAcceptCandidates` label is unaffected and stays exactly as-is (it's
only ever a picker label, discarded after the request is sent).

**2. Extract `FormatCandidateLabel` into a shared, `internal static` helper, used by both
call sites.** It moves out of `ScribeDialogBase.ViewSwitching.cs` (client-only) into
`ScribeInboxContent.cs`, next to `ScribeAcceptCandidate` (the type it already labels) —
both the Accept-candidate picker (client) and the new Accept-placement stamp (server)
call the same method, so the naming rule can never drift between the two. This stays in
`src/Mod` (not `src/Core`): the helper takes an `ItemStack` and calls
`ScribeDocumentAttributes.TryReadFrom`, both Mod-layer/VintagestoryAPI-touching, which
`src/Core` must never reference.

**3. New field: `ScribeAssignment.AcceptedIntoLabel` (nullable string), set once, at
Accept-placement time, alongside the existing `AcceptedDate` stamp.** Set directly on
`record.Assignment` (the store's canonical copy) in `TryPlaceAcceptedAssignment`, the same
object `StampTransitionDate` already mutates at line 169 — so it flows through the exact
same sync path (`PushAssignmentsTo`) `AcceptedDate` already uses, and is carried into the
placed copy for free via the existing `record.Assignment!.Clone()` call at line 229 (once
`Clone()` is updated to copy it, matching the other transition fields).

**4. Store version 4 → 5, additive.** One more `WriteOptionalString`/`ReadOptionalString`
pair after `CompletedDate` in `WriteRecordList`/`TryReadRecordList`
(`ScribeAssignmentStore.cs`), following the exact v4 pattern. A pre-v5 blob has no label
for any existing Accepted record — defaulting to `null` on read is correct (the label
genuinely never existed for that record, mirroring the v4 comment's "not a lossy guess"
reasoning), and the Inbox line degrades to date-only exactly as it does today for such a
record.

**5. Combined line, one new lang key.** Rather than conditionally interpolating into the
existing `scribe-assignment-accepted-on` key, add a second key
(`scribe-assignment-accepted-into-on`, taking `{0}` = label, `{1}` = date) used only when
`AcceptedIntoLabel` is non-null; the existing key stays untouched for the null case. This
avoids a runtime string-surgery hack and keeps both phrasings independently
translatable/wrappable per the mod's `Lang` conventions.

## Risks / Trade-offs

- **[Risk]** A future second use of `FormatCandidateLabel`-the-label (e.g. showing it on
  Sent Assignment History too) would need its own threading work; this change deliberately
  doesn't do that now. → **Mitigation**: none needed yet — flagged as a natural follow-up
  if the Assigner side wants the same info later.
- **[Risk]** `TryPlaceAcceptedAssignment`'s defensive no-op path (ineligible/no-capacity
  target) never reaches the label-computation line, so an assignment that stays "Accepted
  but unplaced" (already a possible, documented edge case today) has no label. →
  **Mitigation**: this is correct, not a bug — there is no destination to name. The Inbox
  line simply shows date-only, same as any other label-less record.
- **[Risk]** Moving `FormatCandidateLabel` changes its access from `private` to
  `internal`, slightly widening its surface. → **Mitigation**: trivial, single-assembly,
  no behavior change to existing callers.

## Migration Plan

Purely additive — no destructive migration. Deploy as a normal version bump:
1. Ship the v5 codec change; existing v1–v4 save blobs keep loading (progressive
   `[MinVersion, Version]` acceptance, unchanged).
2. Any assignment Accepted *after* the update carries a label; anything Accepted before
   it keeps showing date-only, forever (no backfill attempted or needed).
3. No client/server version gate beyond the existing wire-message conventions — this adds
   no new network message, only a new field inside the existing assignment-sync payload.

## Open Questions

None outstanding — scope (Inbox-only, combined line) and helper placement were resolved
before writing this design.
