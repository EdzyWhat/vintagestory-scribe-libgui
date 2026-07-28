## Why

The Lectern's four sidebar nav buttons (Read, Edit, Pinned, Settings) look identical regardless of
which view is active — there is no visual "you are here." A player switching views has no persistent
cue as to their current mode. Highlighting the active button with its own thematic color makes the
current view obvious at a glance.

## What Changes

- Give each nav button a distinct thematic "active" color, applied only when that button's target is
  the current one:
  - **Read** `#465481` (slate blue), **Edit** `#9d4b44` (brick red), **Pinned** `#6b8257` (sage
    green), **Settings** `#746f66` (warm gray).
- When a button is active, its box fills with the thematic color and its glyph switches to cream
  `#eae6dd` for contrast; inactive buttons keep the current neutral resting style unchanged.
- On hover of the *active* button, the fill brightens by +10 HSV Brightness points (reusing the
  existing `ScribeRowConstants.ShiftBrightness` helper).
- Read / Edit / Pinned track the lectern's own `viewMode`. **Settings is active whenever the
  standalone settings window is open** (it is a separate dialog, not a lectern view), so the lectern
  is notified of settings open/close to repaint the gear live.

## Capabilities

### New Capabilities
- `lectern-nav-active-state`: the sidebar nav buttons reflect which view/surface is currently active
  via a per-button thematic color (active fill + cream glyph + hover brightening), including the
  Settings button reflecting the standalone settings window's open state.

### Modified Capabilities
<!-- None. This adds a new visual-state capability; it does not change the existing behavior of any
     requirement in lectern-gui-shell (row affordances, scrolling, backdrop) or settings-tab. -->

## Impact

- **Code**: `src/Mod/GuiDialogScribeLecternLibGui.cs` (nav build + button widget gains an active
  color/state), `src/Mod/ScribeModSystem.cs` (expose settings-open state + a change notification), and
  a small color-constants addition (the four thematic colors + cream glyph). Reuses
  `ScribeRowConstants.ShiftBrightness` for the hover brighten.
- **No new dependencies**; visual-only, no persistence/network/Core change.
- **Cross-dialog coupling (new)**: the lectern subscribes to a "settings visibility changed"
  notification so the gear recolors while the separate settings window is open alongside it.
