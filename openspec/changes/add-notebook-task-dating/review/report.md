# Review: add-notebook-task-dating   (2026-11-16, rounds 1 and 2)

## Paraphrase (from the comprehension reviewer, verbatim)

> Players can put a date on a plain task from the right-click row menu in the read and editor views of the Notebook, Clockmaker's Notebook, Lectern, Chalkboard and Scriptorium. The menu shows Add date on an undated task, and Change date, Clear date and a "Repeat yearly" checkbox on a dated one. Add date and Change date open a picker with a month dropdown, day and year steppers, and Set and Cancel. A dated task shows a chip under its text with the date (in the player's Date order, years from 1) and its standing ("Due today", "in 3 days", "Overdue (2 days)"). Warm colors mark due and overdue, and a checked task shows only the date, dimmed. In the editor view the date is saved by the existing editor save. In the read view a new client-to-server message, `ScribeSetTaskDateMessage`, sets it, and the server refuses it while another player holds the editor lock. Under the hood, Core gets entry gating, a date-picker model, chip-text mapping and `ScribeDocument.TrySetTaskDate`. The menu host gets toggle entries and `RefreshItems()`. A checked task that repeats yearly is forced to behave like the Sink policy, so Delete and Unpin cannot remove it. Reminders, HUD and Pin Tab badges, rollover and the calendar block are left to later changes.

The paraphrase matched the artifacts. Its mismatches were the repeating-task wording (findings 1 to 3), which the fixes below settle.

## Reviewers

Four fresh reviewers (comprehension, spec critic, design critic, readiness) read only the change's files, main specs, project context and the roadmap. Their findings are merged and deduplicated below; each code claim was checked against the repo before triage.

## Findings

| # | Severity | Where | Finding | Raised by |
|---|---|---|---|---|
| 1 | Blocker | spec: A repeating task is not deleted by completion; proposal Modified Capabilities | The repeating-task rule overrides the player's Completion Policy but no MODIFIED delta restates the main requirements it contradicts (`player-pins` complete-by-identity and complete-removes-pin, `pinned-task-tab` policy governs completions, `scribe-dialog-base` single shared Core function). Proposal says Modified Capabilities: None. | Spec critic, Design critic, Readiness, Comprehension |
| 2 | Major | proposal / spec / design: repeating-task completion | Proposal, spec and design disagree on what each policy does (proposal 'never removed by Delete or Unpin'; spec 'keeps its pin'; design forces Sink for all). Keep would start sinking; Unpin and UnpinSink never delete a task so overriding them contradicts the player's choice. | Comprehension, Spec critic, Design critic, Readiness |
| 3 | Major | design: Repeating tasks and completion; tasks 1.5 | `EffectivePolicy(block, policy)` takes one block but Bound and Discard parents act on a whole owned run; 'ApplyDelete's completion callers' is wrong (ApplyDelete is manual trash); order against the read-only Tablet collapse is unspecified. | Comprehension, Design critic, Spec critic, Readiness |
| 4 | Major | design: completion paths without a block | Server's unresolvable-source branch and the HUD's policy-only prediction cannot see the date (pin snapshot has none), so a repeating task's pin can be removed there and the HUD row can fade out then reappear under Delete. | Design critic, Spec critic, Readiness, Comprehension |
| 5 | Major | design/tasks 1.5: editor completion rebuild | `ToggleEditorTask` decides whether to rebuild from the raw `CompletionPolicy`, not the effective outcome. | Readiness |
| 6 | Major | design: scope of the exemption | `EffectivePolicy` keys on date and repeat flag for any kind, so an imported dated Tracker or Link is exempted, and the tracker-completion Delete path (`ScribeDeleteTaskMessage`) bypasses it. | Readiness, Comprehension |
| 7 | Major | design/spec: ScribeSetTaskDateMessage carries whole date state | A toggle or picker Set from a stale view overwrites another viewer's change; two quick toggles send the same state; the spec never says Change date keeps the repeat flag; the read view waits for the round trip. | Comprehension, Design critic, Spec critic, Readiness |
| 8 | Major | design: server handler checks | Handler checks only lock, kind and date range; `SupportsRowDates` is client-side, so a crafted or stale message could date a task on a hard Tablet or an assigned task; server-side validation has no spec scenarios. | Design critic, Spec critic |
| 9 | Major | design/tasks 3.1: client lock check timing | Client checks `IsLockedByOther` only at send time, so the player picks a date then is refused after Set. | Design critic |
| 10 | Major | design/spec: date picker dismissal and the Dropdown | Left-click outside vs not modal, Escape with a month Dropdown's own overlay open, deleted/locked row while open, editor closing are unspecified; the dropdown is both 'owned' and a flagged risk. | Comprehension, Design critic, Spec critic, Readiness |
| 11 | Major | design/tasks 3.1: picker anchor and Escape wiring | Picker needs the row's LayerLink and a vanished-row check that live privately in `ScribeRowMenuHost`; Escape is wired at two call sites that call only `TryCloseRowMenuOnEscape`; Escape order with the dropdown undefined. | Readiness |
| 12 | Major | design/tasks 2.2: RefreshItems hook and placement | The call site for `RefreshItems()` is left to the implementer, and `Open` computes placement from `items.Count`, which changes when entries change. Also `RefreshItems` vs the host's `Tick` could double-handle closing. | Readiness, Comprehension |
| 13 | Major | spec: date entries scope | 'Read-only surface', Scriptorium, assigned tasks (`Assignment != null` is still `Kind == Task`), subtasks, collapsed groups and filtered rows are undefined; `SupportsRowDates` is a second capability flag beside scribe-dialog-base's. | Spec critic, Readiness, Comprehension |
| 14 | Major | spec: Date picker scenarios | Year bounds, a stored day past the month end, a starting year above 9999, and the 'Day follows the month length' scenario (vacuous in vanilla where months are equal) are missing or untestable; whether the day returns is unstated. | Spec critic, Design critic |
| 15 | Major | spec/design: date chip text and staleness | Singular 1-day forms, day-count semantics, a clamped stored day, and a dialog left open across midnight are unspecified; the chip's Done case and a date-only template are missing from `ScribeDateChipText.For`; the separator may lack a glyph. | Spec critic, Comprehension, Readiness |
| 16 | Major | proposal/design/tasks 6.1: lang keys | 'About 15 new lang keys' does not match the design's plural key plan, and no key names are given. | Readiness, Spec critic, Comprehension |
| 17 | Major | spec: Dates travel with copies | Repeats change 2's `dated-task` requirement and bundles an unrelated assignment-drop sentence; only clipboard/Scriptorium transfer is new. | Spec critic, Readiness |
| 18 | Major | tasks: test coverage | No tasks verify clear-keeps-pin, server persistence of the repeat flag, repeat completion across policies and runs, Scriptorium transfer, clipboard copy, assignment drop, Pin Tab having no date entries, or each server refusal. | Comprehension, Readiness |
| 19 | Major | spec: contradicts add-task-row-context-menu | That change's scenarios 'Editor view entries' and 'menu offers Pin or Unpin and Delete' become false for a plain task once date entries exist, and it is not yet archived. | Comprehension, Design critic, Readiness |
| 20 | Major | design/spec: dated task in a collapsed group, filtered row, Task Notice | See datable scope row; chip on a hidden row is moot. | Spec critic |
| 21 | Nit | design: Plural forms | Slavic plural agreement is accepted in Risks but not in the spec or tasks, and `check-locales.py` does not check it. | Comprehension, Spec critic |
| 22 | Nit | design: Date order refresh hook | Claimed: no dialog-level hook rebuilds the chip when the Date order changes. | Design critic |
| 23 | Minor | design: ScribeWorldCalendar / Classify performance | `ToOrdinal` loops year by year up to Year 9999 for each Classify. | Design critic |
| 24 | Minor | design: editor focus after the picker closes | Dirty marking is vague and focus could drop when the row rebuilds. | Readiness |
| 25 | Nit | spec: chip colors | 'Distinct warm colors' is subjective. | Spec critic |
| 26 | Nit | spec: bundled requirements | 'Date entries' bundles gating with the toggle entry; 'Date chip' bundles content, colors and Date order refresh. | Spec critic |
| 27 | Nit | proposal/spec: surface lists | Surface exclusions listed twice with slightly different names. | Comprehension |
| 28 | Nit | design: Pin Tab has local completion? | Design said the Pin Tab goes through local completion paths. | Readiness |
| 29 | Nit | proposal: Pin Tab depends on change 4 | Roadmap says change 4 depends only on change 2. | Comprehension |

Counts: Blocker 1, Major 19, Minor 2, Nit 7 (total 29).

## Triage

| # | Verdict | Reason / proposed fix |
|---|---|---|
| 1 | Fix | Add MODIFIED deltas with full restated text for the three requirements (`specs/player-pins`, `specs/pinned-task-tab`, `specs/scribe-dialog-base`); proposal lists them; design adds a Spec deltas decision. |
| 2 | Fix | Override only Delete (to Sink). Spec states the result per policy with scenarios for Delete, Keep, Unpin and UnpinSink including pin retention and position. Proposal and design reworded. Recorded as assumption 1. |
| 3 | Fix | `EffectivePolicy(policy, blocks)` takes the run; any dated repeating Task in the run overrides Delete to Sink for the whole run; applied in ApplyLeaf, ApplyBoundParent and ApplyDiscardParent after `CollapsePolicyForReadOnlySource`; ApplyDelete named as unaffected in design and spec. Verified: `ApplyDelete` at ScribeCompletion.cs:127 is standalone trash with no policy parameter. |
| 4 | Fix | Verified: ScribeModSystem.PinOperations.cs:163 calls `Decide(nowDone, policy)` with no block; HudScribePins.cs:313 and :350 predict from policy alone. Documented as accepted limitations until change 4 (assumption 10); spec says the resolved document decides. |
| 5 | Fix | Verified: ScribeDialogBase.Editor.cs:245-248 uses the raw policy. With only Delete overridden the raw check still rebuilds, but it is derived from the outcome anyway: `LocalOutcome` gains `Moved`; task 1.5 and 6.1. |
| 6 | Fix | Exemption limited to `Kind == Task`; tracker-completion Delete explicitly unaffected (design, assumption 2). |
| 7 | Fix | Message carries one operation (SetDate keeps repeat, ClearDate, SetRepeat ignored when undated). Read view applies optimistically through `ApplyLocalOptimisticEdit`. New scenarios: two quick toggles, concurrent clear then toggle, Set keeps repeat, refused toggle shows true state. Assumption 3. |
| 8 | Fix | Server refusal list in design (unresolvable, unknown id, not datable, read-only policy, invalid date) with a scenario each in the spec, and Atlas scenarios in task 4.2. Lock refusal keeps the standard locked message; all other refusals are silent. |
| 9 | Fix | Check when Add date, Change date or Repeat yearly is chosen; picker does not open when locked; also closes if the lock is taken while open. Spec scenario added. |
| 10 | Fix | Month stepper replaces the dropdown (dropdown moved to roadmap Later). New requirement 'The date picker closes predictably' with scenarios; left-click outside closes and is consumed; right-click on another row reopens through the barrier. Assumptions 4 and 9. |
| 11 | Fix | Verified: ScribeDialogBase.cs:583 and :612 call only `TryCloseRowMenuOnEscape`. Host owns and ticks the picker and exposes `LinkOf` and `RenderOf`; both call sites call one method closing the picker first, then the menu. Tasks 3.1 and 3.2. |
| 12 | Fix | Verified: ScribeRowMenuHost.cs:148 `Place(press, items.Count)`. Per-frame upkeep in `ServiceRowMenu` compares resolved entries to the shown ones and calls `RefreshItems`, which recomputes placement; `Tick` stays the single closer for a vanished row. Assumption 11 (comparison replaces the 'document version' wording). |
| 13 | Fix | Datable defined as plain Task with no Assignment; read-only = `ScribeDocumentPolicy.ReadOnly`; scenarios for Scriptorium, Chalkboard, a Private Lectern viewer, subtask and assigned task; design says collapsed and filtered rows show no chip because they are not shown. `SupportsRowDates` is a dialog hint, not a scribe-dialog-base requirement, and the server never trusts it. Assumption 8. |
| 14 | Fix | Scenarios for bounds, past-end day, month carrying into the year; the clamp is a Core test with an explicit uneven `IScribeCalendarShape` (task 1.3); the day does not return; starting year above 9999 starts at 9999. |
| 15 | Fix | Spec scenarios for 1 day ahead and overdue, clamped day, midnight staleness accepted (assumption 6). `For` returns `(StatusLangKey, Count, TemplateLangKey)` with a null status key for Done. Separator lives in the lang template; task 7.1 checks the middle dot. |
| 16 | Fix | Design enumerates all 16 key names; proposal says 16; task 7.1 enumerates them. |
| 17 | Fix | Requirement reduced to the clipboard and Scriptorium paths; assignment-drop sentence removed from the spec and kept as a test in task 6.2. |
| 18 | Fix | Tasks 1.2, 1.5, 4.2, 6.1 and 6.2 rewritten to cover every scenario with Core or Atlas tests. |
| 19 | Fix | Amended that change's spec in place ('plus the date entries on a datable task (see add-notebook-task-dating)'); proposal and design state that changes 1 and 2 archive first (assumption 12). |
| 20 | No change needed | Folded into the datable-scope row: hidden rows are not drawn, so no separate rule is needed. |
| 21 | Fix | Stated in design Risks and 'Lang keys'; assumption 7. |
| 22 | Contest | False. ScribeDialogBase.cs:329 subscribes `OnMyPinsChanged` (:460), which rebuilds the editor, Pin Tab and read bodies; `ScribeModSystem.ClientPrefs.cs:301` fires `MyPinsChanged` from `UpdateMySettings`. Design now cites this. |
| 23 | Contest | Not a hot path: it runs once per dated row per rebuild, rows number in the tens, and the loop is integer arithmetic. Design notes per-build caching as a later option. |
| 24 | Fix | Design and task 4.1 name `isDirty`, `RebuildBody()` and `pendingFocusRow`, as `OnMyPinsChanged` does. |
| 25 | Fix | Testable rule: four states use four distinct colors; values tuned in playtest. Separate 'Date chip appearance' requirement. |
| 26 | Fix | Split into 'Date entries', 'The Repeat yearly entry', 'Date chip', 'Date chip appearance', and the picker requirements. |
| 27 | Fix | One list in the spec; proposal summarizes it. |
| 28 | Fix | Verified: ScribeDialogBase.PinTab.cs:213 completes by identity through the server. Design now says the Pin Tab and HUD send `ScribeCompleteTaskMessage`; the read view and editor have the local paths. |
| 29 | No change needed | Accurate: change 4 adds the date to the pin snapshot; this change just excludes the Pin Tab until then. |

Verdicts: Fix 25, Contest 2, No change needed 2, Ask 0.

## Code claims checked

- Editor rebuild reads the raw policy: true (`ScribeDialogBase.Editor.cs:245-248`).
- Server unresolvable branch calls `Decide(nowDone, policy)` with no block: true (`ScribeModSystem.PinOperations.cs` ~163).
- HUD predicts from the policy alone: true (`HudScribePins.cs:313`, `:350`).
- Escape wiring calls only `TryCloseRowMenuOnEscape`: true (`ScribeDialogBase.cs:583`, `:612`).
- `Place(press, items.Count)`: true (`ScribeRowMenuHost.cs:148`).
- `ApplyDelete` is standalone trash with no policy: true (`ScribeCompletion.cs:127`).
- Pin Tab completes through the server by identity: true (`ScribeDialogBase.PinTab.cs:213`).
- No dialog hook rebuilds on a Date order change: FALSE (`ScribeDialogBase.cs:329`, `ScribeModSystem.ClientPrefs.cs:301`); finding 23 contested.

## Applied

Proposal, design, the `notebook-task-dating` spec, the three MODIFIED deltas (`player-pins`, `pinned-task-tab`, `scribe-dialog-base`) and tasks were edited together; `add-task-row-context-menu`'s spec wording and the roadmap's Later section were amended. `openspec validate add-notebook-task-dating --strict` passes. No code was edited.

## Assumptions recorded in design.md (user to confirm)

1. Only the Delete policy is overridden (to Sink) for a repeating task; a run is protected when any Task in it repeats.
2. The exemption covers plain Tasks only; tracker-completion Delete is unaffected.
3. The read view applies date changes optimistically, and the message carries an operation.
4. Month stepper (carrying into the year) instead of a month dropdown.
5. The chip is display-only; placeholder warm colors tuned in playtest.
6. A dialog open across midnight keeps a stale chip status until the next rebuild.
7. Slavic plural agreement is an accepted gap.
8. Assigned tasks get no date entries.
9. A left-click outside the picker closes it and is consumed.
10. The unresolvable-source server branch and the HUD fade-then-reappear under Delete are known limitations until change 4.
11. `RefreshItems` is driven by comparing resolved entries each frame, not a document version (deviates from the 'version' wording in the triage brief).
12. Changes 1 and 2 archive before this one.
13. `SetRepeat` on an undated task is ignored by the server.

---

# Round 2 (2026-11-16, last round)

Round 2 used a spec critic and a readiness reviewer against the round-1 artifacts. This is the last review round: any item still open after these fixes is listed under accepted trade-offs. Findings and decisions below come from the coordinator's summary of the two reports.

## Round 2 findings and triage

| # | Severity | Where | Finding | Raised by | Verdict | Reason / fix |
|---|---|---|---|---|---|---|
| R2-1 | Blocker | ScribeCompletion.ApplyDiscardParent (ScribeCompletion.cs ~210-225) | It deletes the owned run's children under EVERY policy, so a repeating child (or parent) under Discard children loses data even when the policy is Keep, Sink or Unpin; the round-1 rule only overrode Delete. | Spec critic, Readiness | Fix | Verified: ApplyDiscardParent calls `doc.DeleteRange(runStart, runEnd)` before `ApplyLeaf`. Rule: whenever the owned run holds a repeating Task the discard is suppressed for every policy (parent done, children kept as they are, Delete overridden to Sink). Spec scenarios for Discard x {Keep, Sink, Unpin, UnpinSink, Delete}, a repeating child under a non-repeating parent (Independent, Bound, Discard), a depth-1 repeating subtask alone, and a non-repeating task under a repeating parent under Bound with Keep; task 1.5 tests them. |
| R2-2 | Major | design/spec: Escape | 'Picker and menu both open' cases are unreachable (Add date closes the menu first). | Spec critic | Fix | Dropped. Escape closes the open picker or the open menu; one method, two call sites. |
| R2-3 | Nit | design/spec: Private Lectern viewer | A 'viewer who cannot edit / Private Lectern' scenario and ownership check are not real: Private is reserved and every lectern behaves Public. | Spec critic | Fix | Verified in `lectern-access-mode` spec. Scenario and sentence removed; entries hide only where the surface cannot edit (`ScribeDocumentPolicy.ReadOnly`). |
| R2-4 | Major | design/spec: refused optimistic changes | An optimistic read-view change stays on screen after a refusal unless the server re-sends the document; the Risks text claimed 'the authoritative sync' fixes it; discarded editor dates were unspecified. | Spec critic, Readiness | Fix | Every server refusal re-sends the authoritative document to the sender (lock refusal keeps its in-game error). Spec: refused change reverts the chip; editor date discarded without saving is lost. Task 4.3 reuses or adds the per-player resend and adds Atlas scenarios; Risks rewritten. |
| R2-5 | Minor | design/tasks 1.5, 6.1: LocalOutcome.Moved and rangeOrMove | The round-1 fix added a `Moved` flag and a `rangeOrMove` change. | Readiness | Fix | Dropped both. `rangeOrMove` (ScribeDialogBase.Editor.cs:245-248) already rebuilds for Delete, Sink and UnpinSink, and Delete overridden to Sink still rebuilds. Task 6.1 keeps an in-game check that a repeating task checked under Delete sinks in the open editor. |
| R2-6 | Major | tasks 5.1/5.2: chip plumbing | The chip data has no named carrier, construction sites are unnamed, reused rows would not refresh, and the deleted-row ghost ignores the chip height. | Readiness | Fix | Task 5.1 names `ScribeReadRowData` and `ScribeEditRowData` (date, repeat, status), every construction site (read and editor builders; Pin Tab, Assignment Desk and Task Notice pass no chip data), chip built from props in `Build` so the `UpdateWidget` reuse after `OnMyPinsChanged` refreshes, and the ghost including the chip height. |
| R2-7 | Major | tasks order and wiring | In-game checks that need a set date come before any task that can set one; nothing wires the new actions into the existing action handlers or the picker result. | Readiness | Fix | Reordered: checks that need dates moved to 4.5 and 4.6, after setting dates. New task 4.1 wires AddDate, ChangeDate, ClearDate and RepeatYearly into `RunRowMenuAction`, `RunReadRowAction`, `RunEditorRowAction` and the picker callback (verified those four methods exist in ScribeDialogBase.RowMenu.cs). |
| R2-8 | Minor | design/tasks 4.3: server naming | Handler names, the lock call and the error string were unspecified or invented. | Readiness | Fix | Verified in code: `OnServerReceivedDeleteTask` -> public `DeleteTaskForPlayer`; lock `docHost.IsEditorLockHeldByOther(player.PlayerUID)`; `player.SendIngameError("scribe-lectern-locked", Lang.Get("scribe:scribe-gui-locked"))`. Design and task 4.3 use `OnServerReceivedSetTaskDate` -> public `SetTaskDateForPlayer` with exactly those. |
| R2-9 | Major | design/tasks 2.2: RefreshItems comparison | The comparison fields were unspecified, and refreshing Pin or Unpin would contradict change 1 (the entry keeps its opened label and acts on current state when chosen). | Spec critic, Readiness | Fix | Compare `LabelLangKey`, `Checked`, `CloseOnChoose`, `SeparatorBefore` (never `OnChoose`); the refresh covers the Edit entry and the date group only. Change 1's amended sentence reworded to 'where add-notebook-task-dating offers them', and its spec gains 'a menu with no entries left closes' and 'a toggle entry keeps the menu open' scenarios. |
| R2-10 | Major | spec: date picker details | Day stepper end behavior, month stepper carry and bounds, the clamp-without-restore rule's vanilla reachability, out-of-view closing, right-click on the same row or empty space, left-click outside, and wheel scrolling were unspecified. | Spec critic | Fix | Spec scenarios for each: day stepper stops at 1 and the month length (no carry), month stepper carries and is blocked at Year 1 month 1 and Year 9999 month 12, clamp rule labelled a Core-model rule (uneven shape), picker closes when its row scrolls out of view, right-click same row reopens the menu and on empty space only closes, left-click outside closes and is consumed, wheel scrolling not handled by the barrier. |
| R2-11 | Major | spec: repeating-task pin results and limitations | Pin retention per policy cites no source, a possible separate 'unpin on complete' setting is unreconciled with `player-pins`' 'removes the pin unless the player opted out', and the accepted HUD and unresolvable-source divergences are not in the deltas. | Readiness, Spec critic | Fix | Verified: `ScribeCompletionPolicy` is exactly Sink, Unpin, Delete, Keep, UnpinSink and no separate unpin-on-complete setting exists in Core or Mod, so the opt-out is the policy itself (Keep and Sink keep the pin). Design cites the enum and `Decide`; the `player-pins` and `pinned-task-tab` deltas now state the two divergences as accepted limitations; the repeating-task requirement lists the pin result per policy. |
| R2-12 | Major | spec: requirement hygiene | The clipboard/Scriptorium requirement duplicates `dated-task`; the chip requirement bundles content, Date order, staleness, done state and colors; the midnight scenario, color wording and string localization are loose; one clause repeats the surface list; out-of-view row. | Spec critic | Fix | Clipboard requirement removed (tests kept in task 6.2; no assignment-drop scenario fits an existing requirement, so none added). Chip split into 'content and status', 'follows the Date order and the world's day' and 'appearance'. Midnight scenario: 'does not change until the dialog rebuilds'. Colors tied to the named constants `DateChipDue`, `DateChipOverdue`, `DateChipUpcoming`, `DateChipDone` with four different values. Strings localized, English as examples. 'Task Notice, HUD or Inbox' clause dropped from the entries requirement. |

Counts: Blocker 1, Major 8, Minor 2, Nit 1 (total 12). Verdicts: Fix 12, Contest 0, Ask 0.

## Decisions adjusted while applying

- R2-1: the brief said the parent is marked done and the children are kept as they are. When the effective policy sinks the parent, moving the parent alone would strand its children above it, so the parent moves with its children (`MoveRangeToBottom` of the owned run). Recorded as assumption 2.
- R2-11: no separate unpin-on-complete setting exists, so the reconciliation with `player-pins` is stated in design, not as a delta to a second requirement.
- R2-12: no assignment-drop scenario was added, because none fits an existing requirement; it stays a test (task 6.2).
- The `ScribeCompletionPolicy.Keep` doc comment says 'server-side identical to Sink', but `ScribeCompletion.Decide` leaves a Keep task in place. The artifacts follow `Decide`; the stale comment is outside this change.

## Accepted trade-offs (no further review round)

- Placeholder chip colors, tuned in playtest.
- Slavic plural agreement for "in N days".
- A dialog open across midnight keeps its chips until it rebuilds.
- A month dropdown in the picker and a clickable chip are deferred.
- The picker's barrier does not handle wheel scrolling (a scroll out of view closes it).
- Until change 4 adds a repeat bit to the pin: an unresolvable source document follows the player's policy, and the HUD can fade a repeating task out under Delete and bring it back.

## Updated assumptions (user to confirm)

The list in `design.md` is the current one (14 items). Changes from round 1: assumption 2 is new (Discard children suppression and moving the parent with its children); assumption 4 now includes the server re-send on every refusal; assumption 5 adds that a menu and a picker are never open together and Escape closes whichever is open; assumption 7 is reworded to 'does not change until it rebuilds'; assumption 9 drops the ownership idea (no ownership check); assumption 12 now states Pin and Unpin stay as the menu opened. The round-1 item about 'Lectern in Private mode' was removed.
