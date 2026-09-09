## Context

See `proposal.md` - Why for motivation. Two facts from the current code shape this design:

- `ScribeAssignmentStore.TryApplyAction(assignmentId, actingPlayerUid, action)` resolves the
  acting player's role purely by comparing `actingPlayerUid` against the record's own
  `AssignerUid`/`TargetPlayerUid` (`src/Core/ScribeAssignmentStore.cs:175-189`). It has no
  separate notion of "who's allowed to try" — if `TargetPlayerUid` already equals the accepting
  player at the moment this runs, the normal Unaccepted → Accepted transition just legally
  succeeds. Today's identity gate against a non-recipient is a *separate*, earlier check in
  `ApplyTaskNoticeAction` (`src/Mod/ScribeModSystem.Delivery.cs:167-173`), not part of the Core
  transition matrix.
- The client already has everything it needs to detect a mismatch on its own:
  `GuiDialogTaskNotice` holds the parsed `document` (`src/Mod/GuiDialogTaskNotice.cs:70`), whose
  `Blocks[0].Assignment.TargetPlayerUid` is the same field `GetHeldItemInfo`
  (`src/Mod/ItemScribeTaskNotice.cs:104-108`) already reads to show "addressed to X" in the
  tooltip, and `ResolvePlayerName` (`GuiDialogTaskNotice.cs:280`) already resolves a UID to a
  display name for the "assigned by" line. No new network round-trip is needed just to detect
  the mismatch or to word the confirmation.

## Goals / Non-Goals

**Goals:**
- Let a non-recipient holding a sealed Task Notice Accept it, after confirming, redirecting the
  assignment to them.
- Keep the Core transition matrix (`ScribeAssignmentTransitions.CanApply`) completely untouched —
  redirect is target-identity resolution that happens *before* the existing Accept transition,
  not a new transition.
- Leave Decline's existing non-recipient rejection behavior exactly as it is today.

**Non-Goals:**
- No new `ScribeAssignmentState` value and no change to the transition matrix.
- No visible trace for the original recipient (decided: silent disappearance).
- No generic reusable "Yes/No modal" component — this is one purpose-built popup for this one
  flow, matching how `GuiDialogScribeQuestPrompt` is also purpose-built rather than generic.
- No new wire field to carry "this Accept was confirmed" — the confirmation is a client-side UX
  gate, not a server-enforced precondition (see Decisions).

## Decisions

### D1: Redirect happens by rewriting the existing record's `TargetPlayerUid`, not a new state or a forked record
Add two new fields to `ScribeAssignment` (`src/Core/ScribeAssignment.cs`): `RedirectedFromUid`
(string?) and `RedirectedDate` (string?), both null unless a redirect happened. Add one new Core
store method:

```
public bool TryRedirectTarget(Guid assignmentId, string newTargetPlayerUid, string redirectedDate)
```

mirroring `TryMarkReceived`'s shape: looks up the record, requires `State == Unaccepted` (a
redirect only ever precedes an Accept), and if the new target actually differs from the current
one, stamps `RedirectedFromUid = assignment.TargetPlayerUid`, sets
`TargetPlayerUid = newTargetPlayerUid`, and `RedirectedDate = redirectedDate`. Returns false
(record untouched) otherwise. `Clone()` copies both new fields alongside the other metadata
fields it already copies.

Bumps the store's binary codec from v7 to v8, appending
`WriteOptionalString(w, assignment.RedirectedFromUid)` and `RedirectedDate` after the existing
v7 `ReceivedDate` field, following the exact append-only pattern already documented at
`ScribeAssignmentStore.cs:264-292` (a pre-v8 blob genuinely never had a redirect, so defaulting
both to null on read is correct, not lossy).

**Alternative considered**: fork into a linked sibling record (original marked terminal, a new
record created for the new target). Rejected — full audit purity isn't needed here (the decided
UX trade-off already accepts that the original recipient loses all visibility), and forking
would touch every `Sent`/`Received` query path plus a new linking concept the store doesn't have
today, for a benefit nobody asked for.

### D2: The identity check in `ApplyTaskNoticeAction` splits by action instead of gating both
Today's single check (`Delivery.cs:167-173`) runs before branching on Accept vs. Decline and
rejects both alike on a mismatch. This becomes: Decline keeps exactly today's check, unchanged.
Accept no longer checks recipient identity as a rejection condition; instead, for each block
whose `Assignment.TargetPlayerUid != fromPlayer.PlayerUID`, call `TryRedirectTarget` (per-block,
since each row is independently addressable even though in practice every row in one notice
shares the same recipient) before the existing `TryMarkReceived` + `TryApplyAction` calls. After
the redirect, `TryApplyAction`'s own role resolution now legally matches Assignee, and the rest
of the method (placement, `AcceptedIntoLabel`, pushes to both parties) runs completely unchanged.

### D3: The confirmation is enforced client-side only, not via a new wire field
`GuiDialogTaskNotice.AcceptOnto` (`GuiDialogTaskNotice.cs:353-356`) gains a check: if
`document.Blocks[0].Assignment?.TargetPlayerUid != capi.World.Player.PlayerUID`, open the new
confirm popup instead of calling `SendTaskNoticeAction` immediately; only send on confirm. No
new field is added to `ScribeTaskNoticeActionMessage` to mark "this was confirmed."

**Alternative considered**: add a `Confirmed` bool to the wire message and re-check identity
server-side, rejecting an unconfirmed mismatched Accept. Rejected — the server already fully
trusts the client's chosen destination candidate and Accept/Decline intent for every other path;
this confirmation exists purely so an honest client warns its own player before doing something
they might not have meant to do, not as a security boundary against a hostile one. A modified
client that skips its own confirmation only lets that player skip warning *themselves* about
redirecting an item they're already physically holding — no different in kind from any other
client-trust boundary already accepted elsewhere in this codebase (e.g. the destination
candidate itself). Decline stays hard-gated server-side regardless, since that path is unchanged.

### D4: The confirm popup is a new small standalone dialog, not a reusable primitive
New class `GuiDialogTaskNoticeRedirectConfirm`, sibling file to `GuiDialogTaskNotice.cs`,
constructed on demand (`new GuiDialogTaskNoticeRedirectConfirm(capi, recipientName, onConfirm);
dialog.TryOpen();`) exactly like `ItemScribeTaskNotice.OpenTaskNoticeDialog` constructs
`GuiDialogTaskNotice` today — not the self-managing subscribe-and-tick pattern
`GuiDialogScribeQuestPrompt` uses, since there is no pending-queue concept here: this dialog
exists only for the duration of one Accept click's decision and is disposed on either button.
It follows `GuiDialogScribeQuestPrompt`'s `WindowFrame`-hosted construction (fixed size,
draggable, centered, `DrawOrder => 0.2`) so it visually matches the one existing "real popup"
precedent in this codebase, layered over the already-open `GuiDialogTaskNotice` (also `DrawOrder
0.2` — same-band dialogs already stack fine by open-order, per
`GuiDialogScribeQuestPrompt`'s own doc comment about opening over a Lectern/Notebook/Tablet).
Its body is just the warning text (built with the recipient's resolved name, reusing
`ResolvePlayerName`'s pattern) plus Confirm/Cancel buttons — no picker, no settings shortcut,
nothing else `GuiDialogScribeQuestPrompt` has that this doesn't need.

### D5: The redirect trace reuses the existing per-transition metaLine convention, unconditionally
`ScribeInboxRowData` (built in `ScribeDialogBase.ViewSwitching.cs`'s
`ComputeSentAssignmentRows`/`ComputeReceivedAssignmentRows`) gains `RedirectedFromUid` and
`RedirectedDate`. `BuildExpandedDetail` (`ScribeInboxContent.cs:421-449`) appends one more
conditional line, following the exact `if (data.XDate is { } x) metaLines.Add(...)` shape already
used for every other transition stub, with a new lang key
`scribe-assignment-redirected-on`(originalRecipientName, newRecipientName, date)`
resolving both UIDs via the existing `Widget.ResolvePlayerName` helper (`ScribeInboxContent.cs:423`
already calls this for `AssignerUid`).

Like every other metaLine in that method, this is **not** gated by `ViewerRole` — it will show
identically in the Assigner's Sent History (per spec) and, as a side effect of reusing the shared
path, in the redirected-to player's own Inbox view of the same record. That's an acceptable
(arguably desirable) side effect of not building a viewer-conditional special case: the spec only
requires the Assigner see it, it doesn't forbid the new Assignee seeing it too.

## Risks / Trade-offs

- **[A modified client could send an Accept packet without ever showing itself the confirm
  popup]** → Mitigation: accepted per D3 — this is a self-directed UX warning, not a security
  boundary; the server-side behavior it gates (any holder may Accept) is the same regardless of
  whether the popup was shown.
- **[The original recipient gets no signal at all that their notice was taken over]** → this is
  the explicitly decided trade-off from the earlier discovery conversation (silent
  disappearance), not a new risk introduced here — noted for completeness, not proposing a
  mitigation.
- **[The new metaLine also surfaces in the redirected-to player's own Inbox, beyond what the spec
  strictly requires]** → Mitigation: none needed; this is harmless transparency to the player who
  just redirected the notice to themselves, not a leak to an uninvolved party.

## Migration Plan

Codec bump v7 → v8 follows the exact append-only precedent already documented in
`ScribeAssignmentStore.cs` (six prior version bumps, same shape): existing v7 and earlier
save-game blobs read back with both new fields defaulting to null, which is correct (they never
had a redirect), no migration script needed. No rollback concern beyond the existing precedent —
an older mod build reading a v8 blob would need the same `MinVersion`/`Version` handling any
future version bump already requires; this isn't new to this change.
