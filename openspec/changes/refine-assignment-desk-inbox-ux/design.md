## Context

`add-assignment-and-quest-support` archived 2026-08-31 with the Assignment Desk / Inbox feature
functionally complete but visually and behaviorally unpolished. Playtest feedback the same day identified
seven concrete refinement items, all localized to a small cluster of `src/Mod/` files:
`ScribeAssignmentParticleEmitter.cs`, `ScribeInboxContent.cs` (owns `ScribeAssignmentChip`),
`ScribeAssignmentFormContent.cs`, `BlockEntityScribeWritingStation.cs`, `ScribeDialogBase.cs` /
`ScribeDialogBase.ViewSwitching.cs`, `GuiDialogScribeAssignmentDesk.cs`,
`GuiDialogScribeLecternLibGui.cs`, `GuiDialogScribeChalkboard.cs`, `GuiDialogScribeScriptorium.cs`,
`ScribeModSystem.Assets.cs`, `ScribeRowConstants.cs`, and the settings-surface build code.

Two items needed source investigation before they could be specced accurately:

**The particle seen-trigger bug.** `HasUnseenAssignment` (`ScribeModSystem.cs:135`) is a simple
`myReceivedAssignments.Any(b => b.Assignment is { Seen: false })` — it does not filter by state, so a
Decline does clear it correctly *once the record's `Seen` flag is actually true*. The real defect is
upstream: `ScribeMarkAssignmentsSeenMessage` is only ever sent from `OnClickSwitchToInbox()`
(`ScribeDialogBase.ViewSwitching.cs`), which every Inbox-reaching *nav button* calls. But
`GuiDialogScribeInbox` (the standalone Inbox block) never calls it — its constructor calls
`DefaultToInboxView()` and its `EnterGrantedView()` override calls `LeaveEditorIfActive()` +
`ForceRebuild()`, neither of which marks anything seen. So every open of the standalone Inbox block —
first open and every subsequent right-click re-open — leaves `Seen` untouched no matter how long the
player looks at their assignments or what they do to them inside it. This matches the reported symptom
exactly: "particles only dissipate if the player looks at the Inbox on an Assignment Desk" (whose Inbox
nav button *does* call `OnClickSwitchToInbox()`) "but should dissipate after looking at ANY Inbox tab."

**The "ever assigned" gate.** `ScribeAssignmentStore.TryCreate` (`src/Core/ScribeAssignmentStore.cs`)
only refuses new creates once `_records.Count >= MaxAssignments` (a global cap) — it never evicts or
prunes existing records, including terminal ones (Declined/Cancelled/Discarded/Completed). So
`ScribeModSystem.MyReceivedAssignments` is already a durable "every assignment this player has ever
received, in any state" list; no new persisted per-player flag is needed to answer "has this player ever
been assigned a task" — `MyReceivedAssignments.Count > 0` (synced client-side already) answers it
directly.

## Goals / Non-Goals

**Goals:**
- Make the six assignment states visually distinct via color, not just via label text.
- Give the Inbox and Create Assignments tabs icons that read as their function rather than borrowed
  glyphs (book, scroll) that already mean something else in the mod.
- Fix the particle/shimmer seen-trigger so it clears from a single choke point regardless of which
  Inbox-capable dialog or entry path the player used.
- Retune the particle's range/frequency/silhouette per direct playtest feedback.
- Stop showing an Inbox nav button to players who have no reason to ever look at one.
- Surface LibGUI's own theme picker instead of leaving it fully hidden.
- Tighten the Create Assignments form's layout using LibGUI's flex primitives.

**Non-Goals:**
- No change to `ScribeAssignmentTransitions` / the state machine's legality rules.
- No change to what an expanded Inbox row shows (that's `add-assignment-activity-log`'s scope, a sibling
  in-progress change — this proposal doesn't touch `ScribeInboxRow.BuildExpandedDetail`).
- No new persisted preference or block-entity field — the "ever assigned" gate reads existing synced
  state.
- Not attempting exact hex-value fidelity to "Deep Indigo" / "Rich Plum" etc. as brand colors — these are
  descriptive names the palette below approximates; final tuning happens visually in-game like the
  particle's existing tunable constants.

## Decisions

**D1 — Chip colors are new named constants, not repurposed `NavActive*` ones.** `ScribeRowConstants`
already has `NavActiveRead/Edit/Pinned/Settings/Guestbook`, one of which (`NavActiveGuestbook`, a
plum/mauve) visually overlaps what "Rich Plum/Amethyst" would look like. Reusing it for the Accepted chip
would tie two unrelated meanings (the Inbox nav button's active-state color, and an assignment state) to
one constant, so a future change to one silently changes the other. Adding five new constants
(`AssignmentChipNew`, `AssignmentChipAccepted`, `AssignmentChipRejected` — shared by Declined and
Discarded per the proposal's explicit color-sharing — `AssignmentChipCancelled`,
`AssignmentChipCompleted`) costs nothing and keeps the two concerns independent. Proposed values (0-1
RGBA floats, matching the existing constants' format), picked to sit in the same muted/ink palette as the
existing nav colors and stay legible with the chip's fixed `NavActiveGlyph` foreground text:
- New (Deep Indigo): `(0.29, 0.25, 0.55, 1)`
- Accepted (Rich Plum/Amethyst): `(0.48, 0.29, 0.55, 1)`
- Declined & Discarded (Crimson/Burgundy): `(0.48, 0.12, 0.17, 1)`
- Cancelled (Charcoal/Dark Sepia): `(0.24, 0.23, 0.21, 1)`
- Completed (Verdigris/Emerald Ink): `(0.18, 0.42, 0.37, 1)`

These are starting values for implementation, not locked — like `ScribeAssignmentParticleEmitter`'s own
tunable constants, expect a playtest pass to nudge them.

**D2 — Mark-seen moves to one choke point covering every path onto the Inbox tab.** Rather than adding a
second call site (`DefaultToInboxView` and `EnterGrantedView` each separately calling the mark-seen
packet-send, alongside the existing `OnClickSwitchToInbox` call), fold the send into whichever single
method is guaranteed to run whenever `viewMode` becomes `ScribeLecternView.Inbox` for any reason — first
open, granted re-open, or nav-button switch. Concretely: extract the packet-send into a small
`MarkInboxSeenIfNeeded()` helper, call it from `OnClickSwitchToInbox()` (already does), and call it from
`GuiDialogScribeInbox`'s constructor and `EnterGrantedView()` override. This keeps the "only send when
there's something to mark" no-op-on-the-server behavior (the proposal notes the request is already
unconditional and the server no-ops when nothing was unseen) while guaranteeing every entry path fires it,
rather than trying to hoist it into some single base-class layout hook that every dialog's build cycle
would call every frame.

**D3 — "Ever assigned" reads `MyReceivedAssignments.Count > 0` directly, no new flag.** See Context — the
store never prunes records. `GetExtraNavButtons()` on `GuiDialogScribeLecternLibGui`/
`GuiDialogScribeChalkboard`/`GuiDialogScribeScriptorium` wraps their existing unconditional
`yield return` for the Inbox button in `if (modSystem.MyReceivedAssignments.Count > 0)`. Note this is
evaluated at build time (already re-evaluated on every rebuild via `MyAssignmentsChanged`), so the button
appears live the moment a player's first assignment sync arrives — no dialog reopen needed.

**D4 — Icons are new SVGs registered the same way as every existing icon.** `RegisterSvgIcon` in
`ScribeModSystem.Assets.cs` already maps a string icon-name to an `AssetLocation` SVG; adding
`scribeinboxarrow` → `textures/icons/inbox-arrow.svg` and `scribeplus` → `textures/icons/plus.svg`
follows the existing pattern exactly (see `scribegear` → `gear.svg`, `scribeassignment` → `scroll.svg`).
The SVGs themselves need to be authored/sourced — none of the existing 16 icons in
`textures/icons/` fit (checked: grip, guestbook, pin, book, triangle-{up,down,left,right}, check, info,
gear, gear-hud, close, scroll, edit, timer). Simple two-tone line-art SVGs matching the existing icons'
style (checked visually: flat, minimal, single-color-fillable) are in scope for this change; a polished
hand-drawn icon is not — if the author wants a specific illustration style, that's a follow-up, not a
blocker for this change.

**D5 — Particle constants change value only, not shape of the emitter.** `DetectionRadius` (6 → 12),
`CountMultiplier` (1.0 → 0.6) are one-line constant edits. The spawn-origin move (top-of-block →
mid-block) is a change to `SpawnAt`'s `minPos`/`maxPos` Y band (currently `pos.Y + 0.85` to
`pos.Y + 1.25`; shift down to center around block-mid, e.g. `pos.Y + 0.35` to `pos.Y + 0.75` for a
similar-height band centered lower — exact numbers are an implementation/playtest detail). The
"runs too tall, same duration" requirement is a velocity change, not a `LifeLength` change: reduce the
upward `Velocity`/`GravityEffect` magnitude so the particle covers less vertical distance in the same
`LifeLengthAvg` (2s) — do not shrink `LifeLength`, which would make particles disappear faster rather than
travel a shorter distance.

**D6 — The `.ui settings` prompt is a button, not an auto-launch.** The Settings surface gets a labeled
button (Window Appearance section, alongside the Pixel Art / theme-adjacent controls) whose `onTap`
invokes the same command a player would type. The exact API for programmatically triggering a
client-registered chat command from mod code needs a one-time lookup during implementation (VS's
`ICoreClientAPI.ChatCommands` surface, or simulating the chat input) — flagged as a task, not a design
risk, since worst case it's a two-line call once found.

**D7 — Create Assignments form layout uses `Row`/`Expanded`, matching the wiki example verbatim.** Current
`ScribeAssignmentFormContent` stacks heading / label / picker / label / textfield / button as six
full-width rows in a `Column`. New layout: keep the heading and the task-text field/label as their own
rows (unaffected — the proposal only calls out the target-player row), but collapse the "Send to" label +
player picker + Send button into one `Row` per
https://github.com/ripls56/vslibgui/wiki/Layout's pattern: `Text("Send to")` (fixed), `Expanded(flex: 1,
child: <player picker>)`, then the Send button (fixed). This is a like-for-like translation of the
existing `playerPicker` widget into the row — no change to the dropdown's own behavior (self/other player
listing, live-selection tracking).

## Risks / Trade-offs

- [Icon SVGs are new hand-authored assets, not sourced from an existing library] → Keep them simple
  (flat, single-path, matching the existing icon set's visual weight) so a first pass is low-risk to get
  approximately right; icon art is easy to swap later without touching any other part of this change
  (`RegisterSvgIcon` is the only integration point).
- [Chip color values are the author's/AI's interpretation of named colors like "Rich Plum"] → Flagged as
  tunable in D1; a quick in-game screenshot pass during implementation can adjust the five constants
  without any other code change.
- [Moving the particle spawn origin and shrinking its vertical velocity both affect the same visual
  silhouette] → Test them together in-game rather than in isolation, since a value that looks right in
  the old top-of-block position may look different once recentered to block-mid.
- [The "ever assigned" gate and `add-assignment-activity-log` (sibling in-progress change) both touch
  `MyReceivedAssignments`/`ScribeInboxRow` adjacent code] → No actual overlap: this change only reads
  `MyReceivedAssignments.Count`, `add-assignment-activity-log` only touches
  `ScribeInboxRow.BuildExpandedDetail` and the assignment log-entry model. Land in either order without
  conflict.

## Open Questions

- Exact SVG artwork for the inbox-with-down-arrow and plus icons — author to review a first pass in-game
  before considering this item done (see D4).
- Exact API call for triggering `.ui settings` programmatically from Scribe's own button (see D6) —
  resolve during implementation, not blocking the rest of this change.
