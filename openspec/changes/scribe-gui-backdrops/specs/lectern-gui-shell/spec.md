## MODIFIED Requirements

### Requirement: Lectern dialog uses an illustrated backdrop
The lectern's GUI dialog SHALL render its backdrop through the general per-item, per-view backdrop
mechanism (the `gui-backdrop` capability) rather than a single native-GUI custom-drawn panel. The lectern
SHALL back its dialog body (its read and editor views) with a backdrop specification, drawn behind the
dialog content. Backdrops SHALL be drawn only when the `PixelArtDisplay` preference is ON; when it is OFF
the dialog SHALL render its content bare under the plain LibGUI fallback with no backdrop. Because each
backdrop is named by an `AssetLocation` and drawn from a self-loaded, cached bitmap, swapping the art
SHALL require replacing only that PNG — no change to `GuiDialogScribeLecternLibGui`'s layout or
composition logic. When the PNG is missing or unloadable, the dialog SHALL draw a flat placeholder color
so the structure remains visible before art exists.

> NOTE (2026-07-26): as shipped, the lectern exposes a single backdrop-bearing body — its read and editor
> views share one `LecternPage` spec. The originally-planned distinct settings-page backdrop does not
> apply: the in-dialog settings view was removed in the 2026-07-25 pivot (the gear now opens the standalone
> settings window, which deliberately follows the global theme and is not backdrop-wrapped). The
> mechanism's per-view capability (see the `gui-backdrop` capability) is retained for a future item that
> exposes both a page and a settings view; the `LecternSettings` spec is defined and reserved for that.

#### Scenario: Opening the lectern shows its backdrop
- **WHEN** a player opens a lectern with `PixelArtDisplay` ON
- **THEN** the dialog body (read and editor views) draws its declared backdrop (or its placeholder color
  while art is pending) behind the content, rather than the engine's default shaded dialog panel

#### Scenario: Backdrop is swappable without a code change
- **WHEN** the lectern's backdrop PNG is replaced with a different image (of any dimensions)
- **THEN** the dialog renders the new backdrop with no changes required to
  `GuiDialogScribeLecternLibGui`'s layout or composition logic

#### Scenario: Themed-off lectern draws no backdrop
- **WHEN** the player opens the lectern with `PixelArtDisplay` OFF
- **THEN** neither the read nor the editor view draws a backdrop, and both render as the plain LibGUI
  fallback
