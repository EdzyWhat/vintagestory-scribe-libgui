## 1. Core: address notes by entry

- [x] 1.1 In `src/Core/GuestbookStore.cs`, change `TrySetNote(string playerName, string note)` to
      `TrySetNote(string playerName, string inGameDate, string note)`, matching on
      `FirstOrDefault(e => e.PlayerName == playerName && e.InGameDate == inGameDate)`. Keep the
      `MaxNoteLength` clamp and the unchanged-note early-out. Update the XML doc-comment.
- [x] 1.2 Add `tests/Core.Tests` coverage: (a) editing the note on a specific `(player, day)`
      entry updates only that entry when the player has multiple; (b) a `(player, day)` that
      matches no entry is a no-op returning false; (c) the length clamp and unchanged-note early-out
      still hold under the new signature.
- [x] 1.3 Add a soft per-player prune to `TryAddEntry`: introduce a `SoftMaxEntriesPerPlayer` (10)
      constant, and once adding an entry gives that player more than the soft cap, remove that
      player's oldest note-less entry (never one with a note, never the just-added entry; if all
      others carry notes, do not prune). Add `tests/Core.Tests` coverage: (a) oldest note-less
      entry pruned at the cap; (b) noted entries never pruned (player may exceed the cap);
      (c) oldest note-LESS, not oldest overall, is the one pruned; (d) only the acting player's
      entries are considered.

## 2. Wire + server handler

- [x] 2.1 Add an `InGameDate` string field to `ScribeEditGuestbookNoteMessage`
      (`src/Mod/ScribeEditGuestbookNoteMessage.cs`).
- [x] 2.2 Update the server-side `ScribeEditGuestbookNoteMessage` handler to pass
      `msg.InGameDate` through to the new `TrySetNote` overload (find the receiver via the
      network channel registration), keeping the existing sync-back on a successful change.

## 3. Client: per-entry focus + row identity

- [x] 3.1 Replace the single `_guestbookNoteFocusNode` (`ScribeDialogBase.cs:126-127`) with a
      per-entry collection keyed by the entry's natural key, mirroring `pinFocusNodes`. Add a
      sync/prune helper modeled on `SyncPinFocusNodes` (`:2211-2239`) that reuses surviving
      nodes, adds nodes for new own-entries, and disposes nodes for entries that are gone.
- [x] 3.2 In `BuildVisitorsContent` (`:2118-2174`), give each own-entry `ScribeMultilineField`
      its own `FocusNode` from the collection (not the shared one) and give each row a stable
      `key: new ValueKey<string>(...)` built from `(PlayerName, InGameDate)`.
- [x] 3.3 On the field's `onBlur`, send the entry's `InGameDate` on the
      `ScribeEditGuestbookNoteMessage` (in addition to the existing `DocIdBytes` + `Note`).
- [x] 3.4 Update `CaptureAllInputs` (`:413-417`) to return true when ANY guestbook note focus
      node has focus (not the single removed node).
- [x] 3.5 Update the disposal path (`:1547-1548`) to dispose every node in the new collection.

## 4. Spec + verification

- [x] 4.1 `dotnet build src/Mod/Mod.csproj -c Debug` — zero new warnings/errors.
- [x] 4.2 `dotnet test tests/Core.Tests` — all pass, including the new 1.2 cases.
- [x] 4.3 Manual in-game: as a player with entries on ≥2 in-game days, open the Guestbook tab.
      Confirm clicking one note field shows a caret in that field ONLY, typing goes to the
      clicked field (including the newest, not just the oldest), and each day's note saves
      independently and survives closing/reopening the lectern.
- [x] 4.4 Manual in-game (multiplayer, if convenient): confirm another player still sees your
      notes read-only and their own multi-day notes edit independently on their client.
- [x] 4.5 Update `TESTING.md` with the new in-game guestbook items (per-field caret isolation,
      per-day independent save/persist).
