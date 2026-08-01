## Why

The Lectern's three views (Read, Editor, Pinned) have drifted apart in small but felt ways: you can
only pin from the Editor (not the Read view where you're most often just glancing), checking a task's
box behaves differently depending on which view (and on the HUD) you're in, the Pinned view's policy
picker isn't where the eye expects it, and the scroll areas lack the clean top edge the framed
notebook art wants. This is a v1 consistency pass to make the three views feel like one coherent
surface before release.

## What Changes

- **Pin from the Read view.** Add a pin/unpin affordance to Read-view task rows (mirroring the
  Editor's), so a player can pin what they're reading without switching to the Editor. Text-section
  rows still expose no pin control.
- **Uniform completion-policy behavior across all surfaces.** Make checking a task's box in every
  Lectern view apply the player's completion policy (Keep/Sink → drop to bottom, Unpin, or Delete).
  Research found the Read, Pinned, and HUD surfaces **already** route completion through the one
  shared server op (`CompleteTaskForPlayer` → `ScribeCompleteTaskMessage`); only the **Editor** view
  is the outlier — it toggles the uncommitted scratch document's done flag by index, applying no
  policy. This change routes the Editor checkbox through the same identity-addressed policy path so
  all four surfaces behave identically. **Behavior change** to the editor-view checkbox only.
  Per the decision below, all policies apply verbatim in every view (no per-view guards).
- **Pinned view: policy picker above the list.** The Pinned view already has a completion-policy
  picker, but it sits as a footer *below* the list. Move it directly ABOVE the task list.
- **A divider above every scroll area.** Add a horizontal divider directly above the scrolling task
  list in all three views (Read, Editor, Pinned), giving the framed layout the straight top edge it
  wants.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `lectern-gui-shell`: Read-view rows gain a pin-toggle affordance; all three views gain a divider
  above the scroll region; the Pinned view moves its policy picker above the list; the editor-view
  checkbox applies the completion policy (was a plain scratch-doc done-toggle).
- `player-pins`: makes explicit that completing a task by identity applies the player's completion
  policy uniformly from every Scribe surface that shows a checkbox (Read, Editor, Pinned, HUD) —
  closing the Editor-view gap so the behavior no longer depends on which view you're in.

## Impact

- **GUI (`src/Mod/GuiDialogScribeLecternLibGui.cs`)**: (1) thread an `onTogglePinned` into
  `ScribeLecternReadContent`/`ScribeReadRow` and render a `ScribeRowButton("scribepin")` in
  `ScribeReadRowState.Build`, reusing the existing `SendSetPin`/`ScribeSetPinMessage` identity path
  (`ScribeReadRowData` already carries `Pinned` + `TaskId`); (2) add `new Divider()` as the first
  child of each view's outer `Column` (reusing `Gui.Widgets.Basic.Divider`, already used in the
  settings form); (3) reorder the Pinned view's outer `Column` so the existing `policyPicker` is the
  header above `Expanded(scrollBody)`; (4) reroute the Editor checkbox from the index-addressed
  `ToggleEditorTask` scratch toggle to the identity-addressed policy path used by Read/Pinned.
- **Editor reconciliation (the one real risk)**: the Editor holds the edit lock and works on an
  uncommitted `scratch` document; Read/Pinned completion is lock-free and owns no scratch. Routing
  the Editor checkbox through the server completion op must reconcile the server-applied result back
  into the live scratch without clobbering in-progress edits. This is the change's only non-trivial
  problem (detailed in design.md).
- **No new mod dependencies; no `src/Core/` API-surface growth** — `ScribeCompletionPolicy`
  (Keep/Sink/Unpin/Delete) and the shared `CompleteTaskForPlayer` op already exist. Persistence/sync
  unchanged.
- **Decision (settled with the author): uniform, no guards.** All policies — including Delete
  (destructive for everyone) and Sink (reorders the *shared* document, changing order for every
  viewer) — apply verbatim in every view. One mental model, least confusion; the destructive/shared
  consequences are accepted, not gated.
