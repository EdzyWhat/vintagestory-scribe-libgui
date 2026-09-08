# link-task Specification

## Purpose
TBD - created by archiving change add-tracker-link-tasks. Update Purpose after archive.
## Requirements
### Requirement: Link task kind and reference field
The document model SHALL support a `Link` block kind that carries a reference target
(`LinkTarget`, a plain string identifying a Handbook page — an item/block asset code, which MAY be
**attribute-encoded** to identify a specific variant of an attribute-encoded item (see the
`attribute-encoded-item-identity` capability), and which is stored without any Vintage Story API
dependency in `Core`). A Link block SHALL retain the fields common to every block (text, completed
flag, depth, `TaskId`, assignment) but SHALL NOT carry the Tracker quantity fields. The kind value
SHALL be appended to the existing kind enumeration (never renumbering existing kinds). A Link is a
reference, not a counter: it has no progress and is completed only by the player, never automatically.

A `LinkTarget` for an **attribute-encoded item** SHALL resolve to that specific variant, so the Link
shows the variant-correct name and opens the variant-correct Handbook page rather than an
attribute-less fallback. A bare (non-attribute-encoded) `LinkTarget` SHALL resolve exactly as before.

#### Scenario: A link carries its reference target
- **WHEN** a Link block is created referencing Handbook page for `game:ingot-copper`
- **THEN** the block's kind is `Link`, its `LinkTarget` is `game:ingot-copper`, and it has no
  Tracker quantity fields set

#### Scenario: A link to an attribute-encoded item resolves to that variant
- **WHEN** a Link block is created from a specific attribute-encoded item's Handbook page (e.g. the
  "Copper Lantern" page)
- **THEN** the Link shows that variant's name and its label opens that variant's Handbook page, not an
  attribute-less fallback

#### Scenario: A link is not auto-completed
- **WHEN** any inventory or world change occurs
- **THEN** a Link task's completed flag is unchanged (only an explicit player action toggles it)

### Requirement: A Link task behaves as a hyperlink from every surface
Clicking a Link task's label SHALL open its referenced destination, whether the click occurs in a
Scribe UI (Scriptorium, Lectern, Notebook, Tablet) or on the pinned-task HUD. For a plain item, guide
page, or VS Quest Link, that destination is the referenced Handbook page. For a **Progression
Framework** Quest Link, the destination is instead that backend's own Quest Log dialog — a quest has
no Handbook page, so it is never routed through the Handbook-open path. This activation is distinct
from the row's completion control: opening the destination SHALL NOT complete or delete the Link;
completion remains a separate, explicit player action.

#### Scenario: Clicking a link in the Scribe UI opens the handbook page
- **WHEN** the player clicks a Link task's label in any Scribe UI
- **THEN** the game opens that Link's referenced Handbook page

#### Scenario: Clicking a link on the HUD opens the handbook page
- **WHEN** the player clicks a pinned Link task on the HUD
- **THEN** the game opens that Link's referenced Handbook page

#### Scenario: Clicking a Progression Framework Quest Link opens the Quest Log, not a Handbook page
- **WHEN** the player clicks a Quest Link whose backend is Progression Framework, from any surface
- **THEN** the game opens Progression Framework's own Quest Log dialog, landed on its Quest Log tab,
  rather than attempting to open a Handbook page

#### Scenario: Activating a link leaves its completion state unchanged
- **WHEN** the player activates (opens) a Link task that is not completed
- **THEN** after the page or dialog opens, the Link is still not completed

### Requirement: Cooked-meal Handbook pages offer an Add Link action
A cooked-meal (and pie) Handbook page SHALL present an "Add to Scribe" section with a single Add Link
action, consistent with the guide/explainer page's Add Link. Clicking it SHALL create a guide-page
Link targeting that meal's recipe Handbook page (`handbook-mealrecipe-<code>`), labeled with the meal's
displayed title. Meal pages SHALL NOT offer a Tracker or Crafting Task action (a meal has no stable
countable item and is not a grid recipe).

#### Scenario: A cooked meal page shows Add Link

- **WHEN** the player opens a cooked meal's Handbook page (e.g. Vegetable Stew)
- **THEN** an "Add to Scribe" section with a single Add Link action is shown, and no Tracker or Craft
  action appears

#### Scenario: Adding a meal Link creates a guide-page Link to the meal recipe page

- **WHEN** the player clicks Add Link on a meal page and a Scribe surface is open or openable
- **THEN** a Link row is added whose stored target is the meal's recipe page code and whose label is
  the meal's title, and opening that Link navigates back to the meal recipe Handbook page

#### Scenario: The meal Link row displays a readable title, not a raw key

- **WHEN** a meal Link row is shown in the read/editor view, the Pinned tab, or the HUD
- **THEN** it displays the meal's resolved title (e.g. "Vegetable Stew"), not a raw lang key or page
  code

### Requirement: Handbook Add Link label is Link to this page
On an item, block, guide, or cooked-meal Handbook page, the Add-to-Scribe Link action SHALL be
labeled **Link to this page**. The editor Add picker SHALL keep the label **Add Link**. On item
pages the Link action SHALL be listed first among Add-to-Scribe actions (before Count this item and
Add ingredients).

#### Scenario: Handbook shows Link to this page first
- **WHEN** the player opens an item Handbook page that offers Add to Scribe
- **THEN** the first action is "Link to this page"

### Requirement: A Quest Link references an installed quest mod's catalog entry
When a supported quest mod (VS Quest or Progression Framework) is installed and enabled, a Link
block MAY carry a Quest-namespaced `LinkTarget` (e.g. a `quest:` prefix) identifying which
backend it targets and one entry in that backend's public `config/quests/*.json` asset catalog.
Resolving a Quest Link SHALL read only that static catalog for the quest's name/description —
never a live dependency reference, never write access. A Quest Link's name/description text
SHALL be captured at creation time and stored on the block, not re-derived from the catalog on
every render. The recorded backend SHALL be used for all later resolution (auto-detection,
progress mirroring, destination resolution) — see `quest-auto-detect`'s backend-attribution
requirement.

#### Scenario: A Quest Link is created from an installed quest catalog
- **WHEN** a player creates a Quest Link referencing an entry in an installed backend's quest
  catalog
- **THEN** the resulting Link block's `LinkTarget` identifies both that backend and that quest,
  and its displayed name/description are captured from the catalog at creation time

#### Scenario: A Quest Link never queries a live dependency
- **WHEN** a Quest Link is displayed or resolved
- **THEN** only the static catalog asset (or the block's own captured text) is read — no
  compiled reference to any quest mod's DLL is involved

#### Scenario: The picker offers entries from every installed backend
- **WHEN** a player creates a Quest Link with both VS Quest and Progression Framework installed
- **THEN** the picker lists catalog entries from both backends, and the created Link records
  whichever backend the chosen entry actually came from

### Requirement: The Quest Link picker only offers quests the player has started
The "Add Quest Link" picker SHALL list only catalog entries the player has been observed to have
started (accepted or completed) during the current client session, rather than every entry in the
installed backend's static catalog. "Started" is determined per backend from the same server-synced
state the auto-detect watcher already reads: for Progression Framework, any recorded status
(`active` or `completed`) for that quest code; for VS Quest, a quest whose acceptance has been
observed via a loaded quest-giver entity this session. A quest not yet observed as started SHALL be
hidden from the picker, even if it exists in the installed catalog and even if the player started it
in a prior session whose quest-giver hasn't been encountered again yet.

#### Scenario: A started Progression Framework quest appears in the picker
- **WHEN** the player has an "active" or "completed" status recorded for a Progression Framework
  quest this session
- **THEN** that quest appears as a candidate in the Quest Link picker

#### Scenario: An unstarted quest is hidden from the picker
- **WHEN** a catalog entry exists for a quest the player has never been observed to accept or
  complete this session
- **THEN** that quest does NOT appear in the Quest Link picker

#### Scenario: A vsquest quest started in a prior session stays hidden until its giver is seen again
- **WHEN** a player accepted a VS Quest quest in an earlier session, and its quest-giver entity has
  not been loaded/scanned again this session
- **THEN** that quest does NOT appear in the Quest Link picker until its giver is encountered again
  this session and the acceptance is freshly observed

#### Scenario: A player with nothing started this session sees an empty or near-empty picker
- **WHEN** the player has not been observed to start or complete any cataloged quest this session
- **THEN** the Quest Link picker's quest list is empty (or contains only quests separately observed
  as started), which is expected behavior, not an error

### Requirement: Following a Progression Framework Quest Link opens that backend's ledger
When a player activates (clicks/taps) a Quest Link block whose recorded backend is Progression
Framework, the system SHALL open that mod's own ledger dialog via its public standalone toggle
hotkey, using the same activation path already used for every other link kind (no new button, no
new UI surface). A VS Quest Link's activation SHALL remain a no-op — VS Quest exposes no
standalone dialog to open this way (see design.md Decision 5).

#### Scenario: Activating a Progression Framework Quest Link opens the ledger
- **WHEN** a player clicks a Quest Link block whose recorded backend is Progression Framework, in
  any surface (read view, editor, Pin Tab, or HUD pin)
- **THEN** Progression Framework's own ledger dialog opens

#### Scenario: Activating a VS Quest Link remains a no-op
- **WHEN** a player clicks a Quest Link block whose recorded backend is VS Quest
- **THEN** nothing happens, matching the existing (pre-change) behavior

#### Scenario: A missing or renamed ledger hotkey degrades silently
- **WHEN** Progression Framework's ledger hotkey code is absent (e.g. renamed in a future PF
  release)
- **THEN** activating the Quest Link does nothing rather than throwing

### Requirement: Quest Links work on every surface Link tasks already work on
A Quest Link SHALL be usable anywhere an ordinary Link task can be created or shown (Notebook,
Tablet, Lectern, Scriptorium, Chalkboard) — it is not restricted to the Assignment Desk or any
place-bound surface, since it is a personal reference, not a social action.

#### Scenario: Creating a Quest Link from the Notebook
- **WHEN** a player with vsquest installed creates a Quest Link from their Notebook
- **THEN** the Quest Link is created and displayed exactly as any other Link task on that surface

### Requirement: A Quest Link degrades to a plain Link if its quest mod is later removed
If the quest mod backing a Quest Link is later uninstalled, the Link SHALL continue to render
using its captured-at-creation-time name/description text, with no error state and no removal —
it simply stops being eligible for further auto-detection enrichment (see `quest-auto-detect`).

#### Scenario: An orphaned Quest Link still renders
- **WHEN** a world previously used with vsquest is later loaded without vsquest installed
- **THEN** any existing Quest Links still display their captured text normally, with no error
  shown and no auto-detect behavior attempted

### Requirement: A Quest Link row renders a distinct marker icon, no completion checkbox, and a distinct color
On every surface that renders a Link row (Read view, Editor view, Pinned view, HUD pins, and the
Assignment-stage row), a Link block whose `LinkTarget` identifies a quest SHALL render differently
from a plain (non-quest) Link row in three ways:
- It SHALL show a dedicated quest-marker icon (an exclamation mark inside a circle) in place of
  the generic itemless glyph a guide-page Link uses, and SHALL NOT additionally render that icon a
  second time inline alongside the item name.
- On a surface whose Link row shows a completion checkbox (Read view, Editor view, Pinned view, HUD
  pins), it SHALL NOT render that checkbox; the quest-marker icon SHALL occupy the row's leading
  slot the checkbox would otherwise occupy instead. On the Assignment-stage row, whose leading-slot
  checkbox is a row-**selection** control (not a completion toggle — it determines which rows are
  included in the assignment being created), that selection checkbox SHALL be unaffected; the
  quest-marker icon there SHALL replace only the Link's inline icon, not the selection checkbox.
- It SHALL render in a color distinct from a plain Link's color on that surface.

Suppressing a completion checkbox is a presentation-only change: the underlying block SHALL remain
completable exactly as before (it keeps contributing to a document's task totals and keeps a
`Done` value), so no other behavior that depends on completability changes.

#### Scenario: A Quest Link row shows the quest-marker icon, not the book glyph
- **WHEN** a Link row whose `LinkTarget` identifies a quest is rendered on any surface
- **THEN** the row shows the quest-marker (exclamation-in-a-circle) icon exactly once, not the
  `scribebook` glyph a guide-page Link shows

#### Scenario: A Quest Link row shows no completion checkbox
- **WHEN** a Link row whose `LinkTarget` identifies a quest is rendered on the Read view, Editor
  view, Pinned view, or HUD pins
- **THEN** no completion checkbox is shown for that row, and the quest-marker icon appears in the
  row's leading slot instead

#### Scenario: A Quest Link row on the Assignment-stage row keeps its selection checkbox
- **WHEN** a Link row whose `LinkTarget` identifies a quest is rendered on the Assignment-stage row
- **THEN** its row-selection checkbox is still shown and still selects/deselects the row for the
  assignment being created, and the quest-marker icon replaces only its inline Link icon

#### Scenario: A plain Link row is unaffected
- **WHEN** a Link row whose `LinkTarget` does NOT identify a quest (a guide-page or item Link) is
  rendered on any surface
- **THEN** it still shows its completion (or, on the Assignment-stage row, selection) checkbox and
  its icon in its current inline position, unchanged

#### Scenario: A Quest Link row renders in a distinct color from a plain Link
- **WHEN** a Quest Link row and a plain Link row are both visible on the same surface/theme
- **THEN** the Quest Link row's icon and name render in a color distinct from the plain Link row's
  color

#### Scenario: Suppressing the checkbox does not change completion semantics
- **WHEN** a Quest Link block's document computes its task-completion count or exports via the TSV
  codec
- **THEN** the Quest Link block is still counted as completable and its `Done` value is still
  reported, exactly as it was before the checkbox was suppressed from rendering

