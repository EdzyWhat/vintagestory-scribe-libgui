# Review: add-pinned-task-dates   (2026-11-17, round 1)

## Paraphrase (from the comprehension reviewer, verbatim)

**What this change proposes**
A pinned task currently loses its date. The Pin Tab and the HUD draw from the pin's saved snapshot, and that snapshot has no date or repeat flag. The change adds the task's date and its "Repeat yearly" flag to the snapshot (pin blob version 9 becomes 10). The snapshot captures them at pin time, from the server's document when it can be resolved and from the pinning client's copy otherwise.

After that, the date and flag are refreshed for every player who has the task pinned. This happens through a new server method, `RefreshDatesForDocument`, which touches only those two fields and never text or done state. The Pin Tab and the HUD then show the same chip the Notebook shows (status plus date, or date only when checked). The HUD gets its own four chip colors and rebuilds when the in-game day changes. Separately, a new pin-level `EffectivePolicy(policy, pin)` turns Delete into Sink for a repeating pin. The HUD uses it for its pending-completion preview, and the server uses it when it cannot resolve the source document. This removes two limitations the earlier change `add-notebook-task-dating` accepted.

**How the existing system works, per these files**
The server owns the pin store and pushes each player's list as a versioned blob. Each blob version only appends per-pin fields. The Pin Tab and the HUD render only from the snapshot, so they work with the source document unloaded. Snapshots are filled when a task is pinned and refreshed by `ReconcileSnapshotsForActor` only for the player who just saved an edit, so one player's edit cannot rewrite another player's pins. The previous change added the chip, the date operations, the repeat flag and `ScribeCompletion.EffectivePolicy(policy, blocks)`, which operates on documents and not on pins. The HUD previews a completion locally for a short undo window from the player's policy, then sends it.

Paraphrase mismatches: none. The paraphrase matches the artifacts. It overstates one claim ("removes two limitations"): one narrower gap remains (finding 8).

## Findings

Reviewers: C = Comprehension, S = Spec critic, D = Design critic, R = Readiness.

| # | Severity | Where | Finding | Raised by |
|---|---|---|---|---|
| 1 | Blocker | specs: pinned-task-tab, pinned-task-hud vs `add-notebook-task-dating` `notebook-task-dating` | That capability says Pin Tab rows show no chip and chips are computed at build only; this change contradicts it with no delta. After both archive, the main specs conflict. | S |
| 2 | Blocker | specs: HUD "complete by stable identity", player-pins HUD undo window; tasks 5.4 | Scenarios say a repeating pin under Delete rests at the bottom, dimmed, and "does not fade". The HUD and Pin Tab sort and dim only when the policy is Sink or UnpinSink (`HudScribePins.SunkVisual`, `BuildOrderedRows`, `OrderedPinsForDisplay`), so under Delete the row stays in place at full opacity, and a Sink preview does fade (`IsFadingOut` includes Sink). The proposal also ruled out ordering changes. | R, D, (S 7) |
| 3 | Major | design: Refresh for every holder; tasks 2.3 | Claims the refresh runs after an "existing acting-player reconcile" on every path. `SetTaskDateForPlayer` has no reconcile (it only calls `PersistFromReader`); the reconcile exists at Network.cs:394, ExternalTaskApi.cs:85 and the writing station. | C, D, R |
| 4 | Major | design/spec: Refresh paths | The call-site list is hand-made; other persist paths (paste/clone, other whole-document replacement) are neither covered nor ruled out, while the spec says "any path". Unresolvable-host date set refreshes nothing, unstated. | C, D, R, S |
| 5 | Major | design/tasks 2.1 | Client fallback fields are untrusted; no validation rule (TryCreate, kind, partial triple, repeat without date) and no statement of which source wins. | C, S, D, R |
| 6 | Major | tasks 2.1/4.1 | The optimistic pin (`SendSetPin` / `SetOptimisticPin`) carries no date, so a fresh pin shows no chip, then the row grows on the push. | D, R |
| 7 | Major | tasks 5.4 | Unclear whether `PendingCompletion` stores the effective or the raw policy; storing the effective one can make the server sink a task whose document no longer repeats. `awaitingRemoval` is undefined in the artifacts. Mid-window flag change unspecified. | C, D, R |
| 8 | Major | design/specs: gaps | Pin-level rule checks only the pin's flag; a pinned non-repeating parent with a pinned repeating child, and the unpinned-child case, still diverge. The gap is stated as "rare", written into SHALL text, and has no scenario; proposal says both limitations removed. | C, S, D |
| 9 | Major | design: One chip builder, tasks 1.3 | Pin predicates use "not an accepted assignment"; block rule is `Assignment == null`. Pair names also collide (`IsDatablePin` includes the date, `IsDatable` does not). | C, S, D, R |
| 10 | Major | specs/design: Pin Tab chip | Tablet and Assignment Desk return `SupportsRowDates == false` but still build the Pin Tab; the design shows chips on every Pin Tab row. | R (D unsure) |
| 11 | Major | specs: chips | Orphaned pins would show an ever-growing "Overdue (N days)" for a task that no longer exists; unspecified. | S |
| 12 | Major | specs: player-pins | No scenarios for the load rules (invalid-date blob, repeat-without-date), the offline holder, or the saved-edit path. | S, C |
| 13 | Major | design: codec | Wire layout ambiguous (presence flag, repeat flag inside or outside the guard); the "repeat without a date" test is only reachable by hand-built bytes. | D, R |
| 14 | Major | design: Pins that predate | Old pins stay undated until a document save, which a read-only player never does; no refresh on host register or Notebook open. | D, C |
| 15 | Major | design: version 10 | "Newer client cannot be older than server" is false (VS does not sync mods); an older client blanks its pins, and a mod downgrade loads the store empty and the next save overwrites it. | D |
| 16 | Major | specs: HUD day change | Day change is bundled into one requirement; no scenarios for a hidden HUD, a backward time jump, no dated pins; the HUD reaction to a Date order change is unspecified; no helper signature or last-day storage. | C, S, R |
| 17 | Major | specs: HUD chip | Chip behaviour for a pending (checked, unsettled) row is unspecified. | S |
| 18 | Major | specs: MODIFIED HUD requirement | Three specs state the repeat rule with different scope and will drift; the HUD requirement names only three policies. | C, S |
| 19 | Nit | proposal / design | Archive order and delta conflict handling not explained. | C, S |
| 20 | Nit | design: Pin Tab row | Height of the empty chip box and effect on undated rows unstated. | C |
| 21 | Nit | design: orphan wording | "Existing orphan rules" is vague about what the refresh does to other holders' pins. | C |
| 22 | Nit | specs/design: HUD chip | Colour criterion "reads over the world" is subjective; chip size and the smaller-font roadmap note are unreconciled. | C, S |
| 23 | Nit | tasks 6.2 | Playtest item for old pins has no spec scenario; storm corruption "not corrupted" has no verification; 5.3 and 3.1 name no helper signature or asserted value. | C, R, S |
| 24 | Nit | tasks 2.1 | New `SetPinForPlayer` parameters must go after `insertEdge`. | R |

## Code claims checked

- `SetTaskDateForPlayer` (ScribeModSystem.PinOperations.cs:243) calls only `PersistFromReader`: true (finding 3).
- `IsFadingOut` (HudScribePins.cs:348) is true for Unpin, Delete, Sink and UnpinSink; `awaitingRemoval` is only added for Unpin, Delete and UnpinSink: true (finding 2). `SunkVisual` and `sinkOrder` read the raw policy: true.
- `OrderedPinsForDisplay` (ScribeDialogBase.PinTab.cs:182) sinks only for Sink or UnpinSink: true.
- `SendSetPin` builds the optimistic `ScribePinnedRef` without a date: true (finding 6).
- `GuiDialogScribeTablet` and `GuiDialogScribeAssignmentDesk` return `SupportsRowDates == false`; `BuildPinnedContent` is in the base and builds for both: true (finding 10).
- `UpdateMySettings` fires `MyPinsChanged`, which the HUD subscribes to: true, so a Date order change already rebuilds the HUD; the change adds only a verification task (finding 16).
- Reviewer claim that `ApplyEdit` reconciles through `ReconcileActorPins` in the writing station: true (BlockEntityScribeWritingStation.cs:412).
No reviewer finding was false.

## Triage

| # | Verdict | Reason / fix |
|---|---|---|
| 1 | Fix | Added `specs/notebook-task-dating/spec.md` with MODIFIED full text of "Date chip content and status" and "Date chip follows the Date order and the world's day", and of "A repeating task is not deleted by completion" (its accepted-limitations sentence is now stale). Proposal lists the capability; archive order stated in proposal, design (assumption 8). |
| 2 | Fix | Made it pin-aware (decision taken by default, see below): `EffectivePolicy(policy, pin)` also drives `SunkVisual`, `BuildOrderedRows`, `OrderedPinsForDisplay` and `OnPinReorder`; new player-pins requirement "A repeating pin orders and dims like a sunk pin"; preview scenarios reworded to "the ordinary Sink preview, never leaves or collapses"; non-goal reworded; tasks 4.3, 5.4. |
| 3 | Fix | Design and task 2.3 now say `SetTaskDateForPlayer` calls the refresh itself when changed (no reconcile exists there), and the three reconcile paths call it after their reconcile without double pushing. |
| 4 | Fix | New task 2.4 audits every persist site and records in/out; unresolvable host is stated as an accepted no-op; spec lists the paths and has a saved-edit scenario. |
| 5 | Fix | Server validates with `ScribeDate.TryCreate`, Task kind only, partial or invalid is undated, repeat forced off, document wins; spec scenarios and Atlas cases added; writer also drops repeat without a date. |
| 6 | Fix | Optimistic snapshot carries date and repeat (spec requirement, design, task 2.1 with a verify step). |
| 7 | Fix | `PendingCompletion` and `SendCompletion` keep the raw policy; the effective policy is for preview, ordering and `awaitingRemoval` only; `awaitingRemoval` explained; mid-window flag change scenario added. |
| 8 | Fix | Gap widened and stated once in player-pins and design (a run containing a repeating member the pin does not carry, including a pinned non-repeating parent with a pinned repeating child); scenario added; proposal now says one of two limitations is removed and the gap remains. |
| 9 | Fix | Predicates renamed `PinShowsDate` and `IsRepeatingPin`, no assignment clause, with the reason (dates cannot sit on assignment blocks; placement drops the date). |
| 10 | Fix | Pin Tab chip gated on the dialog's `SupportsRowDates`; HUD ungated; spec scenario and task 4.1 verification. |
| 11 | Fix | Orphaned pins show no chip on either surface (`PinShowsDate` excludes them); scenarios in both specs. |
| 12 | Fix | Scenarios added: invalid-date blob, offline holder, saved-edit refresh, client date for a non-task. |
| 13 | Fix | Exact layout fixed in design and task 1.1 (`hasDate`; if set year int, month byte, day byte, repeat bool); the unreachable test replaced by an invalid-date test. |
| 14 | Fix (partly) | Kept as an accepted assumption: no load-time or host-register scan (host load order), now stated in Risks, the player-pins requirement and the playtest item, including read-only Notebooks. Not changed: adding a `RegisterHost` refresh is a scope increase; offered to the user as an option. |
| 15 | Fix | Design corrected: false claim removed; downgrade consequences recorded under Risks as accepted with no mitigation here; roadmap Later gets "Pin store downgrade safety". |
| 16 | Fix | Split into the requirement "HUD chips follow the world's day and the Date order" with scenarios (backward jump, no dated pins, hidden HUD, Date order); helper `ShouldRebuildForDayChange(lastDay, currentDay, anyDatedDisplayed)`, last day stored on the HUD, day from `ScribeWorldCalendar.FromTotalDays`; the Date order path already works via `MyPinsChanged`, task 5.5 verifies. |
| 17 | Fix | HUD chip requirement: a pending row shows the checked form (date only, dimmed) from the displayed done state; reverts on undo; scenario. |
| 18 | Fix | HUD and Pin Tab deltas reference the player-pins repeating-pin rule instead of restating; the HUD requirement names "every other policy (keep, unpin and sink) applies as chosen". |
| 19 | Fix | Proposal "Depends on" and design assumption 8 say how to reconcile deltas if archived with different wording. |
| 20 | Fix | Empty chip box is zero height; undated rows unchanged (design, specs, tasks). |
| 21 | Fix | Design and spec: the refresh leaves a pin whose task is not in the document as it is and never orphans. |
| 22 | Fix | Criterion = four distinct values, distinct from the HUD text color, tuned in playtest; HUD chip is the Notebook size times the HUD font scale, no extra shrink; full month names at smaller font stay a playtest item. |
| 23 | Fix | Playtest item for old pins now has the Risks entry and player-pins text; storm corruption stays a playtest item (6.2); 5.3 gets the helper signature and a Core test; 3.1 asserts the `Classify` status, not localized text. |
| 24 | Fix | State in design and task 2.1: new parameters after `insertEdge`, passed by name. |

## Decisions taken by default (the user is away; overturn any of these)

1. **Notebook-dating deltas.** This change modifies `notebook-task-dating` (chip and repeat requirements) and must archive after `add-notebook-task-dating`.
2. **Pin-aware ordering and dimming for a repeating pin under Delete.** *(Superseded by user decision: the repeat flag does not change completion. Delete deletes a checked dated task, repeating or not. The pin snapshot no longer carries the repeat flag and the effective-policy, ordering and dimming work is dropped.)* Chosen over rewording the scenarios to "stays in place at full opacity", because the Notebook sinks the task and a pin that stays in place would disagree with it. Costs a small ordering change in the HUD and Pin Tab.
3. *(Superseded by user decision, see 2.)* **Sink preview stays a fade then settle** (the existing Sink preview), not a no-fade variant.
4. *(Superseded by user decision, see 2.)* **Raw policy is sent; effective policy is display-only.**
5. *(Superseded by user decision, see 2.)* **Parent and child both pinned follows the player's policy** in the HUD and unresolvable path; the documented gap is widened.
6. **Orphaned pins show no chip.**
7. **Pin Tab chip is gated on `SupportsRowDates`** (none on a Tablet or the Assignment Desk); the HUD always shows it.
8. **Old pins stay undated until the next save or re-pin**, no host-register refresh (finding 14, the one place a different default is plausible).
9. **Downgrade risk accepted**, backlogged on the roadmap.
10. **A pending checked row shows the checked chip form.**

## Superseded by user decision (after round 1)

Completion ignores the repeat flag. Under the Delete policy, checking a dated task deletes it like any task,
repeating or not; deleting one instance leaves other instances alone. This removes: the pin's repeat flag, `IsRepeatingPin`, the pin-level
effective policy, pin-aware ordering and dimming, the HUD preview and `awaitingRemoval` change, the
unresolvable-source change, the parent and child gap, and findings 2, 7, 8 and 17's repeat parts (findings 17's chip-form
part stays). The matching rules in `add-notebook-task-dating` are removed too (see its task group 8).
