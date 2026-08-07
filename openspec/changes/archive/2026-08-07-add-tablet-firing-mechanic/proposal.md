## Why

`add-tablet-clay-type-backdrops` wired the `fired` appearance attribute and per-type fired backdrops, but
explicitly deferred the firing gameplay mechanic as a Non-Goal — so today **nothing ever sets `fired =
true`** and a soft clay tablet stays editable forever. That leaves the clay tablet with no life-cycle: it
never dries, never has a "commit" beat, and its fired art is unreachable.

This change gives the clay tablet a full **wet → hard → fired** life-cycle, matching how real clay behaves
and how Vintage Story already models clay pottery:

- **Wet** — freshly crafted, malleable, fully editable (the current soft behaviour).
- **Hard** — after drying out over ~2 in-game days, the clay is too stiff to scratch new marks into: it
  becomes **read-only**, but it is *not* permanent — dunk it in water (drop the item into water, or wade
  into water while holding it, like a lit torch going out) and it softens back to wet.
- **Fired** — bake it in a firepit and it becomes permanent fired pottery: read-only forever, no
  rehydration, the final ceramic appearance.

Firing is the natural "commit" beat (scratch notes into soft clay, then fire to preserve them permanently);
drying adds a gentle "use it before it sets" pressure with a forgiving water undo. This un-defers firing
*and* adds the intermediate hard state that was missing.

## What Changes

- **Drying (wet → hard).** A wet clay tablet carried or stored dries out over ~2 in-game days into a hard
  clay tablet of the same clay variant, via Vintage Story's native item `Harden` transition
  (`transitionablePropsByType`). The hard output keeps the document (tasks + notes + title). Because the
  native transition rebuilds a fixed output stack and does NOT copy input attributes (same gotcha as
  firepit smelting — confirmed by decompile), the tablet overrides `OnTransitionNow` to carry the document
  onto the hardened stack and mark it `hard = true`. Wax tablets and already-fired tablets do not harden.
- **Rehydration (hard → wet).** A hard clay tablet returns to wet — resetting the dry-out timer and keeping
  its document — when it is exposed to water two ways, mirroring how a lit torch is extinguished by water:
  (a) the item stack is dropped into a water block, or (b) the holding player enters water (swims/wades)
  while it is the active hotbar item. Fired tablets never rehydrate.
- **Firing (unfired → fired).** An unfired clay tablet (wet or hard) placed in a firepit smelts into a
  fired clay tablet of the same clay variant once it reaches the clay firing temperature, exactly like
  vanilla clay-pottery firing (`combustiblePropsByType.smeltedStack`). The output records `fired = true`
  and keeps the document (`DoSmelt` override, since firepit smelting also does not copy input attributes).
- **Read-only when hard or fired.** The tablet dialog opens read-only whenever the stack is not editable
  (`hard` OR `fired`): the document is readable but no task can be added/checked/pinned/reordered/edited and
  the title cannot be changed. A wet tablet keeps its existing always-edit behaviour. A hard tablet's
  read-only state is temporary (rehydrate to edit again); a fired tablet's is permanent.
- **Empty-state message.** A hard-or-fired tablet with no document content (e.g. one pulled straight from
  Creative) shows a small centered message explaining it is set/fired and has no writing, instead of an
  empty editable surface.
- **Distinct appearance per state.** The dialog backdrop is chosen by clay type AND state: wet (smoother /
  glossier), hard (lighter, more textured — dried-clay look), fired (final ceramic colour). The fired
  backdrops already exist from `add-tablet-clay-type-backdrops`; this change adds the hard-state tint and
  makes both hard and fired reachable so they can be tuned in play. (Art beyond interim tints is deferred.)

## Capabilities

### New Capabilities
- `tablet-clay-hardening`: a wet clay tablet drying into a read-only hard tablet over ~2 in-game days
  (carrying its document), and softening back to wet when exposed to water (drop-in-water or
  player-enters-water-while-holding), resetting the dry-out timer.
- `tablet-firing`: firing an unfired (wet or hard) clay tablet in a firepit into a permanently read-only
  fired clay tablet of the same clay variant, carrying its document through the transformation, plus the
  fired read-only nature and the blank-fired empty-state.

### Modified Capabilities
- `clay-wax-tablet-item`: gains a `hard` appearance attribute and native `Harden` transition props
  (alongside the now-reachable `fired`); declares combustible/smelt props; overrides both `OnTransitionNow`
  (drying) and `DoSmelt` (firing) to carry the document; and gains a water-exposure rehydration hook.
- `tablet-dialog`: the dialog gains a read-only mode selected by whether the stack is editable (`hard` OR
  `fired`), with no editor entry / no add-check-pin, plus the centered empty-state, and picks its backdrop
  by clay type AND state (wet / hard / fired).
- `scribe-document-policy`: a read-only (uneditable) policy applies to a hard or fired tablet, distinct
  from the wet tablet's 10-task / 1-pin editable cap.

## Impact

- **Code:** `ItemScribeTablet` (override `OnTransitionNow` for drying + `DoSmelt` for firing to carry the
  document and set `hard`/`fired`; add `ReadHard` mirroring `ReadFired`; add a water-exposure rehydration
  hook and guards so wax/fired don't harden and fired doesn't rehydrate), `scribetablet.json`
  (`transitionablePropsByType` Harden + `combustiblePropsByType` per clay variant), `GuiDialogScribeTablet`
  (read-only mode + empty-state + state-keyed backdrop), `ScribeBackdrop.ForTablet` (add the hard-state
  tint), `TabletHost`/`ScribeDocumentPolicy` (an uneditable policy for hard-or-fired), and lang keys for
  the empty-state text.
- **Assets:** no new textures required — hard and fired reuse the interim tinted-soft art (a distinct hard
  tint + the existing fired tints); dried/ceramic art is deferred like the fired art was.
- **Persistence:** no new packet — document, `hard`, and `fired` all ride the existing stack attributes;
  the native transition state (`transitionstate` tree attr) persists automatically; clay type is the item
  variant.
- **Sequencing:** builds on `add-tablet-clay-type-backdrops` (the variant axis, `fired` attribute, fired
  backdrops). Modifies `clay-wax-tablet-item` / `tablet-dialog` / `scribe-document-policy`, last touched by
  that change — author/apply against its current spec text and mind the archive-order drift trap.
- **No `src/Core/` change**, no new mod dependency.
