## Context

See `proposal.md` for the motivation and `docs/calendar-roadmap.md` (change 4) for the decisions this builds
on. This is the first slice of the roadmap's "Pins, HUD and reminders" change; reminders, chat alerts,
reminder settings and the repeat rollover remain a later change.

What exists:

- A pin (`ScribePinnedRef`) is a durable reference plus a last-known snapshot. The server owns the pin store
  (`ScribePinStore`), saves it as one blob (`SPST`) and pushes each player's list as an `SPIN` blob
  (`ScribePinCodec`, version 9). Reads are progressive: each version only appends fields.
- The Pin Tab and the HUD render only from the snapshot, so they work with the source document unloaded.
- A snapshot is filled when a task is pinned (`SetPinForPlayer`, from the resolved document, else from the
  client's `ScribeSetPinMessage` fallback fields) and refreshed by `ScribePinStore.ReconcileSnapshotsForActor`
  after the acting player saves an edit. Other players' pins are deliberately left alone by that path (a pin
  is its owner's copy; this keeps one player's edit from rewriting another's pins).
- The previous change (`add-notebook-task-dating`) added the date chip (`ScribeRowChip`,
  `ScribeDateChipView`, built by `ScribeDialogBase.BuildRowChip`) and the date operations
  (`ScribeSetTaskDateMessage`, `SetTaskDateForPlayer`). The repeat flag does not affect completion, so the
  pin does not need it.
- The HUD (`HudScribePins`, `HudPinsContent`) previews a completion locally for a short window from the
  player's policy, then sends it. Its rows are `HudPinRow` snapshots; a departing row is replaced by a
  frozen twin (`BuildFrozenGhost`) that the animated list collapses.

## Goals / Non-Goals

**Goals:**
- The pin knows the task's date and keeps it current for every holder.
- The Pin Tab and HUD show the same chip as the Notebook, correct across day changes.

**Non-Goals:**
- Reminders, chat alerts, reminder settings, rollover of a checked repeating task, the calendar block.
- Dates on pins of other kinds (the chip follows the Notebook's datable rule).
- Editing a date from the Pin Tab or the HUD (the chip is display-only; the Notebook menu is the editor).
- Sorting the HUD by date, or any pin ordering change.
- Carrying the repeat flag on the pin: it does not affect completion or display here.

## Decisions

- **The snapshot gains `Date` (nullable `ScribeDate`).** The pin blob moves to version 10, appending after
  the version 9 fields, in exactly the document codec's date layout without the repeat flag: `hasDate`
  (bool); only if `hasDate`: year (int), month (byte), day (byte). An older blob (versions 1 to 9) loads
  undated. A value `ScribeDate.TryCreate` rejects reads as undated and the stream stays aligned (the fields
  are always consumed when `hasDate`). `docs/CODEC-MIGRATION.md` and the codec's type summary list the new
  version. Compatibility is the existing "version above the maximum is rejected" behavior, with the downgrade
  consequences spelled out under Risks.
  Alternative: store the date in the pin as text. Rejected, the same reasoning as the document codec
  (a locale-free, validated value).

- **Capture on pin.** `SetPin` takes the date. `SetPinForPlayer` fills it from the resolved
  block (the document wins). When the server cannot resolve the task, it comes from `ScribeSetPinMessage`,
  which gains optional fields (`SnapshotDateYear`, `SnapshotDateMonth`, `SnapshotDateDay`; zeros mean no
  date). The server builds the date with `ScribeDate.TryCreate`, takes
  it only when the fallback kind is a Task, treats any invalid or partial triple as undated. New `SetPinForPlayer` parameters are added after `insertEdge` (the last
  parameter today), and `OnServerReceivedSetPin` passes them by name. An old client sends none, so the pin is
  undated until the next refresh. The client fills the message and its optimistic pin
  (`ScribeDialogBase.SendSetPin` builds the `ScribePinnedRef` that `SetOptimisticPin` shows until the server
  push) from the block it is pinning, so a pinned dated task shows its chip at once and the row does not grow
  when the push lands.

- **Refresh for every holder, dates only.** Two parts:
  1. `ReconcileSnapshotsForActor` already refreshes the acting player's whole snapshot after their save;
     its dirty check and copy gain `Date`.
  2. A new `ScribePinStore.RefreshDatesForDocument(docId, document)` walks every player's pins on that
     document and copies only `Date` from the task (a plain task; a pin whose task is not
     in the document is left as it is). It returns the uids whose pins changed. It never adds, removes,
     orphans or reorders a pin and never touches text or done state, so the grief rule for those fields is
     unchanged. The caller pushes each returned uid that is online (`PushPinsTo`); an offline player's
     stored pin is already updated and reaches them on join.
  Call sites, checked against the code: `SetTaskDateForPlayer` has no acting-player reconcile today (it only
  calls `PersistFromReader`), so it calls `RefreshDatesForDocument` itself when the operation changed the
  document, which also covers the acting player. The three paths that already reconcile
  (`ScribeModSystem.Network.cs` whole-document save near line 394, `ScribeModSystem.ExternalTaskApi.cs`
  append near line 85, `BlockEntityScribeWritingStation.ReconcileActorPins` for the Lectern and Chalkboard
  editor save) call the refresh after the reconcile, so a pin the reconcile already changed is pushed once
  (the reconcile's pushed uids are not pushed again). Because that list is hand-made, a task audits every
  place a document is persisted or replaced (`PersistFromReader`, `ScribeDocumentAttributes.WriteTo`,
  `Document =` assignments, paste and clone paths) and records each as covered or deliberately out in the
  change's notes. A date set on a document the server cannot resolve refreshes nothing (accepted: the
  existing unresolvable no-op). The refresh is in-memory and bounded by the 500-pin cap per player.
  Alternative: refresh all fields for all holders. Rejected: it would let one player's edit rewrite
  another's snapshot text and done state, which the existing design avoids on purpose.
  Pins that predate this change have no date until their document is next saved through one of those paths
  or the task is pinned again; this includes pins on a Notebook the player only reads. Accepted (see
  Assumptions); a load-time scan is rejected because of host load order.

- **One chip builder for every surface.** The chip text and status color come from a plain function over
  `(date, done, completable, today, shape, order)`: a new `ScribeDateRules.Classify` overload takes plain
  values, and a mod helper builds the `ScribeRowChip` from a world, a date, done-state and a Date order. The
  dialog's `BuildRowChip` and the new Pin Tab and HUD code all call it, so the wording, plurals and "date only
  when done" rule have one definition. The pin-side predicate is `ScribeDateRules.PinShowsDate(pin)` (Kind is
  Task, a date is present, not orphaned). It has no assignment clause: a date cannot be set on an assignment
  block and assignment placement drops it, so a pin of a task that is or becomes an assignment carries no
  date; the name differs from the block rule `IsDatable`, which does not require a date.

- **Pin Tab row.** `ScribePinRowData` gains `Chip`. The row shows `ScribeDateChipView` under the text, as the
  read and editor rows do, always present as an empty box (zero height) without a chip so the row's
  structure and the text field never remount when a date appears and undated rows keep their height. The row
  builds the chip from its props in `Build`, so a reused row refreshes on a pin push (`UpdateWidget`). The
  snapshot used for a departing row's collapse includes the chip. The chip is computed per Pin Tab build and
  only when the dialog's `SupportsRowDates` is true: the Tablet and the Assignment Desk return false but still
  build the Pin Tab, and they keep showing none (the Tablet's cuneiform chrome is why dates were excluded
  there). The HUD has no such gate.

- **HUD row.** `HudPinRow` gains `Chip`. The chip sits under the text inside the row's expanded text column,
  the Notebook chip's size scaled by the HUD's existing font scale (no extra shrink; full month names at a
  smaller size stay a playtest item), SoftWrap on so it wraps within `HudRowWidth`. It uses HUD colors, a new
  set of four constants in `ScribeRowConstants` (due today, overdue, upcoming, done), four distinct values
  distinct from the HUD text color, tuned in playtest. It fades with the text through the same `ScribeFadeText`
  mechanism, the whole row dims through the existing sunk opacity (the policy's own sinking, unchanged), and `BuildFrozenGhost` renders the chip at
  zero opacity so the ghost has the live row's height. A just-checked row in its undo window shows the
  checked chip form (date only, dimmed) from the displayed done state, which is the optimistic override, so an
  undo returns it to the status form. The corruption effect stays on the task text only. The HUD's max-row
  count counts rows, not lines; the anchoring code measures content, so taller rows need no change, but this
  is a playtest item because the anchor cache had a sizing bug before.

- **Chips stay true across days and the Date order on the HUD.** The HUD is the one surface that stays open
  for days. Its tick computes the whole in-game day with the same function the Notebook chip uses
  (`ScribeWorldCalendar.FromTotalDays` over `TotalDays`, floored and clamped at 0) and compares it with a
  stored last day through a pure helper `ShouldRebuildForDayChange(lastDay, currentDay, anyDatedDisplayed)`,
  which returns true when the days differ (forward or backward) and a displayed pin is dated. The tick does
  nothing while the HUD is not open (the existing `IsOpened()` guard); the last day is updated even when no
  rebuild is needed so a later dated pin starts from the current day. The Date order setting is changed
  through `UpdateMySettings`, which fires `MyPinsChanged`, which the HUD already handles with a rebuild; the
  task verifies that rather than adding a path. The Pin Tab and the Notebook compute at build time and are
  accepted as stale across a day change until their next rebuild, as stated in the previous change.

## Risks / Trade-offs

- [Pins made before this change show no date until their document is saved or re-pinned] → Accepted; no
  migration scan on load, which would depend on host load order. It also affects tasks on a Notebook the
  player only reads. The playtest list says so.
- [A save by one player pushes pin updates to others] → Only when a date actually changed, and
  only to the affected connected players.
- [HUD row height changes with chips] → Playtest item for anchors and the row limit.
- [HUD chip colors are placeholders] → Tuned in playtest like the Notebook's.
- [The pin blob version bump] → Same policy as earlier bumps; golden-bytes tests are updated deliberately. An
  older client on a newer server (a stale mod build; Vintage Story does not sync mods to clients) rejects the
  version 10 blob and shows an empty pin list. A mod downgrade on a world whose store is version 10 fails to
  read the store and loads it empty, and the next save then overwrites every player's pins. No mitigation in
  this change; the roadmap's Later list records "refuse to overwrite a store with a newer version".

## Assumptions (user to confirm)

1. Pins that predate this change get their date lazily (next save of the document or re-pin).
2. A date change refreshes the date on every holder's pin; text and done keep the owner-only rule.
3. The chip is display-only on the Pin Tab and the HUD.
4. HUD uses its own four-color palette; chip text is not corrupted by temporal instability.
5. The HUD rebuilds on a day change; the Pin Tab does not.
6. Only plain tasks with a date show a chip, never an orphaned pin; the Pin Tab chip is gated on the dialog
   offering dates (not on a Tablet or the Assignment Desk); no new lang keys (the chip reuses the Notebook's).
7. `add-notebook-task-dating` (and the row menu and dated-core changes it needs) archive before this one,
   in that order. This change's `notebook-task-dating` delta restates its chip and refresh requirements; if
   that change is archived with different wording, reconcile the headers and keep the superset bodies.
8. The repeat flag does not change completion (the user's decision, recorded in `add-notebook-task-dating`),
   so the pin snapshot does not carry it and the HUD and Pin Tab completion paths are untouched.

## Persist-site audit

Every place the server persists or replaces a document, and whether it refreshes pin dates:

- `ScribeModSystem.PinOperations.cs` `SetTaskDateForPlayer`: covered (`RefreshPinDates` when the operation changed the document).
- `BlockEntityScribeWritingStation.ApplyEdit` (Lectern, Chalkboard and Scriptorium editor saves): covered through `ReconcileActorPins`, which calls `ReconcilePinsAfterEdit`.
- `ScribeModSystem.Network.cs` Notebook whole-document save: covered (`ReconcilePinsAfterEdit`).
- `ScribeModSystem.ExternalTaskApi.cs` append: covered (`ReconcilePinsAfterEdit`); appended blocks carry fresh identities, so it only matters for the acting player's own pins.
- `PinOperations` completion, delete and tracker-quantity paths (`PersistFromReader` after `ApplyGivenDone`, `ApplyDelete`, `SetTrackerCurrentQuantityFromReader`): out. They never change a date (rollover is a later change).
- `ScribeModSystem.Network.cs` transcribe copy and import: out. Replace mints a fresh `DocId` and `TaskId`s (no pin refers to the copy); append adds clones with fresh `TaskId`s to the target and leaves its existing tasks untouched.
- `ScribeModSystem.Assignment.cs` and `ScribeModSystem.Delivery.cs` placement and source removal: out. Placed blocks are new tasks without a date; removal only drops blocks.
- `BlockInbox.cs` and `BlockScribeWritingStation.cs` drops: out. They copy the unchanged document onto a dropped item.
- Client `HandleServerReply`, `ApplyLocalOptimisticEdit` and `NotebookHost.Flush`: out. Client copies, not the server's stored document.
