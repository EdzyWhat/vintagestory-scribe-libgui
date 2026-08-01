## 1. Core: address notes by entry

- [ ] 1.1 In `src/Core/GuestbookStore.cs`, change `TrySetNote(string playerName, string note)` to
      `TrySetNote(string playerName, string inGameDate, string note)`, matching on
      `FirstOrDefault(e => e.PlayerName == playerName && e.InGameDate == inGameDate)`. Keep the
      `MaxNoteLength` clamp and the unchanged-note early-out. Update the XML doc-comment.
- [ ] 1.2 Add `tests/Core.Tests` coverage: (a) editing the note on a specific `(player, day)`
      entry updates only that entry when the player has multiple; (b) a `(player, day)` that
      matches no entry is a no-op returning false; (c) the length clamp and unchanged-note early-out
      still hold under the new signature.

## 2. Wire + server handler

- [ ] 2.1 Add an `InGameDate` string field to `ScribeEditGuestbookNoteMessage`
      (`src/Mod/ScribeEditGuestbookNoteMessage.cs`).
- [ ] 2.2 Update the server-side `ScribeEditGuestbookNoteMessage` handler to pass
      `msg.InGameDate` through to the new `TrySetNote` overload (find the receiver via the
      network channel registration), keeping the existing sync-back on a successful change.

## 3. Client: per-entry focus + row identity

- [ ] 3.1 Replace the single `_guestbookNoteFocusNode` (`ScribeDialogBase.cs:126-127`) with a
      per-entry collection keyed by the entry's natural key, mirroring `pinFocusNodes`. Add a
      sync/prune helper modeled on `SyncPinFocusNodes` (`:2211-2239`) that reuses surviving
      nodes, adds nodes for new own-entries, and disposes nodes for entries that are gone.
- [ ] 3.2 In `BuildVisitorsContent` (`:2118-2174`), give each own-entry `ScribeMultilineField`
      its own `FocusNode` from the collection (not the shared one) and give each row a stable
      `key: new ValueKey<string>(...)` built from `(PlayerName, InGameDate)`.
- [ ] 3.3 On the field's `onBlur`, send the entry's `InGameDate` on the
      `ScribeEditGuestbookNoteMessage` (in addition to the existing `DocIdBytes` + `Note`).
- [ ] 3.4 Update `CaptureAllInputs` (`:413-417`) to return true when ANY guestbook note focus
      node has focus (not the single removed node).
- [ ] 3.5 Update the disposal path (`:1547-1548`) to dispose every node in the new collection.

## 4. Spec + verification

- [ ] 4.1 `dotnet build src/Mod/Mod.csproj -c Debug` — zero new warnings/errors.
- [ ] 4.2 `dotnet test tests/Core.Tests` — all pass, including the new 1.2 cases.
- [ ] 4.3 Manual in-game: as a player with entries on ≥2 in-game days, open the Guestbook tab.
      Confirm clicking one note field shows a caret in that field ONLY, typing goes to the
      clicked field (including the newest, not just the oldest), and each day's note saves
      independently and survives closing/reopening the lectern.
- [ ] 4.4 Manual in-game (multiplayer, if convenient): confirm another player still sees your
      notes read-only and their own multi-day notes edit independently on their client.
- [ ] 4.5 Update `TESTING.md` with the new in-game guestbook items (per-field caret isolation,
      per-day independent save/persist).
