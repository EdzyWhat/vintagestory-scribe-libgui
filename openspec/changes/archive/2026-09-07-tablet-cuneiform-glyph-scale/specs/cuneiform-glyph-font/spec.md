## MODIFIED Requirements

### Requirement: A custom render widget paints cuneiform strokes on the Skia canvas

The mod SHALL provide a custom LibGUI render widget that paints a laid-out line's stroke rectangles
by filling each stroke's oriented quad on the LibGUI Skia canvas, scaling grid units to pixels by
the requested em size, using the color supplied by the active theme. It SHALL size itself to the
laid-out line and SHALL guard against a null canvas.

The widget SHALL support an optional **glyph draw scale** — a multiplier folded into the em height
BEFORE the widget's existing line-height formula derives its grid-to-pixel scale — that shrinks or
grows the widget's reserved layout `Size` (advance width and line height) and, following from that,
its rendered ink together. A draw scale of `1.0` SHALL reproduce today's behavior exactly. This is
independent of the tablet's checkbox/control sizing, which is derived separately and is NOT
multiplied by this scale — the two are permitted to differ. The same draw-scale mechanism SHALL apply
to the editable cuneiform field's render path (`ScribeCuneiformFieldRender`), and that field's caret
position, selection highlight, and hit-testing SHALL stay exactly consistent with the drawn ink at
every draw scale and at every buffer length (no divergence as text is typed).

#### Scenario: A line of text renders as filled stroke quads

- **WHEN** the widget is given a string and an em size and painted
- **THEN** it fills each stroke's four-corner quad on the canvas, scaled from grid units to pixels
- **AND** the widget's measured size matches the laid-out line's total advance and line height

#### Scenario: Painting is skipped when the canvas is unavailable

- **WHEN** the widget is painted while the canvas is null
- **THEN** it returns without throwing

#### Scenario: A non-unit draw scale shrinks layout and ink together

- **WHEN** the widget is given a draw scale less than `1.0`
- **THEN** its measured layout `Size` (advance width and line height) is proportionally smaller than
  at draw scale `1.0`
- **AND** the rendered stroke quads are proportionally smaller by that same factor, filling the
  smaller layout box exactly as the unscaled ink fills the unscaled box

#### Scenario: Unit draw scale is a no-op

- **WHEN** the draw scale is `1.0`
- **THEN** the widget renders exactly as it did before this change

#### Scenario: The editable field shares the same draw scale

- **WHEN** the editable cuneiform field (`ScribeCuneiformFieldRender`) is given a non-unit draw scale
- **THEN** its rendered glyph strokes and reserved layout height shrink or grow by the same factor as
  the read-only render widget

#### Scenario: The editable field's caret tracks the draw scale at any buffer length

- **WHEN** the editable cuneiform field is given a non-unit draw scale and text is typed into it,
  character by character
- **THEN** the caret's rendered position continues to land exactly at the boundary of the character
  it follows, with no growing or shrinking offset as more characters are typed
