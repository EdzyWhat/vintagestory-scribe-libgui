## Context

Four v1 consistency tweaks to the Lectern's three views (Read/Editor/Pinned), all in
`src/Mod/GuiDialogScribeLecternLibGui.cs` (abbrev. **GuiDialog**). Research established that three of
the four are small, and the premise "only the HUD applies the completion policy" was **wrong**:

- **Completion policy is already shared.** `ScribeCompleteTaskMessage` (`DocId`, `TaskId`, `Policy`)
  → server handler `OnServerReceivedCompleteTask` (`ScribeModSystem.cs:497`) → the one reusable op
  `CompleteTaskForPlayer` (`ScribeModSystem.cs:613`), which applies `Unpin`/`Delete`/`Sink`/`Keep`
  server-authoritatively. The **Read** view (`OnReadViewCompleteTask`, GuiDialog:1363), the
  **Pinned** view (`OnPinCompleteTask`, GuiDialog:1486), and the **HUD** (`HudScribePins.SendCompletion`)
  all already send this message with the player's policy. Only the **Editor** view is the outlier:
  its checkbox calls `ToggleEditorTask` (GuiDialog:627), which flips the uncommitted `scratch`
  document's done flag **by index** and marks dirty — no policy, no network completion, no pin.
- **Pin from Read** is unbuilt but well-supported: `SendSetPin`/`ScribeSetPinMessage` (GuiDialog:729)
  toggle a pin by `(DocId, TaskId)`; `ScribeReadRowData` already carries `Pinned` + `TaskId`
  (GuiDialog:1314, :1572). The Read row (`ScribeReadRowState.Build`, GuiDialog:1706) currently
  renders only a grip spacer, checkbox, and text — no pin control.
- **Policy picker already exists in the Pinned view** (`policyPicker`, GuiDialog:2465) as the *footer*
  of a `Column[ Expanded(scrollBody), policyPicker ]` (GuiDialog:2485).
- **Divider**: `Gui.Widgets.Basic.Divider` (already imported at GuiDialog:7, used in
  `ScribeSettingsContent.cs:71/74`) is a horizontal theme-border line. Each view is
  `Padding(All(10)) → Column(spacing:8, …)` whose first child is `Expanded(scrollBody)`; nothing sits
  above the scroll area today.
- **Dispatch**: `BuildCentralRegion()` (GuiDialog:1290) switches to `BuildReadContent()` (:1302) /
  `BuildEditorContent()` (:1322) / `BuildPinnedContent()` (:1380).

## Goals / Non-Goals

**Goals:**
- Read-view rows can pin/unpin (task rows only; text sections show no pin control).
- Every Lectern view's checkbox applies the player's completion policy identically.
- The Pinned view's policy picker sits above its list.
- A horizontal divider sits directly above the scroll area in all three views.

**Non-Goals:**
- No new completion policies, no per-view policy scoping/guards (uniform by decision).
- No `src/Core/` API growth — reuse `ScribeCompletionPolicy` + `CompleteTaskForPlayer`.
- No change to the HUD's undo-window behavior (it already sends the same message; untouched).
- Not factoring the duplicated policy-dropdown widget (Pin Tab + settings) into a shared control —
  noted as possible cleanup, out of scope here.

## Decisions

### Decision 1 — Uniform policy in every view, no guards (author-confirmed)
All policies apply verbatim from Read, Editor, Pinned, and HUD. **Delete** removes the task for
everyone; **Sink** reorders the *shared* document (changing order for every viewer, not just the
acting player). These consequences are accepted for a single, predictable mental model — a checkbox
does the same thing wherever you tick it. No confirmation prompt, no read-view softening.

**Correction (found during implementation, author-confirmed to expand scope):** the shipped
completion op was NOT what the proposal assumed. Two facts:
- **"Sink" did not reorder anything.** The server treated Sink and Keep identically; "sink to bottom"
  was a HUD-only *display* ordering (done rows sort below not-done). There was no document reorder.
- **The policy only applied to tasks the player had PINNED.** An unpinned task's checkbox fell to
  `CompleteUnpinnedTaskAtSource`, a plain done-toggle with no policy.

The author chose to **make Sink a real document reorder that applies to ALL tasks** (pinned or not):
add a Core `MoveTaskToBottom(taskId)`, wire it into both server completion paths so completing any
task under Sink moves it to the document's end for every viewer. This makes "drop to bottom"
literally true in the Read/Editor/Pinned lists (which render document order), not just the HUD.
Delete/Unpin remain pin-scoped where they only make sense for a pin (Unpin has no meaning for an
unpinned task; Delete already worked for pinned tasks and — per this decision — should also delete an
unpinned task on completion under Delete policy).

### Decision 2 — Route the Editor checkbox through the identity completion path (the one hard part)
The Editor is the only non-conforming surface. Its rows are index-addressed and it edits an
uncommitted `scratch` document under the edit lock, while the shared completion op is lock-free and
server-authoritative and owns no scratch. Approach:

- The editor row already has each block's `TaskId` in `ScribeEditRowData` (GuiDialog:1325), so the
  checkbox can address completion by `(DocId, TaskId)` like Read/Pinned instead of by index.
- On an editor checkbox toggle, send `ScribeCompleteTaskMessage` with the player's policy (the same
  call Read uses), rather than mutating the scratch done flag locally.
- **Reconcile the result into the live scratch without clobbering in-progress edits.** The server
  applies the policy to the authoritative document and re-syncs; the editor must fold that specific
  task's new state (done, or removed under Delete/Unpin-of-task, or reordered under Sink) into its
  scratch, while preserving other rows' unsaved text and the caret. This mirrors the known editor
  isolation tension already logged elsewhere (`add-pinned-task-hud 80777b7b`: the editor ignores
  external resync while `isEditorMode`). The safest scoped reconciliation is to apply just the
  completing task's transition to the scratch by `TaskId` (toggle its done, or drop it for
  Delete), not a wholesale scratch reseed — full reseed would discard in-progress edits.

**Alternative considered — keep the Editor local-only and just apply policy on commit:** rejected
because it breaks the "same behavior everywhere" goal — ticking a box in the Editor wouldn't
sink/unpin/delete until the edit committed, which is exactly the inconsistency this change removes.

**Risk-scoping note:** if reconciling a live Delete/Sink into the scratch proves genuinely unsafe
mid-edit within v1's time budget, the fallback is to commit-then-complete (commit the scratch,
release nothing visible, then apply the completion) — still uniform from the user's view. The task
list flags this as the decision point to confirm during implementation.

### Decision 3 — Pin on Read rows reuses the existing identity pin path
Thread an `Action<Guid> onTogglePinned` through `ScribeLecternReadContent` → `ScribeReadRow`, wire it
to a dialog method that calls `SendSetPin(taskId, !IsPinnedForMe(taskId))` (reusing GuiDialog:729),
and render a `ScribeRowButton("scribepin")` in `ScribeReadRowState.Build` for task rows only (guard
on `IsTask`, matching the editor and the `lectern-gui-shell` "text sections have no pin control"
requirement). `ScribeReadRowData.Pinned` (already computed) drives the resting/active glyph state.

### Decision 4 — Divider is the first child of each view's outer Column
Add `new Divider()` as the first child (before `Expanded(scrollBody)`) of each view's outer `Column`:
Read (GuiDialog:1659), Editor (:2062), Pinned (:2485). One-line change per view; inherits the
theme's border color and the column's `spacing:8`. For the Pinned view this composes with Decision 5.

### Decision 5 — Move the Pinned policy picker to the header
Reorder the Pinned view's outer `Column` from `[ Expanded(scrollBody), policyPicker ]` to
`[ policyPicker, Divider, Expanded(scrollBody) ]` (picker on top, then the divider, then the list),
keeping `Expanded` on the scroll body so it still fills the remaining height. No new widget — the
existing `policyPicker` (GuiDialog:2465) just changes position.

## Risks / Trade-offs

- **[Editor scratch reconciliation clobbers in-progress edits]** → Decision 2: apply only the
  completing task's transition into the scratch by `TaskId`, never a wholesale reseed; if unsafe,
  fall back to commit-then-complete. Verify in-game that ticking one editor row's box under each
  policy leaves other rows' unsaved text and the caret intact.
- **[Sink reorders the shared document surprisingly]** → accepted per Decision 1; called out so a
  future reviewer knows it's intentional, not a bug. Worth a one-line helptext/tooltip note if
  players report confusion post-v1.
- **[Delete from a glance in Read view is destructive]** → accepted per Decision 1 (uniform). The
  policy is a deliberate per-player setting; a player who dislikes it can pick Keep/Sink/Unpin.
- **[Divider adds vertical height, tightening short lists]** → 1px + one `spacing:8` gap per view;
  negligible, but eyeball the smallest window size during playtest.

## Migration Plan

No data model, wire-format, or persistence change — `ScribeCompleteTaskMessage`, `ScribeSetPinMessage`,
and `ScribeCompletionPolicy` all already exist. Rollback is reverting the GUI edits (and the editor
completion reroute). No Core change, so the Core suite is unaffected.

## Open Questions

- **Editor reconciliation depth** (Decision 2): scoped per-task fold vs. commit-then-complete — settle
  during implementation based on what keeps in-progress edits safe. Not blocking the proposal.
- Factor the duplicated policy dropdown (Pin Tab + settings window) into one widget? Deferred cleanup.
