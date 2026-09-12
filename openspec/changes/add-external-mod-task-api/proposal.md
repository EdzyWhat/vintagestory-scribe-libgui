## Why

Notice Board (mod id `noticeboard`) wants to let a player convert an in-world notice into a Scribe
task with one click, and its author has agreed to call into a public Scribe method for this once one
exists. Scribe currently has no public, external-mod-facing surface at all — every existing entry
point into task creation is internal (the GUI editors, the Assignment system, the Scriptorium
import). This adds the first one, shaped so any other mod author can use it the same way, not just
Notice Board.

## What Changes

- New public method on `ScribeModSystem`: `TryCreateExternalTask(IServerPlayer player, string? title,
  string? bodyText, string? extraInfo) -> bool`. Server-side only, direct in-process call (no
  networking) — a caller takes Scribe as an optional soft dependency and calls the method after an
  `IsModEnabled`/null-check, matching the project's existing soft-dependency convention.
- `title` maps to a Task-kind block; `bodyText` maps to a Text-kind subtask beneath it. If `title` is
  absent, the `bodyText` block is promoted to stand alone at Depth 0 instead of staying a subtask with
  no parent.
- New `ScribeBlock.ExtraInfo` field (Core): an opaque, caller-composed free-text string, capped like a
  task's own text. When present, it attaches to whichever block ends up at Depth 0 (the title Task, or
  the promoted body Text) and renders a new generic "hover for more detail" icon — visually similar in
  spirit to the existing assignment stamp icon, but independent of `ScribeAssignment` and its
  player-to-player lifecycle. The caller decides what text goes in the hover (author name, source,
  date, a pointer back to their own UI, anything) — Scribe never parses it.
- The new icon is visible in the Read view, Editor, and Pin Tab. It is deliberately **not** shown in
  the HUD, mirroring how the existing assignment stamp icon already behaves (verified: `HudScribePins`
  never references `IsAcceptedAssignment` today) — this is not a new pattern, just applying an existing
  one to a new field.
- New server-side per-player "last opened Scribe item" tracker on `ScribeModSystem`, populated by
  piggybacking on the existing `NotifyServerNotebookOpened` → `OnServerReceivedNotebookOpened` message
  (already sent by all three Scribe item types on dialog-open; today it only records history). In-memory,
  per-session, not save-persisted — mirrors the client-side `lastOpenedScribeItemDocId` convention it is
  modeled on.
- `TryCreateExternalTask` resolves its target the same way the Handbook "Add to Scribe" flow already
  does: prefer the last-opened writeable carried Scribe item, else the first writeable one, else fail.
  Unlike Assignment Accept's picker, this never prompts — matching the low-stakes, easily-undone nature
  of a single external add.
- New server-side failure notice to the calling player covering the three known resolution failures (no
  Scribe item carried, every carried item locked/hardened, target document full) — a new mechanism
  because Scribe's existing error UX (`TriggerIngameError`) is client-only and this call has no client
  leg. The method's `bool` return is for the calling mod's own control flow; the player-facing reason
  comes from Scribe itself.

**Explicitly out of scope for this change** (deferred, not rejected):
- Bulk/multi-task import for an external caller (a `TryImportTasks`-shaped method reusing
  `ScribeDocumentJsonCodec`) — needs a server-side equivalent of the client-only `ScribeImportValidator`
  first; revisit once a caller actually needs more than one task at a time.
- Any special handling of links inside `bodyText` (clickable subtasks or inline spans) — links stay as
  plain visible text for now; revisit if demand shows up.
- A way for Scribe to link back to the calling mod's own UI (e.g. Notice Board's board) — that's the
  calling mod's responsibility if they want it, via their own `extraInfo` text.
- A guard/affordance for a call with both `title` and `bodyText` empty — not worth special-casing.

## Capabilities

### New Capabilities
- `external-mod-task-api`: the public `TryCreateExternalTask` method, its title/bodyText/extraInfo
  mapping rules, the new hover-info icon and its Read/Editor/Pin-Tab-only visibility, the last-opened
  per-player tracker, and the target-resolution/failure-notice behavior.

### Modified Capabilities
(none — no existing capability's requirements change; this only adds new surface)

## Impact

- **Core**: new `ScribeBlock.ExtraInfo` field; a binary pin-codec version bump to snapshot it into
  `ScribePinnedRef` for the Pin Tab.
- **Mod**: new public method on `ScribeModSystem`; a new generic hover-info icon widget (parallel to,
  not built from, `ScribeAssignedTaskIcon`); threading `ExtraInfo` through the Read/Editor/Pin-Tab row
  builders the same way `IsAcceptedAssignment` is threaded today; a new per-player last-opened-docId
  dictionary on the server `ScribeModSystem`, populated from the existing `OnServerReceivedNotebookOpened`
  handler; a new server→client failure-notice path for the three known resolution failures.
- **External consumers**: Notice Board is the first caller; the method is designed to generalize to any
  future external mod, not special-cased to Notice Board's content shape.
- **No changes** to `ScribeAssignment`, the Assignment state machine, or any existing Assignment-related
  surface — the new hover-info mechanism is intentionally independent of it.
