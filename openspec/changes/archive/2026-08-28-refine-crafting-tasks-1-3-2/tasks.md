## 1. Core: heal is rescale-only; owned-run scan

- [x] 1.1 Add `ScribeDocument.OwnedRun(parentIndex)` (start exclusive of parent, end exclusive at first non-depth-1). Kind-agnostic. Unit-test contiguous children, a depth-0 gap ending the run, shuffle among depth-1, empty run.
- [x] 1.2 Split `ReconcileCraftIngredients` into create-once vs stepper: Handbook/create still inserts missing Trackers (and notes); the stepper path (`RescaleCraftIngredients` or a `createMissing: false` flag) **only** updates `TargetQuantity` on item-code matches inside the owned run. Never insert, never delete.
- [x] 1.3 Update `ScribeCraftTaskTests` (and any heal tests that expect recreate-on-open or recreate-on-target). Cover: deleted child stays gone after target bump; extra player row in the run is left alone.
- [x] 1.4 `dotnet test tests/Core.Tests` green for this group.

## 2. Core: Subtask Behavior + one range mutation

- [x] 2.1 Add `ScribeSubtaskBehavior` enum (`Bound` default, `Independent`, `DiscardChildren`) on `ScribePlayerSettings`, with `NormalizeSubtaskBehavior` (unknown → Bound). JSON default via serializer.
- [x] 2.2 Extend `ScribeCompletion` (and document helpers) so a **parent** complete/uncheck/sink/delete is **one range mutation** under Bound (parent then children, that order — never N `MoveTaskToBottom`). Child complete stays a leaf. Text in the run has no Done but still moves/deletes with the range. Discard removes children then applies the parent’s completion policy alone. Independent mutates only the parent.
- [x] 2.3 Bound trash deletes the owned run; Independent trash deletes only the parent; Discard-children trash deletes the run. Uncheck Bound unchecks completable rows in the run and does **not** unsink/undelete.
- [x] 2.4 Core tests: Bound Sink keeps parent-first contiguity; Independent Sink leaves children; sequential-per-row sink is **not** the behavior; Discard then uncheck does not restore children; completing a depth-1 sibling does not take the other sibling.
- [x] 2.5 `dotnet test tests/Core.Tests` green for this group.

## 3. Core: pin insert/gather + HudMaxRows 30

- [x] 3.1 Add a Core helper (e.g. on `ScribePinOrdering`) that, given a pin list + source document, **inserts** a depth-1 pin after the parent’s cluster, **appends** when the parent is unpinned/unresolvable, and **gathers** already-pinned owned-run children after a newly pinned depth-0. Never auto-pin the parent. Parent identity is the document walk-back, not “any depth-0 from this notebook.”
- [x] 3.2 Unit-test insert-under-parent, append-when-parent-unpinned, gather-preserving-child-order, mixed pins from another document left in place.
- [x] 3.3 Raise `ScribePlayerSettings.MaxHudMaxRows` from 10 to **30**; update `ClampHudMaxRows` tests and any helptext that still says 1–10.
- [x] 3.4 `dotnet test tests/Core.Tests` green for this group.

## 4. Packets and server write-through

- [x] 4.1 Add Subtask Behavior to `ScribeCompleteTaskMessage` and `ScribeDeleteTaskMessage` (same ProtoMember-append pattern as `Policy`). Missing/old clients → Bound. Server normalizes unknown values to Bound.
- [x] 4.2 Server complete/delete/tracker-auto-complete of a **parent** applies §2 using the packet field (tracker auto-complete of a **child** stays a leaf). Pin policy applies only to rows that option mutates, when pinned.
- [x] 4.3 `ScribePinStore.SetPin` / `SetPinForPlayer` uses the §3.1 helper (document resolved) instead of always `list.Add`.

## 5. Craft heal + tool skip (Mod)

- [x] 5.1 Remove `SelfHealCraftTasks()` from editor entry (`ScribeDialogBase.ViewSwitching.cs`). Handbook create still expands once. Parent target stepper uses the rescale-only Core path.
- [x] 5.2 `ScribeCraftRecipeProbe.DeriveIngredients`: skip `IsTool`, `!Consume`, and tags-only / default `*:*` (never `EncodeWildcard` a `*:*` Tracker). Debarked oak log → parent + oak log only.

## 6. Settings, HUD chrome, Craft counter, pin notes

- [x] 6.1 Settings Behavior: Subtask Behavior dropdown + helptext (Bound / Independent / Discard). HUD section: `HudShowSettingsGear` checkbox (default on). HUD max-rows numeric range 1–30. Persist on `ScribePlayerSettings`.
- [x] 6.2 HUD: title **Scribe Pins** (`scribe-hud-title`); header font = row font (`BaseHudFontSize` × HUD font scale); omit gear when the setting is off; `MaxRenderedRows` uses the new 30 ceiling.
- [x] 6.3 `BuildHudItemContent`: have/need on `IsCarriedCountTracked` (Tracker **or** Craft), not `IsTracker` only.
- [x] 6.4 Pin control on Text rows in Read and Edit (`lectern-gui-shell`). HUD note: text only (no checkbox, no unpin). Pin Tab lists notes; checkbox/unpin unpins; delete still deletes the source when allowed.

## 7. Handbook labels and grip

- [x] 7.1 Handbook-only lang keys: `Link to this page`, `Count this item`, `Add ingredients` / `Add ingredients ({0})`. Heading stays `Add to Scribe`. Order Link → Tracker → Craft. Editor Add ▾ keeps `scribe-gui-addlink` / `scribe-gui-addtracker` / `scribe-gui-addcraft`.
- [x] 7.2 Editor grip: drag starts after pointer movement, not on press; once a drag started, release (including from==to cancel) MUST NOT call `OnGripTap`.

## 8. Copy, CHANGELOG, validate

- [x] 8.1 `en.json` for new keys (handbook, Subtask Behavior, HUD gear, helptext 1–30). Leave `pt-br.json` to English fallback.
- [x] 8.2 `CHANGELOG.md` 1.3.2 player-facing notes for this cut (heal, Bound default, HUD Craft count, pin insert, pin notes, handbook copy, tool skip, HUD chrome). `modinfo.json` is already 1.3.2.
- [x] 8.3 `openspec validate refine-crafting-tasks-1-3-2` passes. `dotnet test tests/Core.Tests` green.

## 9. In-game gates

- [x] 9.1 `.scribeprobe` on a debarked oak log: parent + oak log only; no `*:*` / “Pocketsun (any variant)” child.
- [x] 9.2 Delete an ingredient, bump parent target: child stays gone; remaining children rescale. Opening the editor does not recreate.
- [x] 9.3 Bound + Sink on a Craft parent: parent and children sink together, parent first. Completing one child does not take siblings. Independent leaves children. Discard then uncheck does not restore children.
- [x] 9.4 Pin a child under a pinned parent (insert, not append); pin parent later gathers already-pinned children. Pin a note: HUD text-only; unpin from Pin Tab.
- [x] 9.5 HUD Craft parent shows have/need; title is Scribe Pins at row font size; gear hide works; HudMaxRows 30 sticks after reload.
- [x] 9.6 Grip: tap still nests; press-move-release on the same row does **not** nest.

## 10. Playtest follow-ups

- [x] 10.1 Grip hover must not start a drag (press-gate `onMove`; hover was firing `OnDragStart` because `PointerEvent.Button` defaults to Left).
- [x] 10.2 Drag-reordering a depth-0 parent moves its owned run as one cluster (parent first). Dropping on own children is a no-op.
- [x] 10.3 Item-row FieldPadY is 0 (was 6px). In-game: compare a Tracker/Craft row to a neighboring Task text row in editor AND read — do they still line up, or does the pad need to come back? Also confirm stepper +/− sit in their buttons, and a wrapping item name top-aligns stepper, icon, and text.
- [x] 10.4 Tablet vs Notebook item-row: cuneiform names should share a top edge with the stepper/checkbox (no extra FieldPadY inside the glyph-font label). Compare a Craft/Tracker row on a Notebook and a tablet.
- [x] 10.5 Tablet cuneiform only: checkbox, grip, and Tracker/Craft stepper should match one cuneiform line (~FontSize×1.848). Notebook/Lectern stay at 22px. Disable-cuneiform on a tablet should revert the controls.
- [x] 10.6 Item-row names: single-line ("Leather") should sit on the icon/checkbox horizon; wrapping names stay top-aligned and must not lift the icon. Check editor, read, and Pin Tab.
