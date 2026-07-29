## Why

Players on shared worlds want a lightweight social trace — a sense that others have passed through
a space. A guestbook tab on the Lectern (and later the Desk) captures that without requiring active
participation: opening the block on a given day is enough to leave your name.

## What Changes

- A new **Guestbook tab** is added to the Lectern nav column, alongside Read / Edit / Pins.
- When any player opens the Lectern GUI, the server records their display name + the current in-game
  date (not real-world date) as a visitor entry — one entry per player per in-game day.
- The tab displays entries as a two-column table (Visitor | Date of visit) in reverse-chronological order.
- Entries are capped at a configurable maximum (default 100) on a rolling basis (oldest dropped
  when the cap is exceeded).
- The guestbook is read-only from the GUI — no editing or deleting individual entries.
- Server-authoritative: the entry is written by the server on GUI open, not self-reported by the client.
- The feature is designed to carry forward to the Desk (v0.3) with no interface changes — the
  guestbook capability is block-agnostic.

## Capabilities

### New Capabilities

- `lectern-guestbook`: Persistent visitor log on the Lectern block — server records player name +
  in-game date-only (no time) on GUI open; exposed as a read-only two-column tab in the Lectern GUI. Capped rolling history. Own-entry note field editable.

### Modified Capabilities

- `lectern-block`: The lectern block entity gains a new persisted field (`guestbook` entries) and
  a new network packet path (server writes entry on client GUI open; syncs updated log to client).
- `task-note-document`: No requirement change — the document model is unaffected. Guestbook is a
  parallel store on the BE, not part of the document codec.

## Impact

- `BlockEntityScribeLectern.cs` — new `GuestbookEntries` field, `To/FromTreeAttributes` extension,
  new packet type for "record visitor" and "sync guestbook".
- `GuiDialogScribeLectern.cs` — new Guestbook tab view (read-only two-column table, 4th nav slot after Pins); send record-visitor packet on open (fires on any tab access, not just the Guestbook tab).
- `src/Core/` — new `GuestbookEntry` value type (player name + in-game date string + note) and
  `GuestbookStore` (add, cap, serialize). No VS API references — the date string is passed from the Mod layer as a date-only formatted string (no time component).
- No new mod dependencies. No breaking changes to existing packets or document format.
