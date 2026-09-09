## Why

A Task Notice's document dialog already opens for anyone holding a sealed notice, regardless of
who it's addressed to — there's no gate on reading it. But clicking Accept as a non-recipient
fails silently: the server rejects the action because the acting player's UID doesn't match the
notice's recorded recipient, while the client-side dialog closes immediately as if it succeeded.
The player is left holding an inert notice with no explanation. Since a physical item can change
hands (dropped, traded, found), a Task Notice's addressee and its holder are not the same thing,
and today's silent rejection is a confusing dead end rather than an intentional design choice.

## What Changes

- A non-recipient holding a sealed Task Notice can Accept it, but only after confirming an
  explicit warning dialog naming the original recipient ("This was assigned to X, not to you.
  Are you sure you want to track and consume this Task Notice?").
- Confirming redirects the assignment to the accepting player: the existing assignment record's
  target is updated to them, and the record gains a stamp of who it was originally addressed to
  and when the redirect happened.
- The original recipient's Inbox simply no longer shows the notice once redirected — no
  ghost record, no notification to them.
- The Assigner's Sent Assignment History keeps showing the outcome as "Accepted" (functionally
  true), with an added detail line noting it was originally assigned to someone else and
  accepted by the redirecting player instead — this is the only place the redirect is traceable,
  which is an intentional, accepted trade-off for allowing the behavior at all.
- Decline is unchanged: a non-recipient still cannot decline a notice that isn't theirs.

## Capabilities

### Modified Capabilities
- `task-notice-item`: Accept is no longer restricted to the notice's original recipient — a
  different holder may accept after confirming a warning, which redirects the assignment to
  them and leaves a trace in the Assigner's Sent Assignment History.

## Impact

- `src/Mod/ItemScribeTaskNotice.cs` / `GuiDialogTaskNotice.cs`: the Accept flow gains a
  confirmation step when the acting player isn't the notice's recorded recipient, via a new
  small standalone popup dialog (in the existing style of `GuiDialogScribeQuestPrompt`) layered
  over the already-open notice dialog.
- `src/Mod/ScribeModSystem.Delivery.cs`: `ApplyTaskNoticeAction` no longer silently drops a
  non-recipient's confirmed Accept; instead it rewrites the assignment's target before applying
  the normal Accept transition.
- `src/Core/ScribeAssignment.cs`: gains fields to record the original recipient and redirect
  timestamp for display in Sent Assignment History. No new state and no new transition — the
  existing Unaccepted → Accepted transition is unchanged; only the target-identity resolution
  ahead of it changes for this one item type.
- `src/Mod/ScribeInboxContent.cs` and the assignment lang keys: a new detail-line entry for
  redirected acceptances, following the existing `"{Verb} — {date}"` convention.
