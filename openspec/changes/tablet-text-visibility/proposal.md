## Why

The tablet dialogs render task text as cuneiform strokes over a mid-tone, textured clay/wax GUI
backdrop. Two readability problems surfaced after `enable-tablet-row-links` shipped clickable
Link/Tracker/Craft item-name text:

1. **Link text is nearly invisible.** A tablet row's tappable item name resolves its color as
   `style.LinkColor ?? colors.Primary` (`ScribeEditorContent.cs:878/891`, mirrored in
   `ScribeReadContent.cs:356` and `ScribePinnedContent.cs:469`). The tablet's `DecorateRowStyle`
   (`GuiDialogScribeTablet.cs:185`) never sets `LinkColor`, so it falls through to `colors.Primary`
   — each clay palette's mid-value, mid-saturation accent (terracotta / rose / steel / honey). That
   accent reads as a button *fill* but, as small *text* on a same-value clay ground, it disappears.
   Measured WCAG contrast of link-on-surface is **2.3–3.7 : 1** across the four palettes — all below
   the 4.5 : 1 AA floor for body text (see `design.md`).

2. **The contrast aid is backwards.** The existing lift is a **light** outer glow/halo
   (`CuneiformGlowTable`, alpha 0.30, light RGB per material). A light halo behind dark ink on a
   light-mid ground softens the stroke edges into the background instead of separating them — it
   makes contrast *worse*, the opposite of a halo's purpose. The glow comment even states its intent
   was "a light halo lifts dark ink" — which is the mis-reasoning; a light halo lifts *light* ink on a
   *dark* ground, not the reverse.

This change is the deferred ink-contrast decision the project parked when the tablet shipped its
placeholder earthen palette ("real ink-contrast decision … deferred until the material backdrops
render"). The backdrops now render, so we make the call.

**This artifact set is a DESIGN PROPOSAL for review — no implementation is included.** The user asked
to see before/after options per material and pick a direction before any code is written.

## What Changes

Recommended direction (full rationale, rejected options, and per-material values in `design.md`):

- **Flip the glow polarity from light to dark (user's Option A), as the single shared fix.** A soft,
  tight *dark* halo derived from each palette's own near-black ink acts as a drop-shadow / soft
  outline that thickens and separates the thin, jittered strokes from the clay. This lifts **both**
  body ink and link ink at once, and it is the highest-leverage change because it fixes the aid that
  is currently actively hurting.
- **Give the tablet a distinct, darker/stronger per-material LINK ink (user's Option C, for links).**
  Set `ScribeRowStyle.LinkColor` per material in the tablet's `DecorateRowStyle` (the same seam the
  Chalkboard already uses via `ScribeTheme.ChalkboardLinkText`) to a deeper, more saturated tone that
  clears 4.5 : 1 AA on its backdrop while staying chromatically distinct from the near-black body ink,
  so a link still reads *as a link*.
- **Leave body ink largely as-is.** Measured body-ink contrast is already **6.7–9.5 : 1** on every
  palette — it passes AA/AAA. The body-text problem the user perceives is the light halo eroding the
  strokes, not the ink value; the glow flip resolves it without darkening near-black ink further
  (which would buy almost nothing perceptually). This is a small, honest refinement of Option C.
- **Reject Option B (layered light-over-dark glows)** — matches the user's own instinct ("too busy");
  a single dark halo already carries the load.

Concrete recommended values (hex + `Vector4`) for each of the four tablet palettes — link ink, body
ink, and the dark-glow color/strength/blur — are tabulated in `design.md`. All values are
in-game-tunable constants, in the same manner as the existing glow/jitter constants; none is a
persisted user setting.

Non-goals: no new rendering code (the two-pass glow machinery is unchanged — only its color polarity
and per-material seed values change; the link path already exists and only needs its `LinkColor` set);
no `Core` change; no new dependency; no change to the cuneiform geometry, jitter, rotation, or reveal;
no change to the non-tablet (Lectern/Notebook/Chalkboard/HUD) surfaces.

## Capabilities

### Modified Capabilities

- `cuneiform-contrast-glow`: The per-material glow's light-vs-dark polarity for the light-ish clay/wax
  palettes SHALL be **dark** (a soft dark halo behind dark ink), correcting the current light halo that
  reduces edge contrast on a light-mid ground.
- `tablet-dialog`: The tablet's own theme SHALL supply a distinct, AA-legible **link ink** per material
  for a Link/Tracker/Craft row's tappable content, rather than falling through to the theme accent
  (`colors.Primary`).

## Impact

- `src/Mod/CuneiformGlow.cs` — retune `CuneiformGlowTable` seeds from light halos to dark halos
  (per-material ink-derived color, higher alpha, tighter blur fraction); add a wax-specific seed so wax
  no longer rides the fire glow.
- `src/Mod/ScribeTheme.cs` — add per-material tablet link inks (four `Vector4` constants) and a
  `ForTabletLink(material)` selector, mirroring `ChalkboardLinkText` / `ForTablet`.
- `src/Mod/GuiDialogScribeTablet.cs` — in `DecorateRowStyle`, set `LinkColor = ScribeTheme.ForTabletLink(_material)`
  on the Pixel-Art path.
- No change to `ScribeCuneiformField.cs` / `CuneiformText.cs` (glow is data-driven), `ScribeRowWidgets.cs`,
  the read/editor/pinned content builders (they already honor `style.LinkColor` and `style.CuneiformGlow`),
  `Core`, or any asset.
