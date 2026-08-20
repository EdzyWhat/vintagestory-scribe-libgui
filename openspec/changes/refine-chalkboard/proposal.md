## Why

The chalkboard is a shared, wall-mounted Scribe document surface (added in
`add-chalkboard-block`). Unlike the Lectern/Scriptorium, its slate art reads as a small,
finite board — a wall of endless scrolling tasks doesn't fit the object. It should hold a
bounded list, capped the same way the wax tablet already caps its tier, reusing the existing
`ScribeDocumentPolicy` machinery rather than inventing a new limit mechanism. This proposal is
the first in an ongoing bundle of chalkboard refinements the author will extend over time; it
starts with the task cap.

## What Changes

- Cap the chalkboard at **10 tasks**, enforced exactly the way the wax tablet enforces its cap:
  the chalkboard's host reports a `ScribeDocumentPolicy` and the existing add/mutation boundary
  consults `CanAdd`, disabling the "add task" affordance and surfacing the standard in-game
  refusal at the cap. No new enforcement path is added.
- The cap counts **task blocks only** (Task-kind blocks), matching the wax tablet's
  `MaxBlocks = 10` semantics — freeform notes/text blocks are NOT counted and remain uncapped.
- **No pin cap**: the chalkboard is a shared placed block and pins are per-player, so the policy
  leaves `MaxPins` uncapped (`null`). This differs from the wax tablet's `Tablet` preset (which
  also caps pins at 1); the chalkboard therefore uses its own policy value, not the `Tablet`
  preset.
- Implement by overriding `Policy` on `BlockEntityScribeChalkboard` to return
  `new ScribeDocumentPolicy { MaxBlocks = 10 }` (MaxPins `null`, not read-only), mirroring the
  way `TabletHost` overrides `Policy`. Everything downstream (the disabled add affordance, the
  cap-reached notice) is inherited for free once the host reports the policy.
- Reuse the tablet's existing cap-reached in-game notice (lang key) if it is tier-agnostic;
  add a chalkboard-appropriate string only if the tablet's copy names the tablet specifically.

### Cosmetic refinements (second bundle entry)

- **Brighten the chalkboard accent (`Primary`) by +10 HSV Value points.** The forest-green
  `ChalkAccent` sat at V≈0.42, too dark to read as the task-row CHECKBOX tick against the dark
  slate. Lift it to V≈0.52 (a uniform ×`0.52/0.42` scale on all three channels — hue and
  saturation unchanged, only Value rises) so the accent stays the same green but reads. This is a
  single-constant edit; every role that cascades from `Primary` (button fills, caret, selection
  tint, checkbox) brightens together.
- **Make the completion-policy picker's open menu legible on the chalkboard.** The stock
  `DropdownStyle` fills the SELECTED row from `StateSelected` (a translucent `Primary` wash) and
  draws its label from `SelectionAccentColor = Primary` — dark-green text on a see-through
  dark-green tint over dark slate, i.e. unreadable. For the chalkboard only, recolor the selected
  row to a FULLY-OPAQUE `Primary` fill with an `OnPrimary` (chalk-white) label — the same
  fill/content pairing buttons already use. Every other surface keeps the theme default.
- **Restore the chalkboard's `SideColFrac` to `0.078`** (from the `0.073` it was nudged to),
  widening the right-hand nav column slightly. Pairs with the `refine-nav-button-placement` nav
  centering already in place.

### Cosmetic refinements (third bundle entry)

- **Recolor the chalkboard `Secondary` to fix the amber pinned-row wash.** The pinned-row wash is
  derived from `Secondary` (`PinnedTint`, ×1.35 saturation @ 0.55 alpha), but the chalkboard's
  `Secondary` was an unrelated stained-wood brown, so the wash read as a discordant muddy amber over the
  dark slate. Replace it with a muted SAGE GREEN — a desaturated, lighter sibling of the green `Primary`
  — following the Notebook/Lectern (Light theme) pattern where `Secondary` is a sibling of that theme's
  `Primary`. The wash then reads as a soft green glow, harmonious with the accent and legible over slate.
- **Unify the two input fields' caret + focused-border colors (surfaced on the chalkboard, fixed
  globally).** `ScribeMultilineField` (hand-drawn) and `ScribeNumericField` (a wrapper over LibGUI's stock
  `TextField`) diverged on the caret (multiline used `Primary`; the stock field hardwires a light caret we
  can't theme) and on the focused border (multiline used `Primary`/its chalk seam; the stock field fell
  back to LibGUI's unset gold `FocusOutlineColor`). Point the multiline caret at the text color
  (`OnSurface` — the conventional "caret is ink") and set `FocusOutlineColor` per theme (Light→`Primary`,
  Chalkboard→chalk-white, clay→`Primary`) so both field types read the same on every theme; the latter
  also drops the stray gold focus ring from stock checkboxes/dropdowns on the dark/clay themes.

## Capabilities

### New Capabilities
<!-- None: reuses the existing scribe-document-policy machinery; no new capability introduced. -->

### Modified Capabilities
- `scribe-document-policy`: Add the chalkboard as a capped host — its block entity reports a
  policy of `MaxBlocks = 10` with pins uncapped (`MaxPins = null`), applied at the same
  mutation boundary the tablet uses. Documents that a capped host may cap tasks without capping
  pins.

## Impact

- `src/Mod/BlockEntityScribeWritingStation.cs` — add a `HostPolicy` virtual seam and its explicit
  `IScribeDocumentHost.Policy` delegate (base stays Unlimited; needed because the block-entity
  base uses explicit interface impls + the interface default member, so a bare subclass property
  wouldn't re-map it).
- `src/Mod/BlockEntityScribeChalkboard.cs` — override `HostPolicy` to return the 10-task,
  uncapped-pin policy.
- Possibly `src/Mod/assets/scribe/lang/en.json` — a chalkboard cap-reached notice string, only
  if the tablet's existing notice is tablet-specific rather than generic.
- `src/Mod/ScribeTheme.cs` — brighten the `ChalkAccent` (`Primary`) constant by +10 HSV Value.
- `src/Mod/ScribeDialogBase.Layout.cs` + `ScribeDialogBase.PinTab.cs` + `ScribePinnedContent.cs`
  — a `DecoratePolicyDropdownStyle` seam (default identity) threaded into the pin-tab picker; the
  chalkboard overrides it in `GuiDialogScribeChalkboard.cs` to recolor the selected menu row.
- `src/Mod/BlockEntityScribeChalkboard.cs` — `SideColFrac` `0.073f` → `0.078f`.
- `src/Mod/ScribeTheme.cs` — replace the `StainedWood` constant with a sage-green `ChalkSecondary` for the
  chalkboard `Secondary` role; add `FocusOutlineColor` to the Light theme (`Accent`), the Chalkboard theme
  (`ChalkboardInputFocusBorder`), and the `ClayPalette` factory (`accent`).
- `src/Mod/ScribeMultilineField.cs` — multiline caret `colors.Primary` → `colors.OnSurface` on both render
  paths (global input-field caret consistency).
- No Core changes to `ScribeDocumentPolicy` itself (the value type already supports
  `MaxBlocks` set with `MaxPins = null`); no new dependencies; no persistence/format change.
- Depends on `add-chalkboard-block` (introduces `BlockEntityScribeChalkboard`); apply/archive
  this change after it.
