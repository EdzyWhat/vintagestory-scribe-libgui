## Why

A Quest Link task row today looks identical to a plain (non-quest) Link: the same itemless
"scribebook" glyph, the same standard completion checkbox, and the same link accent color. The
checkbox is actively misleading — toggling it has no effect on the referenced quest (Progression
Framework quest state isn't driven by it), unlike a Tracker's or Task's checkbox, which does
something real. Nothing in the row visually signals "this points at a quest" versus "this points
at a Handbook page."

## What Changes

- Quest Link rows render a new dedicated "quest marker" icon (an exclamation mark in a circle —
  already drawn as `src/Mod/assets/scribe/textures/icons/quest.svg`, a 180°-rotated derivative of
  the existing `info.svg`) instead of the shared `scribebook` glyph a guide-page Link uses.
- Quest Link rows no longer render a completion checkbox. The quest-marker icon takes the
  checkbox's former leading-slot position instead of rendering separately, inline near the item
  name as it does today. This applies on every surface that draws a Link row: Read view, Editor
  view, Pinned view, HUD pins, and the Assignment-stage row.
- Quest Link rows render in a new, distinct accent color, separate from a plain Link's color, via
  a new `ScribeRowStyle.QuestLinkColor` override — authored per-surface/theme the same way the
  existing `LinkColor` override already is for the tablet and chalkboard. The parchment ("Light")
  theme's value is steel-blue `rgb(66,107,183)`; other themes get their own values (see design.md),
  since the Blue-clay tablet's own accent is already essentially that hue.
- **Data/semantics are unchanged.** A Quest Link block stays `Completable=true` and keeps its
  `Done` flag exactly as today — it still counts toward a document's task totals, still
  participates in `ScribeCompletion`/TSV export exactly as before. This is a pure presentation
  change: only the rendered checkbox *widget* is suppressed for a quest-link row; nothing in
  `src/Core/` changes.

## Capabilities

### New Capabilities
(none)

### Modified Capabilities
- `link-task`: gains a new requirement describing how a Quest Link's row renders (icon, checkbox
  suppression, color) distinctly from a plain Link's row, on every surface that draws one.

## Impact

- `src/Mod/assets/scribe/textures/icons/quest.svg`: already created; registered as a new icon in
  `src/Mod/ScribeModSystem.Assets.cs`.
- `src/Mod/ScribeRowWidgets.cs`: `ScribeLinkIcon` (icon selection) and a new shared helper for the
  quest-link leading-slot control, used by every render call site below instead of a duplicated
  branch in each.
- `src/Mod/ScribeRowStyle.cs`: new `QuestLinkColor` field, alongside the existing `LinkColor`.
- `src/Mod/ScribeTheme.cs`: new per-surface/theme `QuestLinkColor` default authoring (parchment,
  chalkboard, and all four tablet clay variants).
- Render call sites updated: `src/Mod/ScribeReadContent.cs`, `src/Mod/ScribeEditorContent.cs`,
  `src/Mod/ScribePinnedContent.cs`, `src/Mod/ScribeAssignmentStageRow.cs`,
  `src/Mod/HudScribePins.cs`.
- No `src/Core/` changes, no new dependencies, no network/persistence changes.
