## Context

See `proposal.md` - Why for the full root-cause account (confirmed against a real diagnostic
server log). In short: `NotebookHost` has two independent paths that can persist a fresh,
uncoordinated `DocId` onto a documentless stack:

1. Its **constructor** treats "I was constructed over a documentless stack" as license to
   immediately mint and write a fresh `ScribeDocument`.
2. `AttachServerContext` → `RecordPickedUpIfNew` (one-time PickedUp-history bookkeeping) calls
   `Flush()` when it adds a new history entry — and `Flush()` persists whatever's currently in
   `_document`, even though `RecordPickedUpIfNew` never touched `_document` itself.

Both are reachable from `ScribeModSystem.History.cs`'s `OnHistoryScanTick` → `FindCarriedNotebooks`
(server-side, every ~10s for every online player, unconditionally over every carried Scribe item)
— entirely independent of anything the player is doing. They're also reachable from the client's
own dialog-open path (`ItemScribeNotebook.OpenNotebookDialog` constructing its own `NotebookHost`
against the client's local stack snapshot). Whichever side stamps a given stack first "wins" the
persisted `DocId`; the other side's client keeps proposing edits under a `DocId` the server never
adopted. `OnServerReceivedNotebookSave`'s existing mismatch guard then silently refuses every
subsequent save for that dialog session, with no reply telling the client anything went wrong.

`TryResolveDocHost`'s inventory scan (`ScribeModSystem.PinOperations.cs`) already has the right
instinct for path 1 — it explicitly `continue`s past a documentless slot instead of constructing a
host — but that guard lives only in that one call site.

## Goals / Non-Goals

**Goals:**
- Make `NotebookHost`/`TabletHost` construction side-effect-free with respect to the `ItemStack` —
  constructing a host to *read* a document must never *write* one.
- Make history-only bookkeeping (`RecordPickedUpIfNew`) persist only history, never the document,
  since it never mutates the document.
- Establish `OnServerReceivedNotebookSave` as the single place a fresh `DocId` is ever persisted
  onto a previously-documentless stack — with both stamp paths above closed, no other code path
  remains that could race it.
- Remove the diagnostic-only scaffolding added for this investigation (`TEMP DIAGNOSTIC
  (task-loss-on-tablet-close)` traces, the diagnostic Atlas test file) and replace it with real
  regression coverage of the fixed mechanism.

**Non-Goals:**
- A client-side resync path for a DocId-mismatch save rejection. Considered (see proposal.md's
  "Out of scope") and dropped: once both stamp paths are closed, the mismatch branch has no
  remaining way to be reached, so there is nothing left for a resync reply to guard against, and
  it isn't worth the client-side plumbing (routing an unsolicited reply to the right open dialog
  by slot identity, since the host registry is keyed by a DocId the client never registered) for a
  path that should now be permanently dead code.
- Redesigning the host registry, the Lectern's DocId flow, or the CarryOn bridge — none of those
  are implicated in this bug.
- Changing how history events themselves are recorded (Death/PvpKill/BossKill/TemporalStorm
  scopes/caps) — this fix explicitly preserves their existing "every carried notebook, document or
  not" behavior; only the *side effect* of recording changes.

## Decisions

**1. Move the "documentless → mint a placeholder" logic out of the constructor's write path.**
`NotebookHost`'s constructor keeps constructing an in-memory `ScribeDocument` placeholder when the
stack has no `scribeDocument` attribute (callers throughout the codebase rely on `host.Document`
always being non-null) — but it no longer calls `ScribeDocumentAttributes.WriteTo(stack, doc)` as
part of doing so. The placeholder lives only in `_document` until an explicit, authoritative write
path decides to persist it.
- *Alternative considered*: have the constructor take a `shouldStamp` flag and only write when
  true. Rejected — every existing call site outside the save handler would need to remember to
  pass `false`, which is the same "callers must remember the rule" hazard this design eliminates.

**2. `RecordPickedUpIfNew` persists history only.**
Change its `if (added) Flush();` to `if (added) FlushHistory();`. `RecordPickedUpIfNew` never
mutates `_document` — it only calls `_history.TryAddEntry(...)` — so `FlushHistory()` (which
writes `scribeHistory` and syncs it to the client, without touching `scribeDocument`) is not just
sufficient but the *correct* call; `Flush()` was writing the document as an unrelated side effect.
- *Alternative considered*: guard `OnHistoryScanTick`'s `FindCarriedNotebooks` to skip
  documentless stacks entirely (skip constructing a host at all, mirroring
  `TryResolveDocHost`'s scan guard). Rejected after closer inspection: `FindCarriedNotebooks`
  feeds Death/PvpKill/BossKill/TemporalStorm recording too, and none of those require a document
  to exist (`notebook-history`'s existing requirements say "every notebook the player carries," no
  document precondition). Skipping construction there would silently stop those events from
  recording on any notebook until after its first save — a real behavior regression the fix at its
  actual source (this decision) avoids entirely.

**3. `OnServerReceivedNotebookSave` becomes the sole bootstrap-write site.**
Its existing "a fresh stack with no prior save has no stored DocId yet — allow that write" branch
already does the right check (`ScribeDocumentAttributes.TryReadFrom` fails or returns null →
accept). No behavior change needed there — with Decisions 1 and 2 in place, this branch is
naturally the only writer left, so there's nothing further to change or guard against.

**4. Drop the resync-on-mismatch reply.** See Non-Goals. The original plan (design.md's earlier
draft) proposed reusing the save-echo message to carry the server's actual DocId/document back to
the client on a rejection. Investigating the client side surfaced real complexity (the host
registry's `Dictionary<Guid, IScribeDocumentHost>` is keyed by DocId, so a reply carrying the
server's *real* DocId can't be looked up by a client that registered under its own wrong one;
fixing that needs slot-identity-based lookup plus a way to force an open dialog to re-seed its
`scratch` from an unsolicited reply). Once Decisions 1-2 close every known stamp path, that
plumbing would guard a branch nothing can still reach — not worth building.

**5. Delete the diagnostic scaffolding as part of this change, not a follow-up.**
The `TEMP DIAGNOSTIC (task-loss-on-tablet-close)` traces in `ScribeModSystem.Network.cs` and
`ItemScribeTablet.cs` did their job (they're what produced the log that root-caused this). They
get removed once the real fix and its regression tests are in place — not left behind as
permanently-on logging. `tests/Integration.Tests/TabletTaskLossDiagnosticScenarios.cs` (which
reproduced two hypotheses that turned out NOT to be the real mechanism) is deleted and replaced
by Atlas scenarios that reproduce the actual confirmed race and assert it's fixed.

## Risks / Trade-offs

- **[Risk] A future code path could reintroduce a third stamp site** (some new server-side
  operation that constructs a host and calls `Flush()` instead of `FlushHistory()` for a
  history-only mutation, or otherwise writes `_document` without an explicit save). → Mitigation:
  the regression tests (see tasks.md §4) assert the two currently-known paths are closed; if a
  future change reintroduces a third, it would need its own `Flush()` vs `FlushHistory()` review —
  the same review this fix itself required. No structural guard fully prevents a *new* call site
  from getting this wrong again, but the fix removes the two paths that actually exist today.
- **[Risk] Removing the constructor's write means some existing caller might have relied on
  "constructing a host guarantees the stack now has a document attribute."** → Mitigation: grep
  every `new NotebookHost(`/`new TabletHost(` call site (six, enumerated in the investigation) and
  confirm each only reads `host.Document`/`host.History` afterward rather than assuming the
  `ItemStack` attribute now exists; none of the six needed to assume that.

## Migration Plan

Standard mod version bump (no data migration — `scribeDocument`/`scribeHistory` attribute shapes
are unchanged, only *when* they get written changes, and no network message shape changes). Ship
as the next patch release; no save-file compatibility concerns since existing persisted documents
are untouched by this change.
