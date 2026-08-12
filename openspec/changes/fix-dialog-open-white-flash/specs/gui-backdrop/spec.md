## ADDED Requirements

### Requirement: Opening a backdropped dialog does not drop the world's opaque terrain pass

Opening a Scribe dialog that paints a pixel-art backdrop (Lectern, Notebook, Clockmaker's Notebook,
or Tablet) SHALL NOT cause the world's opaque chunk-terrain pass to drop for any frame. The backdrop
and dialog content already render correctly on open; this requirement additionally forbids the
one-frame "white flash" in which near terrain vanishes (sky shows through) behind the dialog while
the dialog itself renders pixel-identically. The guarantee is mechanism-agnostic: however the
backdrop bitmap is decoded, uploaded, or kept resident, no cold per-open GPU work may land on a live
frame in a way that blanks the terrain pass.

#### Scenario: Opening a backdropped dialog leaves terrain intact

- **WHEN** the player opens the Lectern, Notebook, Clockmaker's Notebook, or a Tablet with
  `PixelArtDisplay` ON, aimed so near terrain is visible behind the dialog
- **THEN** every frame from open onward shows the near terrain behind the dialog (no frame in which
  the opaque terrain pass is missing and sky shows through), and the backdrop plus dialog content
  render correctly

#### Scenario: Repeated opens do not re-introduce the flash

- **WHEN** the player opens and closes a backdropped Scribe dialog several times in a session
- **THEN** no open exhibits the terrain-pass dropout — the backdrop texture stays resident (or is
  otherwise uploaded off the critical frame) rather than being evicted and cold-uploaded on each open

#### Scenario: Non-backdropped surfaces remain unregressed

- **WHEN** the player opens the `GuiBase`-derived Scribe Settings window or a `.ui` showcase window
  (neither paints the pixel-art parchment backdrop)
- **THEN** they open without any terrain-pass dropout, exactly as before this change
