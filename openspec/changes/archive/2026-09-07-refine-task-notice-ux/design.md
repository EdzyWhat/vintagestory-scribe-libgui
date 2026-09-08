## Context

`add-assignment-physical-delivery-mode` shipped the Task Notice item and its Accept dialog
(`GuiDialogTaskNotice.cs`). Its first real playtest (2026-09-02, submission `2026-09-02T20-53-17`)
surfaced four gaps that survive the model/lang-key fixes already hotfixed directly against that
change's own task 3.3:

- The Accept dialog is plain LibGUI chrome — functional, but doesn't read as "an unfurled letter."
  Its fixed 460×560 window also can't track the player's Pixel Art Size preference the way every
  other Scribe surface does, and a stock `WindowFrame` title strip would sit redundantly on top of
  a parchment asset that already frames its own title area.
- Its footer layout breaks when Accept needs a destination picker: Decline/Accept stretch full-width
  and the picker pushes them off the right edge instead of stacking cleanly.
- **The Assigner gets no record of a sent notice until the Assignee Accepts it.** This was a
  *deliberate* requirement in `task-notice-item`'s spec ("An unaccepted Task Notice has no
  assignment-store record" — the physical item is the sole record) — but the playtester's actual
  reaction ("I thought I was only deleting it for myself..." on a related, since-resolved report;
  and directly here: "no Sent History record is created... it should say 'Sent'") shows the
  no-record design reads as broken, not as intentional. Per the author's own call, this requirement
  is reversed by this change.
- The notice's task rows render a completion checkbox that looks identical to an interactive one,
  but `GuiDialogTaskNotice` passes `readOnly: true, completionAndPinLive: false` to
  `ScribeReadContent` — the checkbox's `onChanged` is already `null`; only its *visual* affordance is
  wrong.

Today's send path (`ScribeModSystem.Assignment.SendBatchViaNotice`) seals every row directly into
the notice item's own `ScribeDocument` via `ScribeDocumentAttributes.WriteTo` and never touches
`ScribeAssignmentStore` — the store record is created only on Accept, via
`ScribeAssignmentStore.TryCreateAccepted` (`OnServerReceivedTaskNoticeAction`).

## Goals / Non-Goals

**Goals:**
- A sent notice shows up in the Assigner's Sent Assignment History immediately, labeled "Sent."
- The Assignee's Inbox stays silent until the physical notice actually reaches their inventory,
  then shows it as "Received," followed by the existing Accept/Decline flow unchanged.
- The Accept dialog reads as a parchment/scroll letter, with a footer layout that never clips or
  overflows regardless of how many eligible destination items the Assignee is carrying.
- The dialog scales with the player's Pixel Art Size setting the same way every other Scribe
  surface does, sized from the parchment art's own aspect ratio rather than a fixed pixel box, and
  reads as noticeably smaller than a full Notebook/Lectern page — it's a short personal note, not
  a whole document.
- The read-only task rows visually communicate "not clickable" instead of looking like a normal,
  live checkbox.

**Non-Goals:**
- Any change to Accept/Decline's actual consequences (destination binding, sync-like-in-range) —
  only when the store record starts existing and how it's labeled before Accept.
- A new interactive "check things off directly from the notice" surface (item #6's alternative) —
  rejected in favor of the disabled-style fix; see Decision 4.
- Re-tuning the notice's 3D model/transforms or the missing-lang-key text — already fixed directly
  against `add-assignment-physical-delivery-mode` 3.3 (small, spec-conformant bug fixes, not a
  design change).

## Decisions

### Decision 1: A new `Sent` assignment state, created at send time, hidden from the Assignee until physically received

Add `ScribeAssignmentState.Sent` (before `Unaccepted` in the lifecycle, store-only — never applied
via the normal `ScribeAssignmentTransitions.CanApply` matrix, matching how `TryCreateAccepted`
already bypasses that matrix for notice-Accept). `SendBatchViaNotice` gains a `TryCreateSent`
store call (parallel to `TryCreateAccepted`) per row, using the *same* `assignmentId`/`TaskId`
already embedded in the sealed notice's document — one logical record, two carriers during the
pre-receipt window (the item, for hand-delivery; the store, for Sent History display).

`ScribeAssignmentStore.Received(playerUid)` gets a `State != Sent` filter so these rows stay
invisible in the Assignee's Inbox. `Sent(playerUid)` gets no such filter — a `Sent`-state row
shows immediately, labeled via a new chip/state string ("Sent," reusing the same chip-rendering
switch `ScribeInboxContent` already has for Unaccepted/Accepted/etc.).

**Alternative considered:** keep today's model (no eager records) and instead show a lightweight,
non-authoritative "pending" placeholder row in Sent History derived from the notice's own embedded
document, reading it directly. Rejected: it would need a second, parallel display path just for
this one pre-receipt window, duplicating logic Sent History already has for every other state, and
would drift the moment `TryCreateSent` needs to also gate deletion/cancellation semantics later.

### Decision 2: "Received" is detected the same way as the existing proximity signal — inventory possession, not dialog-open

The feedback asks for the Inbox to update "at which point [the Assignee] gets the item into their
inventory," not "when they choose to open it." `task-notice-proximity-signal` already runs a
per-player heartbat tick server-side to detect nearby outstanding notices (`AdjustOutstandingNoticeCount`
+ the proximity scan). Extend that same tick: for each `Sent`-state record addressed to this player,
check whether a sealed notice carrying that record's id is now anywhere in their own inventory
(mirrors `ScribeAcceptCandidates`' existing inventory-scan shape). On a match, call a new store
method `TryMarkReceived(assignmentId, receivedDate)` transitioning `Sent → Unaccepted` and stamping
a new `ScribeAssignment.ReceivedDate` (same pattern as `AcceptedDate`/`CompletedDate`). From this
point the record is a completely ordinary Unaccepted assignment — Accept/Decline in
`OnServerReceivedTaskNoticeAction` switch from today's `TryCreateAccepted`/no-op-on-Decline to the
*existing* `TryApplyAction` transition path, since the record already exists. This also simplifies
that handler: it no longer special-cases notice-Accept creation.

**Alternative considered:** transition on dialog-open (right-click) instead of inventory-possession.
Rejected — simpler (no tick reuse needed), but doesn't match "gets the item into their inventory,"
and would leave the Inbox silent even after pickup until the Assignee happens to open the notice,
the exact staleness the feedback is about.

### Decision 3: Custom chrome with a borrowed visual pattern — not stock `WindowFrame`, not a new dialog base class

`GuiDialogTaskNotice` drops LibGUI's stock `WindowFrame` (fixed 28px title-bar chrome, generic
close/drag) entirely and builds its own title bar + 3-column inset frame, visually matching
`ScribeDialogBase.Layout`'s pattern (`BuildTitleBar` / `BuildSectionInnerBox`: drag band, close
button, symmetric side-margin columns framing the center content) — but as **new code local to this
file**, not shared with `ScribeDialogBase`. It still stays a standalone `GuiBase`, per its existing
remarks: `ScribeDialogBase`'s contract requires a live, mutable `IScribeDocumentHost`
(`SetTaskDoneFromReader`, `DeleteTaskFromReader`, `PersistFromReader`, …), none of which make sense
for a frozen, one-shot document snapshot. This is the deliberate middle ground between "keep stock
`WindowFrame`" (redundant once the parchment art frames its own title area, and can't scale with
the player's Pixel Art Size setting) and "become a real `ScribeDialogBase`" (stretches a
live-document contract onto a snapshot).

Backdrop + sizing: add a pixel-art parchment texture as this custom frame's background (same
`SKBitmap`-backdrop mechanism the tablet/lectern surfaces already use — see `gui-backdrop`
capability), replacing the flat `colors.Surface` container fill. Sizing mirrors `ScribeLayout`'s
`H = W * AspectH` shape, computed inline (no `IScribeDocumentHost.GetLayout` — there is no host):
- `AspectH` is fixed at the parchment PNG's own ratio (130/105 ≈ 1.238 — a taller-than-wide scroll,
  unlike the notebook/lectern's near-square art), matching how every *real* (non-placeholder)
  backdrop's `AspectH` already matches its own art's ratio.
- `W` is derived from `modSystem.MySettings.PixelArtSize` (default 600, range 400–1000) scaled by a
  fixed factor of 2/3 (400/600) — the author's explicit call that a notice should read smaller than
  a full Notebook/Lectern page. This factor is a first-pass tuning constant, expected to move after
  the in-game look (task 4.3).
- `GuiBase`'s own band-drag (`WindowConfig.DragHandleHeight`, covering the custom title band) makes
  the window draggable with no grip-drag reimplementation needed — `ScribeDialogBase` only hand-rolls
  grip-drag because a tooltip's `MouseRegion` swallows the click on its grip *glyph* specifically;
  this title bar has no such tooltip, so the plain band-drag suffices.

Footer layout fix (unchanged from the prior draft): `BuildActionRow` wraps Decline/Accept in
`MainAxisSize.Min`-sized buttons (already close; the actual overflow is `BuildAcceptControl`'s
multi-candidate picker rendering as a `Column` *inside* the same `Row` as the buttons instead of
above it) — restructure so the picker, when shown, is its own full-width `Row` above
`BuildActionRow` rather than stacked inside one of its cells.

**Alternative considered:** keep the stock `WindowFrame` and only swap its background fill (the
original Decision 3). Rejected — the parchment art already frames its own title area, so a generic
28px title strip sits redundantly on top of it, and a fixed-size window can't track the player's
Pixel Art Size preference the way every other Scribe surface does.

**Alternative considered:** extract `ScribeDialogBase.Layout`'s title-bar/3-column code into shared
helpers both classes call. Rejected per the author's explicit choice — see the Risks note below.

### Decision 4: Disabled-looking checkbox, not a new interactive surface

Of item #6's two options (make the notice interactive, or visually disable it), pick the latter:
`ScribeRowWidgets.BuildTaskCheckbox` already receives `onChanged: null` for a non-interactive row;
give it a muted `CheckboxStyle` (lower-opacity border/check color) when `onChanged is null`, so
"can't click this" is visible instead of inferred. Making the notice its own interactive
check-off surface would need a real second write-through path for a document that's supposed to be
a frozen, one-shot snapshot (see the class's own remarks) — a much larger change for a dialog whose
entire lifetime is "read it, then Accept or Decline."

## Risks / Trade-offs

- [Duplicated title-bar/margin code between `ScribeDialogBase.Layout` and `GuiDialogTaskNotice`] →
  accepted per the author's explicit choice (a standalone widget with a borrowed visual pattern)
  over sharing code with `ScribeDialogBase`. A future chrome-wide restyle needs updating both
  places, but the two surfaces' underlying contracts (live host vs. frozen snapshot) differ enough
  that shared code would need its own abstraction layer anyway.

- [Two representations of the same pre-receipt assignment — the store's `Sent` record and the
  item's embedded document] → they're never both mutated; the store record is Sent-History-display
  only until `TryMarkReceived` fires, at which point the physical item's copy is what Accept/Decline
  actually read from (unchanged from today). If a notice is lost/destroyed before receipt, its
  `Sent` record simply never transitions — acceptable staleness (`assignment-lifecycle-bug-fixes`
  precedent: a stuck non-terminal record is a known, low-severity class, not a correctness bug).
- [Extending the proximity heartbeat's scan to also check inventory contents] → bounded by the same
  cheap "does this player have anything outstanding" gate the heartbeat already uses
  (`AdjustOutstandingNoticeCount`); no new tick, no new per-tick cost for players with nothing
  outstanding.

## Open Questions

- The 2/3 Pixel-Art-Size scale factor and the exact title-bar/margin proportions are first-pass
  numbers; expect fine-tuning after task 4.3's in-game look.
