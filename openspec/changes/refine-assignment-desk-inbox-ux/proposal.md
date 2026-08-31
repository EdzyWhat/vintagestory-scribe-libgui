## Why

The Assignment Desk / Inbox feature (`add-assignment-and-quest-support`, archived 2026-08-31) shipped
with placeholder-grade polish: a generic "New Assignment" tab name, gray terminal-state chips that don't
read apart from each other, borrowed icons (a book for Inbox, a scroll for Assignment), an oversized
create-and-send form, and an ambient particle indicator with several playtest-reported bugs (it doesn't
stop on Decline, only clears via the Assignment Desk specifically, has a very short trigger range, and
runs visually too tall). It's also currently always-visible on every Lectern/Scriptorium/Chalkboard even
for players who have never received an assignment. This change is a UX refinement pass over that surface
based on direct playtest feedback (2026-08-31), delivered as one batch since every item touches the same
small cluster of files.

A second, deeper limitation surfaced during that same feedback pass: the Create Assignments tab can only
send a bare freeform-text checkbox task. Every richer task kind the mod actually supports (Tracker, Craft,
Link, Text, subtasks) can only be authored in a document's Editor view or via a Handbook "Add to Scribe"
link — neither path can be turned into a player-to-player assignment. That's not viable as the mod's only
assignment-creation surface, so this change also replaces the freeform quick-send with a flow for
delegating existing, already-authored rows of any kind.

## What Changes

- Rename the Assignment Desk's create-and-send tab from "New Assignment" to **"Create Assignments"**
  (label + hover tooltip). The shared Inbox tab's label is already "Assignment Inbox" — no change needed
  there, but its tooltip is confirmed to match.
- Give each of the six assignment-state filter/row chips a distinct, named color: New → Deep Indigo,
  Accepted → Rich Plum/Amethyst, Declined and Discarded → Crimson/Burgundy (sharing one color — both are
  terminal rejections), Cancelled → Charcoal/Dark Sepia, Completed → Verdigris/Emerald Ink. Replaces the
  current scheme where four of the six states render as the same flat gray.
- Add two new icon assets and wire them in: an inbox-with-down-arrow glyph for the shared Inbox tab
  (replacing the borrowed book icon), and a plus glyph for the Assignment Desk's Create Assignments tab
  (replacing the borrowed scroll icon). Neither icon exists on disk today (checked
  `src/Mod/assets/scribe/textures/icons/`) — both are new SVGs.
- Gate the Inbox nav button on the Lectern, Scriptorium, and Chalkboard behind "has this player ever
  received an assignment" (derived from `ScribeModSystem.MyReceivedAssignments` being non-empty —
  assignment records are never pruned from the store, so this is a durable "ever" check with no new
  persisted flag needed). The Assignment Desk and the standalone Inbox block are unaffected — they always
  show their Inbox surface regardless of history.
- Fix the ambient unseen-assignment particle's seen-trigger bug: today `ScribeMarkAssignmentsSeenMessage`
  only fires from `OnClickSwitchToInbox()`, which every Inbox-reaching *nav button* calls — but the
  standalone Inbox block's dialog lands on its (only) Inbox view via `DefaultToInboxView()` on both
  initial construction and every subsequent granted re-open (`EnterGrantedView()`), neither of which calls
  it. So opening the standalone Inbox block never marks anything seen, and the particle (and nav shimmer)
  can persist indefinitely regardless of what the player does inside it, including after a Decline. Fix:
  mark-seen fires from one choke point that covers every path onto the Inbox tab, not just nav-button
  clicks.
- Retune the particle emitter (`ScribeAssignmentParticleEmitter`): trigger/detection radius 6 → 12 blocks;
  frequency (`CountMultiplier`) 1.0 → 0.6; spawn origin moves from just above the block's top face to its
  vertical middle; vertical travel shrinks to ~2/3 of its current height while total particle lifetime is
  unchanged (particles rise more slowly, not for less time).
- Add a button/prompt in Scribe Settings that runs the `.ui settings` client command, surfacing LibGUI's
  own theme picker (currently reachable only if a player already knows that hidden command exists).
- Rework the Create Assignments tab's send form using LibGUI's `Row`/`Expanded` flex layout in place of
  its current full-width-stacked `Column`: the "Send to" label as fixed-width `Text`, the player picker as
  `Expanded(flex: 1)`, and the Send button at fixed width, all on one row — replacing three stacked
  full-width rows (label / dropdown / label / textfield / button) with a tighter layout.
  **Superseded by the item below** — see that item's note.

### New: multi-item assignment creation from an existing document

- **Replace** the freeform-text quick-send entirely (not add alongside it) with an item-staging flow: the
  assigner drops one of their own Scribe items (Notebook, Lectern document, etc.) into a slot on the
  Create Assignments tab — mirroring the Scriptorium's existing Transcribe copy-slot, the closest local
  precedent for "stage an item, then act on its contents." The tab then renders that document's rows
  Read-view-style, each with its own selection checkbox (independent of any Task/Tracker done-checkbox the
  row already has). The assigner multi-selects one or more rows — any kind (Task, Tracker, Craft, Link,
  Text, subtasks) — picks a single recipient for the whole batch via the existing target-player picker
  (unchanged, still one recipient per send), and sends.
- Sending creates one independent `ScribeAssignment` record per selected row, all addressed to that one
  chosen recipient — **not** one bundled assignment. Each behaves exactly like today's assignments (its
  own Accept/Decline/Cancel/Discard lifecycle, its own row in the recipient's Inbox); no new "bundle"
  concept is introduced in the state machine or store.
- Add a "Delete from source on send" checkbox to the form. Checked: every selected row is removed from the
  assigner's staged document the moment the send completes (a move, not a copy). Unchecked (its default):
  the staged document is untouched — the assigner keeps working copies of what they just delegated. The
  checkbox resets to unchecked every time the tab is used; it is a deliberate per-send choice, not a saved
  preference.
- This retires the "Send to" freeform-text field and its Send button added earlier in this same change
  (the flex-row item directly above) — that Row/Expanded layout work is superseded, not extended. The
  target-player picker itself survives unchanged (still one recipient, still the same control); only the
  adjacent free-text field and its immediately-adjacent Send button are removed in favor of the new slot +
  row-list + batch-send flow.
- **Designed (design.md D8-D13); not yet implemented.** This is a materially larger surface than the rest
  of this batch (new staged-item slot state, a Read-view-style multi-select renderer, batch-send
  networking). Its design pass is done and specced (`specs/assignment-multi-item-creation/spec.md`);
  tasks.md group 9 tracks the remaining implementation work.

Out of scope: no change to the assignment state machine's transitions/validity rules beyond what's needed
for the new creation flow, no change to what data an Inbox row shows when expanded, no change to who can
create/send an assignment (still Assignment Desk only). More refinement items may be appended to this same
change as implementation proceeds.

## Capabilities

### New Capabilities
- `assignment-multi-item-creation` (specced — see `specs/assignment-multi-item-creation/spec.md`; not yet
  implemented, tracked by tasks.md group 9): staging an existing Scribe item into a slot on the Create
  Assignments tab, rendering its rows Read-view-style with independent multi-select checkboxes, sending
  each selected row as its own independent assignment to one chosen recipient, and an optional
  move-not-copy "delete from source on send" toggle (default off, resets every send).

### Modified Capabilities
- `inbox-tab`: state-chip colors (five named colors replacing the current two-tone scheme), the Inbox
  tab's icon, the ambient particle's seen-trigger fix / detection radius / frequency / spawn
  position/travel height, and a new gating rule for the Inbox nav button on non-Desk/non-Inbox-block
  surfaces (has the player ever received an assignment).
- `assignment-desk-block`: the Assignment tab's rename to "Create Assignments" (label + tooltip), its new
  plus icon, and the Create Assignments tab's content becoming `assignment-multi-item-creation`'s flow
  rather than a freeform-text field; the target-player row's flex layout survives unchanged (see that
  capability's spec).
- `lectern-block`, `scriptorium-block`, `chalkboard-block`: each block's existing "exposes an Inbox nav
  button" requirement gains the ever-assigned gating clause defined by `inbox-tab`.
- `settings-tab`: a new control that opens LibGUI's theme picker via the `.ui settings` client command.
- `assignment-state-machine`: the existing "New (unseen) is a flag on Unaccepted" requirement is
  tightened to explicitly cover every path onto the Inbox view (not only nav-button clicks) — closing
  the loophole that let the standalone Inbox block's dialog never mark anything seen.

## Impact

- Code: `src/Mod/assets/scribe/lang/en.json` (tab label, tooltip strings), `src/Mod/ScribeModSystem.Assets.cs`
  (icon registration), two new SVGs under `src/Mod/assets/scribe/textures/icons/`,
  `src/Mod/ScribeInboxContent.cs` / `ScribeAssignmentChip` (chip colors), `src/Mod/ScribeRowConstants.cs`
  (new named color constants), `src/Mod/ScribeAssignmentParticleEmitter.cs` (radius/frequency/position/
  travel-height constants), `src/Mod/BlockEntityScribeWritingStation.cs` (particle trigger condition),
  `src/Mod/ScribeDialogBase.ViewSwitching.cs` / `ScribeDialogBase.cs` (mark-seen choke point,
  `DefaultToInboxView`/`EnterGrantedView` paths), `GuiDialogScribeLecternLibGui.cs`,
  `GuiDialogScribeChalkboard.cs`, `GuiDialogScribeScriptorium.cs` (Inbox nav button gating),
  `src/Mod/ScribeAssignmentFormContent.cs` (flex-row form layout), `src/Mod/ScribeModSystem.Settings*.cs`
  or wherever the Settings widget builds its Appearance section (new `.ui settings` button).
- No `src/Core/` involvement for the 7 already-implemented items — every one of those is presentation/
  GUI-adapter layer in `src/Mod/`. No network message shape changes and no persistence schema changes for
  them either (the "ever assigned" gate reads existing synced data; no new preference or block-entity
  field is introduced).
- `assignment-multi-item-creation` (once designed) will very likely need: a new network message shape for
  a batch send (N rows, one recipient, one delete-from-source flag), reuse or extension of
  `ScribeDialogBase.ComputeAcceptCandidates`-style item resolution but for "pick a source item to stage"
  rather than "pick an accept target," and touches `src/Core/` only if the batch-send validation logic
  (e.g., rejecting an empty selection) is judged game-agnostic enough to live there — default assumption
  is it stays in `src/Mod/` alongside every other assignment-flow message handler, matching this change's
  existing pattern.
