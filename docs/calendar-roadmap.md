# Calendar and dated tasks roadmap

Planning notes for the calendar block, dated tasks and date reminders. The driving request, from a
Scribe user: "a calendar you can place on the wall (like the chalkboard) and set tasks/reminders for a
specific date." Each numbered item below becomes its own OpenSpec change. Reference mods reviewed:
Calendar Handbook (TheJimmyBlaze) and Wall Calendar (nikok).

## Decisions

### Model
- **A date is an optional field on a task, not a new task kind.** Any document block can carry one
  in the data model. The UI only offers dates on plain tasks for now. Removing a date turns the task
  back into a normal task, and its id never changes, so pins stay attached.
- **Dates are stored as year, month and day**, never as a day count since world creation.
  If the stored day is past the end of its month, it is shown clamped to the last day. This only
  matters if a world's days per month changes, which is not expected.
- **Displayed years count from 1** everywhere in Scribe, derived from `Calendar.TotalDays`, never the
  game's `Calendar.Year`, which counts from 0. The date text the server stores as an identity (Guest Book dedup,
  History, assignments) keeps its existing form and is never shown when a timestamp exists. Dates stored as
  text before timestamps existed keep their stored text. The year view runs from Year 1 to 10 years ahead
  of today.
- **Calendar shape comes from one place.** Vanilla Vintage Story always has 12 months, each with the
  world's days per month. The month length is asked for, never assumed, so a mod with uneven months
  (Solar Years) can be supported later.
- **Not supported:** multi-day tasks, durations, Tablets (cuneiform font and small pin limit), and
  due-date behavior for assignments.

### Shared block, personal pins
- **A calendar block is shared** and shows only its own document's tasks, like the Chalkboard.
  Anyone can edit, using the existing editor lock. It is wall-mounted.
- **The personal view is your pins.** Pinned dated tasks show on the HUD and the Pinned tab, and only
  the pinning player is reminded.
- **Calendar documents never delete a task through the Completion Policy.** On a calendar block,
  "Delete" behaves like Sink, and a task is only removed when a player deletes it by hand.
- **Clicking a calendar day creates a task dated for that day.** Creating an undated task first is
  still possible, and undated tasks appear in an "Undated" section of the list.
- **HUD order stays manual.** Date badges carry the urgency.

### Display
- **Dates use month names, never numbers**, so day-first and month-first cannot be misread.
- **Date order follows the language** (`scribe:date-format`). Vintage Story has a single `en`
  language, with no US or UK variant, and a mostly non-US audience, so the default stays day-first.
  A Setting can override the order: language default, day-month-year, month-day-year, or year-month-day.
- **Full month names first.** They are tried at a slightly smaller font on the HUD and Pinned rows.
  Short month names (12 new lang keys, translated per locale) are only added if that looks bad.
- **Completed dated tasks stay on the calendar,** struck through and dimmed.
- **The due state is steady text, not blinking:** `Due today: Plant flax`, with the prefix in an
  accent font color. Overdue uses a second, warmer color: `Overdue (2 days): Plant flax`.

### Reminders
- **Passive by default.** A pinned dated task always shows a relative badge ("in 2 days").
  At the reminder moment it enters the due state, and after its date passes while not done, the
  overdue state. Checking it off is the dismissal.
- **Chat reminders are opt-in** through Scribe Settings.
- **Reminder time is a Setting:** dawn (default), noon, dusk, or midnight. Dawn and dusk are read
  from the daylight at the player's position, falling back to a fixed hour (6 or 18) where they do not
  occur. Noon is half the world's hours per day. Midnight is when the date changes.
- **Remind days ahead is a Setting** (0 to 7, default 0).
- **A reminder fires once per date**, and on login for anything that came due while away, as a
  single "while you were away" line.
- **Reminders are computed on the client** from the pin snapshot, which gains the task's date.

### Repeats
- **"Repeat yearly" is a checkbox in the row menu, shown only on a row that has a date** (and can be
  completed: never on notes or quest objectives). An undated row's menu offers Add date instead.
  Clearing the date removes the entry and the flag. Checking off a repeating task completes it like any task, under the player's
  Completion Policy: the repeat flag does not change completion, and under Delete the checked task is
  deleted. Deleting or completing one instance leaves every other instance of the repeat alone; the
  rollover creates the next instance. Once its date has passed, its date moves to the next occurrence on or after today and the check
  clears. Change 4b applies this lazily on the server whenever a document is loaded or viewed, so
  documents left unloaded roll over correctly later. It stays one task, and
  the calendar shows only its next occurrence.
  **To reconcile in change 4b:** completing a repeating task under Delete deletes that task, so a rollover
  that moves one task's date forward only applies when the task survives (Keep, Sink, Unpin, UnpinSink). The
  4b design must decide whether Delete creates the next instance before deleting or ends the repeat.

## Changes

| # | Change | Depends on |
|---|---|---|
| 1 | `add-task-row-context-menu`: right-click menu on task rows (Edit, Pin/Unpin, Delete) | none |
| 2 | `add-dated-task-core`: date field, save-format bump, calendar shape, date formatting, status rules | none |
| 3 | Notebook dating: Add/Change/Clear date and Repeat yearly in the row menu, date chip, date picker | 1, 2 |
| 4a | `add-pinned-task-dates`: the pin snapshot carries the date, Pin Tab and HUD rows show the date chip, HUD chips follow the day | 2, 3 |
| 4b | Reminders: due and overdue reminders, chat alerts, reminder Settings, repeat rollover | 4a |
| 5 | Calendar block and dialog: wall block, split view (filterable list plus month view), Today button | 2, 3 |
| 6 | Year view: 12-month grid, year navigation (world start to 10 years ahead), Today button | 5 |
| 7 | Calendar block face: month name, day grid, today marker, marks on days with tasks | 5 |
| 8 | Drag to reschedule in the month view | 5 |

### Calendar dialog details (changes 5 and 6)
- **Left side:** the block's tasks ordered by date, with filters: all, today, upcoming, past,
  overdue, completed, incomplete, undated.
- **Right side:** the month view or the year view.
- **Month view:** day rectangles with the day number top-left, wrapped to fit the world's days per
  month (there are no weekdays). Today has an accent border. Click a day to create a task, click a
  task to edit it, right-click for the row menu, previous and next month, a way into the year view, and
  a Today button when not on today's month.
- **Year view:** 12 small months with day numbers. Days with tasks are shaded, stronger with more
  tasks. Clicking a month opens it. Previous and next year, and a Today button.
- **Block face:** drawn in the world following the vanilla Sign's text renderer. This is new
  rendering work for Scribe.

## Later
- **Assign in the row menu.** The `assignment-desk-block` spec makes the Create Assignments tab the only
  place assignments are created, so "Assign..." must route into that tab rather than create one directly.
- **Date picker improvements.** The first picker has month, day and year steppers; a month dropdown is a later option. Later
  options are quick choices (Today, Tomorrow, +3 days, Next month, First day of next season) and a mini
  month grid popover reusing the calendar's month view. Typed dates are out because parsing them in
  every language costs too much.
- **Exact reminder times.** These could be a Clockmaker's Notebook feature, matching how timers are
  gated. Time of day is simple enough technology (sundial-level) that it should not require a temporal
  gear.
- **Repeat cadences.** Replace the single "Repeat yearly" checkbox with a **Repeat** entry that opens a
  submenu (like a macOS Finder submenu: hover the entry and a second menu opens beside it) listing Does not
  repeat, Daily, Monthly, Yearly and Custom. Custom opens its own window modeled on Google Calendar's "Custom
  recurrence": "Repeat every N [day/month/year]" with a number stepper and a unit dropdown, and an "Ends"
  group (Never, On a date, After N occurrences); no days-of-week row, since the world has no weekdays. This
  needs a save-format change (a cadence and an end rule per block instead of one flag), rollover and
  reminder rules for each cadence, and a submenu widget in the row menu host. Per-year completion history
  stays out of scope.
- **Pin store downgrade safety.** Refuse to overwrite a pin store with a newer version (a mod downgrade on a
  world whose store is newer loads it empty and the next save replaces every player's pins).
- **Per-task lead time and reminder time.**
- **Month colors and seasonal art.**
- **Event colors** (a small fixed palette that reads on Scribe's backdrops).
- **Short month names**, only if full names do not fit.
- **Dates on assigned tasks.** The assigner sets a due date that both the sender's and the recipient's copy
  show as a separate chip beside the assignment info chip. The assignee cannot edit an assigned task at all,
  including its date. Assignment placement and the Assignment Desk capture stop dropping the date;
  `IsDatable` stops excluding assignments, but only the assigner can change the date.
- **Sorting the HUD by date** as an option.
- **Uneven month lengths** (Solar Years compatibility).

## Not planned
Moon phase, season band, multi-day tasks and durations.
