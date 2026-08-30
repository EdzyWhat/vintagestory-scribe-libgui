## 1. Core: HistoryEntry, HistoryEventKind, HistoryStore

- [x] 1.1 Add `HistoryEventKind.Manual = 7` (after `LoreDiscovery = 6`, never renumber existing values)
- [x] 1.2 Add `HistoryEntry.EntryId` (`Guid`, default `Guid.Empty`)
- [x] 1.3 Add `HistoryStore.MaxManual = 30`; add a `Manual` case to `TryAddEntry`'s switch using the same `DropOldestOfKindIfAtCap` sliding-window helper the other kinds use
- [x] 1.4 Raise `HistoryStore.MaxStorms` from 5 to 10
- [x] 1.5 Add `HistoryStore.TrySetManualEntryText(Guid entryId, string authorName, string text)`: finds the entry by `EntryId`, checks `Kind == Manual && ActorName == authorName`, clamps text to `ScribeDocumentCodec.MaxTaskTextLength`, updates `Detail`, returns whether it changed anything
- [x] 1.6 Add `HistoryStore.TryDeleteManualEntry(Guid entryId, string authorName)`: same ownership check, removes the entry, returns whether it removed anything
- [x] 1.7 Bump `HistoryStore.Version` to 2, `PriorVersion` to 1; `Serialize` writes `EntryId.ToByteArray()` (16 bytes) after `InGameDate` for every entry; `Deserialize` reads it for v2 payloads and defaults to `Guid.Empty` for v1 payloads (the migration stub — no real transform needed, just a fill)
- [x] 1.8 Update `HistoryStore`'s class doc-comment (serialized format description, version table) to match the new v2 layout, per `docs/CODEC-MIGRATION.md`'s "keep the accepted-version table current" rule

## 2. Core.Tests

- [x] 2.1 `TryAddEntry` with `Kind = Manual`: adds correctly, respects `MaxManual` sliding window (31st add drops the oldest Manual entry, keeps 30)
- [x] 2.2 `TrySetManualEntryText`: succeeds when `ActorName` matches, no-ops when it doesn't, no-ops on an unknown `EntryId`, clamps text over `MaxTaskTextLength`
- [x] 2.3 `TryDeleteManualEntry`: succeeds when `ActorName` matches, no-ops when it doesn't, no-ops on an unknown `EntryId`
- [x] 2.4 Codec round-trip: a `Manual` entry with a non-empty `EntryId` serializes and deserializes correctly (v2)
- [x] 2.5 Migration: a hand-built v1 byte payload (no `EntryId` field) deserializes with every entry's `EntryId == Guid.Empty` and no error
- [x] 2.6 `MaxStorms` is 10, not 5 — a 11th TemporalStorm entry drops the oldest, keeping exactly 10

## 3. Mod: network messages + server-side handlers

- [x] 3.1 Add `ScribeAddHistoryEntryMessage(DocIdBytes, EntryId, Text)` — client → server, mirrors `ScribeEditGuestbookNoteMessage`'s doc-comment style (server authorizes by sender identity)
- [x] 3.2 Add `ScribeSetHistoryEntryTextMessage(DocIdBytes, EntryId, Text)` — client → server
- [x] 3.3 Add `ScribeDeleteHistoryEntryMessage(DocIdBytes, EntryId)` — client → server
- [x] 3.4 Register all three via `channel.SetMessageHandler<...>` in `ScribeModSystem.StartServerSide`, alongside the existing history/guestbook handlers
- [x] 3.5 Add `NotebookHost` methods `AddManualEntry(ICoreServerAPI, IServerPlayer, Guid entryId, string text)`, `SetManualEntryText(IServerPlayer, Guid entryId, string text)`, `DeleteManualEntry(IServerPlayer, Guid entryId)` — each calls the matching `HistoryStore` method (task group 1) then `FlushHistory()` only if it actually changed something (mirrors `RecordPickedUpIfNew`'s "only flush on a real add" pattern)
- [x] 3.6 Wire the three server-side handlers in `ScribeModSystem.Network.cs`, resolving the target `NotebookHost` the same way `OnServerReceivedEditGuestbookNote`/`OnServerReceivedCompleteTask` do (via `TryResolveHost`/`ResolveItemPacketSlot` as appropriate for an item-hosted document)

## 4. Mod: History tab UI

- [x] 4.1 Add an "Add Entry" button at the bottom of `GuiDialogScribeNotebook.BuildHistoryContent`, below the scrollable entry list, always visible
- [x] 4.2 Clicking "Add Entry" inserts a local-only draft state (mint a `Guid`, empty text, today's date, the local player's own name) at the top of the rendered list and autofocuses its `ScribeMultilineField`; if a draft is already pending, clicking again with the current draft still empty just refocuses it rather than creating a second draft
- [x] 4.3 Render a `Manual` row (including the pending local draft) with: uneditable player-name + date line (same style as automatic entries), a `ScribeMultilineField` for the text (`maxLength = ScribeDocumentCodec.MaxTaskTextLength`), and — only when `ActorName` matches the local player's own name — a delete button; no grip/drag glyph, no pin control, ever
- [x] 4.4 Wire the field's commit callbacks: first non-empty commit on the pending draft sends `ScribeAddHistoryEntryMessage`; any commit on an already-created entry sends `ScribeSetHistoryEntryTextMessage`; losing focus while still empty on the pending draft discards it locally (no packet sent)
- [x] 4.5 Wire the delete button to send `ScribeDeleteHistoryEntryMessage` and remove the row locally on confirmation (server sync is the source of truth on the next `RefreshHistoryView`)
- [x] 4.6 Update `KindLabel` to give `Manual` a real label (new `scribe:scribe-gui-history-kind-manual` lang key, e.g. "Entry") instead of falling through to `entry.Kind.ToString()`
- [x] 4.7 Update the empty-state check to also treat a pending, still-empty local draft as "not empty" (so the empty-state prompt doesn't flash over an active compose box)

## 5. Verify end-to-end

- [x] 5.1 `dotnet test tests/Core.Tests` — all green, including the new cases from task group 2
- [x] 5.2 Local Atlas integration suite green (`build/verify.sh Debug --no-restage`)
- [x] 5.3 Manual/local verification: add an entry, close and reopen the notebook, confirm it persisted with correct name/date/text
- [x] 5.4 Manual/local verification: add an entry, close the dialog without typing, reopen — confirm no entry was created
- [x] 5.5 Manual/local verification: re-edit an existing entry's text twice across two separate dialog sessions — confirm both edits stick
- [x] 5.6 Manual/local verification: give the notebook to a second player; confirm they can see the first player's entry but have no edit/delete affordance on it, and can add their own alongside it
- [x] 5.7 Manual/local verification: delete an entry, confirm it's gone after reopening
- [x] 5.8 Manual/local verification: confirm a Manual entry never shows a drag/grip handle and is absent from the Pin Tab / has no pin control
- [x] 5.9 Manual/local verification: trigger 11 temporal storms (or seed via dev tool) and confirm exactly the newest 10 TemporalStorm entries survive

## 6. Docs

- [x] 6.1 Update `CHANGELOG.md`'s `[Unreleased]` section with the new custom History entries feature and the TemporalStorm cap increase
- [x] 6.2 If any non-obvious LibGUI/codec wrinkle comes up during implementation (e.g. draft-row focus handling, or a codec migration subtlety), add a note to `VSAPI-NOTES.md` per the project's usual practice

## 7. UI refinement (2026-08-30 playtest feedback)

- [x] 7.1 Fix keyboard capture leak: override `CaptureAllInputs()` in `GuiDialogScribeNotebook` to
  also return true when `Focused && _manualFocusNodes.Values.Any(n => n.HasFocus)`, OR'd with
  `base.CaptureAllInputs()` — without this, typing a Manual entry leaks WASD/hotbar keys to the
  character controller (design.md Decision 8)
- [x] 7.2 Convert the Manual entry row into its own small `StatefulWidget`/`State` pair so it can
  own a `hovered` bool; move the delete button into a `Stack` + `Positioned` floating over the
  text/input line's right edge, shown only via `MouseRegion(onEnter/onExit)` on that line —
  mirroring `ScribeEditorContent`'s row State exactly, including reuse of `ScribeRowButton`,
  `ScribeRowControlNudge.FloatingButtonTop`, and `ScribeRowButton.BoxShrink` for button positioning
- [x] 7.3 Merge the kind-line label and author name into one string, `"{ActorName}'s Note"` (update
  the `scribe:scribe-gui-history-kind-manual` lang value to the format string), for every viewer;
  remove the now-redundant separate uneditable name `Text` next to the input/detail text
- [x] 7.4 Restyle the "Add Entry" button: Caudex/`ScribeTaskFont.ButtonFamily` label font matching
  the Edit tab's footer buttons, default theme `ButtonStyle` chrome, and exactly 50% of the tab's
  content width via a 1:2:1 `Expanded`-flex `Row` (mirroring the Edit tab footer's own
  `Expanded(Button)` pattern) — centered because the two flex-1 spacers are equal. Superseded a
  `LayoutBuilder` + `ConstrainedBox` + `Center` attempt (Center greedily claimed the whole tab's
  height — see the `RenderPositionedBox` fact in `VSAPI-NOTES.md`) and, after that, a
  `LayoutBuilder` + loose-`ConstrainedBox` cap (had no visible effect — `Button` shrink-wraps under
  a loose constraint, so the cap was never actually reached; a flex `Row` gives it a *tight*
  constraint instead, which `RenderBox.Constrain` snaps to exactly)
- [x] 7.4a Bug fix (found after 7.4 shipped): the "Add Entry" lang value was `"Add entry"`
  (lowercase "e"), inconsistent with every other button's capitalization — corrected to `"Add
  Entry"` in `scribe:scribe-gui-history-add`
- [x] 7.5 Add a faint bottom divider (`colors.OnSurface with { W = 0.2f }`, via
  `Border.Only(bottom: ...)`) between adjacent History rows (including a pending draft), omitted
  after the last row — requires collecting row content into a list first so "last" can be
  determined before wrapping. Refined per playtest: each non-last row also gets its own 6px gap
  between its content and its divider line (previously the divider sat flush against the text,
  with the only gap being the 6px *after* the line before the next row's subtitle) — 12px total
  between one row's text and the next row's subtitle, 6px between text and its own line
- [x] 7.6 `dotnet build` (full solution) and `build/verify.sh Debug --no-restage` green; restage
  Debug for manual verification
- [x] 7.7 Manual/local verification: typing a Manual entry no longer moves the player or triggers
  hotbar swaps; the delete button appears only on hovering the text line (not the kind/date line
  above it, not anywhere else); the kind line reads "`PlayerName's Note`" for both the author's own
  entries and another player's; the "Add Entry" button reads "Add Entry" (capitalized), is
  Caudex-styled, exactly half the tab's width, and centered; a faint divider appears between
  entries but never before the first or after the last, with a visible gap on both sides of every
  divider line (not just flush-then-gap)
