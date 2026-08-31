## MODIFIED Requirements

### Requirement: Inbox-capable blocks show an ambient particle when the player has an unseen assignment
Every Inbox-capable block (Assignment Desk, standalone Inbox, and any Lectern, Scriptorium, or
Chalkboard exposing the Inbox nav button) SHALL emit an ambient particle effect, visible only to
the local client, when the viewing player has at least one New (unseen) assignment and is within
**12 blocks** of that block. The check SHALL be client-side and player-local; it SHALL NOT reveal
another player's unseen assignments. The particle field SHALL spawn from the block's vertical
midpoint (not from above its top face), SHALL rise to roughly two-thirds of the vertical distance
it covered before this requirement's range/position values changed while its total particle
lifetime is unchanged (particles travel more slowly, not for a shorter time), and SHALL spawn at a
reduced rate — 0.6× the per-tick particle count used before this requirement changed.

#### Scenario: A nearby block particles when the player has a new assignment
- **WHEN** a player with an unseen assignment walks within 12 blocks of any Inbox-capable block
- **THEN** that block emits the ambient particle effect for that player's client only, spawning
  from around the block's vertical midpoint

#### Scenario: No particle once every assignment is seen or resolved
- **WHEN** the player has no assignment in the New (unseen) state
- **THEN** no Inbox-capable block emits the particle effect for that player

#### Scenario: A player outside 12 blocks sees no particle
- **WHEN** a player with an unseen assignment is farther than 12 blocks from every Inbox-capable
  block
- **THEN** no particle is emitted for them until they come within that range

## ADDED Requirements

### Requirement: Each assignment state's chip renders in a distinct, named color
Each of the six assignment states SHALL render its filter chip and its row's compact state chip in
a color distinct from every other state's, except Declined and Discarded, which SHALL share one
color (both read as terminal rejections). The mapping SHALL be: New → Deep Indigo, Accepted → Rich
Plum/Amethyst, Declined and Discarded → Crimson/Burgundy, Cancelled → Charcoal/Dark Sepia,
Completed → Verdigris/Emerald Ink. The filter-chip row and each row's own state chip SHALL use the
same color for a given state (they SHALL NOT disagree), consistent with the existing requirement
that ties both to one shared lookup.

#### Scenario: Five distinct colors across six states
- **WHEN** the Inbox tab's filter-chip row or any row's state chip renders
- **THEN** New, Accepted, Cancelled, and Completed each render in their own distinct color, and
  Declined and Discarded render in the same color as each other

#### Scenario: The filter chip and a row's chip never disagree
- **WHEN** an assignment is in a given state
- **THEN** its filter-row chip and its own row's collapsed-state chip render in the same color for
  that state

### Requirement: The Inbox tab uses a dedicated inbox-with-arrow icon
Every Inbox nav entry point (the Assignment Desk's Inbox button, the standalone Inbox block's own
nav button, and the Inbox nav button on Lectern/Scriptorium/Chalkboard) SHALL render a dedicated
inbox-with-a-downward-arrow icon, not an icon already used elsewhere in the mod for an unrelated
purpose.

#### Scenario: The Inbox icon is distinct from the Scriptorium's inventory icon
- **WHEN** any Inbox-tab nav button renders its icon
- **THEN** it shows the dedicated inbox-with-down-arrow glyph, not the same icon used for the
  Scriptorium's own inventory-tab affordance or any other unrelated Scribe surface

### Requirement: The Inbox nav button is gated on assignment history for view-only surfaces
On the Lectern, Scriptorium, and Chalkboard — surfaces whose Inbox nav button is a view-only
addition, not their primary capability — the Inbox nav button SHALL be shown only once the viewing
player has received at least one assignment (in any state, ever, including a terminal one). Before
that, no Inbox nav button SHALL appear on those three surfaces. This gating SHALL NOT apply to the
Assignment Desk (whose Inbox tab is one of its exactly two tabs) or the standalone Inbox block
(whose sole capability is the Inbox tab) — both SHALL always show their Inbox surface regardless of
assignment history.

#### Scenario: A player with no assignment history sees no Inbox button on the Lectern
- **WHEN** a player who has never received an assignment opens a Lectern's dialog
- **THEN** no Inbox nav button is present alongside its Guestbook/History nav buttons

#### Scenario: The Inbox button appears once an assignment arrives
- **WHEN** a player who previously had no assignment history receives their first assignment
- **THEN** the Lectern's, Scriptorium's, and Chalkboard's dialogs each show an Inbox nav button on
  their next rebuild, without requiring the dialog to be closed and reopened

#### Scenario: A terminal-only history still counts as "ever assigned"
- **WHEN** a player's only assignment was Declined, Cancelled, or Discarded and no other assignment
  followed
- **THEN** the Inbox nav button remains shown on the Lectern, Scriptorium, and Chalkboard

#### Scenario: The Assignment Desk and standalone Inbox block are never gated
- **WHEN** a player who has never received an assignment opens the Assignment Desk or the
  standalone Inbox block
- **THEN** its Inbox tab (or, for the Inbox block, its sole view) is present exactly as it would be
  for a player with assignment history
