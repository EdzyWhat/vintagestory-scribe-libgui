## Why

The two-line title wrapping that was just built and confirmed in-game exists on exactly one
surface: the wet Tablet with cuneiform ON. On every other Scribe dialog — Lectern, Notebook,
Scriptorium, and the Tablet with cuneiform OFF (the readable RichText path) — a long title is
still rendered single-line and hard-truncated with an ellipsis, so players lose the tail of any
title longer than the band. The wrapping machinery in `ScribeDialogBase` is already generalized
(the `TitleMaxLines` seam grows the band); only the two rendering leaves and the default are still
tablet-scoped. Generalizing it now is cheap, uses standard fonts/layouts, and removes a visible
inconsistency.

## What Changes

- Long titles wrap to **at most two lines** on ALL standard Scribe dialog surfaces — Lectern,
  Notebook, Scriptorium, and the Tablet with cuneiform OFF — matching the wet-cuneiform Tablet
  that already does this. A title that fits on one line renders exactly as today (no band-height
  change, byte-identical layout).
- The base `ScribeDialogBase.TitleMaxLines` default flips from `1` to `2`, so the shared band-growth
  path (`BuildTitleBar`) reserves the second line for every surface at once instead of per-surface
  overrides. The Tablet's conditional override collapses to unconditional two-line wrapping (both
  cuneiform ON and OFF).
- The base resting-title renderer (`BuildTitleDisplay`) wraps its `RichText` to `TitleMaxLines`
  lines instead of clamping to one line with an ellipsis. A title longer than two lines still
  ellipsizes on the second line.
- The **editing** title on the readable path stays single-line (the stock LibGUI `TextField` has no
  multi-line mode — see design); only the resting/display title wraps. Editing-wrap parity is a
  documented non-goal for this change.
- Surfacing the **Tablet title-band width knob** so the user can tune where the tablet title wraps
  (`ScribeLayoutProportions.TitleBtnsWFrac`). No behavioral change ships from this change beyond
  exposing/documenting the knob; the actual value is a follow-up the user will set.

## Capabilities

### New Capabilities
- `dialog-title-wrapping`: The shared, cross-surface rule that any Scribe dialog title
  (`ScribeDialogBase`-hosted) wraps a too-long title to at most two lines in its resting/display
  state, growing into the title band's existing vertical slack, with standard fonts and layouts —
  and that the editing title on the readable path remains single-line.

### Modified Capabilities
<!-- None modified in the base specs. NOTE: the pending, not-yet-archived change
     `wrap-tablet-title-band` ADDs a requirement to `tablet-dialog` that scopes wrapping to the
     Tablet and asserts other surfaces "SHALL remain single-line as today". This change supersedes
     that scoping clause. Because that requirement is not yet in the base spec (its change is
     unarchived), it cannot be MODIFIED here without the known archive-order header-drift trap;
     the reconciliation is called out in design.md (Open Questions / Migration). -->

## Impact

- **Code (src/Mod/ only; no Core, no VS API additions, no new deps):**
  - `ScribeDialogBase.Layout.cs` — flip `TitleMaxLines` default to `2`; wrap the base
    `BuildTitleDisplay` `RichText` to `TitleMaxLines`.
  - `GuiDialogScribeTablet.cs` — simplify the `TitleMaxLines` override to unconditional `2` (or
    remove it now that the base default matches); the cuneiform-OFF path then inherits the base
    two-line RichText.
  - `TabletHost.cs` / `IScribeDocumentHost.cs` — the tablet title-band width knob
    (`ScribeLayoutProportions.TitleBtnsWFrac`, default `0.80f`, currently inherited unmodified by
    the tablet) is documented for tuning; a value change is deferred to the user.
- **Surfaces affected:** Lectern, Notebook, Clockmaker's Notebook, Scriptorium, Chalkboard, and the
  Tablet (both cuneiform states). The HUD pinned-task chrome is NOT a `ScribeDialogBase` title bar
  and is unaffected.
- **Persistence/network:** none. This is a client-side layout/render change only.
