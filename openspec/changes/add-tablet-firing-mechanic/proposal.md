## Why

`add-tablet-clay-type-backdrops` wired the `fired` appearance attribute and a per-type fired-ceramic
backdrop tint, but explicitly deferred the firing gameplay mechanic as a Non-Goal — so today **nothing
ever sets `fired = true`**. The fired backdrops are unreachable in normal play and the tint values ship
un-eyeballed. This change un-defers firing: a soft clay tablet becomes a fired clay tablet by baking it
in a firepit, the same intuitive path players already use to fire clay pottery and cook food. Firing a
tablet also gives the tablet tier a natural "commit" beat — scratch notes into soft clay, then fire it to
preserve them permanently — which fits the tool's fiction (baked clay can no longer be edited).

## What Changes

- **Firepit firing.** A soft clay tablet placed in a firepit smelts into a fired clay tablet once it
  reaches the clay melting point, exactly like vanilla clay-pottery firing (declared via the tablet's
  `combustibleProps.smeltedStack`). The output records `fired = true`.
- **Task data carries through the fire.** The fired tablet keeps the document (tasks + notes + title) and
  the `clayType` from the soft tablet it was made from — mirroring how a Notebook's data carries into a
  Clockmaker's Notebook. Vanilla firepit smelting clones a fixed output stack and does NOT copy the
  input's stack attributes, so this requires overriding the tablet's smelt hook to copy the document +
  clayType onto the fired output.
- **A fired tablet is read-only.** Its dialog opens in a view-only mode: existing tasks/notes/title are
  readable but not editable, no task can be added/checked/pinned, and the always-edit affordance the soft
  tablet has is gone (the clay is baked).
- **A fired tablet with no prior data opens blank + uneditable**, e.g. one pulled straight from Creative
  Inventory. Rather than an empty editable surface, its dialog shows a small centered message that it was
  fired without any tasks.

## Capabilities

### New Capabilities
- `tablet-firing`: firing a soft clay tablet in a firepit into a fired clay tablet, carrying its document
  and clay type through the transformation, and the read-only nature of a fired tablet (including the
  blank-fired empty-state message).

### Modified Capabilities
- `clay-wax-tablet-item`: `fired` becomes reachable in real play (set true on the firepit output), where
  before it was an appearance record nothing set. The item declares combustible/smelt properties.
- `tablet-dialog`: the dialog gains a read-only mode selected by the stack's `fired` state — no editor
  entry, no add/check/pin, plus the centered "fired without tasks" empty-state.
- `scribe-document-policy`: a fired tablet is immutable (a read-only document policy), distinct from the
  soft tablet's 10-task / 1-pin editable cap.

## Impact

- **Code:** `ItemScribeTablet` (override the smelt hook to copy document + clayType + set `fired`; declare
  combustible props if not JSON-only), `scribetablet.json` (`combustibleProps` with `smeltedStack`),
  `GuiDialogScribeTablet` (read-only mode + empty-state message), `TabletHost`/`ScribeDocumentPolicy` (an
  immutable policy), and a lang key for the empty-state text.
- **Assets:** no new textures — the fired backdrops + tints already exist from
  `add-tablet-clay-type-backdrops`; firing simply makes them reachable.
- **Persistence:** no new packet — document + clayType + fired ride the existing stack attributes.
- **Sequencing:** builds on `add-tablet-clay-type-backdrops` (the `fired` attribute + fired backdrops).
  Modifies requirements that change last touched; apply/author against its current spec text and mind the
  archive-order drift trap.
- **No `src/Core/` change**, no new mod dependency.
