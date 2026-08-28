## Why

Crafting Tasks shipped in v1.3.0 as a loose generator: ingredient rows are ordinary Trackers that sit under a Craft parent, and opening the editor silently recreates any “missing” child. Completing or deleting the parent only touches that one row, so shopping lists orphan, Sink tears the group apart, and unchecking a sunk parent duplicates children. Playtest plus u/thepeebrain’s reports make that contract the wrong default for v1.3.2.

## What Changes

- **Heal only on the Craft parent’s target stepper.** Rescale Trackers still in the contiguous depth-1 run (matched by item code, any order among themselves). Create nothing. Stop at the first non-subtask. Handbook create still expands once. Editor open, complete, delete, unindent, and reorder do not heal.
- **Parent + owned run is a range.** A parent is any depth-0 row plus the contiguous depth-1 rows under it. Complete, Sink, Delete, and trash apply as **one** range mutation (not N independent completions).
- **Subtask Behavior** (Settings picker): **Bound to parent** (default) / **Independent** / **Discard children**. Bound completes the run with the parent, applies the existing completion and pin policies to the mutated rows, and uncheck mirrors the run (no unsink). Independent leaves children. Discard removes children (uncheck cannot restore them). Trash follows the same picker.
- **HUD Craft count.** Have/need on pinned Craft parents, not only Trackers.
- **Pin insert.** Depth-1 pins insert under a pinned parent’s HUD cluster; pinning a parent later gathers its already-pinned children. If the parent is not pinned, the child appends (no auto-pin parent).
- **Pin notes.** Text rows can be pinned. HUD is text-only (no checkbox, no unpin). Unpin from the Pin Tab or the source surface.
- **Handbook copy** (handbook-only; editor Add ▾ unchanged): heading `Add to Scribe`; links in order `Link to this page`, `Count this item`, `Add ingredients` (variants `Add ingredients ({0})`).
- **HUD chrome:** title **Scribe Pins**; header font matches row font (16 × HUD font scale); Settings boolean to show/hide the HUD gear (default on); max HUD rows ceiling **30**.
- **Grip:** drag starts only after the pointer moves; once a drag has started, release must not nest.
- **Tool leak bug:** tag-only tools (`isTool` + `tags`, no `code` — e.g. debarked-log axe/hammer) must not become Trackers (today they merge into a junk `*:*` family such as “2 Pocketsun (any variant)”). No tools setting.
- **Out of scope:** visual collapse of subtask groups; clear-HUD-and-refill from this document; non-task handbook bookmarks; kind conversion in the editor.

No codec bump expected. Existing bogus tool-children are not auto-deleted (heal no longer recreates *or* strips).

## Capabilities

### New Capabilities

- None. Parent-range and Subtask Behavior extend `task-subtasks`; the rest is requirement change on existing capabilities.

### Modified Capabilities

- `craft-task`: heal contract (stepper rescale-only); skip non-consumed / tag-only tools; handbook Craft link wording.
- `task-subtasks`: owned-run range ops; Subtask Behavior; grip drag vs nest.
- `player-pins`: insert-under-parent; gather on pin-parent; pin Text notes.
- `pinned-task-hud`: Craft have/need; title and header size; optional hide gear; note rows text-only.
- `pinned-task-tab`: notes appear and can be unpinned.
- `settings-tab`: Subtask Behavior picker; HUD gear visibility; HudMaxRows ceiling 30.
- `lectern-gui-shell`: pin control on Text rows; grip tap suppressed after a drag.
- `tracker-task`: handbook “Count this item” label (handbook surface only).
- `link-task`: handbook “Link to this page” label (handbook surface only).

## Impact

- **Core:** owned-run scan; range complete/sink/delete; Subtask Behavior enum + settings field; heal no longer creates rows; pin-store insert/gather; `HudMaxRows` clamp to 30.
- **Mod:** completion/delete write-through applies the range; `ScribeCraftRecipeProbe` skips tag-only/`!Consume` tools; HUD Craft counter and header; handbook lang keys (split from editor picker); grip `GestureDetector` threshold.
- **Tests:** Core unit tests for heal, range ops, pin insert, tool skip; in-game `.scribeprobe` on debarked oak log; HUD/title/gear/cap playtest.
- **Saves:** fully compatible with 1.3.0/1.3.1 documents and pins. No new packet types anticipated (settings stay client-local except completion policy already on the complete request; Subtask Behavior must travel with complete/delete the same way).
