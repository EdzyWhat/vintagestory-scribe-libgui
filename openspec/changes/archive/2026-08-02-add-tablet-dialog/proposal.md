## Why

The tablet items (Proposal B) currently open the Notebook's dialog as a stopgap — a tabbed,
Notebook-flavored surface that neither fits the tablet's always-edit, scratch-tier identity nor
shows off the cuneiform font (Proposal A) that is the whole point of the feature. This proposal
gives the tablet its own bespoke dialog and lands the first real cuneiform text rendered inside
production dialog chrome.

## What Changes

- Add `GuiDialogScribeTablet`, a subclass of `ScribeDialogBase` that reuses the inherited title
  editing, drag-grip, and close chrome — built by extension, not from scratch.
- Widen the base dialog with two narrow `virtual` seams in `ScribeDialogBase.Layout.cs` whose default
  bodies are the *existing* code moved verbatim: `BuildRightColNav` (so the tablet can return an
  empty, nav-less right column) and `BuildCentralRegion` (so the tablet can supply its own always-edit
  center), plus promoting `BuildEditorContent` from `private` to `protected` so the tablet reuses the
  editable list rather than forking it. The shared three-column layout skeleton
  (`[ spacer | center | right ]`) stays intact; the Lectern and both Notebook dialogs inherit the
  defaults and build byte-identically.
- No tabs on the tablet: the overridden `BuildRightColNav` returns an empty right column (its
  `SideColW` spacer preserves the side margin) and `GetExtraNavButtons` stays empty — so none of the
  Read/Edit/Pinned/Settings nav buttons render.
- Add a `ScribeTheme.Tablet` earthen/clay palette alongside `Light`, selected in the tablet's
  `Build()` theme wrapper.
- Add two tablet backdrop specs to `ScribeBackdrops`, keyed to the tablet item's real `material`
  variant axis (`clay` and `wax`), each a distinct backdrop. This round both point at the existing
  `textures/gui/scribe-lectern.png` placeholder (its 1024×1160 ratio already matches) — a pure
  additive use of the existing per-item backdrop mechanism. (The fuller 7-backdrop vision — 3 clay
  types × fired/unfired + wax — is deferred to the followup `add-tablet-clay-type-backdrops`, which
  first needs new clay-type/fired variant data on the item and a tile/frame backdrop renderer.)
- Central region this round **keeps the inherited editable task list** (themed, tabless) so the
  tablet does not regress the working editor Proposal B shipped, and adds a **display-only cuneiform
  banner** above it rendering the document's **title** via `CuneiformText`, given an explicit height
  derived from `fontSizeEm` (an `Expanded` is inert inside a scroll view). This proves the font in
  real chrome without removing task editing; the editable-rows-in-cuneiform work is Proposal D.
- One branch point: compute `UseCuneiform` once from the `DisableCuneiformFont` setting. When the
  setting disables cuneiform, the banner renders through the normal text path via
  `ScribeTaskFont.Resolve` instead of `CuneiformText`.
- Switch `ItemScribeTablet.OpenTabletDialog` from `GuiDialogScribeNotebook` to the new
  `GuiDialogScribeTablet`.

## Capabilities

### New Capabilities

- `tablet-dialog`: the bespoke tablet GUI — a no-tabs, always-edit `ScribeDialogBase` subclass with
  its own earthen theme and clay/wax backdrops, whose central region keeps the inherited editable
  task list and adds a display-only cuneiform title banner gated by the disable-cuneiform setting.

### Modified Capabilities

- `clay-wax-tablet-item`: the "Right-click opens the Scribe document dialog" requirement's interim
  scenario changes — the tablet now opens the bespoke `GuiDialogScribeTablet` instead of the
  interim `GuiDialogScribeNotebook`.
- `scribe-dialog-base`: adds one virtual layout-seam extension point (`UseTabletLayout` + a tablet
  content builder) parallel to the existing `GetExtraNavButtons` extension point, letting a subclass
  replace the tabbed central region with its own single-view layout while the default path is
  unchanged.

## Impact

- **New code:** `src/Mod/GuiDialogScribeTablet.cs`.
- **Modified code:** `src/Mod/ScribeDialogBase.Layout.cs` (the new seam), `src/Mod/ScribeTheme.cs`
  (the `Tablet` palette), `src/Mod/ScribeBackdrop.cs` (four tablet specs), `src/Mod/ItemScribeTablet.cs`
  (open the new dialog).
- **Consumes:** `CuneiformText` and `ScribeTaskFont.Resolve` (Proposal A), `ScribeDocumentPolicy`
  and `TabletHost` (Proposal B), the existing per-item backdrop mechanism (`gui-backdrop`).
- **Assets:** no new PNG this round — both clay/wax backdrop slots reuse `scribe-lectern.png`; new
  `scribe:` lang keys for any tablet-dialog label strings.
- **No new dependencies, no new network packets** — the tablet keeps saving through the existing
  `TabletHost` / `ScribeNotebookSaveMessage` write-through.
- **Followup spun off:** the full 7-backdrop vision (3 clay types × fired/unfired + wax, using VS
  pottery textures and/or a custom frame renderer) is a separate proposal
  `add-tablet-clay-type-backdrops`, sequenced after this one archives, because it first requires new
  clay-type/fired variant data on the item and extending `WrapBackdrop` beyond stretch-to-fill.
- **Out of scope (deferred):** the pencil-toggle editable input/output row (Proposal D); the
  clay-type/fired backdrops (the followup above); and all deferred tablet mechanics (firing, water
  damage, carry-forward, wax-wipe, stylus gate).
