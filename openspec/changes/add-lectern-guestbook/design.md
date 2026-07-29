## Context

The Lectern block entity currently holds a `ScribeDocument` (tasks + notes) and a
`ScribePinStore` (per-player pins), both persisted via `ToTreeAttributes` / `FromTreeAttributes`
and synced via `SendBlockEntityPacket`. This change adds a third store — a `GuestbookStore` —
following the same structural pattern.

The key constraint is that Core must remain VS-API-free. The in-game calendar date is a
Mod-layer concern, passed into Core as a plain pre-formatted string. The date is built
date-only (no time) from `IWorldCalendar` properties: `DayOfMonth`, `MonthName`, `Year` —
e.g. `$"{cal.DayOfMonth} {Lang.Get("month-" + cal.MonthName)}, Year {cal.Year}"` (produces
"8 August, Year 0"). `PrettyDate()` is NOT used because it appends a time component.

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
**Decision:** `GuestbookEntry` holds `string PlayerName`, `string InGameDate`, and `string Note`.
No group field — groups were removed from scope. The Mod layer builds the date-only string from
`IWorldCalendar` properties (see Context above) and passes it in; Core never touches the VS API.

**Why:** keeps Core VS-API-free. Groups were dropped as unnecessary complexity for a social-trace
feature — player name alone is sufficient identity. The date is date-only (no time) to keep the
display readable and to ensure deduplication is per-day rather than per-session.

### D2: Deduplication key is (PlayerName, InGameDate)
**Decision:** `TryAddEntry` returns `false` without adding if an entry with the same
`(PlayerName, InGameDate)` already exists.

**Why:** simple, stateless check with no extra indices. The date string is built from
`DayOfMonth`/`MonthName`/`Year` which are deterministic per-day — all opens on the same in-game
day produce the same string, so the check is reliable.

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
**Decision:** The Guestbook nav button is injected via `GetExtraNavButtons()` in
`GuiDialogScribeLecternLibGui` (not hardcoded in `ScribeDialogBase`), since the Guestbook is
planned for Lectern + Desk but not Notebook. Nav order: Read → Edit → Pins → **Guest Book** → Settings.
The tooltip reads `"Guest Book"` (two words).

`BuildVisitorsContent()` is `protected virtual` in `ScribeDialogBase`, so the Desk can override
it if needed. `BuildCentralRegion()` routes the `Visitors` view state there.

The tab content is a **two-column** table (Visitor + Date of visit, no Group) plus an editable Note field per own entry:
- Outer structure: `Padding(EdgeInsets.All(10))` wrapping a `Column(mainAxisSize: Max, crossAxisAlignment: Stretch)` — required to fill the available height correctly (missing these caused content to overflow into the title bar).
- A fixed header `Row` with two `Expanded(Text)` children plus a third for Note, all styled Caudex Bold.
  Column labels: `"Visitor"` (left), `"Date of visit"` (centre), `"Note"` (right).
- A thin horizontal `Divider` below the header.
- A `Scrollbar { AutoHide = false }` wrapping a `SingleChildScrollView` of data rows — consistent with Read/Editor/Pinned views (always-visible track).
- Rows sorted newest-first; `Expanded` on the scroll body fills remaining height.
- **Date of visit** text is styled at `0.8 × WindowFontScale` size and `alpha = 0.8` (slightly smaller, slightly transparent) to de-emphasise it relative to the visitor name.
- Own-entry Note slot: `TextField` (80-char cap, borderless, transparent fill). Other entries: plain `Text`.

**Why:** LibGUI has no built-in table widget; `Row` + `Expanded` is the established pattern.
Groups were dropped — player name alone is sufficient. Date-only (no time) keeps the column
narrow and the dedup key per-day. The date style treatment follows the visual hierarchy of the
row editor where secondary metadata reads smaller/dimmer.

## Risks / Trade-offs

- **BE size growth over long play:** bounded by the 100-entry cap, so worst case is ~100
  `(name, date)` string pairs — negligible. [Low risk]
- **Calendar API change in a future VS version:** the date string is built from `DayOfMonth`, `MonthName`, `Year` — stable properties. A format change would only affect display, not correctness. [Low risk]
- **Player name changes:** entries are written with the display name at time of visit. A
  player who renames will appear under their old name for prior entries — acceptable for a
  guestbook. [Known, acceptable]
- **First-open race (client sends packet before server is ready):** the `RecordVisitor`
  packet handler runs on the server thread; the BE is loaded by the time the client can
  open its GUI. No race. [No mitigation needed]
