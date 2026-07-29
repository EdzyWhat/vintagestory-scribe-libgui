## 1. Core — GuestbookEntry and GuestbookStore

- [ ] 1.1 Add `GuestbookEntry` record to `src/Core/`: `string PlayerName`, `string Groups`, `string InGameDate`.
- [ ] 1.2 Add `GuestbookStore` class: holds a `List<GuestbookEntry>`, `MaxEntries = 100`.
- [ ] 1.3 Implement `GuestbookStore.TryAddEntry(playerName, groups, inGameDate)`: returns `false` if a matching `(PlayerName, InGameDate)` already exists; otherwise appends and drops the oldest if over cap.
- [ ] 1.4 Implement `GuestbookStore.ToTreeAttributes(ITreeAttribute)` and `FromTreeAttributes(ITreeAttribute)` — serialize as a list of attribute trees under key `"guestbook"`.
- [ ] 1.5 Write unit tests for `GuestbookStore`: dedup, cap rollover, empty state, round-trip serialization (no VS API — pass a plain `TreeAttribute` or mock).

## 2. Mod — Block Entity plumbing

- [ ] 2.1 Add a `GuestbookStore _guestbook` field to `BlockEntityScribeLectern`.
- [ ] 2.2 Extend `ToTreeAttributes` / `FromTreeAttributes` to delegate to `_guestbook`.
- [ ] 2.3 Define a new `ScribePacketType.RecordVisitor` variant (client → server): carries no payload beyond the sender's player UID (available server-side from the packet handler).
- [ ] 2.4 Define a new `ScribePacketType.GuestbookSync` variant (server → client): carries the full serialized `GuestbookStore`.
- [ ] 2.5 Add server-side handler for `RecordVisitor`: call `Calendar.PrettyDate()` for the date; build the groups string from `string.Join(", ", player.Groups.Select(g => g.GroupName))` (or `"-"` if empty); call `TryAddEntry`; if `true`, call `MarkDirty` and send `GuestbookSync` back to the opening client.
- [ ] 2.6 Add client-side handler for `GuestbookSync`: deserialize the store and update the GUI's guestbook tab if it is open.

## 3. GUI — Send packet on open

- [ ] 3.1 In `BlockEntityScribeLectern.OpenDialog()` (called from `HandleServerReply` — the single GUI open moment), send a `RecordVisitor` packet to the server. This ensures the record fires on every Lectern open regardless of which tab the player navigates to.

## 4. GUI — Guestbook tab

- [ ] 4.1 Add `ScribeLecternView.Visitors` enum variant. In `BuildRightColNav()`, insert the Guestbook `TitleButton` as the 4th child — after `scribepin`, before `scribegear`. Add `OnClickSwitchToVisitors()` following the editor-teardown pattern of `OnClickSwitchToPinned()`. The dialog continues to open in `Read` view by default.
- [ ] 4.2 Add `Visitors` case to `BuildCentralRegion()` switch, calling `BuildVisitorsContent()`. Implement `BuildVisitorsContent()`: fixed header `Row` with three `Expanded(Text)` children (`"Visitor"`, `"Group"`, `"Date of visit"`) styled `{ FontFamily = "Caudex", Weight = FontWeight.Bold }` + thin divider + `SingleChildScrollView` wrapping a `Column` of data rows sorted newest-first.
- [ ] 4.3 Each data row is a `Row` with `Expanded(Text(entry.PlayerName))` + `Expanded(Text(entry.Groups))` + `Expanded(Text(entry.InGameDate))`.
- [ ] 4.4 Add an empty-state label ("No visitors yet") shown when the store has zero entries.
- [ ] 4.5 On `GuestbookSync` received: if the Guestbook tab is active, rebuild the list in place (call `ForceRebuild` on the dialog or refresh the list widget).

## 5. Lang keys

- [ ] 5.1 Add lang key for the Guestbook tab nav tooltip (e.g. `gui:scribe-tab-guestbook` → "Guestbook").
- [ ] 5.2 Add lang key for the empty-state label (e.g. `gui:scribe-guestbook-empty` → "No visitors yet").

## 6. In-game verification (Atlas / manual)

- [ ] 6.1 Open a lectern for the first time — confirm a guestbook entry appears with your name and today's in-game date.
- [ ] 6.2 Close and reopen the same lectern on the same in-game day — confirm no duplicate entry.
- [ ] 6.3 Advance to the next in-game day (sleep or use `/time`), reopen — confirm a new entry for the new date.
- [ ] 6.4 Have a second player open the same lectern — confirm both names appear.
- [ ] 6.5 Save and reload the world — confirm entries are still present.
- [ ] 6.6 Confirm the Guestbook tab is read-only (no edit/delete controls).
