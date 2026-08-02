## Context

Proposal B shipped the tablet items, crafting, docId persistence, and the `ScribeDocumentPolicy`
caps, but deliberately opened the **existing** `GuiDialogScribeNotebook` as a stopgap so the item
was testable before its own dialog existed. That interim dialog is tabbed (Read / Edit / Pinned /
Settings, plus History on the notebook) and Notebook-themed — it neither matches the tablet's
always-edit, scratch-tier identity nor exercises the cuneiform font (Proposal A), which is the
whole point of the tablet tier.

The base dialog `ScribeDialogBase` already carries all shared chrome: title editing, drag-grip,
close, autosave, network send helpers, view-switch, scroll management. Three dialogs subclass it
today (Lectern + two Notebooks). The one existing extension seam is `GetExtraNavButtons()`
(returns empty by default; the Notebook overrides it to add History). Proposal C adds the tablet
as a fourth subclass and adds two more narrow `virtual` seams — over the nav column and the central
content — so the tablet can present an empty (nav-less) right column and its own always-edit center,
while keeping the shared three-column layout skeleton and not disturbing the three incumbents.

Because B already gave the tablet a working editable task list, C is careful to **preserve** that
editor (just presented tabless and themed) rather than replace it. The cuneiform font debuts as a
display-only **title banner** above the editor — proving the font without regressing editing. The
full cuneiform-editable rows are Proposal D.

Constraints carried from the plan and prior proposals:
- `src/Core/` must never reference the VS API (the load-bearing invariant). Nothing in C needs
  Core changes — the layout math (`CuneiformLineLayout`) and policy already live in Core from A/B.
- No new mod dependencies, no new network packets. The tablet keeps saving through `TabletHost`
  and the frozen `ScribeNotebookSaveMessage`.
- The GUI uses ForceRebuild (see the animation-lessons memory / `docs/animation-lessons-learned.md`)
  — the tablet dialog follows the same rebuild discipline as its siblings.

## Goals / Non-Goals

**Goals:**
- A bespoke `GuiDialogScribeTablet` that reuses inherited base chrome (title-edit, grip, close)
  rather than reimplementing it.
- Two narrow `virtual` seams in `ScribeDialogBase.Layout.cs` (`BuildRightColNav` and
  `BuildCentralRegion`, defaults = the existing code) that the three incumbent dialogs inherit
  unchanged — verified byte-identical — keeping the shared three-column layout skeleton intact.
- A no-tabs, always-edit layout: the inherited editable task list in the center column, an **empty**
  right column (no Read/Pinned/Settings/History nav) whose spacer width preserves the side margins.
- **No regression of B's editor** — the tablet keeps the working editable task list it has today.
- First real cuneiform text rendered inside production dialog chrome — a display-only title banner.
- A one-line branch (`UseCuneiform`) that honors the `DisableCuneiformFont` setting from A.
- The tablet's own earthen theme and clay/wax backdrops, both via the existing per-item mechanisms
  (`ScribeTheme`, `ScribeBackdrops`).
- Switch `ItemScribeTablet` to open the new dialog.

**Non-Goals:**
- The pencil-toggle editable input/output row (rendering the task rows themselves in cuneiform) —
  that is Proposal D. This round the cuneiform is a **display-only title banner** sitting above the
  normal editable task list.
- Authentic tablet backdrop art, and the fuller clay-type/fired backdrop set — the followup
  `add-tablet-clay-type-backdrops` owns the 7-backdrop vision. This round both slots (clay, wax)
  point at the `scribe-lectern.png` placeholder (its 1024×1160 ratio already matches).
- Any deferred tablet mechanic: firing→archive, water damage, carry-forward migration, wax-wipe,
  stylus-in-offhand edit gate.
- Task editing/pinning UX changes — those already work through the inherited editor and Proposal
  B's policy. The tablet simply presents them without tabs.

## Decisions

### 1. Subclass `ScribeDialogBase`; do not build a new dialog from scratch
`GuiDialogScribeTablet : ScribeDialogBase`, mirroring `GuiDialogScribeNotebook`. Title editing
(`_isTitleEditing` / `_titleController` / `_titleFocusNode`), grip drag (`OnGripDragStart/Move/End`),
close, autosave, and the network send helpers are inherited unchanged.

*Alternative considered:* a standalone `GuiDialog`. Rejected — it would duplicate the delicate
title/focus/autosave machinery that took multiple rounds to stabilize, and diverge on every future
base fix.

### 2. Keep the 3-column structure; seam the nav column and the central content
The base lays the content area out as a three-column `Row`:
`[ SideColW spacer | TasksColW center | SideColW right-nav ]`, whose widths sum to `InnerW`. The
tablet **keeps this exact structure** — the left/right spacer columns give the proportional side
margins the other dialogs use, and keeping one layout idiom across all four dialogs is worth more
than shaving a vestigial column (see the side-spacing decision below). The tablet differs in only two
places, so the base gets two matching seams in `ScribeDialogBase.Layout.cs`:
- `BuildRightColNav()` becomes overridable so the tablet returns an **empty** right column (no nav
  buttons) — the column still occupies `SideColW`, preserving the symmetric right margin.
- `BuildCentralRegion()` becomes overridable (with `BuildEditorContent()` promoted from `private`
  to `protected` so the tablet can reuse the editable list rather than fork it) so the tablet
  supplies its own center content (banner + editor) instead of the `viewMode`-switched view.

The three incumbents inherit the defaults and take the identical pre-existing path — verified by
reading the diff and by an in-game check that Lectern/Notebook are unchanged.

*Alternatives considered:* (a) collapse the tablet to a single center column (override
`BuildSectionInnerBox` wholesale). Rejected — the author wants the spacer columns kept for the
proportional side margins, and a one-column tablet would diverge structurally from its siblings.
(b) Force `viewMode = Editor` and zero the nav width. Rejected — it keeps the tab enum alive on a
dialog that has no tabs and offers no clean insertion point for the banner. Two narrow `virtual`
seams that leave the three-column skeleton intact are the minimal honest change.

### 3. No nav buttons on the tablet
The tablet overrides `BuildRightColNav()` to return an empty right column, so none of the baseline
nav buttons (Read/Edit/Pinned/Settings) render. `GetExtraNavButtons()` also stays empty (inherited).
The right column still exists as a `SideColW` spacer, so the center content keeps its symmetric side
margins — the tablet simply has no tabs to navigate.

### 4. Keep the editable task list; add a display-only cuneiform title banner
The overridden central region is a column: a display-only cuneiform **title banner** on top, then the
**inherited editable task list** below it (`BuildEditorContent()`, now `protected`). The banner is a
single `CuneiformText` (from A) rendering the document's **title** (which always exists — defaults to
`"Tablet"` via `TabletHost`), given an **explicit pixel height derived from `fontSizeEm`** because an
`Expanded` is inert inside a scroll view (a LibGUI fact recorded in `docs/libgui-reference.md`). This
proves the font in real chrome while the task list stays fully editable.

*Why not a display-only cuneiform row instead of the editor?* The original plan staged C as
"display-only row, editing returns in D." But Proposal B already shipped a **working editable task
list** on the tablet (through the interim notebook dialog). Making C display-only would *remove* a
feature that works today for a whole proposal's duration — a real user-facing regression. Rendering
the *title* in cuneiform proves the font on real, always-present document data with zero regression;
D then upgrades the task rows themselves to cuneiform-capable editable rows.

*Why the title, not a task block or a hardcoded demo string?* The title always exists (never empty —
falls back to `"Tablet"`), is short enough to read the strokes clearly, and is real document data
(unlike a hardcoded demo) without being blank on a fresh tablet (unlike the first task block).

### 5. Single `UseCuneiform` branch point
Compute `UseCuneiform` once in the tablet dialog from the `DisableCuneiformFont` player setting.
- `true`  → render the title banner via `CuneiformText`.
- `false` → render the same title text through the normal path using `ScribeTaskFont.Resolve(...)`
  (`ScribeRowConstants`), exactly as the notebook renders task text.

Keeping the branch in one place (not scattered per-widget) matches A's plan and keeps the fallback
trivially correct.

### 6. Theme and backdrops via existing per-item mechanisms
- Add `ScribeTheme.Tablet` (earthen/clay palette) next to `Light` in `ScribeTheme.cs`; the tablet's
  `Build()` wraps content in that theme, just as the notebook selects its own. **This palette is a
  placeholder this round.** The existing `Light` theme is already a warm parchment palette (tan/ochre/
  dark-ink), so the earthen `Tablet` palette is a first pass; the *real* text-contrast decision is
  deliberately **deferred until the clay/wax backdrops render in-game**, because the VS materials the
  backdrops target span the color gamut (pale-blue and pale-pink unfired clays, dark-grey and brown
  fired clays, gold beeswax) — a single fixed ink color may not stay legible across all of them. The
  eventual choice (one ink color / dark text + light shadow-glow / per-backdrop ink) is captured in
  the `tablet-theme-contrast-vs-backdrops` note and revisited once the art exists.
- Add **two** named backdrop slots (`clay`, `wax`) to `ScribeBackdrops` in `ScribeBackdrop.cs`,
  keyed to the tablet item's real `material` variant axis, each a distinct spec. Both point at
  `textures/gui/scribe-lectern.png` for now. `WrapBackdrop` / `BuildOuterArtBox` are reused
  unchanged. This is a purely additive use of the per-item backdrop capability (`gui-backdrop`), so
  no backdrop requirement changes.

*Why two, not the four (clay/fired/wax/spare) the plan named?* `ScribeBackdropSpec` holds a single
texture that `WrapBackdrop` **stretches to fill** the whole 1024×1160 page, and the tablet item has
**only** a `material: [clay, wax]` axis today (no clay-type, no fired state — firing is a deferred
non-goal). So the dialog has no data to key a `fired`/clay-type backdrop off of, and `spare` names
nothing. Two slots map exactly to what the item can distinguish now. The fuller 7-backdrop vision (3
clay types × fired/unfired + wax, sourcing VS pottery textures and possibly a custom frame renderer)
needs new variant data on the item *and* extending `WrapBackdrop` past stretch-to-fill — that is the
separate followup `add-tablet-clay-type-backdrops`, sequenced after this change archives.

### 7. Side margins stay spacer-column-based, not percentage padding
The tablet keeps the base's `[ SideColW | TasksColW | SideColW ]` spacer-column idiom for its side
margins rather than switching its center content to `Padding(horizontal: pct × W)`. Percentage
padding reads more cleanly in isolation and drops the sum-to-`InnerW` arithmetic, but the Lectern and
Notebook siblings **must** keep a real right column to hold their nav buttons — so a padding idiom on
the tablet alone would leave the base class expressing "side margin" two different ways. One idiom
across four dialogs beats a slightly cleaner tablet plus a lasting split. (Converting *all* dialogs to
percentage padding + an explicit nav element is a coherent uniform refactor, but it re-lays-out the
three shipped dialogs and is out of scope here — a separate change if ever wanted.)

### 8. Switch the item's open call
`ItemScribeTablet.OpenTabletDialog` (or its `OnHeldInteractStart` open site) constructs
`GuiDialogScribeTablet` instead of `GuiDialogScribeNotebook`. The `TabletHost` it passes is unchanged.
This is the one behavior change to `clay-wax-tablet-item`'s interim-dialog requirement.

## Risks / Trade-offs

- **Seam disturbs the incumbents** → The three existing dialogs must build byte-identically. Mitigate
  by making the two seams pure `virtual` methods whose default bodies are the *existing* code moved
  verbatim (`BuildRightColNav`, `BuildCentralRegion`) — the incumbents call the same code by
  inheritance — and by verifying via diff review plus an in-game Lectern/Notebook smoke test (all
  views still work). Promoting `BuildEditorContent` from `private` to `protected` is a visibility
  widening only; no call site changes.
- **`Expanded` swallowed in a scroll view** → Already bitten once (libgui-reference). Mitigate by
  giving the cuneiform title banner an explicit computed height from `fontSizeEm`, never relying on
  `Expanded`.
- **Cuneiform banner + editor competing for vertical space** → The banner takes a fixed height and
  the editable task list fills the rest. Mitigate by measuring the banner height first and giving the
  list the remainder, mirroring how the notebook stacks its fixed title bar over its scrolling body.
- **Cuneiform illegible/misaligned in real chrome at game font sizes** → This is the first time the
  font renders inside a production dialog. Mitigate by starting display-only with tunable demo text,
  and by the `DisableCuneiformFont` fallback that routes to the proven normal text path.
- **Placeholder backdrop aspect mismatch** → `scribe-lectern.png` is 1024×1160, matching the tablet
  layout aspect (`1160/1024`) TabletHost already reports, so reuse is safe; authentic art later just
  swaps the file path per slot.
- **Theme palette contrast on cuneiform strokes** → The stroke ink color comes from the theme; an
  earthen palette must keep strokes legible. Mitigate by tuning `ScribeTheme.Tablet` ink against the
  backdrop in-game.
