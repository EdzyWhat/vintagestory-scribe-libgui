## 1. Core: pure-data pin-edit convenience (no VS API)

- [ ] 1.1 Add an optional `ScribeDocument.SetTaskText(Guid taskId, string text)` over the existing
      `FindByTaskId` + `SetBlockText`, honoring the blank/whitespace-only rejection invariant and
      reporting success/failure. Pure data — no VS API reference.
- [ ] 1.2 (Optional) Add a Core reorder helper only if the store's permute cannot be expressed cleanly
      server-side; keep it pure-data if added. Skip if `ScribePinStore` handles reorder directly.
- [ ] 1.3 Add `Core.Tests` coverage for `SetTaskText`: edits by present id, rejects blank/whitespace,
      no-op on absent id, leaves other blocks untouched. Run
      `dotnet test tests/Core.Tests/Core.Tests.csproj`.

## 2. BlockEntity: lock-free set-text-from-reader

- [ ] 2.1 Add `SetTaskTextFromReader(Guid taskId, string text)` to `BlockEntityScribeLectern`, mirroring
      `SetTaskDoneFromReader` (BlockEntityScribeLectern.cs:268): lock-free, mutate the authoritative
      `Document` via the Core set-text path, `MarkDirty`. Return whether it wrote.
- [ ] 2.2 Comment the same race caveat noted on the done-flag path (a concurrent whole-doc `ApplyEdit`
      under the edit lock can clobber this lock-free write; last write wins).

## 3. New messages + server handlers (mirror CompleteTaskForPlayer)

- [ ] 3.1 Add `ScribeEditPinnedTaskMessage { DocId:byte[16], TaskId:byte[16], Text:string }` (C→S) next
      to the existing Scribe messages.
- [ ] 3.2 Add `ScribeReorderPinsMessage` (C→S) carrying the new per-player pin order (ordered
      `(DocId, TaskId)` list, or a from/to permutation).
- [ ] 3.3 (Optional) Add a standalone delete/unpin message, or extend the existing complete message with
      an action enum — whichever keeps the handler set smallest.
- [ ] 3.4 Register all new messages **appended** to the frozen registration order in
      `ScribeModSystem.Start` — never inserted mid-list.
- [ ] 3.5 Edit-text handler: resolve via `TryResolveLectern(docId)`; if resolvable call
      `SetTaskTextFromReader`; always update the pin snapshot via `pinStore.ReconcileSnapshotsForActor`
      (or equivalent) and re-push. Degrade to snapshot-only when unresolvable (matches Delete-policy at
      ScribeModSystem.cs:476).
- [ ] 3.6 Standalone delete handler: resolve; if resolvable call `DeleteTaskFromReader`; always
      `pinStore.RemovePin` and re-push. Safe no-op if the pin is already gone / source unloaded.
- [ ] 3.7 Reorder handler: permute the actor's list in `ScribePinStore`, persist, re-push. Do NOT touch
      document block order.
- [ ] 3.8 Validate all inbound payloads (bounded pin count, ids are 16 bytes, unknown ids ignored) so a
      malformed/hostile message cannot corrupt the store.

## 4. ScribePinStore: reorder + persist

- [ ] 4.1 Add a reorder operation that permutes `_pins[uid]` into a client-supplied order, ignoring
      unknown/duplicate ids and preserving any pins the client omits.
- [ ] 4.2 Persist the reordered list (already saved under `scribe:pins:v1` — no format change) and
      expose the op to the reorder handler.

## 5. ScribePinTray.cs: slide-out pin-editor widget

- [ ] 5.1 New `src/Mod/ScribePinTray.cs` — a `Positioned` child in the dialog `Stack`, wrapped in
      `AnimatedSlide` (offset `Zero` ↔ off-screen X, EaseOut) and `Clip`ped to the window edge.
- [ ] 5.2 Bind rows to `modSystem.MyPins` (all pins across documents, in pin-list order); use the
      `HudPinsContent`/`HudPinRow` template.
- [ ] 5.3 Per row: complete, inline text edit (reuse `ScribeMultilineField`), delete, unpin, and a
      reorder grip. Each action sends the identity-addressed message; NO undo/sink timer.
- [ ] 5.4 Give the tray's Stateful node a `Key` so its slide-animation State survives `ForceRebuild`;
      `Dispose()` any animation controller in `State.Dispose()`.

## 6. Wire the tray + handle into the Lectern dialog

- [ ] 6.1 In `GuiDialogScribeLecternLibGui`, wrap the dialog body in a `Stack` and add the `ScribePinTray`
      as a `Positioned` child at the window edge.
- [ ] 6.2 Add an `isPinTrayOpen` field and a `GestureDetector` handle affordance that toggles it →
      `SetState`/`ForceRebuild` to drive the slide.
- [ ] 6.3 Add the edit/delete/unpin/reorder client senders that emit the new messages, addressed by
      `(DocId, TaskId)` / pin order.
- [ ] 6.4 Add pin-tray / handle / row-action labels to `assets/scribe/lang/en.json`.
- [ ] 6.5 Confirm no change to `HudScribePins` behavior — both surfaces read the same `MyPins`.

## 7. In-game verification

- [ ] 7.1 Open the Lectern; handle slides the pagelet in/out smoothly and it lists all pins across
      documents.
- [ ] 7.2 Controls are hit-testable mid-slide (interact with a row while the tray is partway open).
- [ ] 7.3 Complete a task from the pagelet → applies immediately with NO undo delay; confirm the HUD
      still applies its undo window when used from the HUD.
- [ ] 7.4 Edit a pin whose source Lectern is **loaded** → source doc text updates + persists (reopen to
      confirm); pin snapshot updates.
- [ ] 7.5 Edit/delete a pin whose source is **unloaded** → pin snapshot/removal updates, no crash, source
      unchanged until loaded (accepted best-effort, matches Delete-policy today).
- [ ] 7.6 Unpin removes only the pin (task survives); delete removes the task.
- [ ] 7.7 Reorder pins → order persists per-player across relog (saved under `scribe:pins:v1`); confirm
      document block order is unchanged.
- [ ] 7.8 Cross-check the corner HUD updates in lockstep with every pagelet action (same `MyPins`).
- [ ] 7.9 Confirm blank/whitespace-only inline edit is rejected and leaves the task unchanged.

## 8. Docs

- [ ] 8.1 Append a LibGUI slide-out lesson to `VSAPI-NOTES.md` (`## LibGUI`): `AnimatedSlide` is
      hit-test-correct (inverts the render transform); `Positioned`+`Stack`+`Clip` host a window-edge
      pagelet; keyed Stateful node survives `ForceRebuild`; plus in-game animation/legibility verdicts.
