## Why

The first playtest of the Task Notice (`add-assignment-physical-delivery-mode`) surfaced four
real gaps beyond the model/lang-key bugs already hotfixed directly: the Accept dialog is bare
LibGUI chrome with a layout bug, the Assigner gets zero record of a sent notice until it's
Accepted (surprising, not what the playtest expects), and the notice's read-only task checkboxes
look interactive when they aren't.

## What Changes

- Replace the Accept dialog's stock LibGUI window chrome (generic title strip, fixed 460×560 size)
  with Scribe's own custom title bar and 3-column inset frame, visually matching the
  Notebook/Lectern pattern (implemented as new, standalone code — not shared with
  `ScribeDialogBase`), backed by a pixel-art parchment/scroll asset so it reads as an unfurled
  scroll instead of a plain window.
- Size the dialog from the player's Pixel Art Size setting and the parchment art's own aspect
  ratio, scaled down so it reads as noticeably smaller than a full Notebook/Lectern page.
- Fix the Accept dialog's footer layout: Decline and Accept size to their text (not stretched
  wide), and when Accept needs a destination-item picker, the picker renders in its own row
  ABOVE the two buttons instead of pushing them off the right edge.
- **BREAKING** (reverses an existing requirement): sending a Task Notice now immediately creates
  a `ScribeAssignmentStore` record and a "Sent" row in the Assigner's Sent Assignment History,
  instead of no record existing until Accept. The Assignee's Inbox still shows nothing until they
  physically receive the item, at which point it appears as "Received" and then
  Accepted/Declined, matching the existing post-Accept behavior.
- Resolve the Task Notice's read-only-but-clickable-looking checkboxes: render them in a visibly
  disabled/inert style (not a new interactive surface) so the read-only state is legible instead
  of looking broken.

## Capabilities

### Modified Capabilities
- `task-notice-item`: the Accept dialog's chrome, sizing, and button layout change; the "no
  assignment-store record until Accept" requirement is reversed to "a Sent record exists from
  the moment a notice is created"; the read-only task rows must render as visibly disabled.

## Impact

- `GuiDialogTaskNotice.cs` (custom title bar + 3-column frame replacing stock `WindowFrame`,
  Pixel-Art-Size-driven sizing, footer layout), a new parchment backdrop asset under
  `src/Mod/assets/scribe/textures/gui/`, `ScribeReadContent`'s disabled-checkbox styling for a
  read-only host.
- `ScribeModSystem.Delivery.cs` / the Task Notice send path: create the `ScribeAssignmentStore`
  record and Sent History row at send time instead of at Accept time (Accept then transitions an
  existing record rather than creating one).
- Depends on `add-assignment-physical-delivery-mode` (still in-progress, 27/29 tasks — only its
  remaining manual-playtest tasks are open). That change owns `task-notice-item`'s baseline spec
  as an ADDED capability that hasn't been archived into `openspec/specs/` yet — archive it before
  (or coordinate the archive order with) this change so the MODIFIED delta here has a real
  baseline to target.
