# clay-wax-tablet-item Specification

## Purpose
TBD - created by archiving change add-tablet-items-and-crafting. Update Purpose after archive.
## Requirements
### Requirement: One tablet item class exposes all material variants

The system SHALL provide a single `ItemScribeTablet` class exposed through an item type whose
`material` variant axis yields every tablet item (the clay variants across color × life-cycle state,
plus the wax tablet — see "Each clay type and life-cycle state is a discrete tablet item" for the full
axis). Every variant SHALL have `MaxStackSize = 1` and SHALL appear in the Creative-mode inventory.

#### Scenario: Tablet variants exist in Creative

- **WHEN** a player in Creative mode browses the Scribe items
- **THEN** the tablet variants are present, each a variant of the same `ItemScribeTablet` item type

#### Scenario: Tablets are not stackable

- **WHEN** a player attempts to stack two tablet items of the same variant
- **THEN** the stack size remains 1

### Requirement: Tablets are crafted by simple grid recipes

Each tablet variant SHALL be obtainable through a simple crafting-grid recipe (NOT clayforming):
the clay tablet from a clay + sticks style recipe and the wax tablet from a beeswax + frame style
recipe. Crafting a tablet SHALL, server-side, record a `Crafted` history entry on the resulting
item stack in the same manner as the Notebook.

#### Scenario: Clay tablet crafted on the grid

- **WHEN** a player places the clay tablet recipe ingredients in the crafting grid and takes the
  output
- **THEN** a clay tablet item is produced and its stack carries a `Crafted` history entry naming
  the crafter

#### Scenario: Wax tablet crafted on the grid

- **WHEN** a player places the wax tablet recipe ingredients in the crafting grid and takes the
  output
- **THEN** a wax tablet item is produced and its stack carries a `Crafted` history entry naming
  the crafter

### Requirement: Tablet document persists on the ItemStack

A tablet's document SHALL be stored on the `ItemStack`'s attributes under the key `"scribeDocument"`
by pure reuse of `ScribeDocumentAttributes` — the same codec and attribute key the Notebook and
Lectern use. No new persistence code or network packet SHALL be introduced; the existing
`ScribeNotebookSaveMessage` (and its frozen registration order) is reused for server write-through.
A fresh tablet with no prior document SHALL open with an empty document carrying a fresh `DocId`.

#### Scenario: Fresh tablet starts empty with a fresh DocId

- **WHEN** a player obtains a new tablet that carries no document attribute
- **THEN** opening it shows an empty document with a freshly generated `DocId`

#### Scenario: Document and title survive close and reopen

- **WHEN** a player writes tasks and a title into a tablet, closes the dialog, and reopens the same
  tablet
- **THEN** the same tasks and title are shown

#### Scenario: Document survives drop and pickup

- **WHEN** a player drops a tablet carrying a document and then picks the same item back up
- **THEN** the document (with its `DocId` and task ids) is unchanged, because the bytes ride on the
  ItemStack attributes

### Requirement: Tablet held tooltip shows the document title

The tablet SHALL append the stored document's title (quoted, or an untitled placeholder) to its
held/inventory tooltip, reading the document via `ScribeDocumentAttributes.TryReadFrom`. A tablet
that has never been opened carries no document attribute and SHALL show the placeholder.

#### Scenario: Titled tablet shows its title in the tooltip

- **WHEN** a player hovers a tablet whose document has a non-default title
- **THEN** the tooltip includes that title

#### Scenario: Never-opened tablet shows the placeholder

- **WHEN** a player hovers a tablet that has never been opened
- **THEN** the tooltip shows the untitled placeholder line rather than erroring

### Requirement: Right-click opens the Scribe document dialog

Right-clicking (or using the interaction key) while holding a tablet SHALL open the Scribe document
editing dialog for the document stored in that specific tablet stack. The dialog opened SHALL be the
bespoke `GuiDialogScribeTablet` (see `tablet-dialog`), constructed with a `TabletHost` for that stack.

The tablet's held-interaction modifier map SHALL be:

- **Right-click** (no modifier): open the tablet dialog.
- **Shift+Right-Click aimed at a water block**: quench/soften a hard tablet (unchanged; the water-aim
  branch is the discriminator).
- **Shift+Right-Click NOT aimed at water**: perform quick-add (see `quick-add-interaction`) — open the
  dialog with a new empty task at the top and the caret focused.
- **Ctrl+Shift+Right-Click**: pass through to the base collectible behaviors (including GroundStorable)
  for ground placement, following the vanilla spear placement convention.

Ground placement SHALL NO LONGER trigger on plain Shift+Right-Click; it SHALL require the
Ctrl+Shift+Right-Click modifier combination.

#### Scenario: Right-click opens the document

- **WHEN** a player right-clicks while holding a tablet
- **THEN** the Scribe document editing dialog opens showing that tablet's document

#### Scenario: The bespoke tablet dialog is opened

- **WHEN** a tablet is opened
- **THEN** the dialog shown is `GuiDialogScribeTablet` (the always-edit, no-tabs tablet dialog), not
  the interim `GuiDialogScribeNotebook` used before Proposal C

#### Scenario: Shift+right-click on water quenches

- **WHEN** a player Shift+Right-Clicks while holding a hard tablet aimed at a water block
- **THEN** the tablet softens/quenches and the dialog does not open (unchanged behavior)

#### Scenario: Shift+right-click off water quick-adds

- **WHEN** a player Shift+Right-Clicks while holding a tablet and is NOT aiming at a water block
- **THEN** the tablet dialog opens with a new empty task at the top and the caret focused, and ground
  placement does not occur

#### Scenario: Ctrl+Shift+right-click stores on the ground

- **WHEN** a player Ctrl+Shift+Right-Clicks while holding a tablet
- **THEN** the base ground-storage behavior runs and the dialog does not open

### Requirement: Tablet dialog closes on switch-away but survives in-place re-sync

The tablet dialog SHALL close when the player switches their active hand away from the tablet whose
document it is showing, using the same document-identity rule as the Notebook dialog: on a real
active-hand change, the dialog closes unless the newly active hand item hosts the SAME document
(compared by the stable `DocId`).

On an in-place slot modification of the CURRENTLY held tablet (the active hotbar slot's contents are
rewritten by a server re-sync, e.g. the one-time "Picked up" history write), the dialog SHALL NOT
close solely because the re-synced stack's `DocId` no longer matches the open document. On this path
the dialog SHALL close ONLY if the active hand no longer holds a Scribe document item at all. This
prevents the first-open flicker on a freshly obtained tablet.

The tablet's legitimate in-place material-state transition (wet → hard → fired), which also arrives
via slot modification, SHALL continue to be handled correctly and SHALL NOT be broken by the
flicker fix.

#### Scenario: Dialog stays open on first open of a picked-up tablet

- **WHEN** a player opens, for the first time, a tablet they picked up (did not craft), triggering
  the server's one-time "Picked up" history write and an in-place re-sync of the held stack
- **THEN** the tablet dialog stays open (no flicker) and shows the document, without requiring a
  second right-click

#### Scenario: Dialog closes when switching to a different Scribe item

- **WHEN** a player switches the active hotbar slot to a DIFFERENT Scribe document item while the
  tablet dialog is open
- **THEN** the tablet dialog closes

#### Scenario: In-place wet-to-hard transition is preserved

- **WHEN** a held wet tablet transitions to hard (or hard to fired) in place while its dialog is open
- **THEN** the tablet's state transition is handled as before and is not regressed by the
  flicker-close fix

### Requirement: TabletHost adapts the tablet stack to the dialog

The system SHALL provide a `TabletHost` implementing `IScribeDocumentHost` as a thin variant of
`NotebookHost`. It SHALL report a layout with aspect ratio `1160/1024`, a `DefaultDocumentTitle` of
`"Tablet"`, and SHALL enforce the tablet document policy (see `scribe-document-policy`) at the
mutation boundary. It SHALL reuse the Notebook's server write-through and `Flush()` flow (writing
the document back to the ItemStack and syncing via the existing save packet).

#### Scenario: Host reports tablet layout and title

- **WHEN** the dialog requests the layout and default title from a `TabletHost`
- **THEN** the layout aspect ratio is `1160/1024` and the default title is `"Tablet"`

#### Scenario: Host enforces the policy at the mutation boundary

- **WHEN** an edit would exceed the tablet policy's caps
- **THEN** `TabletHost` refuses the mutation at the boundary, leaving the uncapped `ScribeDocument`
  model unchanged in its own contract

### Requirement: Each clay type and life-cycle state is a discrete tablet item

The clay tablet SHALL exist as discrete registered items across two axes expressed in the tablet's
`material` variant list: three clay types (red, blue, fire) × three life-cycle states (soft, hard, fired),
plus the `wax` state. The soft state keeps the bare clay-type codes (`clay-red`, `clay-blue`, `clay-fire`)
so already-placed items need no migration; the hard and fired states add `-hard` and `-fired` siblings
(`clay-red-hard`, `clay-red-fired`, …). Clay type AND life-cycle state SHALL therefore both be the item's
own variant, NOT stack attributes. Each SOFT clay item and the wax item SHALL have its own handbook entry
and crafting recipe; the `-hard` and `-fired` variants SHALL appear in the handbook and Creative inventory
(so all three states are discoverable, matching the raw→cooked-meat precedent) but SHALL NOT add new
crafting recipes — they are reached only through hardening and firing. When a tablet's `material` variant is
unrecognized, consumers SHALL treat it as red + soft.

#### Scenario: The three clay types are discrete craftable soft items

- **WHEN** a player browses the handbook or creative inventory
- **THEN** a Red Clay Tablet, a Blue Clay Tablet, and a Fire Clay Tablet appear as separate craftable
  entries, each with its own recipe, alongside the Wax Tablet

#### Scenario: Hard and fired states are discoverable variants

- **WHEN** a player browses the handbook or creative inventory
- **THEN** each clay color's hard and fired tablet also appears as its own entry (no separate recipe), so
  all three life-cycle states are visible

#### Scenario: Clay type and state are fixed by the item, not a stack attribute

- **WHEN** a blue clay tablet is fired
- **THEN** the resulting item is the `clay-blue-fired` variant (its clay type and fired state are the item
  itself, requiring no stack attribute to record either)

#### Scenario: An unrecognized variant defaults to red + soft

- **WHEN** a tablet stack has an unrecognized `material` variant
- **THEN** consumers treat it as red + soft

### Requirement: The clay tablet item declares firepit combustible/smelt properties

Each UNFIRED clay tablet variant — soft (`clay-<color>`) and hard (`clay-<color>-hard`) — SHALL declare
combustible properties enabling firepit firing (a smelt stack producing the `-fired` variant of the SAME
clay color, at the clay firing temperature, needing no container, one-to-one) with `smeltingType` `cook` so
the tablet fires in a firepit and, because a pit kiln only forms over `Fire`-type items, cannot be pit-kiln
fired. The item SHALL override its smelt behavior so the fired output carries the source tablet's document.
Fired state is the output's variant, not a stack attribute. The wax tablet and the already-`-fired` variants
SHALL NOT declare firing combustible properties.

#### Scenario: Unfired clay tablets are firepit-combustible, wax and fired are not

- **WHEN** the tablet item's combustible properties are queried
- **THEN** each soft and hard clay tablet variant reports a firepit (`cook`) smelt into the `-fired` variant
  of the same clay color, and a wax tablet or an already-`-fired` tablet reports no such firing

#### Scenario: The smelt output preserves stack data

- **WHEN** the clay tablet's smelt behavior produces the fired output
- **THEN** that output is the `-fired` variant of the same clay color and carries the source tablet's
  document bytes

### Requirement: The clay tablet item declares a Harden transition and carries data through it

Each SOFT clay tablet variant SHALL declare a native `Harden` transition (via `transitionablePropsByType`)
whose transitioned stack is the `-hard` variant of the SAME clay color and whose fresh-hours duration is
approximately two in-game days, and the item SHALL override its transition behaviour so the hardened output
carries the source tablet's document. Hard state is the output's variant, not a stack attribute. The wax
tablet SHALL NOT declare a hardening transition, and the `-hard` and `-fired` variants SHALL NOT declare one
(a tablet hardens exactly once, and a fired tablet never hardens).

#### Scenario: Soft clay tablets declare hardening; hard, fired, and wax do not

- **WHEN** the tablet item's transitionable properties are queried
- **THEN** each soft clay tablet variant reports a `Harden` transition into the `-hard` variant of the same
  clay color, and the hard, fired, and wax tablets report no hardening transition

#### Scenario: The hardened output preserves stack data

- **WHEN** the clay tablet's transition behaviour produces the hard output
- **THEN** that output is the `-hard` variant of the same clay color and carries the source tablet's
  document bytes

### Requirement: The clay tablet exposes editable state and a rehydration hook

The clay tablet item SHALL expose whether a stack is editable (editable ⇔ its `material` variant is a SOFT
clay state — neither a `-hard` nor a `-fired` variant, and not wax-as-non-writing if applicable), derived
from the variant code rather than from stack attributes. It SHALL provide a rehydration behaviour that, on
water exposure (item dropped into water, or held while its holder enters water), converts a `-hard` clay
tablet stack back to the SOFT variant of the same clay color — swapping the variant, resetting the hardening
timer, and preserving the document — while leaving a `-fired` tablet unchanged.

#### Scenario: Editable reflects the variant state

- **WHEN** a tablet stack's editable state is queried
- **THEN** it is editable only when its `material` variant is a soft clay state (not `-hard`, not `-fired`)

#### Scenario: Rehydration softens a hard tablet, not a fired one

- **WHEN** the rehydration behaviour is applied to a `-hard` clay tablet stack
- **THEN** the stack becomes the soft variant of the same clay color with the hardening timer reset and the
  document preserved; a `-fired` tablet stack is left unchanged

### Requirement: Clay tablets render a custom model with per-color, per-state textures

Each clay tablet variant SHALL render the custom `scribe:item/tablet-clay` shape (replacing the shared
placeholder `game:block/clutter/tablet-clay1` clutter mesh), textured per variant by its clay color AND
life-cycle state: red/blue/fire × soft/hard/fired maps to the nine authored body textures
(`scribe:items/{r,b,f}{s,h,f}`), with the shared `scribe:items/writing` overlay for the engraved text. The
wax tablet SHALL retain the placeholder shape and texture until its own art lands. Texture selection SHALL
be by variant code (`texturesByType`), not by stack attribute, so each held/inventoried tablet shows its
own state's art.

#### Scenario: A hard blue tablet shows the hard blue texture

- **WHEN** a player holds or views in inventory a `clay-blue-hard` tablet
- **THEN** it renders the `scribe:item/tablet-clay` model textured with the `bh` (blue-hard) body texture,
  distinct from the `bs` (soft) and `bf` (fired) blue textures

#### Scenario: Wax keeps the placeholder

- **WHEN** a wax tablet is rendered
- **THEN** it still uses the placeholder shape/texture, unaffected by the clay art wiring

### Requirement: The handbook entry describes the life-cycle and reflects the tablet's state

The tablet handbook entry SHALL explain the wet → hard → fired life-cycle and how a player moves between
states: a wet tablet is editable; a hardened tablet is locked but can be softened back to wet by water OR
fired to become permanent; a fired tablet is permanent and water cannot soften it. The soft/wax entries
SHALL carry a life-cycle overview section; the `-hard` and `-fired` variants SHALL carry a state-specific
section (via `attributesByType`) telling the player what that state means and how to leave it (hard → dunk
in water to edit, or fire to make permanent; fired → permanent, cannot be softened). Each `attributesByType`
handbook entry SHALL be self-contained (it replaces the base `attributes` value on match), so it SHALL also
carry the shared `groundStorageTransform`.

Each color×state clay variant is its own registered item, so the handbook SHALL show a discrete page per
variant (nine clay pages), while the three state-specific text bodies (soft-overview, hard, fired) are SHARED
across all colors of that state — the same three lang strings back all nine pages. Because the text is shared
across colors, the three bodies SHALL be color-agnostic and SHALL NOT hardcode a single color's cross-link;
inter-state navigation is left to the handbook's auto-generated color-correct sections ("Processes into" from
the Harden transition, the smelt section from the fired smelt stack, and "Created by" on the fired page).

#### Scenario: Each clay color and state has its own page but shares state text

- **WHEN** a player browses the handbook for the clay tablet variants
- **THEN** each of the nine color×state combinations shows its own page, and the hard pages (red/blue/fire)
  all render the same hard body text, the fired pages all render the same fired body text, and the soft
  pages all render the same life-cycle overview — three unique bodies across nine pages, none naming a
  specific clay color

#### Scenario: A hardened tablet's handbook explains both exits

- **WHEN** a player opens the handbook entry for a `-hard` clay tablet
- **THEN** the entry states the tablet is locked and describes both softening it in water (to edit again)
  and firing it (to make its writing permanent)

#### Scenario: A fired tablet's handbook explains permanence

- **WHEN** a player opens the handbook entry for a `-fired` clay tablet
- **THEN** the entry states the writing is permanent and that water will not soften it

#### Scenario: The soft tablet's handbook previews the life-cycle

- **WHEN** a player opens the handbook entry for a wet clay tablet
- **THEN** the entry includes an overview of the wet → hard → fired progression and how to move between
  states

