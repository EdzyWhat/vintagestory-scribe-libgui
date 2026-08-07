## ADDED Requirements

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

## RENAMED Requirements

- FROM: `### Requirement: Each clay type is a discrete tablet item; fired appearance is a stack attribute`
- TO: `### Requirement: Each clay type and life-cycle state is a discrete tablet item`

## MODIFIED Requirements

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
