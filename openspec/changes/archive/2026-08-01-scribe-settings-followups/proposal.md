## Why

The `add-settings-tab` change landed and was playtest-confirmed (2026-07-25), but the tester
raised follow-ups on otherwise-passing items: two are real behavioral gaps in the HUD completion
animation that `add-settings-tab` deliberately deferred, and the rest are settings-form layout/UX
refinements. Bundling them keeps the settings surface and its HUD completion feedback feeling
finished before moving on to the larger GUI-visual changes.

## What Changes

**HUD completion animation (the deferred behavior gaps):**
- **Gradual fade for unpin/delete.** Today a pending unpin/delete completion snaps the task text to
  ~40% opacity for the duration of the undo window. Instead, the text opacity ramps *linearly* from
  100% to 0% across the ~1.5s `PinHudWaitMs` window, so the fade reads as a countdown to removal.
- **Sink-reorder-and-stay for Sink/Keep.** Today a completed Sink/Keep task only gets a mute-fade
  visual cue; the row does not actually move. Instead, when the window elapses the completed task
  **reorders to the end of the pin list** and **stays there even if later unchecked** (unchecking no
  longer reverts it to its prior slot). This is the real row-reorder `add-settings-tab` punted
  because LibGUI's implicit pixel-offset `AnimatedSlide` can't animate a `Column` reorder.

**Settings-form layout/UX polish (`ScribeSettingsContent`):**
- Max HUD rows and HUD row width share one row as two side-by-side columns.
- HUD text size and window text size share one row as two side-by-side columns.
- The "Collapse the HUD" checkbox hugs its label instead of stretching the full window width.
- Up/down arrow keys, while a numeric field is focused, step its value by that field's increment.
- HUD Position dropdown labels renamed: `Left` → `Mid-Left`, `Right` → `Mid-Right`.
- The gear icon on the pinned-task HUD is scaled down (~25% smaller) so it reads proportionally.

## Capabilities

### New Capabilities
<!-- none — this refines existing capabilities only -->

### Modified Capabilities
- `player-pins`: the "brief undoable window with animated feedback" requirement is tightened — the
  unpin/delete preview is a *linear* text-opacity ramp over the window, and a Sink/Keep completion
  performs a durable reorder-to-end that survives a later uncheck (not just a transient visual cue).
- `settings-tab`: the "labeled input control" requirement gains a two-column pairing for related
  numeric controls and keyboard (arrow-key) stepping; the HUD-anchor control's presented labels are
  renamed for the mid-edge anchors; the collapse toggle hugs its label. (HUD gear sizing is an
  implementation detail of the HUD render, not a spec-level behavior — captured in tasks only.)

## Impact

- **Code:** `src/Mod/HudScribePins.cs` (gradual fade, durable sink-reorder ordering, gear size),
  `src/Mod/ScribeSettingsContent.cs` (two-column rows, checkbox alignment, arrow-key stepping,
  dropdown label source), `src/Mod/assets/scribe/lang/en.json` (renamed anchor labels). Possibly a
  small ordering helper in `src/Core/` if the "stay at end after uncheck" state needs a Core-tested
  rule (kept Core-pure, no VS API).
- **No new dependencies**, no persistence-format change (ordering is a client-local HUD concern), no
  server/protocol change. Follows the existing client-local preference + HUD render patterns.
