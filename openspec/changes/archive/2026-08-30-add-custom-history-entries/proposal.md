## Why

The Notebook's History tab currently only records events the game generates automatically
(Crafted, PickedUp, Death, PvpKill, BossKill, TemporalStorm). Players want to write their own
entries into that same chronicle — a personal log entry alongside the automatic ones — so the
History tab becomes a mixed timeline of "what the game recorded" and "what I chose to write down."

This capability was actually spec'd once before: `openspec/specs/notebook-history/spec.md` already
has a "Player can add and edit up to 10 manual entries" requirement (from an earlier archived
change), and `HistoryEntry.Detail`'s own doc-comment already says "manual text." But it was never
actually built — `HistoryEventKind` has no `Manual` value in code, `HistoryStore` has no cap or
add/edit/delete logic for it, and the old spec's numbers (140-char cap, cap of 10 entries, no
author-ownership rule) don't match what the mod author now wants. This change finishes and
supersedes that unbuilt requirement with a concrete design: reuse the Edit tab's
`ScribeMultilineField` for the text box, and the Guestbook's "server matches sender identity"
mechanism for restricting edit/delete to the entry's own author.

## What Changes

- Add a `Manual` `HistoryEventKind` and wire up create/edit/delete for it — the enum value the spec
  already named but the code never defined.
- Add an "Add Entry" button at the bottom of the Notebook's History tab. Clicking it starts a new,
  author-attributed, auto-timestamped (day-level, matching every other entry) entry with an empty,
  focused `ScribeMultilineField` text box — the same editable-field widget the Edit tab uses.
- A custom entry's player name and timestamp are always shown, uneditable, exactly like an automatic
  entry's — only the text itself is editable, and only by the entry's own author (checked
  server-side by matching the packet sender's name, mirroring the Guestbook note pattern, never a
  client-claimed identity).
- A custom entry can be deleted by its author. It cannot be pinned and has no drag/grip handle —
  it's an ordinary History row otherwise, just an editable/deletable one.
- If an "Add Entry" draft is left empty (dialog closed, or another draft started before this one is
  given any text), it's discarded and never round-trips to the server — no entry is ever created
  with blank text.
- **BREAKING (spec-level, not shipped code):** supersedes the old, never-implemented "up to 10
  manual entries, max 140 characters" requirement with a 30-entry sliding-window cap (matching
  Death/PvpKill's existing cap) and a 1000-character limit (matching `MaxTaskTextLength`, per the
  mod author's explicit choice over the Guestbook note's 140-char limit). No player-visible
  regression, since the old requirement was never actually built.
- Scoped to the Notebook and Clockmaker's Notebook only (both via `NotebookHost`). The Tablet's
  History tab (a separate dialog class, `GuiDialogScribeTablet`) is untouched — no Add Entry button
  there, regardless of clay state.
- 2026-08-30 playtest feedback, folded into this same change (not shipped separately):
  - **Bug fix:** typing into a Manual entry's field no longer leaks WASD/hotbar keys to the
    character controller — `GuiDialogScribeNotebook` now also captures input while a Manual entry
    field is focused, matching the Editor tab/Pin Tab/Guestbook.
  - The delete button is now hover-only and floats over the text line's right edge (like the Edit
    tab's delete button), instead of an always-visible inline button that permanently reserved a
    column.
  - The kind-line label and author name merge into one string — `"{ActorName}'s Note"` — instead of
    a generic "Note" label plus a separate name column next to the text, for every viewer.
  - The "Add Entry" button now matches the Edit tab's footer-button font/chrome, and is capped at
    50% of the tab's width and horizontally centered, instead of stretching full-width in a plain
    style.
  - A faint (20%-opacity ink) divider now separates adjacent History entries, never leading or
    trailing the list.
- Incidental fix: the existing "Per-kind caps" requirement's table (10/10/10/5 for
  Death/PvpKill/BossKill/TemporalStorm) has drifted from the actual shipped caps
  (`HistoryStore.MaxDeaths`/`MaxPvpKills`/`MaxBossKills`/`MaxStorms` = 30/30/20/5). Corrected while
  touching this requirement anyway — no behavior change for Death/PvpKill/BossKill, the code was
  already right for those.
- Separately requested: raise `HistoryStore.MaxStorms` from 5 to 10 — a real (small) behavior
  change, not just a doc correction. TemporalStorm entries now keep a 10-entry sliding window
  instead of 5.

## Capabilities

### New Capabilities
(none)

### Modified Capabilities
- `notebook-history`: implements the "Manual" event kind end-to-end (create/edit/delete, ownership,
  cap, empty-draft discard) and updates the History tab display requirement to describe the new
  row's shape (always-visible author/date, editable text, delete affordance, no pin/no drag).
  Supersedes the existing but never-implemented "Player can add and edit up to 10 manual entries"
  requirement's numbers and adds the ownership/deletion rules it never specified.

## Impact

- `src/Core/HistoryEventKind.cs` — add `Manual`.
- `src/Core/HistoryEntry.cs` — add a stable per-entry `EntryId` (`Guid`), needed because a player
  can create multiple manual entries (unlike the Guestbook's one-per-day natural key) — mirrors
  `ScribeBlock.TaskId`'s existing client-generated-Guid pattern for addressing edit/delete requests.
- `src/Core/HistoryStore.cs` — `MaxManual` cap + sliding-window handling for `Manual`; `MaxStorms`
  raised from 5 to 10; codec version bump (`SHST` v1 → v2) to serialize the new `EntryId` field;
  migration stub per `docs/CODEC-MIGRATION.md`.
- `src/Mod/NotebookHost.cs` — new server-side methods to add/edit/delete a manual entry, gated by
  author-identity match, following the `Flush`/`FlushHistory` write-through pattern.
- New client↔server network messages for add/edit/delete of a manual entry (mirroring
  `ScribeEditGuestbookNoteMessage`'s sender-authorizes-by-identity pattern).
- `src/Mod/GuiDialogScribeNotebook.cs` — History tab gains the "Add Entry" footer button and renders
  `Manual` rows with an editable `ScribeMultilineField` + delete button instead of static text.
- No `Core`-to-VS-API boundary violation; no new mod dependency.
