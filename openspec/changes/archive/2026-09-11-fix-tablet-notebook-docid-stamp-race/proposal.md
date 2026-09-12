## Why

Notebooks and Tablets silently lose in-progress edits: a player types a task, closes the
dialog, reopens it, and the task is gone (a pinned copy of it survives, which is what made
the report — Discord user Eli_ink, reported via Nick/"Raptor" — initially look like a
pin-vs-document bug). Root-caused via a diagnostic build shipped to the reporter and their
returned `server-main.log`: `NotebookHost`'s constructor (inherited unchanged by
`TabletHost`) eagerly mints a brand-new random `DocId` and writes it to whatever `ItemStack`
it's constructed over, the instant that stack has no `scribeDocument` attribute yet, with no
coordination between the client and the server. A second, subtler stamp path exists too:
`AttachServerContext`'s one-time PickedUp-history bookkeeping (`RecordPickedUpIfNew`) flushes
through `Flush()`, which persists the in-memory document (and thus a freshly-minted `DocId`)
as a side effect of recording history — even though it only ever mutates `_history`. Both
paths are reachable from the server's own `OnHistoryScanTick` (a background sweep firing
every 10s for every online player, unconditionally over every carried Scribe item) — entirely
independent of anything the player is doing. Whichever side (the player's own client, opening
its dialog, or the ambient server sweep, resolving a host for its own bookkeeping) stamps a
stack first "wins" the real DocId; the loser's client keeps proposing edits under a DocId the
server never adopted, and every save silently fails the mismatch guard in
`OnServerReceivedNotebookSave` with no error surfaced back to the player. This is why it
reproduces reliably on a long-running, heavily-modded server (natural read-then-type pacing
routinely leaves a multi-second window open) but rarely shows up in quick manual testing on a
lightly-modded dev world.

## What Changes

- Remove the eager self-stamp-on-construct from `NotebookHost`'s constructor. Constructing a
  host over a documentless stack SHALL populate an in-memory placeholder document only; it
  SHALL NOT write anything to the `ItemStack` as a side effect of construction.
- `RecordPickedUpIfNew` (the one-time PickedUp-history bookkeeping run whenever a document
  host is attached to a server context — including from the ambient `OnHistoryScanTick`
  sweep) SHALL persist only the history store (`FlushHistory`), never the document
  (`Flush`), since it only ever mutates history. This closes the sweep as a stamp path
  without disabling Death/PvpKill/BossKill/TemporalStorm recording on a documentless stack —
  those never depended on a document existing and continue to fire normally.
- `OnServerReceivedNotebookSave`'s existing "no stored DocId yet — allow the write" bootstrap
  branch becomes the single place a fresh `DocId` is ever persisted onto a previously-
  documentless stack, since both stamp paths above are now closed at the source.
- Remove the `TEMP DIAGNOSTIC (task-loss-on-tablet-close)` trace lines added to
  `ScribeModSystem.Network.cs` and `ItemScribeTablet.cs` for this investigation, and delete
  the diagnostic-only `tests/Integration.Tests/TabletTaskLossDiagnosticScenarios.cs`,
  superseded by regression tests for the actual confirmed mechanism.
- Out of scope (considered and deliberately dropped): a client-side resync reply for a
  DocId-mismatch save rejection. With both stamp paths closed, the server's save handler is
  the sole writer of a stack's first-ever `DocId` — no remaining code path can race it, so a
  mismatch reply has nothing left to guard against and isn't worth the added client-side
  plumbing (routing an unsolicited reply to the right open dialog by slot identity, since the
  registry is keyed by a DocId the client never registered).

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `notebook-item`: "Notebook saves are server-authoritative" — clarify that the server's save
  handler is the sole writer of a stack's first-ever `DocId`, and that no other server-side
  operation (history recording, resolving a document host for any other reason) writes one
  ahead of it.
- `clay-wax-tablet-item`: "Tablet document persists on the ItemStack" — same DocId-ownership
  clarification (via the shared `TabletHost`/`NotebookHost` base).
- `notebook-history`: recording a PickedUp history entry (including from the periodic
  carried-notebook sweep) must never persist a document as a side effect, while continuing to
  record Death/PvpKill/BossKill/TemporalStorm on documentless stacks exactly as before.

## Impact

- `src/Mod/NotebookHost.cs` (ctor no longer writes to the stack; `RecordPickedUpIfNew` uses
  `FlushHistory` instead of `Flush`)
- `src/Mod/TabletHost.cs` (inherits both fixes; no direct changes expected)
- `src/Mod/ScribeModSystem.Network.cs` (`OnServerReceivedNotebookSave` becomes the sole
  bootstrap-write site — no reply-shape change; diagnostic traces removed)
- `src/Mod/ItemScribeTablet.cs` (diagnostic traces removed)
- `tests/Integration.Tests/TabletTaskLossDiagnosticScenarios.cs` (deleted, superseded by real
  regression coverage of the fixed mechanism)
- No network protocol change (the previously-proposed resync reply was dropped)
