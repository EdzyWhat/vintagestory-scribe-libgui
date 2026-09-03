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

**`assignment-multi-item-creation`'s investigation** (once the 7-item batch above was underway, the same
playtest pass surfaced this deeper need — see proposal.md's "New: multi-item assignment creation"
section). Three structural facts, found by reading the actual code rather than assuming from the
proposal's wording:
- `ScribeAssignmentStore.TryCreate` (`src/Core/ScribeAssignmentStore.cs:59`) only knows how to build a
  Task-kind record from plain text — it never touches `ScribeBlock.Kind`/`TargetItemCode`/
  `TargetQuantity`/`LinkTarget`/`LinkLabel`/`LinkDescription`/`Depth`, even though `ScribeBlock` already
  carries every one of those fields and the store's own binary serializer (`WriteRecordList`/
  `TryReadRecordList`) already round-trips all of them. The storage layer is not the constraint; only the
  creation entry point is narrower than what it stores.
- `IScribeDocumentItem` is implemented only by `ItemScribeNotebook`, `ItemClockmakerNotebook`, and
  `ItemScribeTablet` — a placed Lectern/Scriptorium/Assignment Desk's document lives on its
  `BlockEntityScribeWritingStation`, not an `ItemStack`, so it alone would not qualify a picked-up Lectern
  for an `IScribeDocumentItem`-gated slot.
- That gap is already closed by a purpose-built slot type: `ItemSlotScribeDocument.CanHold`
  (`src/Mod/ItemSlotScribeDocument.cs:35`) accepts a source slot whose `Collectible` is either
  `IScribeDocumentItem` **or** `BlockScribeWritingStation` — the latter covers a picked-up Lectern/
  Scriptorium/Assignment Desk, which carries its document onto the block-item via
  `BlockScribeWritingStation.GetDrops`. This is the exact slot type `BlockEntityScriptorium`'s three copy/
  import-export slots already use, so a Lectern really can be staged the same way a Notebook can — no new
  accept-filter needs writing.
- `BlockEntityAssignmentDesk` (`src/Mod/BlockEntityAssignmentDesk.cs`) currently has no `Inventory` at
  all — unlike its sibling `BlockEntityScriptorium`, which declares a 3-slot `InventoryGeneric` (lazy
  `EnsureInventory`, `Initialize`/`LateInitialize`, `ToTreeAttributes`/`FromTreeAttributes` under a
  dedicated sub-tree key, `OnReceivedClientPacket` packet forwarding, `OnBlockBroken` → `DropAll`). Adding
  a 1-slot inventory to the Assignment Desk is a mechanical mirror of that existing pattern, not new
  architecture.

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
- Let the assigner delegate any existing, already-authored row (Task, Tracker, Craft, Link, Text,
  subtasks) from one of their own Scribe items, instead of only a bare freeform-text quick-send.

**Non-Goals:**
- No change to `ScribeAssignmentTransitions` / the state machine's legality rules.
- No change to what an expanded Inbox row shows (that's `add-assignment-activity-log`'s scope, a sibling
  in-progress change — this proposal doesn't touch `ScribeInboxRow.BuildExpandedDetail`).
- No new persisted preference or block-entity field — the "ever assigned" gate reads existing synced
  state. (The new "Delete from source on send" checkbox is UI-only session state too — see D13 — not a
  new persisted preference either.)
- Not attempting exact hex-value fidelity to "Deep Indigo" / "Rich Plum" etc. as brand colors — these are
  descriptive names the palette below approximates; final tuning happens visually in-game like the
  particle's existing tunable constants.
- No "bundle" concept in the state machine or store — a multi-row send still creates N fully independent
  `ScribeAssignment` records, each with its own lifecycle, per proposal.md.

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

### `assignment-multi-item-creation` decisions

**D8 — The staging slot mirrors `BlockEntityScriptorium`'s copy-slot verbatim, including its slot type.**
Add a 1-slot `InventoryGeneric` to `BlockEntityAssignmentDesk`, following the Scriptorium's exact
plumbing (lazy `EnsureInventory`, `Initialize`/`LateInitialize` with `Pos` set, `ToTreeAttributes`/
`FromTreeAttributes` under its own sub-tree key so it's additive for existing saves, packet-id routing in
`OnReceivedClientPacket`, `OnBlockBroken` → `DropAll`). Populate it with the SAME `ItemSlotScribeDocument`
slot type the Scriptorium already uses — per author direction ("borrow that model") this is a literal
reuse, not a new type. Its existing `CanHold` already accepts both `IScribeDocumentItem` (Notebook/
Tablet) and `BlockScribeWritingStation` (a picked-up Lectern/Scriptorium/Assignment Desk), so nothing
about acceptance needs to change to support staging any of those. Whatever lands in the slot is read
fresh via `ScribeDocumentAttributes.TryReadFrom` on every rebuild (a pure client-side read of already-
synced inventory state, exactly like the Scriptorium's own slot reads) — an item with no readable
document (or the slot sitting empty) shows the tab's empty state, the same graceful "no document" the
Scriptorium's slots already fall back to.

**D9 — No new lock concept.** Held items (Notebook/Tablet) have no lock at all
(`NotebookHost.IsLockedByOther` hardcodes `false` — single-owner). A picked-up writing-station item has
already left the player's own inventory and become an inert item stack the moment it's picked up; nobody
else can be mid-edit against a stack sitting in someone's inventory or in the Assignment Desk's slot. So
staging introduces no new concurrent-edit hazard and needs no new lock plumbing — the existing
`IsLockedByOther` server-lock concept (Lectern/Scriptorium/Chalkboard, while still PLACED) is simply
orthogonal to this flow.

**D10 — The staged-rows renderer is a new row widget, reusing `ScribeReadRowData` as its value snapshot
but not `ScribeReadRow` itself.** `ScribeReadRowData` already carries everything a staged document's row
needs to render (`Kind`, `Text`, `DisplayStack`/`DisplayName`, `TargetQuantity`/`CurrentQuantity`,
`LinkTarget`, `Depth`) — reused verbatim as the data model, resolved by the dialog the same way
`ScribeReadContent`'s caller already resolves it (Tracker/Link icon + name lookups stay in the dialog,
keeping the row widget itself API-free). But `ScribeReadRow`'s checkbox is hard-wired to `Done`
(completion); overloading that same control to also mean "selected for this batch" would conflate two
different concerns on one widget rather than composing them. A new row widget (working name
`ScribeAssignmentStageRow`) reuses `ScribeReadRow.BuildItemContent`'s per-kind rendering (task text / item
icon+name / tracker have-need counter) but swaps the Done-checkbox for a Selected-checkbox, and drops the
pin affordance and the read view's "switch to editor" footer entirely — this is a picker surface, not an
editable or completable one.

**D11 — Selection cascades from parent to subtask as a convenience default, but every row stays
independently overridable.** Per author direction ("auto-include but allow deselect... independent
selections with a bit of helping"): checking a parent row's Selected checkbox also sets every immediate
subtask's Selected state to true in the same `SetState`. From that point every row — parent or subtask —
is a fully independent toggle: unchecking one subtask afterward leaves the parent and its siblings checked
and simply drops that one subtask from the batch; there is no re-locking or re-graying once the cascade
has run once. Mirrors the flat, independently-keyed (by `TaskId`) row state `ScribeReadRow`/
`ScribeInboxRow` already use — no new "linked selection" data structure, just a cascade at toggle time.

**D12 — `ScribeAssignmentStore.TryCreate` gains a richer parameter set carrying the full block shape.**
Broaden it from `(assignmentId, assignerUid, targetPlayerUid, taskText, assignedDate)` to also accept
`Kind`, `TargetItemCode`, `TargetQuantity`, `LinkTarget`, `LinkLabel`, `LinkDescription`, and `Depth` — a
strict superset of what it validates/writes today, additive to (not a rewrite of) the existing method,
since every one of those fields is already part of `ScribeBlock` and already round-tripped by the store's
serializer. The existing single-item Task-only send path (`ScribeSendAssignmentMessage` →
`OnServerReceivedSendAssignment`) keeps calling it exactly as today (Kind defaults to Task, the new fields
default empty) — zero behavior change to that path, even though proposal.md retires its UI-side quick-send
field in the same change.

**D13 — The batch-send message carries N row snapshots, one recipient, and one delete flag; "Delete from
source on send" is UI-only session state, not a preference.** A new message (working name
`ScribeSendAssignmentBatchMessage`) carries a list of per-row payloads (each row's `Kind`/`Text`/item
fields/`Depth` — no cross-row bundling id needed, since each becomes its own independent
`ScribeAssignment` per proposal.md), one `TargetPlayerUid`, and one `DeleteFromSource` bool. The server
handler loops the list, calling the D12-broadened `TryCreate` once per row, then — only if
`DeleteFromSource` is true — removes the selected rows from the staged item's document and re-syncs the
Assignment Desk's slot, mirroring how `ScribeTranscribeCopyMessage`/`ScribeTranscribeImportMessage`
already mutate a slotted document server-side and push the result back through the inventory channel. The
checkbox itself lives as plain `bool` state on the Create Assignments tab, defaulting `false` and reset on
every tab (re)open/rebuild — never written to `ScribePlayerSettings` or any other persisted store, per the
earlier "resets every send" decision.

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
- [Broadening `ScribeAssignmentStore.TryCreate`'s signature (D12) touches its one existing caller] →
  Zero regression risk: the old call site passes the new parameters at their Task-only defaults, so its
  behavior is byte-identical; there is exactly one call site today (`OnServerReceivedSendAssignment`) to
  update.
- [A 4th `BlockEntityScribeWritingStation`-derived inventory (Assignment Desk, alongside the Scriptorium's)
  duplicates the same `InventoryGeneric` lazy-init/tree-persistence/packet-routing boilerplate a second
  time (D8)] → Accepted duplication, matching the existing precedent (the Scriptorium didn't extract a
  shared base for this either). A shared helper is a fair follow-up refactor, out of scope here.

## Open Questions

- Exact SVG artwork for the inbox-with-down-arrow and plus icons — author to review a first pass in-game
  before considering this item done (see D4).
- Exact API call for triggering `.ui settings` programmatically from Scribe's own button (see D6) —
  resolve during implementation, not blocking the rest of this change.
- Exact wire-format field layout for the new batch-send message and its per-row payload type (D13) —
  an implementation detail (likely a length-prefixed list of fixed-shape row structs, mirroring how
  `ScribeAssignmentStore`'s own serializer already encodes a `ScribeBlock`), not a design blocker.
- Working names (`ScribeAssignmentStageRow`, `ScribeSendAssignmentBatchMessage`) are placeholders to be
  finalized during tasks/implementation, not locked by this design pass.
