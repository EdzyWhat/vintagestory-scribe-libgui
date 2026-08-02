## ADDED Requirements

### Requirement: Selection highlight over cuneiform strokes

The tablet's cuneiform render SHALL paint a selection-highlight box over the glyphs
corresponding to the current selection range in `ScribeMultilineFieldState`.

#### Scenario: Selecting a range highlights the cuneiform strokes

- **WHEN** a text range is selected in an editable cuneiform row or title
- **THEN** a highlight box is drawn behind the cuneiform strokes spanning exactly that range
