## Context

The History tab (`GuiDialogScribeNotebook.BuildHistoryContent`, `src/Mod/GuiDialogScribeNotebook.cs`)
renders `NotebookHost.History.Entries` (a `Scribe.Core.HistoryStore`) as static, read-only rows:
kind label + date on one line, `ActorName — Detail` (or just `Detail` if `ActorName` is empty) on
the next. `HistoryEntry` (`src/Core/HistoryEntry.cs`) already carries a `Detail` field whose own
doc-comment says "manual text," and `openspec/specs/notebook-history/spec.md` already has a "Player
can add and edit up to 10 manual entries" requirement — but neither `HistoryEventKind` nor
`HistoryStore` actually define a `Manual` kind or any add/edit/delete/cap logic for it. This design
finishes that unbuilt requirement, with numbers and rules the mod author has now settled on
(30-entry sliding-window cap, 1000-character limit matching `MaxTaskTextLength`, strict per-entry
author ownership, day-level timestamp only, freely re-editable, empty drafts never persist).

Two existing patterns are the direct precedent for this design:
- **Ownership/authorization**: the Guestbook note edit path
  (`ScribeEditGuestbookNoteMessage`/`BlockEntityScribeWritingStation.UpdateGuestbookNote`) never
  trusts a client-claimed identity — the server always resolves "whose entry is this" from the
  packet SENDER's own name, and the client can only ever address its own entry. A custom History
  entry needs the identical rule, but its addressing key can't reuse the Guestbook's
  `(PlayerName, InGameDate)` trick, because a player can create many custom entries (no
  one-per-day cap), so two of a player's own entries could collide on that key.
- **Stable per-item identity**: `ScribeBlock.TaskId` (`src/Core/ScribeBlock.cs`) is a client-generated
  `Guid`, assigned once at creation, that every later edit/delete/reorder request addresses by. The
  same shape — a `Guid` minted client-side when "Add Entry" is clicked, carried on every subsequent
  request for that entry — solves the addressing problem above.

## Goals / Non-Goals

**Goals:**
- Implement the `Manual` `HistoryEventKind` end-to-end: create, edit (any number of times), delete —
  all restricted server-side to the entry's own author.
- Reuse existing widgets/patterns wherever they fit: `ScribeMultilineField` for the text box, the
  Guestbook's sender-identity-authorizes pattern for ownership, `ScribeBlock.TaskId`'s Guid-addressing
  pattern for the entry, `MaxTaskTextLength` for the character cap.
- Never let an entry with no text ever reach the server or sync to any other future reader of the
  notebook.

**Non-Goals:**
- The Tablet (`GuiDialogScribeTablet`, a separate dialog class from `GuiDialogScribeNotebook`) is
  untouched. Its History tab stays automatic-entries-only.
- Time-of-day granularity. Custom entries use the exact same day-level `InGameDate` string every
  other entry already uses (`NotebookHost.FormatDate`).
- Retroactively deleting an entry if the author later edits its text back down to empty — auto-
  discard only applies to a draft that was NEVER given text in the first place (see Decision 4).
- Any change to `src/Core/`'s VS-API-free boundary, or a new mod dependency.

## Decisions

### 1. Add `HistoryEventKind.Manual = 7`

Appended after the existing reserved `LoreDiscovery = 6` (never renumber existing values — the
enum is serialized as a raw byte). `Manual` is what distinguishes a custom entry from an automatic
one everywhere in the code — display (editable vs. read-only row), caps (sliding-window cap vs. the
automatic kinds' own caps), and ownership (only `Manual` entries are ever author-restricted). This
replaces the stale, never-implemented `isManual bool` mentioned in `HistoryStore`'s own serialized-
format doc-comment — one enum value is simpler than a kind byte plus a redundant flag.

### 2. Give `HistoryEntry` a stable `EntryId` (`Guid`)

```csharp
public Guid EntryId { get; set; } = Guid.Empty;
```

Minted client-side (`Guid.NewGuid()`) the moment "Add Entry" is clicked, exactly like
`ScribeBlock.TaskId`. Every automatic entry keeps `EntryId = Guid.Empty` — it's meaningless for them,
since they're never individually addressed for edit/delete. Serialized unconditionally as a fixed
16-byte field per entry (simplest, consistent with the existing fixed-field-per-entry layout;
negligible overhead against `MaxHistoryBytes`'s 64 KB guard for the low tens of entries this store
ever holds).

**Why not reuse `(ActorName, InGameDate)` like the Guestbook?** The Guestbook caps a player to one
entry per in-game day (`TryAddEntry`'s own dedup check), so that pair is a real natural key there. A
custom History entry has no such cap — a player can add several in one day — so the pair isn't
unique. A dedicated ID sidesteps the whole question.

**Why client-generated, not server-generated?** Matches `ScribeBlock.TaskId` and every other
client-addressed mutation in this mod (`SetTaskTextFromReader(Guid taskId, ...)`,
`DeleteTaskFromReader(Guid taskId)`) — the client already needs a stable local key to track the
in-progress draft row before any server round-trip completes, so generating it up front (rather than
waiting for the server to assign and echo one back) avoids a UI state where the draft box exists but
has no id to attach later edits to.

### 3. Codec bump: `HistoryStore` SHST v1 → v2

Per `docs/CODEC-MIGRATION.md`'s append-only rule: `EntryId` (16 bytes, `Guid.ToByteArray()`) is
appended to the END of each entry's existing field sequence (`kind, actorName, detail, inGameDate`).
`Version` becomes 2; `PriorVersion` becomes 1. Reading a v1 payload synthesizes `EntryId =
Guid.Empty` for every entry (there are no `Manual` entries in any v1 payload, since the kind didn't
exist yet, so this is a placeholder fill, not a real migration transform) — a one-line
`ApplyV1ToV2Migrations`, replacing the current no-op stub.

### 4. Ownership and empty-draft handling: the draft never round-trips until it has text

"Add Entry" does NOT immediately send a create-entry packet. It only:
1. Mints a local `EntryId`.
2. Inserts a local-only draft row into the client's rendered list (author = the local player's own
   name, date = today, shown immediately — same visual position a real entry would occupy) with an
   autofocused, empty `ScribeMultilineField`.

The FIRST time that field commits non-empty text (Enter, or losing focus, or the dialog closing —
same three commit triggers the Edit tab's rows already use), the client sends ONE new message,
`ScribeAddHistoryEntryMessage(DocIdBytes, EntryId, Text)`. The server adds a real `HistoryEntry`
(`Kind = Manual, ActorName = sender.PlayerName, EntryId, Detail = Text, InGameDate =
NotebookHost.FormatDate(sapi)`), enforces the cap, flushes, and syncs back. If the field never
receives any text before the dialog closes (or before another "Add Entry" click replaces the pending
draft), the client simply drops the local draft row — nothing was ever sent, so there is nothing to
discard server-side. This is what makes "auto-discard if left empty" exact rather than "create then
immediately delete."

Every SUBSEQUENT edit to an already-created entry uses a second message,
`ScribeSetHistoryEntryTextMessage(DocIdBytes, EntryId, Text)` — the server looks up the entry by
`EntryId`, checks `entry.ActorName == sender.PlayerName` (mirroring
`BlockEntityScribeWritingStation.UpdateGuestbookNote`'s identity check exactly), and no-ops silently
if it doesn't match (never an error — the Add Entry / Delete affordance for someone else's entry
simply isn't shown client-side, so a legitimate client never sends this for an entry it doesn't
own; a mismatch only happens for a tampered client, which gets silent rejection like every other
server-authoritative write in this mod).

Deletion (`ScribeDeleteHistoryEntryMessage(DocIdBytes, EntryId)`) uses the identical ownership check.

Editing text back down to empty on an entry that DOES already exist server-side does NOT delete it
— it just persists as an entry with blank `Detail`. Only a draft that was never given text in the
first place is discarded (Non-Goal above). This keeps the rule simple: discard applies to
"never-created," not "edited-to-empty."

### 5. Cap: `MaxManual = 30`, sliding window

Matches `MaxDeaths`/`MaxPvpKills`'s existing sliding-window pattern in `HistoryStore.TryAddEntry` —
oldest `Manual` entry is dropped once a 31st is added. This only applies to the ADD path; editing or
deleting an existing entry never engages the cap logic.

### 6. Max length: `ScribeDocumentCodec.MaxTaskTextLength` (1000 chars), not the Guestbook's 140

Per the mod author's explicit choice. Passed to `ScribeMultilineField`'s existing `maxLength`
parameter (already used this way for checkbox Task rows) — the field clamps typing/paste at the UI
layer, and the server-side handler clamps again as the authoritative backstop (matching every other
length-capped write in this mod).

### 7. Display: reuse the existing row layout, add an editable/deletable variant

The current row layout (kind/date line, then a second line) is kept for `Manual` rows too, with
changes settled across two rounds of author feedback (first pass 2026-08-29, refined 2026-08-30):

- The kind-line label merges the author's name into it directly — `"{ActorName}'s Note"` — instead
  of a generic kind word plus a separate name text on the line below. This applies for every
  viewer, author or not (2026-08-30 AskUserQuestion: "merge for everyone").
- The second line replaces the static `Detail` `Text` widget with a `ScribeMultilineField` when
  `Kind == Manual`, wrapped so only the entry's own author sees it as editable (any other viewer
  sees the same text as a plain, non-interactive `Text`).
- A delete button (the Edit tab's own `ScribeRowButton`, matching its glyph AND its hover mechanism)
  floats over the text/input line's right edge — via the same `Stack` + `Positioned` +
  `MouseRegion(onEnter/onExit)` pattern `ScribeEditorContent`'s row State already uses — appearing
  only while the pointer hovers that line, for `Manual` entries whose `ActorName` matches the local
  player's own name; never rendered otherwise. This requires the Manual row to become its own small
  `StatefulWidget`/`State` pair (to own the hover bool via `SetState`), unlike every other History
  row which stays a plain `Widget`-returning method. (2026-08-30 AskUserQuestion: hover-reveal was
  narrowed from "anywhere on the entry" to "only the text/input line," since the kind/date line
  above never hosts the button anyway and hovering it revealing a button that floats a line away
  would be a confusing miss-target.)
- No grip/drag glyph and no pin affordance are ever added to a `Manual` row — the row simply never
  gets the `ScribeRowWidgets` grip-column treatment the Task/Pin rows use.

### 8. UI refinements from 2026-08-30 playtest feedback

Four more presentation/input fixes, folded into this same change since they land on the same
`BuildHistoryContent`/`GuiDialogScribeNotebook` surface before this change ships:

- **Keyboard capture leak (bug, not a style choice).** `ScribeDialogBase.CaptureAllInputs()` is the
  mechanism `GuiManager.OnKeyDown` uses to let a focused Scribe text field swallow movement/hotbar
  keys before the character controller sees them (see its own doc-comment, `fix-settings-numeric-
  arrow-focus-leak`); it explicitly enumerates editor rows, Pin Tab rows, and Guestbook notes, but
  has no way to know about `GuiDialogScribeNotebook`'s own `_manualFocusNodes` dictionary (added by
  this change, living on the subclass). Fix: `GuiDialogScribeNotebook` overrides
  `CaptureAllInputs()` again, OR-ing in `Focused && _manualFocusNodes.Values.Any(n => n.HasFocus)`
  on top of `base.CaptureAllInputs()`. Without this, a player typing a Manual entry walks around
  and triggers hotbar/other hotkeys with every keystroke that happens to be a bound key.
- **"Add Entry" button chrome.** Restyled to match the Edit tab's footer buttons exactly: the
  Caudex-family `ScribeTaskFont.ButtonFamily` label font and the theme's default `ButtonStyle`
  (Notebook is never on the cuneiform path, so this is a straight reuse, not a new style). Width is
  capped at 50% of the tab's content width and the button is horizontally centered — via
  `LayoutBuilder` (to read the incoming `MaxWidth`) wrapping a `ConstrainedBox` (half that width)
  wrapping a `Center`. No existing widget in this codebase does fractional-width sizing, so this is
  the first use of `LayoutBuilder` in the Mod project; `Gui.Widgets.Layout.LayoutConstraints` (not
  `BoxConstraints`) is LibGUI's actual constraints type.
- **Divider between entries.** A faint line — `colors.OnSurface with { W = 0.2f }` (the "Ink" color
  the theme names `OnSurface`/`OnBackground` internally; 20% opacity is a literal alpha here, not a
  further multiply against its already-1.0 alpha) — renders via `Border.Only(bottom: new
  BorderSide(1f, dividerColor))` on a `Container` wrapping every row's content except the last.
  Determining "last" requires collecting each row's bare content widget into a list first, THEN
  wrapping all but the final one with the border, rather than deciding per-row in the same pass the
  rows are built (the pending draft, when present, is always first/topmost and therefore never the
  last row on its own).

## Risks / Trade-offs

- **[Risk] A player who is not the notebook's current holder can never legitimately send an edit for
  someone else's entry, but nothing stops the server from RECEIVING a forged packet claiming to
  target any `EntryId` on any notebook the sender can resolve.** → Mitigation: identical exposure
  already exists for every other server-authoritative write in this mod (Guestbook notes, task
  text, pin edits) and is accepted as this project's baseline trust model (a malicious client can
  always attempt off-contract packets; the server-side ownership/identity check is what actually
  matters, not client-side UI hiding).
- **[Trade-off] `EntryId = Guid.Empty` on every non-Manual entry is a wasted 16 bytes per entry.**
  → Accepted: at the store's realistic scale (a few dozen entries total across all kinds, capped at
  64 KB total), this is immaterial.
- **[Risk] The multi-message flow (Add → then separate Set-text edits) means a player who adds an
  entry, types text, and immediately closes the dialog before the Add packet's round-trip completes
  could see a brief desync (their local draft looks saved; server hasn't confirmed yet).**
  → Mitigation: this is the same optimistic-then-synced pattern every other Notebook edit already
  uses (`ApplyLocalOptimisticEdit` / server sync overwrding on confirm) — no new risk class, just the
  existing one applied to a new field.

## Migration Plan

Additive codec bump only (v1 → v2, backward-compatible read of v1 payloads via the migration stub in
Decision 3). No world-save schema change, no rollback concerns beyond a normal code revert — a
reverted build simply stops writing `Manual` entries; existing ones still round-trip harmlessly
through the v1 reader path's default-fill behavior for any client still on the old version (an old
client's `HistoryStore` doesn't know about `Manual` display, but it also doesn't crash on an unknown
kind byte, since the kind switch already has a default/fallthrough).

## Open Questions

None outstanding — every fork identified during proposal review was resolved with the mod author
via AskUserQuestion before this design was written (timestamp grain, item scope, re-editability,
cap policy, text length, empty-draft handling).
