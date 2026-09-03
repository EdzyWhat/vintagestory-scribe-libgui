## 1. Core: per-side hidden flags + store versioning

- [x] 1.1 Add `public bool HiddenFromAssignee { get; set; }` and
      `public bool HiddenFromAssigner { get; set; }` to `ScribeAssignment`
      (`src/Core/ScribeAssignment.cs`), default `false`, documented alongside the existing
      terminal-date fields.
- [x] 1.2 Copy both forward in `ScribeAssignment.Clone()`.
- [x] 1.3 Bump `ScribeAssignmentStore.Version` 5 → 6 (`src/Core/ScribeAssignmentStore.cs`),
      with a version-history comment matching the existing v2-v5 style.
- [x] 1.4 In `WriteRecordList`, write both new flags (plain `w.Write(bool)`, not
      `WriteOptionalString` — these are non-nullable) immediately after the
      `AcceptedIntoLabel` write.
- [x] 1.5 In `TryReadRecordList`, read both flags gated on `version >= 6` (default `false`
      for an older blob — reuse or mirror the `ReadOptionalString`-style version gate, but
      for a plain bool this is just `version >= 6 && r.ReadBoolean()`).

## 2. Core: per-side deletion + filtering

- [x] 2.1 Change `ScribeAssignmentStore.TryDelete`'s signature to
      `TryDelete(Guid assignmentId, string actingPlayerUid, ScribeAssignmentActor side)`.
      Validate: `side == Assignee` requires `actingPlayerUid == assignment.TargetPlayerUid`;
      `side == Assigner` requires `actingPlayerUid == assignment.AssignerUid`; otherwise
      reject (record unchanged). On success, set the matching flag
      (`HiddenFromAssignee`/`HiddenFromAssigner`) true; if BOTH flags are now true, remove
      the record from `_records` entirely. Keep the existing terminal-state gate unchanged.
- [x] 2.2 Update `Received(playerUid)` to also filter out records where
      `Assignment.HiddenFromAssignee` is true.
- [x] 2.3 Update `Sent(playerUid)` to also filter out records where
      `Assignment.HiddenFromAssigner` is true.
- [x] 2.4 Update the class doc comment's "one deliberate hole" note if its wording no
      longer matches (it currently describes deletion as removing the record outright on
      the first authorized request — now that's only true once both sides have deleted).

## 3. Core tests

- [x] 3.1 Update every existing `TryDelete` call site in
      `tests/Core.Tests/ScribeAssignmentStoreTests.cs` to the new 3-argument signature,
      passing the appropriate `ScribeAssignmentActor` for each existing scenario (Assignee
      deletes a terminal record; Assigner deletes a terminal record; rejected on
      non-terminal; rejected for uninvolved player; unknown id).
- [x] 3.2 Add a test: Assignee deletes their side of a terminal record — confirm it drops
      out of `Received(assigneeUid)` but still appears in `Sent(assignerUid)`.
- [x] 3.3 Add a test: Assigner deletes their side of a terminal record — confirm it drops
      out of `Sent(assignerUid)` but still appears in `Received(assigneeUid)`.
- [x] 3.4 Add a test: a self-assignment (Assigner == Assignee) — delete as Assignee only,
      confirm it's gone from `Received` but still present in `Sent` for the same uid.
- [x] 3.5 Add a test: delete both sides (Assignee then Assigner, or vice versa) — confirm
      `TryGet` returns null afterward (record fully purged).
- [x] 3.6 Add a test: a caller passes a `side` that doesn't match their actual role (e.g.
      claims `Assigner` but `actingPlayerUid` is actually the Assignee, or a stranger's uid
      entirely) — confirm rejection, record unchanged, neither flag set.
- [x] 3.7 Add a round-trip test: set one or both hidden flags, serialize via
      `SerializeList`/`SerializeStore`, confirm they survive
      `TryDeserializeList`/`LoadFrom`.
- [x] 3.8 Add a backward-compat test: hand-construct a v5-shaped blob (no hidden-flag
      bytes), confirm it still loads and both flags default to `false`.

## 4. Mod: network + server handler

- [x] 4.1 Add `public byte Side { get; set; }` to `ScribeDeleteAssignmentMessage`
      (`src/Mod/ScribeDeleteAssignmentMessage.cs`), mapped to `ScribeAssignmentActor`
      (`0 = Assigner`, `1 = Assignee`, matching the enum's existing byte values).
- [x] 4.2 In `ScribeModSystem.Assignment.cs`'s `OnServerReceivedDeleteAssignment`, decode
      `message.Side` into a `ScribeAssignmentActor` and pass it to the new `TryDelete`
      overload. Keep the existing capture-both-uids-before-deleting and
      `PushAssignmentSyncToBothParties` resync — each party's own resync now naturally
      excludes what's hidden from THEM via the updated `Received`/`Sent` filters, no extra
      client-side logic needed.

## 5. Mod: client send-side

- [x] 5.1 In `ScribeDialogBase.ViewSwitching.cs`'s `DeleteAssignmentRecord`, set
      `Side = (byte)(viewMode == ScribeLecternView.Inbox ? ScribeAssignmentActor.Assignee : ScribeAssignmentActor.Assigner)`
      on the outgoing `ScribeDeleteAssignmentMessage` — the delete control only ever
      renders while `viewMode` is `Inbox` or `SentHistory`, so this is unambiguous.

## 6. Manual verification

- [x] 6.1 Manual test: send an assignment to yourself (Assigner == Assignee), bring it to a
  - Confirmed 2026-09-01: TESTING.md `0000007f` "(no note)" (submission 2026-09-01T18-15-36)
      terminal state, delete it from the Inbox only — confirm it disappears from the Inbox
      but STILL shows in Sent Assignment History.
- [x] 6.2 Manual test: from the same still-visible Sent Assignment History row, delete it
  - Confirmed 2026-09-01: TESTING.md `00000080` "(no note)" (submission 2026-09-01T18-15-36)
      too — confirm it now fully disappears from both.
- [x] 6.3 Manual test: with two separate players, Assigner deletes a terminal record from
  - Confirmed 2026-09-01: TESTING.md `00000081` "(no note)" (submission 2026-09-01T18-15-36)
      Sent Assignment History — confirm the Assignee's Inbox still shows it unaffected, and
      vice versa when the Assignee deletes first.
- [x] 6.4 Restage both client and server builds together before testing (store version bump
      — a mismatched pair reading/writing different versions, or an old client omitting the
      new `Side` byte, could misparse or misbehave).
      - Confirmed 2026-09-01: playtest submission 2026-09-01T18-15-36 exercised the
        restaged build (all 6.1-6.3 verdicts passed against it).
