## Context

The Lectern block entity currently holds a `ScribeDocument` (tasks + notes) and a
`ScribePinStore` (per-player pins), both persisted via `ToTreeAttributes` / `FromTreeAttributes`
and synced via `SendBlockEntityPacket`. This change adds a third store — a `GuestbookStore` —
following the same structural pattern.

The key constraint is that Core must remain VS-API-free. The in-game calendar date is available
as `api.World.Calendar.PrettyDate()` (returns a formatted string like "10th of Harvestmonth,
year 3") — this is a Mod-layer concern, passed into Core as a plain string.

## Goals / Non-Goals

**Goals:**
- Passive visitor recording: opening the GUI is enough, no action required.
- Per-player, per-day deduplication (in-game date, not real-world date).
- Rolling cap to bound BE size over long play.
- Read-only tab in the Lectern GUI, newest-first.
- Core types (`GuestbookEntry`, `GuestbookStore`) reusable by the Desk without modification.

**Non-Goals:**
- User-configurable cap (hardcoded 100 for v1).
- Real-world timestamps.
- Ability to delete or hide individual entries.
- Showing online/offline status of visitors.
- Any global or cross-lectern visitor tracking.

## Decisions

### D1: Store in Core, all fields as strings passed from Mod layer
**Decision:** `GuestbookEntry` holds `string PlayerName`, `string Groups`, and `string InGameDate`.
The Mod layer calls `api.World.Calendar.PrettyDate()` for the date and builds the groups string
from `IPlayer.Groups` via `string.Join(", ", player.Groups.Select(g => g.GroupName))` (or `"-"` if
the array is empty).

**Why:** keeps Core VS-API-free; both the date and group names are human-readable strings stable
enough for a guestbook display (not used for arithmetic or lookup). Capturing group names at
record time is intentional — it's a snapshot of membership at the moment of the visit, which is
what a guestbook should reflect. Alternative of storing group UIDs and resolving at display time
was rejected: it couples Core to the server's group registry and breaks if groups are renamed or
deleted.

### D2: Deduplication key is (PlayerName, InGameDate)
**Decision:** `TryAddEntry` returns `false` without adding if an entry with the same
`(PlayerName, InGameDate)` already exists.

**Why:** simple, stateless check with no extra indices. Assumes `PrettyDate()` returns the same
string for all ticks on the same in-game day — confirmed by reading VS calendar source
(`PrettyDate` is deterministic per-day).

Alternative considered: store a `HashSet<(name, date)>` separately. Rejected — redundant; the
entries list itself is the source of truth and scanning 100 entries is trivial.

### D3: Packet flow mirrors the document pattern
**Decision:** client sends a new `RecordVisitor` packet on GUI open. Server calls
`TryAddEntry`; if it returns `true`, marks dirty and sends a `GuestbookSync` packet back to
the opening client only (not a broadcast — other readers don't need it immediately).

**Why:** consistent with how document edits flow (client → server → server syncs back to
requester). No new infrastructure. Broadcast is unnecessary since the guestbook is not
collaboratively edited.

### D4: Separate tree attribute key, not embedded in the document
**Decision:** persisted as `"guestbook"` in a `TreeAttribute` on the BE, independent of the
`"document"` key used by `ScribeDocument`.

**Why:** keeps the two stores decoupled; avoids any risk of codec interference. The document
model has a tight schema; the guestbook is a flat list and doesn't belong in it.

### D5: Guestbook tab is the 4th nav slot (before Settings gear), two-column table layout
**Decision:** insert the Guestbook tab as the 4th child of `BuildRightColNav()`'s `Column`,
after the `scribepin` button and before the `scribegear` Settings button. A new
`ScribeLecternView.Visitors` enum variant is added; `BuildCentralRegion()` gets a `Visitors`
case that calls `BuildVisitorsContent()`. The dialog opens in `Read` view by default — the
Guestbook tab is not the initial active view.

The tab content is a three-column table built from LibGUI primitives:
- A fixed header `Row` with three `Expanded(Text)` children styled
  `{ FontFamily = "Caudex", Weight = FontWeight.Bold }` (matching `ScribeRowControlNudge.TitleFontFamily`).
  Column labels: `"Visitor"` (left), `"Group"` (centre), `"Date of visit"` (right).
- A thin horizontal divider below the header.
- A `SingleChildScrollView` wrapping a `Column` of data rows, each a `Row` with three
  `Expanded(Text)` children (`playerName`, `groups`, `inGameDate`). Non-virtualized — 100-entry cap
  keeps the row count trivially small.
- Rows sorted newest-first (most-recent `inGameDate` string at the top).

**Why:** LibGUI has no built-in table widget; `Row` + `Expanded` is the established ad-hoc
two-column pattern used by `PairedControls()` in `ScribeSettingsContent`. Placing the tab
before Settings is consistent with the lectern's functional hierarchy (content tabs first,
utility gear last). Not making it the default open view avoids surprising players who open
the lectern to read or edit.

## Risks / Trade-offs

- **BE size growth over long play:** bounded by the 100-entry cap, so worst case is ~100
  `(name, date)` string pairs — negligible. [Low risk]
- **`PrettyDate()` format change in a future VS version:** the dedup key and display text are
  the same string, so a format change would only affect how dates look in the guestbook, not
  correctness. [Low risk]
- **Player name changes:** entries are written with the display name at time of visit. A
  player who renames will appear under their old name for prior entries — acceptable for a
  guestbook. [Known, acceptable]
- **First-open race (client sends packet before server is ready):** the `RecordVisitor`
  packet handler runs on the server thread; the BE is loaded by the time the client can
  open its GUI. No race. [No mitigation needed]
