## Why

On the Tablet, a long Tracker/Link/Craft item title (e.g. "Beige nadiya sleeveless peasant shirt")
clips mid-word and runs off its bounds, while the exact same title wraps cleanly on the HUD, Lectern,
Notebook, and Scriptorium. The clip is worse for subtasks, which have less horizontal room. The
Tablet is the one surface a player reads item tasks on in cuneiform, so a clipped, unreadable name is
a real legibility regression there.

## What Changes

- Item-kind titles (Tracker / Link / Craft) rendered in cuneiform on the Tablet SHALL wrap to the
  available width instead of clipping mid-word, matching every other surface's wrapping behavior.
- The fix is at the single shared choke point `ScribeItemLabel.Build` (`src/Mod/ScribeRowWidgets.cs`):
  its cuneiform branch stops emitting the single-line `CuneiformText` render object and instead emits
  the already-existing **wrapping** cuneiform renderer (`ScribeCuneiformFieldRenderWidget`,
  display-only) that Task-text rows on the Tablet already use. Because every surface funnels item
  titles through `ScribeItemLabel.Build`, this fixes parent rows and subtasks in one place.
- The intentional single-line title BAND (the dialog title chrome) is unaffected — it uses the
  same renderer with `singleLine: true` and MUST stay single-line/clipped.
- No behavior change on the HUD/Lectern/Notebook/Scriptorium (they already wrap via a plain `Text`
  with `SoftWrap`); this only changes the cuneiform (Tablet) path.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `tablet-dialog`: adds a requirement that item-kind titles wrap to the available width on the
  cuneiform surface rather than clipping.

## Impact

- Code: `src/Mod/ScribeRowWidgets.cs` (`ScribeItemLabel.Build` cuneiform branch only). No change to
  `ScribeReadContent`/`ScribeEditorContent` call sites (they already call `ScribeItemLabel.Build`),
  no change to `CuneiformText` (kept for the single-line title band), no change to
  `ScribeCuneiformFieldRenderWidget` (reused as-is, display-only).
- `src/Core/` is untouched (this is a Mod-layer render change; the API-free invariant holds).
- No new mod dependencies; no fork of `gui`.
- Surfaces affected: Tablet only (the sole `UseCuneiform` surface). Parent rows and subtasks both.
