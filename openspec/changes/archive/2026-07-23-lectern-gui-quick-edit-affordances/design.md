## Context

Read view is currently designed and implemented as strictly non-mutating and lock-free:
`BlockEntityScribeLectern.RequestAccess` grants read access unconditionally and "never
touches the lock"; all document mutation flows exclusively through Editor view's
`ScribeEditDocumentMessage` (full document bytes, gated by `ApplyEdit`'s
`fromPlayer.PlayerUID != lockHolderUid` check). This clean separation is why Read view
needs no lock-contention handling at all today — it's a pure projection of server state.

Playtesting flagged that requiring a full mode-switch (right-click → wait for editor
grant → toggle → switch back) just to check off one task is real friction for the single
most common interaction with this GUI. This design covers how to let Read view toggle a
task's done state without abandoning the read/editor separation's other guarantees
(server-authoritative, multiplayer-safe, single-editor-lock-for-bulk-edits).

The other two items in this change (unify Read/Editor dialog width; scale icon-column
widths with `TextSizeScale`) are plain `Mod`-layer GUI constant/layout changes with no
architectural tradeoff — covered briefly at the end, most of this document is about the
Read-view-toggle decision since that's the one with real design weight.

## Goals / Non-Goals

**Goals:**
- Let a player toggle a task's done state from Read view, without first switching to
  Editor mode.
- Preserve multiplayer safety: a Read-view toggle must not silently overwrite a
  concurrent Editor-mode edit, and must not require Read view to newly track/renew a
  lock the way Editor view does.
- Unify Read/Editor dialog width and scale icon columns with `TextSizeScale` (low-risk,
  no architectural tradeoff).

**Non-Goals:**
- Read view is not becoming a second general-purpose editor. Text edits (renaming a
  task, adding/deleting rows, reordering, pinning) stay Editor-view-only in this change —
  only the task-done toggle moves to Read view. If that boundary should move further,
  that's a separate proposal.
- Not changing the single-editor-lock model itself for Editor view's own bulk edits.

## Decisions

**1. A new, narrow message type for the toggle — not routing it through
`ScribeEditDocumentMessage`.**
`ScribeEditDocumentMessage` carries a full serialized document and is checked against
`lockHolderUid`. Reusing it for a Read-view toggle would require either (a) granting the
toggling player the lock transiently (see Decision 2's rejected option) or (b) bypassing
the lock check for this message entirely, which would also let it clobber a concurrent
Editor-mode edit if sent as a full document snapshot. Instead: a new, minimal
`ScribeToggleTaskMessage { PosX, PosY, PosZ, int BlockIndex }` (client → server), applied
server-side via a new `BlockEntityScribeLectern.ToggleTask(int index)` that calls
`Document.ToggleTask(index)` directly on the authoritative document — no lock check, no
full-document round-trip. Server replies by re-syncing the document as normal
(`MarkDirty(redrawOnClient: true)`), the same mechanism Read view already relies on to
stay live.
*Why:* smallest possible surface area — one field mutated in place, not a document
snapshot that could race with an in-progress Editor-mode edit. *Alternative rejected:*
extending `ScribeEditDocumentMessage` with an "index-only toggle" mode — rejected because
it overloads one message type with two different semantics (full snapshot vs.
single-field patch) gated by which fields happen to be null, which is more error-prone
than a distinct type with only the fields it needs.

**2. No lock acquisition for a Read-view toggle — apply directly against the
authoritative `Document`, not through the lock-gated scratch-copy flow.**
Editor view's lock exists to serialize *bulk* edits from a scratch copy that could
otherwise race with another editor's own bulk edit. A single task-toggle is a much
narrower operation: `ScribeDocument.ToggleTask` is already a self-contained, idempotent
mutation on one block's one field. Applying it directly to the live `Document` (not a
scratch copy) server-side means there's nothing to "hold a lock" over — the mutation
either lands or it doesn't, with no multi-step edit session to protect. *Why:* avoids
inventing lock semantics for Read view at all, preserving its current
"never touches the lock" property from the player's perspective (it still doesn't
acquire/release/contend for the editor lock) while still being real writes.
*Alternative considered and rejected:* transiently acquire the lock for the duration of
the toggle request, same as Editor view's `WantEditor` flow, then release immediately.
Rejected: this reintroduces exactly the contention/refusal case ("locked by someone
else") for what should be a fast, low-stakes action — a player toggling a task from Read
view while someone else is in Editor mode would get refused or would silently interrupt
the other player's session, neither of which is acceptable for what's meant to be a
lightweight affordance.
*Risk this creates, accepted:* a Read-view toggle and an in-flight Editor-mode edit could
now both mutate `Document` back-to-back without coordination. Concretely: if Editor
view's autosave tick (`ApplyEdit`) and a Read-view toggle land in the same server tick
window, whichever arrives second wins outright (last-write-wins on the whole document
for `ApplyEdit`, single-field for the toggle) — no merge, no rejection. This is judged
acceptable because: the toggle only ever touches `Done` on one block by index, so the
realistic collision (someone edits text via Editor while someone else toggles the same
or a different task via Read) at worst loses one player's edit to the other's, not
silent corruption; this already matches how any two independent editors' un-coordinated
writes would behave without a lock at all, and the lock's actual job (serializing a
*session's worth* of Editor edits behind one holder) is untouched by this change.
*Mitigation, not a fix:* keep the toggle's window as narrow as possible (send
immediately on click, no batching/debounce) to minimize collision odds. Flagged in Open
Questions below since this is the one place this design accepts a new, previously-absent
race — worth the user's explicit sign-off before implementation, not a silent default.

**3. Read view's row rendering needs a real interactive toggle element, not
`AddStaticText`.**
`ComposeReadView` currently renders every row (task or note) as one `AddStaticText` call
with a `"[x] "`/`"[ ] "` prefix baked into the string — there is no clickable element at
all. Making the checkbox interactive means Read view's task rows need an
`AddSwitch`/toggle element (mirroring `ScribeBlockRowCell`'s existing editor-view toggle,
scaled by `TextSizeScale` per the already-shipped fix), wired to send
`ScribeToggleTaskMessage` on click, while non-task rows keep the existing plain
`AddStaticText` path unchanged. *Why:* reuses the exact toggle-sizing logic Editor view
already has (`ToggleWidth * TextSizeScale`) rather than inventing a second convention.
*Alternative considered:* keep Read view's rows as plain text and require a click
anywhere on the row to toggle — rejected, ambiguous for note rows (which have no toggle
concept) and less discoverable than a real checkbox affordance.

**4. Unify `ReadListWidth`/`EditorListWidth` into one `ListWidth` field.**
Straightforward: both currently exist only because the portrait-reshape work
(`skeuomorphic-lectern-gui`) picked slightly different values (300 vs. 340) per view
without an explicit reason to differ. Collapsing to one field removes the visible
resize-on-switch and one config knob to keep in sync. *Why now, not before:* this is a
one-line config change but was out of scope for the GUI-redesign change that introduced
the two separate fields; bundling it here since the read/editor-parity theme already
connects it to this change's other items.

**5. Icon-column widths scale with `TextSizeScale` via the same `* TextSizeScale`
convention `ToggleWidth` already uses, not a new mechanism.**
`DragHandleWidth`, `PinWidth`, `DeleteWidth` become base values multiplied by
`clientConfig.TextSizeScale` at their `ScribeBlockRowCell` use sites, exactly mirroring
how `ToggleWidth * TextSizeScale` already works (fixed earlier this session). *Why:* no
new pattern, just applying an already-proven one to three more constants that were
missed the first time.

## Risks / Trade-offs

- **[Risk] Decision 2's accepted last-write-wins race between a Read-view toggle and an
  in-flight Editor-mode edit** → Mitigation: send the toggle immediately, no
  client-side batching, to minimize the collision window; documented explicitly rather
  than silently accepted. See Open Questions — needs explicit user sign-off.
- **[Risk] A malicious/buggy client could spam `ScribeToggleTaskMessage` with an
  out-of-range `BlockIndex`** → Mitigation: `ScribeDocument.ToggleTask` already
  bounds-checks via `IsValidIndex` and returns `false`/no-ops rather than throwing
  (confirmed existing behavior, reused as-is) — no new validation needed server-side
  beyond what `ToggleTask` already does.
- **[Risk] Read view previously had zero network write traffic; this adds one small
  message per toggle** → Mitigation: negligible — same order of magnitude as Editor
  view's existing per-click messages (delete, pin, add-task), no batching needed at this
  scale.

## Open Questions

- **RESOLVED 2026-07-20 — accepted as-is.** The last-write-wins race in Decision 2
  (Read-view toggle racing an in-flight Editor-mode autosave) was flagged for explicit
  sign-off; the user accepted it as described, with no stronger guarantee required. The
  Read-view toggle stays lock-free and applies directly to the authoritative document.
  Accepted worst case: a single lost toggle/edit under a same-tick collision — no
  corruption, and no worse than two uncoordinated editors without a lock. Recorded on
  task 1.1.
