## Why

The Guestbook tab lets a player who has visited on multiple in-game days edit a note on
each day's entry, but the feature is broken in three linked ways: (1) the text caret
appears in every one of the player's own note fields at once, (2) keystrokes only ever
reach the *oldest* entry regardless of which field was clicked, and (3) even when a note
is committed, the server writes it onto the player's first entry, so per-day notes
collapse onto one. The result is that a returning player cannot leave (or even see the
caret in) a distinct note for each visit — the exact behaviour the tab presents in its UI.

## What Changes

- **Per-entry focus:** replace the single shared `FocusNode` used by every own-entry note
  field with one `FocusNode` per editable entry, so only the clicked field shows a caret
  and captures keystrokes (fixes symptoms 1 and 2).
- **Stable per-row identity:** give each guestbook row (and its note field) a stable
  `ValueKey` derived from the entry's identity, so LibGUI can distinguish the otherwise
  structurally-identical sibling rows and route focus/state correctly.
- **Per-day note addressing (server + wire):** the edit path SHALL address a note by the
  specific entry it belongs to, not just by document + player. `GuestbookStore` gains a
  set-note operation keyed by `(playerName, inGameDate)`; the
  `ScribeEditGuestbookNoteMessage` wire packet carries the entry's `inGameDate`
  discriminator; the server handler routes the edit to the matching entry (fixes symptom 3).
- **Note length reconciliation:** the spec text still says "max 80 chars" while the code
  enforces `MaxNoteLength = 140`; reconcile the spec to the shipped value (140) so the
  requirement matches reality (no code change to the cap).
- No persistence-format change: existing SGBK v1 saves already store `(playerName,
  inGameDate, note)` per entry, so the new addressing reads/writes the same fields.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `lectern-guestbook`: the "own-entry Note is editable" requirement is strengthened so that
  a player with multiple entries edits each entry's note **independently** — focus and
  keystrokes are per-field, and the committed note is stored on the specific
  `(player, day)` entry rather than collapsing onto the player's first entry. The note
  length in the display requirement is corrected from 80 to the shipped 140.

## Impact

- **Core** (`src/Core/GuestbookStore.cs`): `TrySetNote(playerName, note)` becomes
  entry-addressed — `TrySetNote(playerName, inGameDate, note)` matching on
  `(PlayerName, InGameDate)`. Unit-testable without the game; add `Core.Tests` coverage.
- **Wire** (`src/Mod/ScribeEditGuestbookNoteMessage.cs`): add an `InGameDate` field.
- **Server handler** (the `ScribeEditGuestbookNoteMessage` receiver): pass the date through
  to `TrySetNote`.
- **Client view** (`src/Mod/ScribeDialogBase.cs`, `BuildVisitorsContent` + focus-node
  lifecycle at `:126-127`, `:413-417`, `:1547-1548`): per-entry `FocusNode` collection
  (mirroring `pinFocusNodes`/`editorFocusNodes`), per-row `ValueKey`, and send the entry's
  `InGameDate` on blur. `CaptureAllInputs` must consider any guestbook note node focused.
- **Spec**: `openspec/specs/lectern-guestbook/spec.md` (via delta).
- No new dependencies; no serialization/codec version bump.
