# tablet-dialog Specification

## Purpose
TBD - created by archiving change add-tablet-dialog. Update Purpose after archive.
## Requirements
### Requirement: GuiDialogScribeTablet is a ScribeDialogBase subclass

The system SHALL provide a `GuiDialogScribeTablet` class in `src/Mod/GuiDialogScribeTablet.cs` that
subclasses `ScribeDialogBase` and reuses the inherited title editing, drag-grip, close, autosave,
and network-send chrome rather than reimplementing it. It SHALL be constructed with a `TabletHost`
so it operates against the tablet's `ItemStack`-backed document through the existing
`IScribeDocumentHost` contract.

#### Scenario: Tablet dialog reuses inherited chrome

- **WHEN** a player opens a tablet and edits its title, drags it by the grip, and closes it
- **THEN** title editing (pencil → edit, unfocus/Done → save), grip dragging, and close all behave
  identically to the Notebook dialog, because they are inherited from `ScribeDialogBase`

### Requirement: Tablet dialog is always-edit with no tab navigation

The tablet dialog SHALL present a single always-edit central region with NO tab navigation column.
It SHALL NOT show the Read / Edit / Pinned / Settings baseline nav buttons, and SHALL NOT show a
History button. `GetExtraNavButtons()` SHALL remain empty for the tablet, and the tablet layout
path SHALL render no nav column.

#### Scenario: No nav column on the tablet

- **WHEN** a player opens a tablet
- **THEN** the dialog shows the editable document directly with no Read/Edit/Pinned/Settings/History
  tab buttons

### Requirement: Central region keeps the editable task list

The tablet dialog's central region SHALL retain the editable task list inherited from
`ScribeDialogBase` (the same editor Proposal B exposed through the interim dialog), presented without
tab navigation. Adding, editing, checking off, and pinning tasks SHALL continue to work under the
tablet document policy (10-entry / 1-pin caps). This change SHALL NOT remove task-editing capability
that the tablet has today.

When an add is refused because the tablet already holds the maximum number of entries of any kind (10),
the dialog SHALL surface a standard in-game error through the game's transient-error path rather than
silently doing nothing, so the player learns why no row appeared. The refusal SHALL be reported at
every add gesture that the cap governs (the footer add-task control and the keyboard insert-below
gesture), and the add-task control MAY additionally remain visually disabled at the cap.

#### Scenario: Tasks remain editable on the tablet

- **WHEN** a player opens a tablet and adds, edits, checks, or pins a task
- **THEN** the edit is applied and saved exactly as before, subject to the tablet's 10-entry / 1-pin
  policy, with no tab navigation shown

#### Scenario: Adding an 11th entry shows an in-game error

- **WHEN** a player attempts to add an entry of any kind to a wet tablet that already holds 10 entries
  (via the add-task control or the keyboard insert gesture)
- **THEN** no entry is added and a standard in-game error message tells the player the tablet is full

### Requirement: A single branch honors the disable-cuneiform setting

The tablet dialog SHALL compute a single `UseCuneiform` value from the player's positive
`CuneiformTablets` setting (default **true**), via `ScribeTaskFont.UseCuneiform`. When the setting is on
(cuneiform enabled), this single branch SHALL route the tablet's cuneiform surfaces — the editable title
bar text, the editable task-row text (both at rest and while being typed), and the tablet's button
labels — to the cuneiform render path; when off, the same surfaces SHALL render through the normal text
path using `ScribeTaskFont.Resolve` in the player's resolved task font (and rows/title use the normal
editable field). The branch SHALL be evaluated in one place and threaded to every surface, not scattered
per widget. An existing client config carrying the former `DisableCuneiformFont` key SHALL migrate to
`CuneiformTablets = !DisableCuneiformFont`; absent, the setting SHALL default to true.

#### Scenario: Fallback to normal font when cuneiform is turned off

- **WHEN** a player has `CuneiformTablets` turned off and opens a tablet
- **THEN** the title bar, task rows, and button labels all render and edit in the normal resolved task
  font instead of cuneiform strokes

#### Scenario: Cuneiform renders when the setting is on

- **WHEN** a player has `CuneiformTablets` on (the default) and opens a tablet
- **THEN** the title bar, task rows, and button labels all render as cuneiform strokes, and typing in
  the title or a row produces cuneiform

#### Scenario: A prior disable-cuneiform preference is preserved across the rename

- **WHEN** a player who had previously enabled `DisableCuneiformFont` loads the mod after the setting is
  renamed
- **THEN** their preference migrates to `CuneiformTablets = false`, so cuneiform stays off for them
  rather than reverting to the on-by-default state

### Requirement: Tablet dialog uses its own theme and material-keyed backdrops

The tablet dialog SHALL select a clay-type-specific `ScribeTheme` palette in its `Build()` theme
wrapper, keyed to the tablet item's `material` variant, when Pixel-Art Display is ON. There SHALL be
three authored per-clay-type palettes (red, blue, fire) whose colors harmonize with each type's
backdrop art; `wax` and any unrecognized material SHALL resolve to the fire palette (its interim
backdrop twin), so the resolved theme and the resolved backdrop always agree.

The **body ink** (`OnSurface`/`OnBackground`) and the per-material **link ink** (the row style's
`LinkColor`) SHALL be resolved with the tablet's **drying state** as an additional input, sourced from
the readability bundle for the current `(material, state)` view, so ink can differ across wet, hard, and
fired (fired ink is darker). The remaining material-identity roles — the accent (`Primary`, which
programmatically drives button fill, button text, hover, press, caret, focused-input border, and text
selection), the secondary tone (`Secondary`, which drives the pinned-row tint), the input field
background (`SurfaceHigh`), the input/divider border (`Border`), and the panel `Background` — SHALL
remain per-material (state-independent). Within each palette, `Secondary` SHALL read clearly distinct
from `Primary` so a focused input inside a pinned row shows a legible focus border against the pinned
wash.

The per-material **muted-text role (`OnSurfaceVariant`)** — used for hint/placeholder and secondary
text — SHALL be **derived from that view's own ink** by a single shared HSV **Value** lift (via
`ScribeRowConstants.ShiftBrightness`, which preserves hue and chroma), governed by one shared constant
across all clay palettes, rather than authored as an independent per-palette color. This makes the
muted-vs-ink contrast a consistent perceptual step across fire, red, and blue, and makes "darken the
muted text" a single-constant adjustment. The derived muted tone SHALL remain clearly lighter/weaker
than the body `ink` so it still reads as secondary text, not body text.

When Pixel-Art Display is OFF, the tablet dialog SHALL follow the player's global theme
(`ThemeData.Default`), unchanged — per-clay theming and backdrop art both apply only when Pixel-Art is
ON. The backdrop SHALL continue to be applied through the existing `WrapBackdrop` / `BuildOuterArtBox`
mechanism, and backdrop selection SHALL remain keyed to the `material` variant as before (see "The
tablet dialog backdrop is chosen by clay type and state").

#### Scenario: Tablet opens with its own theme and backdrop

- **WHEN** a player opens a red, blue, or fire clay tablet with Pixel-Art Display ON
- **THEN** the dialog is drawn with that clay type's palette (its own ink, accent, input
  background/border, and panel background) and the backdrop slot for that material

#### Scenario: Ink and link ink vary by drying state

- **WHEN** a wet, a hardened, and a fired tablet of the same clay type are each opened with Pixel-Art
  Display ON
- **THEN** the body ink (`OnSurface`) and the link ink resolve to that clay's authored values for each
  state (the fired view's ink is darker than the wet view's), while the accent (`Primary`), secondary,
  surfaces, border, and background stay the same across the three states

#### Scenario: Muted text contrast is consistent across clay types

- **WHEN** a red, blue, and fire tablet are each opened with Pixel-Art Display ON
- **THEN** each palette's `OnSurfaceVariant` is the palette's own `ink` lifted by the same shared HSV
  Value amount, so the muted-vs-ink contrast step is perceptually equal across all three clay types
- **AND** each derived muted tone stays recognizably that clay's hue and reads as secondary (weaker
  than the body ink), not as body text

#### Scenario: Muted text darkens via a single constant

- **WHEN** the shared muted-text lift constant is lowered
- **THEN** the muted/placeholder text on all three clay palettes darkens by the same perceptual amount,
  with no per-palette color edits required

#### Scenario: Wax and unknown materials fall back to the fire palette

- **WHEN** the tablet dialog resolves the theme for a `wax` tablet or an unrecognized material with
  Pixel-Art Display ON
- **THEN** it resolves to the fire clay palette for the matching state, matching the fire interim
  backdrop that material uses

#### Scenario: Pixel-Art off follows the global theme

- **WHEN** a tablet of any material is opened with Pixel-Art Display OFF
- **THEN** the dialog follows the player's global theme (`ThemeData.Default`) with no per-clay coloring
  and no backdrop art, exactly as before this change

#### Scenario: Focus cue stays distinct on a pinned row

- **WHEN** a player focuses an input field inside a pinned task row on a tablet
- **THEN** the focused-input border (from `Primary`) reads clearly against the pinned-row wash (from
  `Secondary`), so the focus is unambiguous

#### Scenario: Non-tablet dialogs and readable path are unaffected except the pinned tint

- **WHEN** the Lectern or Notebook dialog is opened, or any dialog is rendered on the non-cuneiform
  readable path
- **THEN** its theme is unchanged from before this change (the parchment `Light`/global theme), EXCEPT
  the pinned-row tint, which is now derived from `Secondary` instead of `Primary` (the same global
  remap applied for focus clarity)

### Requirement: Tablet Link/Tracker/Craft rows use a distinct per-`(material, state)` link ink

On the Pixel-Art path, the tablet dialog SHALL supply a dedicated **link ink** for the tappable content
of a Link/Tracker/Craft row (the item-name hyperlink, the guide-page book glyph, and the Tracker
have/need count) via `ScribeRowStyle.LinkColor`, rather than letting the row fall through to the theme
accent (`colors.Primary`). The link ink SHALL be resolved from the readability bundle for the current
`(material, state)` view — the same source as body ink — so it can differ across wet, hard, and fired.
Each view's link ink SHALL be a deeper, more-saturated tone than that palette's accent, chosen to clear
WCAG AA (≥ 4.5 : 1) against that material's clay/wax face while remaining chromatically distinct from
the near-black body ink, so a link reads as a legible, tappable colored link and not as body text.

The link ink SHALL be keyed off the same `material` and drying `state` the theme and backdrop use (one
parameterized dialog, not a subclass per material). With Pixel-Art Display OFF, the tablet follows the
global theme over a flat panel and MAY use the theme accent for links unchanged.

#### Scenario: A tablet link reads as a distinct legible link

- **WHEN** a Link/Tracker/Craft row is shown on a tablet with Pixel-Art Display ON
- **THEN** the item name renders in the view's dedicated link ink, clearly distinct from both the
  clay backdrop and the near-black body ink

#### Scenario: Link ink is material- and state-keyed

- **WHEN** the same row is shown on a fire vs. red vs. blue vs. wax tablet, and across wet/hard/fired
  of one clay
- **THEN** each uses the link ink authored for that `(material, state)` view (a deep rust / wine /
  steel-blue / amber-bronze family respectively), all clearing AA on their own backdrop

#### Scenario: Flat-panel fallback is unchanged

- **WHEN** the tablet is shown with Pixel-Art Display OFF
- **THEN** the row link color follows the global theme accent exactly as before (no material link ink is
  applied)

### Requirement: Empty-tablet hint text reads legibly on the clay backdrops

The tablet's empty-field hint text (drawn from `OnSurfaceVariant`) SHALL read clearly against the
mid-tone clay backdrops. On the tablet, this text is the empty-task-list hint rendered at **full alpha**
from the muted role — NOT the multiline field's `0.55` placeholder, which the cuneiform (tablet) render
path never applies. Its legibility is therefore governed by the muted role's **color**, which the derived
`OnSurfaceVariant` (see the muted-text requirement above) makes darker and consistent across clay types.
No alpha change to the multiline field's placeholder path SHALL be made, because that `0.55` seam serves
only the readable (Pixel-Art-off) Lectern/Notebook path — raising it would darken that path, which this
change leaves unchanged.

#### Scenario: Empty tablet shows a legible hint

- **WHEN** a player opens a red, blue, or fire clay tablet with an empty task list and Pixel-Art
  Display ON
- **THEN** the empty-task-list hint text is clearly legible against that clay's backdrop (not barely
  visible), by virtue of the darker derived muted role

#### Scenario: Readable path placeholder is unchanged

- **WHEN** a player opens a Lectern or Notebook (readable / Pixel-Art-off path) with an empty field
- **THEN** its placeholder alpha (`0.55`) is unchanged from before this change

### Requirement: The tablet dialog backdrop is chosen by clay type and state

The tablet dialog SHALL declare named backdrop slots in `ScribeBackdrops` and select exactly one from
both the clay type (the item variant) and the tablet state (wet, hard, or fired), so each of the three
states reads as visually distinct: wet is the smoother/glossier soft appearance, hard is a
lighter/drier appearance, and fired is the final ceramic appearance. The three soft-clay backdrops SHALL
be sourced from authored full-page clay-tablet illustrations (one per clay type), rendered through the
existing stretch-to-fill backdrop path; hard and fired backdrops MAY, until bespoke art exists, reuse
the matching clay art under a per-type tint so the clay types stay distinguishable. When `material` is
wax the wax backdrop SHALL be selected; when the material variant is unrecognized the selection SHALL
default to red + soft so every tablet resolves to a valid backdrop. Where `hard` and `fired` are both
set, the fired appearance SHALL take precedence.

#### Scenario: Each state shows a distinct backdrop

- **WHEN** the same clay-variant tablet is opened wet, then hard, then fired
- **THEN** the dialog shows three visually distinct backdrops (glossy wet, dried hard, ceramic fired) for
  that clay type

#### Scenario: Each of the three clay types selects a distinct backdrop

- **WHEN** a player opens the `clay-red`, `clay-blue`, and `clay-fire` tablet items in turn
- **THEN** each opens with a backdrop distinct to its clay type, so the three clay types are visually
  distinguishable

#### Scenario: A wax tablet opens with the wax backdrop

- **WHEN** a player opens a tablet whose `material` variant is wax
- **THEN** the dialog shows the single wax backdrop, not any clay-type-keyed backdrop

#### Scenario: A tablet with an unrecognized variant falls back to a default backdrop

- **WHEN** a player opens a clay tablet whose `material` variant is unrecognized (e.g. a legacy stack)
- **THEN** the dialog selects the red + soft clay backdrop as the default and does not fail

### Requirement: The tablet dialog has a read-only mode for a non-editable tablet

The tablet dialog SHALL resolve whether its stack is editable — editable ⇔ NOT hard AND NOT fired, via the
tablet's `hard`/`fired` read helpers — at one place and, when not editable, open in a read-only mode: it
SHALL NOT enter editor mode in its constructor, SHALL render the document view-only, and SHALL present no
editor entry, add/check/pin, reorder, or title-edit affordance. A wet (editable) tablet SHALL keep its
existing always-edit behaviour unchanged.

#### Scenario: A hard or fired tablet opens view-only

- **WHEN** a player opens a hard clay tablet or a fired clay tablet
- **THEN** the dialog does not enter editor mode and shows the document read-only with no editing
  affordances

#### Scenario: A wet tablet is unaffected

- **WHEN** a player opens a wet (unfired, un-hardened) clay tablet
- **THEN** the dialog opens always-edit exactly as before

### Requirement: The non-editable tablet dialog shows a state-appropriate empty-state message when blank

When the tablet dialog opens a non-editable tablet (hard or fired) whose document has no tasks and no notes,
it SHALL show a small centered message (a Scribe lang key) in place of an empty content region: for a fired
tablet, that it was fired without any writing; for a hard tablet, that it has dried out and can be edited
again after being dunked in water.

#### Scenario: Blank fired tablet shows the fired message

- **WHEN** a player opens a fired clay tablet with no tasks and no notes
- **THEN** the dialog shows a small centered "fired without any writing" message and no editable surface

#### Scenario: Blank hard tablet shows the dried message

- **WHEN** a player opens a hard clay tablet with no tasks and no notes
- **THEN** the dialog shows a small centered "dried out — dunk in water to edit" message and no editable
  surface

### Requirement: A hardened or fired tablet keeps checkboxes and pins live while blocking text edits

A hardened or fired tablet SHALL present its task list read-only with respect to **text** — the player
SHALL NOT be able to edit task text, add rows, delete rows, or reorder rows — while its **completion
checkboxes and pin toggles SHALL remain interactive**. Checking a task complete and pinning or unpinning
a task SHALL work on a hard or fired tablet exactly as on a wet one. This ensures a task pinned to the HUD
before the tablet hardened or was fired can still be unpinned, so firing a tablet never permanently strands
a pin. This behavior is specific to the tablet's read view; the tabbed Lectern/Notebook read view is
unaffected.

#### Scenario: Completing and unpinning work on a fired tablet

- **WHEN** a player opens a fired (or hardened) tablet and taps a task's checkbox or its pin control
- **THEN** the task's completion toggles and its pin toggles respectively, and the change is saved — the
  read-only state does not disable the checkbox or hide the pin control

#### Scenario: Text remains uneditable on a hardened tablet

- **WHEN** a player attempts to edit a task's text, add a row, delete a row, or reorder rows on a hardened
  or fired tablet
- **THEN** no such text edit is possible, and attempting to edit a row's text surfaces a material-specific
  in-game message explaining why (hardened: soften it in water to make changes; fired: it cannot be changed)

### Requirement: Completion policy collapses to unpin-only on a read-only tablet

When a task on a hardened or fired tablet is completed, any completion policy that would MUTATE the locked
document — *delete*, *sink*, or *unpin-and-sink* — SHALL resolve to *unpin* only, and *keep* SHALL remain
*keep*. The task's completion state and its pin removal SHALL still apply, but the underlying locked
document SHALL NOT be reordered or have rows deleted. This collapse SHALL be enforced at the
server-authoritative completion path so it holds for completion from the read view and from the HUD alike.
On a wet (editable) tablet the completion policy SHALL behave unchanged.

#### Scenario: Delete policy unpins instead of deleting on a fired tablet

- **WHEN** a player whose completion policy is *delete* completes a pinned task that belongs to a fired
  tablet
- **THEN** the task is marked complete and its pin is removed, but the task is not deleted from the tablet's
  document

#### Scenario: Sink policy unpins instead of reordering on a hardened tablet

- **WHEN** a player whose completion policy is *sink* or *unpin-and-sink* completes a pinned task on a
  hardened tablet
- **THEN** the task is marked complete and its pin is removed, but the tablet's document order is unchanged

#### Scenario: Wet tablet completion is unchanged

- **WHEN** a player completes a task on a wet tablet under any completion policy
- **THEN** the policy applies with its full effect (delete, sink, unpin, unpin-and-sink, or keep) exactly as
  before

### Requirement: The tablet's always-edit view activates item-row links

Because the tablet dialog has no separate read view — a wet (editable) tablet renders the editor
row path directly — the click-to-open-Handbook affordance that the Lectern/Notebook read view
provides SHALL be surfaced on the tablet's always-edit central region for item-kind rows (Link,
Tracker, and Craft). Activation SHALL be scoped to the tablet: the shared editor row path SHALL
gain the affordance only when the dialog opts in (a `ScribeDialogBase` seam the tablet turns on),
so the Lectern and Notebook editor views remain non-clickable and continue to rely on their own
read view for link activation.

The affordance SHALL be a distinct hit region from the row's editing controls, per kind:

- A **Link** row SHALL open its referenced Handbook page when its name label is clicked. A Link
  row has no editable inline field, so the whole name label is the activation region.
- A **Tracker** or **Craft** row SHALL open its target item's Handbook page when its **name
  label** is clicked, while the row's existing inline numeric target-quantity field (the `+/-`
  stepper) SHALL continue to receive clicks on the **number** and edit the target quantity. The
  name label and the numeric field SHALL be independent hit regions on the same row.

Activation SHALL be distinct from the row's completion control: opening a Handbook page SHALL NOT
complete, delete, or reorder the task. This behavior applies only to a wet (editable) tablet;
a hardened/fired tablet already renders through the read view, which provides link activation
unchanged.

#### Scenario: Clicking a Link task on a wet tablet opens its page

- **WHEN** a player clicks the name of a Link task on a wet (editable) tablet
- **THEN** the game opens that Link's referenced Handbook page, and the Link's completion state is
  unchanged

#### Scenario: Clicking a Tracker/Craft name on a wet tablet opens the item page

- **WHEN** a player clicks the item **name** of a Tracker or Craft task on a wet tablet
- **THEN** the game opens that item's Handbook page (the Tracker's `TargetItemCode` or the Craft
  parent's output item), and the task's completion state is unchanged

#### Scenario: Clicking the number still edits the target on a wet tablet

- **WHEN** a player clicks the numeric target-quantity control (or its `+/-` steppers) on a
  Tracker or Craft row on a wet tablet
- **THEN** the numeric field edits the target quantity as before, and no Handbook page is opened

#### Scenario: Lectern and Notebook editors remain non-clickable

- **WHEN** a player views an item-kind row in the Lectern or Notebook **editor** view
- **THEN** the row's name is not a Handbook link (link activation remains available only through
  their read view), unchanged from before this change

### Requirement: Craft rows resolve their Handbook page on activation

The link-activation dispatch that opens a Handbook page from an item-row name SHALL resolve a
**Craft** row to its output item's Handbook page (via the Craft parent's `TargetItemCode`), in
addition to the existing Link (`LinkTarget`) and Tracker (`TargetItemCode`) resolution. This SHALL
hold wherever the dispatch is used — the tablet's always-edit view and the Lectern/Notebook read
view — so that a Craft parent name, which already renders as a clickable link, actually opens its
page instead of silently doing nothing.

#### Scenario: Clicking a Craft name opens the output item's page in the read view

- **WHEN** a player clicks a Craft parent's name in the Lectern or Notebook read view
- **THEN** the game opens the Handbook page for the Craft's output item, rather than doing nothing

### Requirement: Cuneiform item-kind titles wrap to width

Item-kind titles (Tracker, Link, and Craft rows) rendered on the cuneiform (Tablet) surface SHALL
wrap to the available row width rather than clipping mid-word, matching the wrapping behavior every
other surface (HUD, Lectern, Notebook, Scriptorium) already provides for the same titles. This applies
to both parent rows and their indented subtasks, and to both the read view and the wet-tablet editor
view (where the item name is display-only).

The single-line dialog title band (the title chrome) is out of scope and MUST remain single-line.

#### Scenario: A long Tracker/Link/Craft name on the Tablet wraps

- **WHEN** a Tracker, Link, or Craft row whose referenced item has a name longer than the row width is
  shown on the Tablet (read view or wet editor view)
- **THEN** the cuneiform name wraps onto additional lines within the row's bounds and no glyphs are
  clipped or run past the row edge

#### Scenario: A subtask item name on the Tablet wraps

- **WHEN** an indented (Depth 1) Tracker/Craft ingredient subtask with a long item name is shown on the
  Tablet, which has less horizontal room than a parent row
- **THEN** the cuneiform name wraps within the narrower indented bounds rather than clipping

#### Scenario: The dialog title band stays single-line

- **WHEN** the Tablet dialog renders its title chrome (which uses the same cuneiform renderer with
  single-line mode)
- **THEN** the title remains single-line and unaffected by this change

#### Scenario: Non-cuneiform surfaces are unchanged

- **WHEN** the same Tracker/Link/Craft titles are shown on the HUD, Lectern, Notebook, or Scriptorium
  (which render with a plain wrapping text style, not cuneiform)
- **THEN** their rendering is byte-for-byte unchanged by this change

### Requirement: Tablet title band wraps a long title to at most two lines

On the Tablet dialog, the title band SHALL wrap a title that is too wide for a single line onto a
second line, up to a maximum of two lines, instead of clipping the title to one line. This applies in
both the resting (display) state and the editing (title-field) state. A title that fits on one line
SHALL be rendered exactly as before (single line, no band-height change). A title longer than two
lines' worth SHALL clip at the end of the second line (cuneiform has no ellipsis glyph). This behavior
SHALL be scoped to the Tablet; the Lectern, Notebook, Scriptorium, and HUD title chrome SHALL remain
single-line as today. When cuneiform is disabled or the glyph bundle is unavailable, the tablet MAY
fall back to the base single-line title rendering.

#### Scenario: Long title wraps to two lines at rest

- **WHEN** a player views a tablet whose title is longer than one line of the title band
- **THEN** the resting title renders across two lines within the band, and the drag-grip, pencil, and
  close chrome stay clear of the wrapped title

#### Scenario: Long title wraps to two lines while editing

- **WHEN** a player edits a tablet title and types past the width of one line
- **THEN** the editing title field shows the text wrapped onto a second line rather than clipping the
  overflow off the right edge, and pressing Enter still commits the title (no newline is inserted)

#### Scenario: Short title and other surfaces unchanged

- **WHEN** a tablet title fits on one line, or a Lectern/Notebook/Scriptorium title of any length is shown
- **THEN** the title renders on a single line exactly as before, with no change to band height or layout

