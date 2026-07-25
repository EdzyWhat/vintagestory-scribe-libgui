## ADDED Requirements

### Requirement: Each item and view declares its own backdrop art of any size
The mod SHALL model a Scribe dialog backdrop as a per-item, per-view specification that names a texture
asset by `AssetLocation` and makes NO assumption about the art's pixel dimensions. Each item (Lectern
now; Desk / Notebook / Clay Tablet later) SHALL be able to declare its own backdrop specifications, and
within an item the read/editor page and the settings page SHALL each be able to reference a different
specification. Adding a new item's backdrops SHALL require only new specifications plus their PNGs — no
change to the drawing helper or the cache.

#### Scenario: An item declares distinct page and settings backdrops
- **WHEN** a new item's backdrops are added to the backdrop-specification holder
- **THEN** the item contributes at least two specifications (a page/read-editor spec and a settings
  spec), each naming its own texture `AssetLocation`, without any shared-size constraint or change to
  the `Wrap` helper or the bitmap cache

#### Scenario: A backdrop of any dimensions is accepted
- **WHEN** a backdrop specification names a PNG whose dimensions differ from another view's PNG
- **THEN** both are handled by the same drawing helper, and neither is required to match a fixed or
  shared backdrop size

### Requirement: A backdrop is drawn behind dialog content when themed mode is on
When the `ThemedBackgrounds` preference is ON, a Scribe dialog view SHALL draw its declared backdrop
behind the dialog's content. The backdrop SHALL be composed by wrapping the view's content in a LibGUI
`Container` whose `BoxStyle` paints the backdrop, so the art sits behind the content automatically
without a separate `Stack` layer. The content SHALL remain fully interactive over the backdrop.

#### Scenario: Themed-on view shows its backdrop behind content
- **WHEN** the player opens a Scribe dialog view with `ThemedBackgrounds` ON
- **THEN** the view's declared backdrop is painted behind the view's widgets, and the widgets remain
  visible and interactive in front of it

### Requirement: A missing backdrop asset draws a flat placeholder color
When a view's backdrop PNG is missing or cannot be loaded, the dialog SHALL draw a flat placeholder
color in place of the texture rather than failing. The dialog SHALL NOT crash, SHALL render the rest of
its structure normally over the placeholder, and SHALL log exactly one warning for the unloadable asset
(not one per frame or per open). This guarantees the full dialog structure is visible and testable
in-game before any art exists.

#### Scenario: Missing PNG falls back to a flat color
- **WHEN** a view's backdrop PNG is absent or fails to decode and the player opens that view with
  themed mode ON
- **THEN** the view draws a flat placeholder color behind its content, the dialog does not crash, and
  the structure renders normally over the placeholder

#### Scenario: An unloadable backdrop logs a single warning
- **WHEN** a backdrop asset cannot be loaded
- **THEN** the failure is logged once for diagnosis, not repeatedly on every frame or every dialog open

### Requirement: The read/editor page and the settings page render distinct backdrops
Within a single item, the read/editor page view and the settings page view SHALL render backdrops from
different specifications, so the two pages are visually distinct even before final art is drawn.

#### Scenario: Switching to settings shows a different backdrop
- **WHEN** the player switches a Scribe dialog from its read/editor page to its settings page with
  themed mode ON
- **THEN** the settings page's backdrop is drawn from a different specification than the read/editor
  page's backdrop (a distinct texture, or a distinct placeholder color while art is pending)

### Requirement: Backdrop bitmaps are self-loaded, shared, and cached
Backdrop bitmaps SHALL be self-loaded on the mod system using `TryGet(loc, loadAsset: true)` so they
survive Vintage Story's post-startup asset unload (the same trap the SVG icon loader avoids), decoded to
an `SKBitmap`, and cached — including a null/absent result — keyed by asset location. Every dialog open
of a given backdrop SHALL reuse the one cached bitmap rather than reloading or re-decoding it; a dialog
SHALL NOT dispose a backdrop bitmap. All cached bitmaps SHALL be disposed when the mod system is
disposed.

#### Scenario: A backdrop drawn after asset unload still renders
- **WHEN** a backdrop is drawn some time after client init, after the engine has unloaded asset data
- **THEN** the bitmap is re-resolved via the self-loading path and renders, rather than being null and
  drawing nothing

#### Scenario: Repeated opens reuse one cached bitmap
- **WHEN** the same backdrop view is opened, closed, and reopened
- **THEN** the bitmap is loaded and decoded at most once and reused from the cache on subsequent opens,
  and closing a dialog does not dispose the shared bitmap

#### Scenario: Bitmaps are disposed on mod-system dispose
- **WHEN** the mod system is disposed
- **THEN** every cached backdrop bitmap is disposed

### Requirement: No backdrop is drawn when themed mode is off
When the `ThemedBackgrounds` preference is OFF, a Scribe dialog view SHALL NOT draw any backdrop. The
view's content SHALL be used bare (no backdrop `Container` wrap), yielding the plain LibGUI fallback
appearance with zero art required.

#### Scenario: Themed-off view draws no backdrop
- **WHEN** the player opens a Scribe dialog view with `ThemedBackgrounds` OFF
- **THEN** no backdrop texture or placeholder color is drawn behind the content, and the view renders
  as the plain LibGUI fallback
