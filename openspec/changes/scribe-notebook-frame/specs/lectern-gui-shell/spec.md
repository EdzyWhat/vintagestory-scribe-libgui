## MODIFIED Requirements

### Requirement: Lectern dialog uses a portrait, custom-drawn backdrop
The lectern's GUI dialog (both read view and editor view) SHALL be an art-sized outer box (the
`OuterArtBox`) whose width is the layout's driving width `W` and whose height matches the backdrop art's
aspect ratio (`H = W × 1160/1024`), rendering the custom-drawn backdrop image filling that box without
distortion in place of the engine's default shaded dialog panel. The dialog window SHALL be sized to the
`OuterArtBox` and SHALL be non-resizable so the art cannot be stretched off-aspect. The functional views
(read and editor) SHALL be laid out INSIDE the `OuterArtBox` so the backdrop art frames the functional
content rather than being filled edge to edge by it. When the backdrop preference is OFF, the box SHALL be
used without the texture (the existing fallback), and when the art asset is missing it SHALL fall back to a
flat placeholder color.

#### Scenario: Opening the lectern shows a portrait, skinned dialog
- **WHEN** a player right-clicks or shift+right-clicks a placed lectern with the backdrop enabled
- **THEN** the opened dialog is taller than it is wide, its background is the custom backdrop image
  rendered without distortion (not stretched off its native aspect ratio), and the functional read/editor
  content is laid out inside the box with the backdrop art visible framing it

#### Scenario: Backdrop is swappable without a code change
- **WHEN** the backdrop asset is replaced with a different image of the SAME aspect ratio
- **THEN** the dialog renders the new backdrop with no changes required to the dialog's layout or
  composition logic

#### Scenario: The window is not resizable
- **WHEN** the player attempts to resize the lectern dialog window
- **THEN** the window does not resize, so the backdrop art's aspect ratio (and therefore its
  distortion-free rendering) is preserved

## ADDED Requirements

### Requirement: The lectern lays its content out proportionally from one driving width
The lectern dialog's layout SHALL be derived from a single driving width `W` (the "Pixel Art Size"): every
structural region's size SHALL be expressed as a proportion of `W` (or of `H = W × 1160/1024`). The
`OuterArtBox` SHALL contain, stacked top to bottom, a `TitleBar` band of height `0.13 × H` and a
`SectionInnerBox` of `0.9 × W` by `0.8 × H` (centered horizontally), leaving the remaining vertical space
as bottom margin. Changing `W` SHALL rescale the entire layout consistently.

#### Scenario: All regions scale with the driving width
- **WHEN** the Pixel Art Size `W` changes
- **THEN** the outer box, title bar, inner section, and its columns all resize in proportion, preserving
  their relative ratios and the framed appearance

### Requirement: The lectern has a draggable title bar with title text and SVG buttons
The `TitleBar` band SHALL be the dialog's draggable region (click-drag within it moves the window). It SHALL
contain a bottom-anchored, horizontally-centered `TitleTextButtons` row (`0.75 × W` wide, `0.065 × H` tall)
holding the dialog's title text on the left (rendered at the window text size scaled by ×1.1) and a
right-aligned group of icon buttons drawn from the mod's custom SVGs. The group SHALL include a close button
that reuses the delete SVG at 1.4× the delete control's size. Each button SHALL provide a tooltip. Closing
and dragging SHALL work without relying on the stock window frame.

#### Scenario: The title bar drags the window and closes it
- **WHEN** the player click-drags inside the title bar band
- **THEN** the window moves; and clicking the close button (the 1.4× delete SVG) closes the dialog

#### Scenario: Title text and buttons are laid out and labeled
- **WHEN** the lectern opens
- **THEN** the title text sits on the left of the bottom-anchored centered row at window-text ×1.1, the SVG
  button group sits on the right, and hovering any button shows its tooltip

### Requirement: The inner section is a three-column layout framing the scrolling content
The `SectionInnerBox` SHALL be a row of three full-height columns: a left spacer column (`0.0675 × W`), a
tasks column (`0.765 × W`) that hosts the existing scrollable read/editor content, and a right column
(`0.0675 × W`) holding a vertical stack of icon buttons for navigation (Scribe Settings, Read view, Edit
view, Pinned tasks). The navigation buttons SHALL be icon-only and SHALL each provide a tooltip. The three
column widths SHALL sum to the inner box width so no column overflows.

#### Scenario: The scrolling content sits in the center column framed by side columns
- **WHEN** the lectern opens
- **THEN** the existing task/note scroll region renders in the center column, with the left spacer and the
  right icon-button column on either side, all within the framed inner section

#### Scenario: The right column exposes tooltipped navigation icons
- **WHEN** the player hovers a button in the right column
- **THEN** its tooltip appears, and activating it performs its navigation (open settings, switch to read,
  switch to edit, or show pinned tasks)
