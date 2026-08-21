## Context

The task cap is already a solved, generic mechanism in `ScribeDialogBase`. The dialog consults
`CanAddTaskUnderPolicy()` → `host.Policy.CanAdd(doc.TaskCount)` at every add boundary (editor
add, handbook "Add to Scribe", craft/tracker/link add), disables the "Add Task" affordance via
`addTaskEnabled: CanAddTaskUnderPolicy()`, and on refusal calls `NotifyTabletFull()` which
raises the standard in-game error with the `scribe:tablet-full` string. The wax tablet opts into
all of this by overriding one member: `TabletHost.Policy` returns a capped `ScribeDocumentPolicy`.

The chalkboard host (`BlockEntityScribeChalkboard`, added in `add-chalkboard-block`) currently
inherits `IScribeDocumentHost.Policy => ScribeDocumentPolicy.Unlimited`, so it is uncapped. To
cap it, it needs to override `Policy` — the same one-member opt-in the tablet uses. Everything
downstream is inherited for free.

Two decisions differ from the tablet: (1) the pin cap, and (2) the cap-reached notice wording,
which is currently hardcoded to a tablet-specific string.

## Goals / Non-Goals

**Goals:**
- Cap the chalkboard at 10 task blocks, reusing the tablet's exact enforcement path.
- Leave pins uncapped (chalkboard is shared; pins are per-player).
- Surface a correctly-worded cap-reached notice (not "A tablet holds…").

**Non-Goals:**
- No change to `ScribeDocumentPolicy`'s Core type (it already supports `MaxBlocks` set with
  `MaxPins = null`).
- No new task kinds, persistence, or sync. No change to how notes/text (uncapped) behave.
- No new dependency; no Core edits.

## Decisions

**Decision: Cap via a `HostPolicy` seam on the block-entity base, not the `Tablet` preset.**
Unlike the item hosts (`NotebookHost`/`TabletHost`, plain classes with a `public virtual
Policy`), the block-entity base `BlockEntityScribeWritingStation` uses EXPLICIT interface
implementations and never provides `Policy` — it relies on the `IScribeDocumentHost.Policy`
DEFAULT interface member. A bare `Policy` property on a subclass would therefore NOT re-map the
interface member (the exact hazard `NotebookHost.Policy`'s doc-comment warns about). So the base
gains a `protected virtual ScribeDocumentPolicy HostPolicy => Unlimited;` seam plus an explicit
`ScribeDocumentPolicy IScribeDocumentHost.Policy => HostPolicy;`, and the chalkboard overrides
`HostPolicy => new() { MaxBlocks = 10 }` (MaxPins `null`, ReadOnly false). The chalkboard is a
shared placed block whose pins are per-player, so it deliberately does NOT reuse
`ScribeDocumentPolicy.Tablet` (which also caps `MaxPins = 1`). A fresh policy value with only
`MaxBlocks` set expresses "cap tasks, not pins."
- *Why not add a `Chalkboard` preset to Core*: a single call site with one field set doesn't
  warrant a named preset; presets earn their keep when reused. If a second cap-tasks-only host
  appears, promote it then. (Noted, not built.)
- *`MaxBlocks` counts task blocks only* — this is the existing `ScribeDocument.TaskCount`
  semantics `CanAdd` already uses, so notes/text stay uncapped with no extra work.

**Decision: Make the cap-reached notice key a seam; override it on the chalkboard dialog.**
`NotifyTabletFull()` currently hardcodes `Lang.Get("scribe:tablet-full")` ("A tablet holds at
most 10 tasks."), which is wrong wording on a chalkboard. Introduce a virtual seam on
`ScribeDialogBase` — e.g. `protected virtual string TaskCapReachedLangKey =>
"scribe:tablet-full";` — and have `NotifyTabletFull()` read it. `GuiDialogScribeChalkboard`
overrides it to `"scribe:chalkboard-full"`; add that lang string ("A chalkboard holds at most 10
tasks." or similar). The tablet path is byte-identical (default key unchanged).
- *Why a seam over reusing `tablet-full`*: the string names the object; showing "tablet" on a
  chalkboard is a visible bug. A seam keeps the enforcement generic while letting each surface
  word its own notice — the same override-seam pattern the theme/nav work uses.
- *Alternative considered*: generalize the string to "This holds at most 10 tasks." Rejected —
  object-named copy reads better and the mod already has per-surface strings elsewhere.

**Decision: Brighten `Primary` at the source constant, not via a per-widget seam.**
The checkbox tick (and caret, button fill, selection tint) all cascade from `Primary`, so the
"too dark to read" symptom is the accent itself being dark, not one widget mis-mapping it. Fix it
once at `ScribeTheme.ChalkAccent`: lift HSV Value by +10 points (0.42→0.52) by scaling all three
channels by `0.52/0.42`, which holds hue and saturation constant so it's the *same* green, just
brighter. No new seam — every role that reads `Primary` brightens in lockstep, which is the intent.
- *Why not recolor only the checkbox*: the darkness affected the caret and selection too; a single
  accent lift is the smaller, more honest change.

**Decision: Fix the picker's open-menu legibility with a `DecoratePolicyDropdownStyle` seam,
chalkboard-only.** LibGUI's `DropdownItemTile` paints the SELECTED row's fill from
`DropdownStyle.SelectionColor` (defaulting to `ColorScheme.StateSelected`, a translucent `Primary`
wash) and its label from `SelectionAccentColor` (defaulting to `Primary`) — verified in the
decompiled `Gui.dll` (`color = IsSelected ? SelectionColor : …; color2 = IsSelected ?
SelectionAccentColor : TextStyle.Color`). On the dark board that's dark-green text on a see-through
dark-green tint: unreadable. Add `private protected virtual DropdownStyle
DecoratePolicyDropdownStyle(DropdownStyle)` on `ScribeDialogBase` (default identity), thread it
through `ScribePinnedContent` (a nullable `Func<DropdownStyle,DropdownStyle>`, applied after the
font tweak), and override it on the chalkboard to set a fully-opaque `Primary` fill + `OnPrimary`
label — the exact fill/content pairing buttons already use. Gated on `PixelArtDisplay` like the
other chalkboard theme seams, so the global-theme menu is untouched.
- *Why not change `StateSelected`/`Primary` in the theme*: those roles are shared by many widgets
  (hover/select washes across every list and row); making the dropdown selection opaque there would
  turn every selection tint into a solid block. The seam scopes the fix to the picker.
- *Why brightening `Primary` doesn't already fix it*: brightening lifts the green, but selected
  text stays *green-on-green* — the label must move OFF the accent hue (to `OnPrimary`) and the
  fill must go opaque, independent of how bright the accent is.

**Decision: `SideColFrac` back to `0.078`.** A tuning revert (from `0.073`), widening the nav
column a hair; pairs with the `refine-nav-button-placement` centering. One-line constant change in
`BlockEntityScribeChalkboard.LayoutProportions`.

**Decision: Recolor the chalkboard `Secondary` to a sage-green sibling of `Primary`, not a new tint
constant.** `ScribeRowConstants.PinnedTint` derives the pinned-row wash from `Secondary` (×1.35 saturation
@ 0.55 alpha) — a deliberate choice so the wash stays distinct from the `Primary` focus cue. The chalkboard
had set `Secondary` to an unrelated stained-wood brown, so the wash read as a discordant amber over the dark
slate. Fix it at the source role: make `Secondary` a muted, lighter SAGE GREEN `(0.36, 0.52, 0.40)` — a
desaturated sibling of the green `Primary`. This mirrors how the Light theme (Notebook/Lectern) authors its
`Secondary` (`#A07F4D`) as a lighter/desaturated sibling of its gold `Primary` (`#955F21`): same hue family,
differentiated by value + saturation, not by an unrelated hue.
- *Why keep sourcing the wash from `Secondary` (not re-point it at `Primary`)*: the Secondary-sourced wash is
  an intentional, cross-theme design (documented on `PinnedTint`). The bug was the chalkboard's *choice of
  Secondary color*, not the mechanism — so the smaller, consistent fix is to recolor the role.
- *Sage green, not a fully neutral gray-green*: it must stay clearly in the accent's hue family so the pinned
  wash reads as "the chalkboard's color," harmonious with the green buttons. Exact value to be tuned in-game.

**Decision: Unify the two input fields' caret + focused-border colors globally, via the theme (not a
chalkboard-only seam).** The caret and focused border were the last two roles where `ScribeMultilineField`
(a hand-drawn render object) and `ScribeNumericField` (a wrapper over LibGUI's stock `TextField`) diverged;
background/text/resting-border were reconciled earlier. The divergence isn't chalkboard-specific — it exists
on every theme — so fix it at the shared layer:
- *Caret* → set the multiline caret to `colors.OnSurface` (the field's text color) on both render paths. The
  stock `TextField` **hardwires** its caret to the light content tone (verified in the decompiled `Gui.dll`
  `TextFieldRenderWidget`, which draws the caret with a fixed color and never reads a style value), so it
  can't be themed without forking `gui`. Matching the multiline to the *text color* is the conventional
  editor behavior AND makes the two fields agree on the dark chalkboard (where `OnSurface` ≈ the stock light
  caret). The accent-green multiline caret it replaces was the mismatch the user flagged.
- *Focused border* → set `FocusOutlineColor` on each theme (Light→`Primary`/`Accent`, Chalkboard→
  `ChalkboardInputFocusBorder`, clay→`accent`). It was left unset, so the stock `TextField` (and stock
  checkboxes/dropdowns/radios) fell back to LibGUI's bright gold default `(0.95, 0.78, 0.38)` — a gold focus
  ring on every theme, diverging from the multiline's `Primary`/chalk focus border. Pointing it at each
  theme's intended focus color reconciles both field types and removes the stray gold ring elsewhere.
- *Residual*: stock mouse-focus lerps only 35% from `Border` toward `FocusOutlineColor` (vs the multiline's
  full jump), so a mouse-focused numeric border is the same hue but a touch softer than the multiline's.
  Accepted — matching it exactly would require forking the stock focus resolution.
- *Why global, not a chalkboard seam*: the user asked for the two field types to "compose more similarly …
  use the same colors from the theme." That's a cross-theme concern; a chalkboard-only seam would leave the
  same (subtler) mismatch on Lectern/clay. Folded into this change per the packaging decision, documented
  here as global.

## Risks / Trade-offs

- [Ordering: `BlockEntityScribeChalkboard` only exists once `add-chalkboard-block` is applied] →
  This change depends on it; apply/archive `add-chalkboard-block` first. The spec delta targets
  `scribe-document-policy` (an already-archived capability), so it validates independently.
- [Existing chalkboards with >10 tasks] → The cap is enforced only at the ADD boundary (never
  inside `ScribeDocument`), so a document that somehow already holds >10 tasks is not truncated
  or corrupted — it simply refuses further adds until it drops below the cap. This matches the
  tablet's behavior and the "policy applied at the mutation boundary" requirement.
- [`NotifyTabletFull` name now slightly misleading] → It stays (renaming ripples across call
  sites for no behavior gain); the seam it reads is the meaningful part. Optionally note in its
  doc-comment that the key is now overridable.

## Open Questions

- Exact chalkboard cap-reached copy — placeholder "A chalkboard holds at most 10 tasks.";
  confirm wording at implementation.
- This is the first entry in an ongoing chalkboard-refinements bundle; later refinements will be
  appended to this change's proposal/specs/tasks before it is applied and archived.
