## 1. Core — GuestbookEntry and GuestbookStore

- [x] 1.1 Add `GuestbookEntry` record to `src/Core/`: `string PlayerName`, `string InGameDate`, `string Note` (empty string default). No Groups field. Add `const int MaxNoteLength = 80` alongside `MaxEntries`.
- [x] 1.2 Add `GuestbookStore` class: holds a `List<GuestbookEntry>`, `MaxEntries = 100`.
- [x] 1.3 Implement `GuestbookStore.TryAddEntry(playerName, inGameDate)`: returns `false` if a matching `(PlayerName, InGameDate)` already exists; otherwise appends (with empty Note) and drops the oldest if over cap. Add `bool TrySetNote(playerName, note)`: finds the first entry matching `PlayerName`, clamps note to `MaxNoteLength`, sets `Note`, returns true; returns false if no entry found or note is unchanged.
- [x] 1.4 Serialization via `GuestbookStore.Serialize()` / `GuestbookStore.Deserialize()` binary codec (SGBK magic, versioned). BE wraps as `tree.SetBytes("guestbook", ...)` / `tree.GetBytes("guestbook")`.
- [x] 1.5 Write unit tests for `GuestbookStore`: dedup, cap rollover, empty state, round-trip serialization (including Note field), TrySetNote (happy path, unknown name no-op, 80-char clamp). 14 tests, 158/158 passing.
- [x] 1.6 Remove `Groups` field from `GuestbookEntry` and update `TryAddEntry` signature (drop `groups` param). Update `GuestbookStore.Serialize`/`Deserialize` accordingly. Update existing unit tests — remove any group-related assertions, update `TryAddEntry` calls to 2-arg form.

## 2. Mod — Block Entity plumbing

- [x] 2.1 Add a `GuestbookStore _guestbook` field to `BlockEntityScribeLectern`.
- [x] 2.2 Extend `ToTreeAttributes` / `FromTreeAttributes` to delegate to `_guestbook`.
- [x] 2.3 Define new packet classes `ScribeRecordVisitorMessage`, `ScribeGuestbookSyncMessage`, `ScribeEditGuestbookNoteMessage`. All three appended to registration list in `ScribeModSystem.Start()`. Handlers wired in `StartClientSide`/`StartServerSide`.
- [x] 2.4 Add `GuestbookStore Guestbook { get; }` to `IScribeDocumentHost`; implemented in `BlockEntityScribeLectern`.
- [x] 2.5 Server handler for `ScribeRecordVisitorMessage`: `RecordVisitor()` on the BE — builds date + groups, calls `TryAddEntry`, marks dirty and syncs if added.
- [x] 2.8 Update `BlockEntityScribeLectern.RecordVisitor()`: remove groups string construction; build date-only string from `sapi.World.Calendar.DayOfMonth`, `MonthName`, `Year` using `Lang.Get("month-" + cal.MonthName)` (e.g. `$"{cal.DayOfMonth} {monthName}, Year {cal.Year}"`); call `TryAddEntry(player.PlayerName, dateString)` (2-arg).
- [x] 2.6 Client handler for `ScribeGuestbookSyncMessage`: `ApplyGuestbookSync()` on the BE — deserializes and calls `RefreshGuestbookView()`.
- [x] 2.7 Server handler for `ScribeEditGuestbookNoteMessage`: `UpdateGuestbookNote()` on the BE — calls `TrySetNote`, marks dirty and syncs if changed.

## 3. GUI — Send packet on open

- [x] 3.1 In `BlockEntityScribeLectern.HandleServerReply()`, send `ScribeRecordVisitorMessage` immediately after `OpenDialog()` on a fresh open. Fires on every Lectern open regardless of which tab the player navigates to.

## 4. GUI — Guestbook tab

- [x] 4.1 `ScribeDialogBase`: `Visitors` enum variant, `OnClickSwitchToVisitors()`, `BuildCentralRegion` Visitors case, `RefreshGuestbookView()`, `IsVisitorsView` property, `NavButtonSize`/`NavButtonShadow` exposed as protected. `GuiDialogScribeLecternLibGui`: overrides `GetExtraNavButtons()` with Guestbook button (`scribeguest` icon, `OnClickSwitchToVisitors`, active when `IsVisitorsView`).
- [x] 4.2 `BuildVisitorsContent()` (protected virtual in `ScribeDialogBase`): header in Caudex Bold + Divider + SingleChildScrollView of newest-first data rows.
- [x] 4.3 Each data row: Visitor + Date of visit + Note columns. Note slot is TextField (own entry) or plain Text (others). On blur/Enter: sends `ScribeEditGuestbookNoteMessage` if text changed.
- [x] 4.4 Empty-state label when store has 0 entries. 80-char cap enforced via onKeyDown.
- [x] 4.5 `RefreshGuestbookView()` called from `ApplyGuestbookSync`; rebuilds only if Visitors tab is active.
- [x] 4.6 Fix `BuildVisitorsContent()` layout: wrap the outer `Column` in `Padding(EdgeInsets.All(10))` and add `mainAxisSize: MainAxisSize.Max, crossAxisAlignment: CrossAxisAlignment.Stretch` to the Column — without these the content overflows into the title bar.
- [x] 4.7 Fix `BuildVisitorsContent()` scrollbar: replace bare `SingleChildScrollView` with `Scrollbar(controller: sharedScrollController, child: SingleChildScrollView(...)) { AutoHide = false }` — matching Read/Editor/Pinned pattern.
- [x] 4.8 Remove the Group column from `BuildVisitorsContent()`: drop the Group `Expanded(Text)` from the header Row and from each data row. Update `"scribe-guestbook-col-group"` lang key removal from `en.json`.
- [x] 4.9 Style the Date of visit column text at `0.8 × WindowFontScale` size and `alpha = 0.8` (slightly smaller and slightly transparent). Apply to both the header and the data rows' date cell.
- [x] 4.10 Update nav button tooltip lang key value: `"scribe-tab-guestbook"` → `"Guest Book"` (two words, capitalised).

## 5. Lang keys

- [x] 5.1 `"scribe-tab-guestbook"` → `"Guestbook"` (value needs updating to `"Guest Book"` — see task 4.10).
- [x] 5.2 `"scribe-guestbook-empty"` → `"No visitors yet"`.
- [x] 5.3 `"scribe-guestbook-col-visitor"`, `"scribe-guestbook-col-group"`, `"scribe-guestbook-col-date"`, `"scribe-guestbook-col-note"` added. `scribe-guestbook-col-group` to be removed (task 4.8).

## 6. In-game verification (Atlas / manual)

- [ ] 6.1 Open a lectern for the first time — confirm a guestbook entry appears with your name and today's in-game date.
- [ ] 6.2 Close and reopen the same lectern on the same in-game day — confirm no duplicate entry.
- [ ] 6.3 Advance to the next in-game day (sleep or use `/time`), reopen — confirm a new entry for the new date.
- [ ] 6.4 Have a second player open the same lectern — confirm both names appear.
- [ ] 6.5 Save and reload the world — confirm entries are still present.
- [ ] 6.6 Type a note on your own entry — confirm it saves and persists after close/reopen.
- [ ] 6.7 Confirm another player's entry shows their note as plain text (no input field).
- [ ] 6.8 Confirm typing more than 80 characters in the note field is blocked.
