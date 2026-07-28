## Context

The lectern has a server-authoritative single-editor lock (`BlockEntityScribeLectern.lockHolderUid`).
The flow already exists and is mostly correct:

- **Server** (`RequestAccess`): read access is always granted; editor access is granted only if the
  lock is free or already held by the requester, otherwise it replies `granted: false` with
  `RefusalReason = "scribe:scribe-gui-locked"`. On grant it sets `lockHolderUid` to the requester.
- **Server** (`ApplyEdit`): rejects an autosave whose sender is not the current `lockHolderUid`
  (`fromPlayer.PlayerUID != lockHolderUid` → returns `false`).
- **Client** (`HandleServerReply`): on a refused reply it fires `capi.TriggerIngameError(...)` and
  falls back to `EnterReadMode()`; it only calls `EnterEditorMode(...)` when `Granted && EditorMode`.

So on paper the client should never enter the editor on refusal. Yet a two-client playtest
(2026-07-28) found player 2 *can* open the edit view and type, with edits reverting within a few
frames — and, more tellingly, the same revert happens **even when no other player is editing**. That
second symptom cannot be explained by the refusal path alone (which nobody is hitting when the lock
is free), so the client must be entering the editor view along a path that does **not** wait for the
server's granted reply. If entry is optimistic (before/without the grant), then `ApplyEdit`'s lock
check rejects the autosave and the editor's document resyncs back over the local edit — exactly the
observed "types, then reverts."

Constraints: server-authoritative model must hold (no client-trusted lock). No new dependencies.
Multiplayer is only verifiable manually on the two-machine setup (no game install on CI runners).

## Goals / Non-Goals

**Goals:**
- Root-cause BOTH symptoms: (a) player 2 entering the editor + typing despite the lock, and (b) an
  editing player's edits reverting when the lock is free. Confirm whether they share one cause
  (optimistic/ungated editor entry) before changing behavior.
- Editor entry is gated on an actually-granted lock: never enter the editor optimistically or on a
  refused reply.
- A contended lock disables player 2's "switch to editor" affordance and blocks entry; refusal
  surfaces a native in-game error ("Another player is making edits.").
- Preserve the working single-player / uncontended path (enter, edit, autosave persists, lock
  releases on leaving).

**Non-Goals:**
- No collaborative-presence feature (showing which task another player has focused, à la Google
  Docs) — that is a separate roadmap item, explicitly out of scope here.
- No change to the read view (always lock-free), the checkbox-toggle path (always allowed), or the
  document/Core model.
- Not reworking the lock into anything other than the existing single-holder server lock.

## Update (2026-07-28): the "optimistic entry" hypothesis was DISPROVEN during apply

The task 1.1 audit found editor entry is **already fully grant-gated**: `isEditorMode` is set only inside
`EnterEditorMode`, whose sole caller is `HandleServerReply` on `Granted && EditorMode`; every "switch to
editor" affordance only sends the request packet and never flips the view. So there was no optimistic entry
to remove (Decision 1's premise below is wrong), and the "types then reverts" symptom cannot originate in
this client's code. The tester was the **joining** player and the lock is host-authoritative
(`ApplyEdit`/`lockHolderUid` run server-side; `requiredOnServer: true`) — so the governing build was the
**host's**, and a host running an older/mismatched DLL is the leading explanation for symptom (b). What was
actually implemented is the defensive UX (Decisions 3–5): sync lock state to clients, gate the affordance
client-side, and reword the native error. The true root-cause of symptom (b) is still pending a same-build
two-client repro (tasks 1.2/5.3). Decisions 1–2 are retained below as the original reasoning, now known
incomplete.

## Decisions

**1. Diagnose before patching; treat the two symptoms as one hypothesis until proven otherwise.**
The leading hypothesis is a single defect: the editor view is entered without waiting for (or
retaining) the granted lock. First locate every path that calls `dialog.EnterEditorMode(...)` or
otherwise flips the dialog into editor mode, and confirm which ones are reached *without* a
corresponding `Granted` reply. Candidate paths to audit: the read-view "switch to editor" control,
the right-column nav Edit button, the Back-from-settings re-request, and any optimistic pre-grant
entry for responsiveness. Rationale: the reply handler is already correct, so guessing a fix risks
"correcting" the wrong path; a short diagnosis (with a two-client repro) is cheaper than a wrong fix.
Alternative considered — jump straight to disabling the button — rejected: it would mask symptom (b),
which happens with the lock free and no contention at all.

**2. Editor entry is grant-gated, server-authoritative.** The client MUST NOT show the editor view
until `HandleServerReply` delivers `Granted && EditorMode`. Any optimistic pre-grant editor entry is
removed; "switch to editor" requests the lock and stays in read view until the grant arrives, then
swaps. Rationale: this is the direct fix for symptom (b) and keeps the lock the single source of
truth. Alternative — let the client enter optimistically and roll back on refusal — rejected: it is
the current behavior and produces the "type then revert" fl: bad UX and the reported bug.

**3. A contended affordance is disabled client-side, but the server remains the gate.** The
read-view "switch to editor" affordance reflects the unavailable lock (disabled/inert) when the
lectern's lock is known to be held by another player, so player 2 does not get a wasted round-trip or
a misleading affordance. This requires the client to know the lock is held: surface the lock's
held-by-other state to the client (e.g. via the existing block-entity sync `lockHolderUid`, compared
against `capi.World.Player.PlayerUID`), so the dialog can render the affordance state without a
request. Rationale: matches the confirmed UX preference (disable + block activation). Even so, the
authoritative refusal + native error stays as the backstop for the race where the lock is taken
between render and click. Alternative — client allows the click and only the server refuses — rejected
per the scope decision (wasted round-trip, affordance looks usable when it isn't).

**4. Reuse the native error path and an explicit refusal string.** Keep
`capi.TriggerIngameError(this, "scribe-lectern-locked", Lang.Get(...))`. Add/adjust a lang string so
the copy reads "Another player is making edits." (the current `scribe-gui-locked` copy can be reused
or reworded). Rationale: consistent with vanilla feedback, already wired.

**5. Sync the lock state to clients.** To render the disabled affordance (decision 3), `lockHolderUid`
(or a boolean "locked by other") must reach the client via the block-entity tree sync
(`ToTreeAttributes`/`FromTreeAttributes`), following the same Sign-pattern sync used for the document.
Rationale: lets the dialog reflect the lock without polling; matches the persistence guardrail.
Alternative — a dedicated lock-state packet — rejected as heavier than reusing the existing tree sync.

## Risks / Trade-offs

- **Racing the lock between render and click** → the disabled affordance can be briefly stale (player
  2 clicks just as player 1 grabs the lock). Mitigation: the server refusal + native error remains the
  authoritative backstop; entry is still grant-gated, so the worst case is a click that yields a
  native error, not a broken editor.
- **Lock leak on disconnect / crash** → if `lockHolderUid` is not cleared when the holder leaves, the
  lectern stays locked forever. Mitigation: verify the existing release-on-close/disconnect path (the
  spec already requires release on leaving the editor or disconnect); include a scenario for it in the
  retest. This is existing behavior, but the diagnosis should confirm it actually fires.
- **Syncing `lockHolderUid` exposes a player UID to clients** → minor; it is only compared locally to
  decide affordance state. Mitigation: sync a minimal "locked by other" boolean derived server-side if
  exposing the raw UID is undesirable.
- **Manual-only verification** → no CI coverage for multiplayer; regressions can only be caught on the
  two-machine setup. Mitigation: the retest is scripted as explicit two-client scenarios in tasks.

## Migration Plan

No data migration. Behavior-only change to the adapter/GUI layer plus a lang string and a block-entity
tree-sync field. Deploy by restaging the mod and fully relaunching both clients (DLL + lang load at
boot). Rollback is reverting the change; no persisted format changes to unwind (a synced
`lockHolderUid` field is additive and ignored by older clients).

## Open Questions

- Is player 2's editor entry actually optimistic today, or is it reached through the Back-from-settings
  / nav-button path? (Resolved by the diagnosis step — drives exactly which call sites change.)
- Should the synced lock state be the raw `lockHolderUid` or a server-derived "locked by another"
  boolean? (Lean boolean unless the UID is already useful client-side.)
- Does the lock reliably release on an ungraceful disconnect (crash/timeout), not just a clean close?
  (Confirm during diagnosis; add a scenario if it does not.)
