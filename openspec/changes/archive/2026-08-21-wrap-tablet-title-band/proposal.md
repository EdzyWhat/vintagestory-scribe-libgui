## Why

On the Tablet, a long document title runs off the title band and is hidden: the resting title is
hard-clipped to the band width (`Clip` + single-line `CuneiformText`) and the editing title is a
single-line input, so a player who types past the band's width can no longer see the whole title —
it "scrolls off the pixel art and is hidden." A player raised exactly this on ModDB
(https://mods.vintagestory.at/scribe#tab-description — Fey_Shadow, "make the title line span two
lines when it runs past the display width"), and the author committed to the fix there: "I'll work
on making it a growable section. I think I'll have it limit to 2 lines though."

This directly reverses a non-goal shipped by `wrap-tablet-item-titles`, which wrapped item-kind row
titles but deliberately kept the title BAND single-line ("the intentional single-line title BAND …
MUST stay single-line/clipped"). That decision is what this change flips, so it is its own change
rather than an edit to the shipped one.

## What Changes

- The Tablet dialog title band SHALL wrap a long title to a maximum of **2 lines** instead of
  clipping it to a single line, in BOTH the resting (display) and editing states. A title that fits
  on one line is visually unchanged; a title longer than two lines' worth clips at the end of the
  second line (the existing ellipsis/clip behavior, just one line lower).
- The title band grows to accommodate the second line: the band's inner title-slot height expands
  from one line to up to two, and the surrounding drag/close/pencil chrome stays vertically centered
  and clear of the wrapped text. The band does not grow unbounded — two lines is the hard cap.
- Scope is the **Tablet only** (the sole cuneiform surface, and the surface the ModDB report is
  about). The Lectern/Notebook/Scriptorium/HUD title chrome is unchanged (they render a plain
  single-line `RichText` with ellipsis and were not reported as clipping in practice).
- Reuses the wrapping cuneiform renderer already in the codebase (`ScribeCuneiformFieldRenderWidget`,
  the same one `wrap-tablet-item-titles` routed item labels through) capped at two lines — no new
  rendering machinery, no `gui` fork, no `Core` change, no new dependency.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `tablet-dialog`: the title band gains a requirement that a long title wraps to at most two lines
  (resting and editing) instead of clipping to one, superseding the single-line-title-band non-goal
  that `wrap-tablet-item-titles` recorded.

## Impact

- Code: `src/Mod/GuiDialogScribeTablet.cs` (`BuildTitleDisplay` resting override → two-line wrapping
  cuneiform; `BuildTitleField` editing override → two-line cuneiform input) and the title-band height
  metric in `src/Mod/ScribeDialogBase.Layout.cs` (`BuildTitleBar` / the `TitleBtnsH` slot) so the
  band can host two lines without the chrome colliding. The base default (`BuildTitleDisplay` /
  `BuildTitleField` in `ScribeDialogBase.Layout.cs`) stays single-line for the other surfaces.
- `src/Core/` untouched (Mod-layer render/layout change; the API-free invariant holds).
- Depends on the wrapping cuneiform widget shipped by `wrap-tablet-item-titles`; no other change
  ordering constraints.
- No new lang strings, no new mod dependency, no `gui` fork.
