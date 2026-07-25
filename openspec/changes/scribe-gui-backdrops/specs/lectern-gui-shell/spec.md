## MODIFIED Requirements

### Requirement: Lectern dialog uses a per-view illustrated backdrop
The lectern's GUI dialog SHALL render its backdrop through the general per-item, per-view backdrop
mechanism (the `gui-backdrop` capability) rather than a single native-GUI custom-drawn panel. The
lectern SHALL declare distinct backdrop specifications for its read/editor page and for its settings
page, and each view SHALL draw its own backdrop behind the dialog content. Backdrops SHALL be drawn only
when the `ThemedBackgrounds` preference is ON; when it is OFF the dialog SHALL render its content bare
under the plain LibGUI fallback with no backdrop. Because each backdrop is named by an `AssetLocation`
and drawn from a self-loaded, cached bitmap, swapping a view's art SHALL require replacing only that
view's PNG — no change to `GuiDialogScribeLecternLibGui`'s layout or composition logic. When a view's
PNG is missing or unloadable, the view SHALL draw a flat placeholder color so the structure remains
visible before art exists.

#### Scenario: Opening the lectern shows the read/editor backdrop
- **WHEN** a player opens a lectern with `ThemedBackgrounds` ON
- **THEN** the read/editor page draws its declared backdrop (or its placeholder color while art is
  pending) behind the dialog content, rather than the engine's default shaded dialog panel

#### Scenario: The settings page shows a distinct backdrop
- **WHEN** the player switches the open lectern to its settings page with themed mode ON
- **THEN** the settings page draws a backdrop from a different specification than the read/editor page

#### Scenario: Backdrop is swappable without a code change
- **WHEN** a view's backdrop PNG is replaced with a different image (of any dimensions)
- **THEN** that view renders the new backdrop with no changes required to
  `GuiDialogScribeLecternLibGui`'s layout or composition logic

#### Scenario: Themed-off lectern draws no backdrop
- **WHEN** the player opens the lectern with `ThemedBackgrounds` OFF
- **THEN** neither the read/editor page nor the settings page draws a backdrop, and both render as the
  plain LibGUI fallback
