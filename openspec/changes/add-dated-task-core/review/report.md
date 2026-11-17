# Review: add-dated-task-core

Four fresh-context reviewers (comprehension, spec critic, design critic, readiness) read only this change,
the main specs it touches, the project context and the repo. `openspec validate --strict` passed before
review. Code claims below were spot-checked; all held (one path correction: `ScribeImportValidator` is in
`src/Mod/`).

## Paraphrase (comprehension reviewer, verbatim)

**What this change proposes.** This is groundwork for the calendar features, and players will see almost
nothing yet: nobody can set a date on a task until a later "Notebook dating" change. Under the hood, a
`ScribeBlock` gains an optional `ScribeDate` (year from 1, month 1-12, day) and a `RepeatYearly` flag. These
are stored as written, not as a day count, and saved in binary codec v14, JSON codec v3, and the TSV
`Special` cell of plain tasks. Core gets three pure, tested pieces: a calendar-shape interface (12 months of
the world's days-per-month, with a uniform implementation), a status function (Done / DueToday /
Upcoming(n) / Overdue(n)) that clamps a day past the month's end, and a next-occurrence function for
repeating tasks that nothing calls yet. The one change players will notice is a new client "Date order"
setting (Language default / DMY / MDY / YMD). It applies to every displayed date through three new lang
templates, and `NotebookHost.FormatDate` stops using the game's 1386-based `Calendar.Year` and counts years
from 1, like the timestamp path already does.

**How the existing system works, per these files.** `ScribeDocumentCodec` (v13, progressive reads of
v5-v13) appends each new per-block field behind a `version >=` gate. JSON (`v`=2 in code) and TSV (six fixed
columns, extra data packed into `Special`) are lossy-by-design clipboard lanes. Dates are display strings
today. The server stamps an "identity" date string with `NotebookHost.FormatDate`, which uses
`Calendar.Year` and so shows 1386. That string is stored on History entries, Guest Book visits, assignments
and tablets, and Guest Book dedup and note-edit addressing key on it. When a numeric `TotalDays` timestamp
exists, the viewing client re-formats it in its own locale with `FormatDateFromTimestamp`, which counts
years from 1. Without a timestamp, the baked string is shown.

**Mismatch with intent:** the paraphrase matches what the change says. The problem is that what the change
says about `FormatDate` is wrong. It treats `FormatDate` as a display helper, but it is the server's
identity stamp (finding 1).

## Findings

| # | Severity | Where | Finding | Raised by |
|---|----------|-------|---------|-----------|
| 1 | Blocker | design "All existing date helpers route through the formatter"; task 4.2 | `FormatDate(sapi)` makes server identity strings: History `InGameDate`, Guest Book dedup key `(PlayerName, InGameDate)` (`GuestbookStore.cs:44`), note-edit addressing, assignment transitions and pin dirty-check (`ScribePinStore.cs:187`). The server has no Date order (`ScribePlayerSettings` is client-local, never synced). Changing the year to count-from-1 changes every new stamp, so a same-day revisit across the upgrade duplicates a Guest Book entry. In singleplayer, a shared formatter that reads settings would make stamps depend on a display preference. | all 4 |
| 2 | Blocker | spec "Years count from 1", "Dates display in the player's chosen order"; design Risks | Rows without a usable timestamp show their baked string: v1 Guest Book, pre-v3 History (synthetic negative timestamps), pre-v13 assignments and older pins. The "always from 1" and "applies to all dates" SHALLs can't hold for them, and notebook-history already forbids reverse-deriving. The Risks claim that those rows change is false: they never change. | all 4 |
| 3 | Blocker | proposal "Modified Capabilities: None" | notebook-history, lectern-guestbook, player-pins and task-note-document all require formatting "via `scribe:date-format`". The `-dmy/-mdy/-ymd` templates contradict them. lectern-guestbook's example "8 August, Year 0" also contradicts count-from-1. | all 4 |
| 4 | Blocker | settings-tab; task 4.3 | settings-tab lists each section's contents exhaustively and requires labels, helptext and localized option labels. The change never names the section, the control type, the helptext or live-update behavior. | all 4 |
| 5 | Major | design "Date order"; task 3.2; scribe-locales | Two ordering mechanisms conflict: Core "arranges" the arguments while each template also fixes the order. scribe-locales lets translators reorder placeholders, and `check-locales.py` only counts placeholders, so an `-mdy` translation could silently go day-first. The three keys are not in the required set. | spec, design, readiness |
| 6 | Major | proposal "three lang keys"; task 3.3 | It is about 9 keys per locale (3 templates, label, helptext, 4 option labels), following the `settings-newtaskinsert*` pattern (`en.json:305-308`). Task 3.3 can't pass `verify.sh` as scoped. | spec, design, readiness |
| 7 | Major | design codec v14; tasks 2.1-2.3 | No rule covers invalid dates in binary or JSON (month 0/13+, day 0, year < 1, `int.MaxValue`, malformed `"date"`, `repeatYearly` without `date`). Blobs come from clients, and the codec already bounds other hostile input. | all 4 |
| 8 | Major | spec "Export round trip" vs design TSV | TSV carries a date only in a plain task's `Special` cell. Tracker/Link/Craft already use `Special` (`ScribeDocumentTsvCodec.cs:55-63`), so their dates drop, contradicting "each task keeps its date". The change never says which kinds may carry a date. | all 4 |
| 9 | Major | spec "status relative to today"; design `Classify` | Status assumes a done flag. A dated non-completable block (Text, QuestObjective) would be overdue forever. Scenarios are missing for due today, upcoming with count, the year boundary, and a clamped day. | spec, comprehension, design |
| 10 | Major | spec "next occurrence" | "Earliest year after today" skips today's occurrence when rollover runs late on an anniversary. The spec doesn't say whether the stored or the clamped day is carried. Scenarios are missing for multi-year gaps and unchecked past-due repeats. | all 4 |
| 11 | Major | spec Req 1 vs next-occurrence | Req 1 says date changes "SHALL NOT change done state", but rollover changes the date and clears the check. | spec |
| 12 | Major | spec Req 1 vs codec | `RepeatYearly` is written only when `hasDate`. If a date is cleared and re-added, the flag survives in memory but not across a save. | spec |
| 13 | Major | design "`CloneWithNewIdentity` copies both fields"; task 1.3 | `CloneWithNewIdentity` is on `ScribeDocument` (`:77`). The copy is in private `CloneBlockWithNewTaskId` (`:119`, shared with `AppendClonedBlocksFrom`) plus the `ScribeBlock` constructor. The change doesn't decide whether the other rebuild paths keep or drop the date: `ScribeImportValidator` Degrade, the Desk capture (`GuiDialogScribeAssignmentDesk.cs:374`), and assignment placement (`ScribeModSystem.Assignment.cs`, `.Delivery.cs:229`). | all 4 |
| 14 | Major | proposal (codec-migration unchanged); task 2.4 | The codec-migration main spec pins `[5, 8]` / Version = 8. The code is v13, and this change makes it v14. `docs/CODEC-MIGRATION.md` still says JSON is v1 (code v2) in both the section and the table (lines 552, 600), and has no TSV date format. | design, readiness, comprehension |
| 15 | Major | design "Date order"; task 4.3 | The design doesn't say how the static display helpers (`FormatDateFromTimestamp(IWorldAccessor, double)`, `HistoryDisplay`, `AssignmentDisplay`, the slot tooltip) get the viewer's Date order, or how open views refresh. Task 4.3 has no verification method. | design, readiness |
| 16 | Major | design Risks bullet 1 | The bullet is false both ways. Timestamp-less rows don't change, and every new identity stamp does. | spec, comprehension, readiness |
| 17 | Nit | task 4.1 | The day-of-month check is circular (the same `TotalDays % DaysPerMonth` math). Compare against `Calendar.Month` and `Year - StartYear + 1` only. | readiness |
| 18 | Nit | task 2.1 | Hand-built older-blob tests exist only for v5/6/7/11/12. The comment at `ScribeDocumentCodecTests.cs:485` still says `Version=8`. | readiness |
| 19 | Nit | design `Classify` | The return type and the Core file are unnamed. | readiness |
| 20 | Nit | design "rollover applied by a later change" | No roadmap change owns calling the rollover, and unloaded documents (calendar blocks, notebooks in chests) need a trigger. | comprehension |
| 21 | Nit | docs/calendar-roadmap.md | The roadmap lists 3 date orders; the change ships 4. | comprehension, design |
| 22 | Nit | proposal/spec wording | "today's ... dates" reads as "dates of today". "Tooltip dates" is undefined. | comprehension, spec |
| 23 | Nit | design ScribeDate rationale | "A days-per-month change would silently move every task" overclaims: "today" also moves. Storing Y/M/D only keeps the label stable. | design |
| 24 | Nit | spec "Undated tasks are unchanged" | "Every existing behavior is unchanged" is not pass/fail. | spec |
| 25 | Nit | spec | Requirements bundle behaviors: shape plus clamping; month names plus order plus default. | spec |
| 26 | Nit | design Risks "missing keys fall back to English" | Misleading: the locale checker rejects missing required keys. | comprehension |
| 27 | Nit | design (unsure) | Vanilla shows 1386-based years in places. The design doesn't weigh that. | design |
| 28 | Nit | task 4.2 | "both give the same date" is ambiguous when three functions are listed. | comprehension |

## Triage

| # | Verdict | Reason |
|---|---------|--------|
| 1 | Ask → **Decided: keep stamps** | Recommended and chosen: identity stamps stay exactly as today (`FormatDate(sapi)`: fixed `scribe:date-format`, 1386 year, never reads Date order). Every stamp written since timestamps were added sits next to a `TotalDays` timestamp, and display already rebuilds from that, so players see count-from-1 everywhere without touching any key. That means no upgrade-day duplicate and no migration. The alternative switches stamps to count-from-1 and accepts one possible duplicate Guest Book row on upgrade day. |
| 2 | Fix | Scope both requirements to dates formatted from a timestamp or a `ScribeDate`. Add a scenario: "legacy rows without a timestamp show their stored text unchanged". |
| 3 | Fix | Add MODIFIED deltas to notebook-history, lectern-guestbook (including the example), player-pins and task-note-document: "via Scribe's date formatter, which follows the Date order setting (Language default = `scribe:date-format`)". |
| 4 | Fix + Ask → **Decided: Window Appearance** | Add a MODIFIED settings-tab delta: a dropdown with localized options and helptext, updating open views live. Which section it goes in is the Ask (recommended: Window Appearance). |
| 5 | Fix | All templates keep `{0}`=day, `{1}`=month, `{2}`=year. Only the template text sets the order, and Core only picks the key. MODIFY scribe-locales so the three keys are required with fixed placeholder order, and add `_comment` translator notes. |
| 6 | Fix | List all 9 keys in the proposal Impact and task 3.3. |
| 7 | Fix | Valid range: year 1-9999, month 1-12, day >= 1 (past month end is allowed and clamped at use). Any invalid value reads as undated in all three codecs. `repeatYearly` without a date reads false. Add tests per lane. |
| 8 | Fix | Data model: any block may carry a date (as designed). TSV carries it for plain tasks only, and other kinds' dates are dropped in TSV (documented like `LinkDescription`). JSON carries it for all kinds. Narrow the scenario to match. |
| 9 | Fix | Status and rollover apply only to completable blocks, and `Classify` returns no status otherwise. Add the missing scenarios. |
| 10 | Ask → **Decided: on or after today** | Recommended and chosen: "on or after today" (an anniversary checked late is due today), carry the stored day (not the clamped one), and an unchecked past-due repeat stays Overdue until checked. Add the three scenarios. |
| 11 | Fix | Exempt rollover in Req 1. |
| 12 | Fix | Clearing a date clears `RepeatYearly`. Spec and codec agree. |
| 13 | Fix | Name the constructor and `CloneBlockWithNewTaskId`, and test append mode. Import Degrade keeps the date. Desk capture and assignment placement drop it deliberately (roadmap: no dates on assignments). |
| 14 | Fix | MODIFIED codec-migration delta that names the window generically (current `Version`/`MinVersion` constants, not literal numbers). Task 2.4 updates the binary row, the JSON section and table (v2 to v3, noting v2 `extraInfo`) and the TSV date payload. |
| 15 | Fix | Client display helpers take a `ScribeDateOrder` argument; the server stamp path has its own overload that never sees it. A settings change triggers the existing settings write-through `RebuildBody`. 4.3 is verified by a TESTING.md item. |
| 16 | Fix | Rewrite the Risks text to match finding 1's resolution. |
| 17-19, 21-26, 28 | Fix | Task and wording corrections as listed. Name `ScribeDateStatus` (enum plus day count, record struct) in `src/Core/ScribeDateRules.cs`. |
| 20 | Fix | The roadmap names the reminders/HUD change as the rollover owner. Rollover is lazy (applied when a document is loaded or viewed, server-side), so unloaded documents roll over on next load, and "on or after today" makes that safe. |
| 27 | Contest | The user decided to count years from 1 for readability across Scribe. Vanilla's 1386 only appears in vanilla UI, and Scribe's dates are self-consistent. No change. |

## Applied

All Fix items and the three decisions were applied. New MODIFIED deltas: notebook-history, lectern-guestbook, inbox-tab, player-pins, scribe-item-hover-summary, task-note-document (generated from the main-spec text, only the date-format phrase and the Year 0 example changed), settings-tab, scribe-locales and codec-migration. `docs/calendar-roadmap.md` updated (4 orders, stamp rule, rollover owner). `openspec validate --strict` passes.

## Correction found during apply

Decompiling `GameCalendar` showed `Calendar.Year` is `(int)(TotalDays / DaysPerYear)`: it counts from 0,
not from 1386 (the `IGameCalendar` doc-comment and `StartYear` constant are misleading). Every "1386" above
should read "Year 0 in a world's first year". The decisions are unaffected: stamps still keep their form to
protect Guest Book dedup, and displayed years still count from 1. The lectern-guestbook example "Year 0" was
in fact accurate for stamps; the delta's move to "Year 1" matches the display. Recorded in `VSAPI-NOTES.md`.
