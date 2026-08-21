# Design — tablet text visibility

## Context

The tablet dialog (`GuiDialogScribeTablet`) is one parameterized dialog keyed by a material variable;
it must NOT be subclassed per material (project directive). Its colors come from a per-material
`ThemeData` (`ScribeTheme.ForTablet`) and a per-material `CuneiformGlow` (`CuneiformGlowTable.For`),
both selected from the same `material` string. Cuneiform strokes are drawn by `CuneiformTextRender`
(display: title, item-name labels, tracker counts) and `ScribeCuneiformFieldRender` (live editable
rows); both paint an optional two-pass glow (all blurred halos, then all crisp ink) behind the ink.

### Naming: "unfired / wax / fired" vs. the real palette axis

The user framed the problem as three tablets — *unfired/wet*, *wax*, *fired/hardened*. The code has
**two orthogonal axes**, which is worth stating plainly so the recommendations land on the right knob:

- **Material (color) axis — the one that drives color:** `clay-fire`, `clay-red`, `clay-blue`, and
  `wax`. Each maps to its own `ThemeData` (`TabletFire/Red/Blue/Wax`) and glow seed. There are
  **four** palettes, not three.
- **State (drying) axis — orthogonal, does NOT change color:** `Wet` (editable) → `Hard` → `Fired`
  (read-only). A fired `clay-fire` tablet still renders in the `TabletFire` palette; firing changes
  editability and the backdrop wear, not the ink/accent colors.

So "unfired vs. fired" is the same palette in two states, and the readability fix must apply to a
palette regardless of state. This document gives recommendations for all **four material palettes**;
each holds for both the wet and the fired form of that clay. Where the user said "each tablet," read
"each of the four palettes."

## The measurement (why we know what's wrong)

WCAG relative-contrast ratios, computed from the actual `ScribeTheme` `Vector4` values (sRGB
linearized: `L = 0.2126·R + 0.7152·G + 0.0722·B` after gamma expansion; ratio `= (L1+0.05)/(L2+0.05)`).
"Surface" is the tablet face panel (`colors.Surface`); the textured backdrop art samples near the same
value, so these are representative, not exact per-pixel.

| Palette   | Surface (face)         | Body ink (`OnSurface`) | Body : surface | Link today (`= Primary`) | Link : surface |
|-----------|------------------------|------------------------|----------------|---------------------------|----------------|
| clay-fire | `#CCA880` (.80,.66,.50)| `#331A0D` (.20,.10,.05)| **7.4 : 1**    | `#8C4C26` (.55,.30,.15)   | **3.0 : 1**    |
| clay-red  | `#D19E94` (.82,.62,.58)| `#3D1A17` (.24,.10,.09)| **6.7 : 1**    | `#8F4C47` (.56,.30,.28)   | **2.7 : 1**    |
| clay-blue | `#C2D1DB` (.76,.82,.86)| `#1F2933` (.12,.16,.20)| **9.5 : 1**    | `#426B85` (.26,.42,.52)   | **3.7 : 1**    |
| wax       | `#DEC79E` (.87,.78,.62)| `#47381F` (.28,.22,.12)| **6.9 : 1**    | `#9E7D42` (.62,.49,.26)   | **2.3 : 1**    |

**Read this table before choosing.** It changes the framing:

- **Body ink already passes AA (and nearly AAA).** Its raw contrast is 6.7–9.5 : 1. Darkening a
  near-black ink further (Option C's "make body stronger") moves the ratio by a rounding error and is
  perceptually invisible. What actually degrades body legibility today is the **light halo** blurring
  the thin, jittered strokes into a light ground — a rendering defect, not an ink-value problem.
- **Link ink fails AA on every palette (2.3–3.7 : 1), wax worst.** This is the real, measurable
  failure, and it is caused by reusing `colors.Primary` (a mid-value fill color) as small text on a
  same-value ground.

## Research: designing for contrast over a busy, variable ground

Grounded and practical, not exhaustive.

### WCAG contrast targets, and why they matter here

WCAG 2.x defines a contrast *ratio* from 1 : 1 (identical) to 21 : 1 (black on white). The relevant
thresholds:

- **4.5 : 1** — AA for normal-size body text. This is the bar our link text must clear.
- **3 : 1** — AA for *large* text (≈ ≥ 18.66 px bold or 24 px regular) and for UI-component/graphic
  boundaries. Our cuneiform rows render oversized (`LineHeightRatio = 1.848`), so a strict reading
  would let large runs sit at 3 : 1 — but cuneiform is an *unfamiliar* letterform with hand-jitter and
  per-glyph rotation, so the reader has none of the shape-priming that lets them tolerate low contrast
  in a known alphabet. We therefore target the stricter **4.5 : 1** for link text regardless, and treat
  3 : 1 as a hard floor never to dip below.
- **7 : 1** — AAA. Body ink already lands here on three of four palettes; we simply don't regress it.

Contrast ratio matters because legibility is driven by *luminance* difference, not hue difference. Two
colors can be different hues yet nearly equal luminance (our link-vs-clay case) and the text will
"vibrate" and smear rather than read. Getting the luminance step right is the whole game; hue is then
free to signal "this is a link."

### Why a light halo on a light ground fails

A glow/halo is a blurred copy of the glyph drawn *behind* the crisp ink. Its job is to insert a band of
contrasting luminance between the ink and whatever is behind it, so a thin stroke keeps a clean edge
over a noisy background. That only works when the halo's luminance is on the **opposite** side of the
ink from the background:

- Dark ink on a **dark** ground → a **light** halo separates them (this is what the light halo was
  designed for, and the code comment describing "a light halo lifts dark ink" is only true in *this*
  case, which is not ours).
- Dark ink on a **light-mid** ground (our clay) → a light halo sits *between the ink and a ground of
  nearly its own luminance*. It adds no separating step; worse, at the stroke edge the light halo
  bleeds INTO the dark stroke, lightening its border and eroding the very edge contrast it should
  protect. The net effect is a faint bright fringe on light clay (invisible) plus softened, mushier
  ink (a real loss). That is precisely the "makes contrast worse" the user reported.

The fix is to match the halo polarity to the ink: **dark ink wants a dark halo** — i.e. a soft outline
/ drop-shadow that deepens the immediate surround, so the thin stroke reads as a slightly heavier,
firmly-seated mark on the clay.

### Dark-halo / outline / drop-shadow alternatives for text on busy grounds

Standard techniques for legible text over photographic or textured/variable backgrounds, in rough
order of subtlety:

1. **Soft dark halo (blurred outline behind ink).** A short-radius dark blur behind the glyph. Reads as
   a gentle "seating shadow." Cheapest and least fussy; this is what our existing two-pass machinery
   already produces — we only need to flip its color to dark and tighten its radius. **Chosen.**
2. **Hard outline (stroke around the glyph).** Crisp 1–2 px contrasting border. Maximum legibility, but
   heavy and cartoonish at small sizes, and cuneiform's many thin strokes would clot. Would also need
   new rendering (stroke-and-fill), which this change avoids.
3. **Drop shadow (offset dark blur).** A directional offset halo. Good for floating captions; the
   directional offset fights the hand-pressed "engraved into clay" fiction we want, and offset shadows
   read as "UI floating over," not "ink in the surface."
4. **Scrim / plate behind the text block.** A semi-opaque panel behind the whole run. Effective but it
   would cover the clay texture that is the tablet's entire aesthetic point; rejected on look.

A tight, low-alpha *dark* halo (technique 1) is the right trade for an engraved-clay look: it darkens
only the immediate stroke surround, so thin jittered strokes hold their edge without a UI-looking
outline or a texture-hiding plate. Keep it **tight** (small blur sigma) and **moderate alpha** — a wide
dark cloud would read as grime over the mid-tone clay.

### Ink-on-parchment vs. ink-on-clay across the three (four) materials

The materials span a real legibility gradient, which is why one flat treatment won't do:

- **clay-blue** — lightest, coolest face (`#C2D1DB`). Highest headroom (body 9.5 : 1). A dark slate ink
  reads almost like ink on cool paper. Links have the most room to stay chromatic (a saturated blue at
  5 : 1 is easy).
- **clay-fire** — warm tan (`#CCA880`), the classic "clay tablet." Mid headroom. Warm dark browns read
  well; the link must go to a deep rust to clear AA because warm mid accents collapse into the tan.
- **clay-red** — dusty rose (`#D19E94`), the most *saturated* face. The ground itself carries chroma, so
  a rosy accent (today's link) camouflages against it — this is why red's link is 2.7 : 1 despite an OK
  body. The link must shift to a deep wine that is both darker AND more saturated than the rosy ground.
- **wax** — pale honey (`#DEC79E`), warm and *low-contrast by nature* (beeswax is a pale, warm, nearly
  monochrome surface). This is the hardest: a honey accent on a honey ground is the 2.3 : 1 worst case.
  Wax needs the largest link shift (to a deep amber-bronze) and benefits most from the dark halo.

The practical upshot: **body ink is fine on all four** because a near-black mark on a pale-to-mid warm
or cool ground is inherently high-contrast (this is the ink-on-parchment regime). The **link** is where
the material gradient bites, because it must remain a recognizable *accent hue* while still clearing the
luminance bar — hardest on the two low-headroom warm grounds (wax, then fire/red).

## Options considered (the user's three directions)

### Option A — make the glow a DARK glow instead of light

Flip `CuneiformGlowTable` from light halos to dark. **Strong yes**, and it is the single
highest-leverage move: it repairs the aid that is currently *hurting*, and because both the body-ink
renderer and the link-label renderer read the same `style.CuneiformGlow`, one change lifts **both** at
once. On its own it does not fully fix links (a dark halo behind a mid-value link on a mid ground helps
the edges but can't manufacture the missing luminance step in the fill), so A is necessary but not
sufficient for links.

### Option B — layered glows (small light glow over a larger dark glow)

The user's own read: *"I think that has potential to be way too busy, so I don't love that."* We agree
and **reject it.** Two overlaid halos on an already hand-jittered, per-glyph-rotated stroke field is
visual noise, doubles the paint cost, and the light inner glow re-introduces exactly the edge-erosion
Option A removes. A single dark halo carries the whole load.

### Option C — make the link color much darker/stronger AND the body text darker/stronger too

The user's lead pick. **Adopt for links; decline the body half as unnecessary.** The measurement shows
body ink is already 6.7–9.5 : 1 — there is no meaningful "stronger" left in a near-black ink, and the
perceived body weakness is the light halo (fixed by A), not the ink. For **links**, C is exactly right:
today's link reuses `colors.Primary`, a mid-value *fill* color, which is the root cause of the 2.3–3.7
: 1 failure. Setting a dedicated, deeper, more-saturated per-material link ink is the direct fix, and
the seam already exists (`ScribeRowStyle.LinkColor`, used today only by the Chalkboard).

### Recommendation — a hybrid: A (dark glow, shared) + C-for-links (dedicated link ink)

Do **both** high-value halves and drop the low-value ones:

1. **A — dark glow** for all four palettes (the shared fix that repairs body legibility and reinforces
   links).
2. **C-for-links** — a dedicated, AA-clearing per-material `LinkColor` (the targeted fix for the one
   measured failure).
3. **Not** Option B, and **not** darkening body ink (no perceptual payoff).

This is minimal, honors the "one parameterized dialog" directive (values are data, keyed by material),
and touches only three files.

## Concrete recommended values (before → after)

All `Vector4` are RGBA in 0–1; A = 1 unless noted. Contrast ratios are link/body ink vs. that palette's
`Surface`; all recommended link inks were chosen to clear **≥ 4.5 : 1** while staying a distinct accent
hue from the near-black body ink.

### Link ink — `ScribeRowStyle.LinkColor`, set per material in `DecorateRowStyle`

| Palette   | Before (`= colors.Primary`)         | Ratio | After (recommended link ink)                | Hex       | Ratio | Rationale |
|-----------|-------------------------------------|-------|---------------------------------------------|-----------|-------|-----------|
| clay-fire | `Vector4(0.55,0.30,0.15,1)` `#8C4C26`| 3.0   | `Vector4(0.42,0.18,0.07,1)`                 | `#6B2E12` | 4.7   | Deep rust: darker + more saturated than the tan ground; clearly warmer/brighter than the near-black body so it still reads as a link. |
| clay-red  | `Vector4(0.56,0.30,0.28,1)` `#8F4C47`| 2.7   | `Vector4(0.44,0.11,0.11,1)`                 | `#701C1C` | 4.8   | Deep wine: pushes value down AND saturation up so it stops camouflaging against the chromatic rosy ground. |
| clay-blue | `Vector4(0.26,0.42,0.52,1)` `#426B85`| 3.7   | `Vector4(0.15,0.33,0.46,1)`                 | `#265475` | 5.1   | Deep steel-blue: the light cool ground gives headroom, so the link stays a lively, obviously-blue accent well above AA. |
| wax       | `Vector4(0.62,0.49,0.26,1)` `#9E7D42`| 2.3   | `Vector4(0.44,0.28,0.06,1)`                 | `#70470F` | 4.9   | Deep amber-bronze: the largest shift, because honey-on-honey is the worst case; saturated + dark separates it from both the pale ground and the muted-brown body ink. |

### Body ink — `OnSurface` (recommend NO change)

| Palette   | Body ink (`OnSurface`)          | Hex       | Ratio | Recommendation |
|-----------|---------------------------------|-----------|-------|----------------|
| clay-fire | `Vector4(0.20,0.10,0.05,1)`     | `#331A0D` | 7.4   | Keep. Already AAA-adjacent; the dark glow (below) is what sharpens it. |
| clay-red  | `Vector4(0.24,0.10,0.09,1)`     | `#3D1A17` | 6.7   | Keep. |
| clay-blue | `Vector4(0.12,0.16,0.20,1)`     | `#1F2933` | 9.5   | Keep. |
| wax       | `Vector4(0.28,0.22,0.12,1)`     | `#47381F` | 6.9   | Keep. |

If, after the glow flip, the user still wants a body nudge on the two warm palettes for taste, the
only defensible tweak is a *hue/chroma* deepening (not a value change): e.g. fire `#2B1408`
`(0.17,0.08,0.03)`, wax `#3B2E15` `(0.23,0.18,0.08)`. This is optional and cosmetic — the ratios barely
move (they were already high). Do not touch clay-blue (already 9.5 : 1).

### Glow — `CuneiformGlowTable`, light → dark per material

Before (all four ride two light seeds; wax rides fire): light RGB, **alpha 0.30**, **blur fraction
0.117**. After: a soft *dark* halo derived from each palette's own near-black ink, **tighter** so it
reads as a seating outline rather than a cloud, and **wax gets its own seed**.

| Palette   | Before `CuneiformGlow(Color, BlurFraction)`              | After (recommended)                                        | Note |
|-----------|----------------------------------------------------------|------------------------------------------------------------|------|
| clay-fire | `((0.98,0.94,0.85, 0.30), 0.117)` light                  | `((0.20,0.10,0.05, 0.55), 0.060)` dark (fire ink)          | Tight dark halo = soft engraved shadow; ink-derived so it stays in-hue. |
| clay-red  | `((0.98,0.92,0.88, 0.30), 0.117)` light                  | `((0.24,0.10,0.09, 0.55), 0.060)` dark (red ink)           | Same. |
| clay-blue | `((0.95,0.97,0.99, 0.30), 0.117)` light                  | `((0.12,0.16,0.20, 0.55), 0.060)` dark (blue-slate ink)    | Same. |
| wax       | *(rode fire: light `((0.98,0.94,0.85,0.30),0.117)`)*     | `((0.28,0.22,0.12, 0.55), 0.060)` dark (wax ink) — NEW seed| Wax needs the most help; give it its own dark seed rather than the fire twin. |

Tuning guidance for the in-game pass: **alpha 0.55** and **blur fraction 0.060** are the starting
point. If the halo reads as grime, drop alpha toward 0.40; if strokes still smear on the noisiest
backdrop patches, raise toward 0.65 before widening the blur. Keep blur fraction in **0.05–0.08** — the
value is a soft outline, not an aura; the old 0.117 was tuned for a *light* aura and is too wide for a
dark outline. These are baked constants (like the jitter/reveal constants), not user settings.

Note the two-pass renderer (halos first, crisp ink second, ink overwrites overlaps) already guarantees
a dark halo won't darken *inside* the glyph — the crisp ink covers it — so the dark halo shows only as a
thin darkened fringe where it spills onto the clay. This is exactly the desired outline behavior and
requires **no rendering-code change**, only the data values above.

## Risks / trade-offs

- **Dark halo reading as grime.** Mitigated by the tight radius + moderate alpha; the in-game tuning
  pass (task 5) confirms per material. The `.cuneiformglow`-style dev command (already specced) makes
  this a quick dial-in.
- **Link ink drifting toward body ink.** On wax the recommended link (`#70470F`) is only ~1.4× the body
  ink's luminance, so the *hue/chroma* distinction (saturated amber vs. muted brown) carries the "this
  is a link" signal more than luminance does. Acceptable and verified distinct; wax is inherently the
  hardest and there is no honey-family link that is both high-luminance-contrast AND obviously a link.
- **Colorblind consideration.** Link-vs-body relies partly on hue on the warm palettes. Because the
  link is also *darker and higher-chroma* than the body on every palette, a colorblind reader still gets
  a luminance/saturation cue, not hue alone. The underline/hover affordance that already marks links is
  the non-color backstop.
- **Scope creep into other surfaces.** All values are gated to the tablet's Pixel-Art path and keyed by
  `material`; Lectern/Notebook/Chalkboard/HUD are untouched (they set their own `LinkColor`/glow or
  none).
