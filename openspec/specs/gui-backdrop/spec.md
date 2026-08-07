# gui-backdrop Specification

## Purpose
TBD - created by archiving change scribe-gui-backdrops. Update Purpose after archive.
## Requirements
### Requirement: Each item and view declares its own backdrop art of any size
The mod SHALL model a Scribe dialog backdrop as a per-item, per-dialog specification that names a texture
asset by `AssetLocation` and makes NO assumption about the art's pixel dimensions. Each item (Lectern,
plain Notebook, and Clockmaker's Notebook now; Desk / Clay Tablet later) SHALL be able to declare its own
backdrop specification. Adding a new item's backdrop SHALL require only a new specification plus its PNG —
no change to the dialog's backdrop-wrapping logic or the bitmap cache.

#### Scenario: An item declares one or more backdrops
- **WHEN** a new item's backdrop is added to the backdrop-specification holder
- **THEN** the item contributes one specification naming its own texture `AssetLocation`, without any
  shared-size constraint or change to the backdrop-wrapping logic or the bitmap cache

#### Scenario: A backdrop of any dimensions is accepted
- **WHEN** a backdrop specification names a PNG whose dimensions differ from another item's PNG
- **THEN** both are handled by the same dialog backdrop-wrapping logic, and neither is required to match
  a fixed or shared backdrop size

#### Scenario: Distinct items render distinct art
- **WHEN** the player opens the Lectern, the plain Notebook, and the Clockmaker's Notebook with
  `PixelArtDisplay` ON
- **THEN** each draws its own declared backdrop specification, even where two items share an underlying
  dialog host

### Requirement: A backdrop is drawn behind dialog content when themed mode is on
When the `PixelArtDisplay` preference is ON, a Scribe dialog view SHALL draw its declared backdrop
behind the dialog's content. The backdrop SHALL be composed by wrapping the view's content in a LibGUI
`Container` whose `BoxStyle` paints the backdrop, so the art sits behind the content automatically
without a separate `Stack` layer. The content SHALL remain fully interactive over the backdrop.

#### Scenario: Themed-on view shows its backdrop behind content
- **WHEN** the player opens a Scribe dialog view with `PixelArtDisplay` ON
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
When the `PixelArtDisplay` preference is OFF, a Scribe dialog view SHALL NOT draw any backdrop. The
view's content SHALL be used bare (no backdrop `Container` wrap), yielding the plain LibGUI fallback
appearance with zero art required.

#### Scenario: Themed-off view draws no backdrop
- **WHEN** the player opens a Scribe dialog view with `PixelArtDisplay` OFF
- **THEN** no backdrop texture or placeholder color is drawn behind the content, and the view renders
  as the plain LibGUI fallback

### Requirement: A backdrop spec may tint its texture

A backdrop specification MAY declare an optional tint color multiplied into its texture. When a tint is
declared, the dialog backdrop-loading logic SHALL bake that tint into a cached copy of the decoded bitmap
(an `SKColorFilter` modulate — the same tint primitive the GUI framework's icon renderer uses) and render
the tinted copy through the existing stretch-to-fill texture path, so the same source PNG can back several
visually-distinct specs without additional art. A backdrop specification that declares no tint (every
full-page illustration spec) SHALL be rendered from the decoded bitmap unchanged through the existing
stretch-to-fill path.

Note: an earlier draft of this requirement anticipated tiling a small vanilla material swatch at native
resolution plus a composited page-frame overlay. Implementation found (a) the authored clay backdrops are
full-page illustrations that take the existing stretch path directly, so no tiling was needed, and (b) the
GUI framework's `BoxStyle` texture path only ever stretches one bitmap to fill and exposes no tint, and it
lives in the read-only `gui` dependency, so tiling/frame-overlay could not be added there. The tint is
therefore baked at the bitmap level and the tiling/overlay machinery was dropped — the full-page authored
art is the design's own stated target state, reached directly.

#### Scenario: An optional tint distinguishes same-source specs

- **WHEN** two backdrop specs name the same source PNG but declare different tint colors
- **THEN** each renders that PNG in its own tint so the two are visually distinguishable

#### Scenario: Full-page specs are unchanged

- **WHEN** an existing full-page backdrop spec (declaring no tint) is drawn
- **THEN** it renders through the existing stretch-to-fill path exactly as before

### Requirement: A textured backdrop always renders at full opacity

A themed-mode textured backdrop SHALL render at the opacity authored into its PNG, independent of what any
prior frame drew. The backdrop-wrapping logic SHALL guarantee this even though the underlying GUI framework
reuses a single shared paint across draw operations and across frames and its textured-box draw op reuses
that paint's color without re-setting it — so an unguarded backdrop would be modulated by whatever color the
previous frame's last draw op happened to leave (e.g. a read-only view whose last painted element is a
low-alpha scrollbar track, which uniformly faded the backdrop). The guarantee SHALL hold for every themed
view regardless of which element paints last, and SHALL NOT alter the appearance of any view that was
already rendering correctly.

#### Scenario: A read-only view's backdrop is fully opaque

- **WHEN** the player opens a themed-mode view whose last-painted element is a low-alpha element (such as
  the always-visible scrollbar track on a read-only tablet)
- **THEN** the backdrop renders at its authored opacity rather than being modulated toward transparency by
  the prior frame's residual paint color

#### Scenario: Correctly-rendering views are unchanged

- **WHEN** the player opens a themed-mode view that already rendered its backdrop opaquely (such as the
  editor or a tabbed Lectern/Notebook view that paints an opaque element last)
- **THEN** its backdrop appearance is unchanged

