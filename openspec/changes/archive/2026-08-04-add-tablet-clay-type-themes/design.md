## Context

The tablet dialog resolves a single `ScribeTheme.Tablet` earthen palette for all clay types via
`GuiDialogScribeTablet.ResolveTheme(pixelArt) => ScribeTheme.ForTablet(pixelArt)`. The `material`
variant (`clay-red`/`clay-blue`/`clay-fire`/`wax`) flows only into backdrop selection
(`ScribeBackdrops.ForTablet(material, fired)`), never into theming. All descendants recolor for free by
reading `Theme.Of(context).ColorScheme`, and every per-widget style (buttons, checkboxes, inputs)
cascades from the 17-role `ColorScheme` through `*.Default(colors)` factories — so authoring per-material
schemes is sufficient to recolor the whole tablet without touching widget code.

Cuneiform strokes are painted as filled `SKPath` quads directly on the raw Skia canvas in
`CuneiformTextRender.PaintInternal` (`src/Mod/CuneiformText.cs`) and `ScribeCuneiformFieldRender`
(`src/Mod/ScribeCuneiformField.cs`), using the shared `PaintingContext.SharedPaint`. There is no
per-glyph shadow/glow today. LibGUI's own text renderer already does a glow by drawing a blurred copy
underneath (`PaintingContext.DrawText` with `GetBlurFilter`), and `RenderBox` box-shadows use the cached
`GetOrCreateBlurMask(sigma)` — establishing both the primitive and the cache-don't-dispose discipline.

Sampled center-region backdrop colors (the region text sits over): red `#aa6f6d`, blue `#98a6af`,
fire `#ccaf89` — all mid-tone, which is why a contrast boost is warranted. This design realizes the
long-deferred decision recorded in the `tablet-theme-contrast-vs-backdrops` memory note: option (3),
per-material ink, plus a shadow/glow boost.

## Goals / Non-Goals

**Goals:**
- Each clay type reads as its own material: ink, accent (and everything it drives), input
  background/border, and panel background harmonize with that clay's backdrop.
- Cuneiform ink stays clearly legible over the mid-tone textured clay via a soft per-stroke glow that
  does NOT stack/darken where strokes overlap within a glyph.
- Fast in-game tuning of the glow via a dev console command, so tuned constants can be found and
  reported back before being baked in.
- Zero change to: the Pixel-Art-off path (global theme), the Lectern/Notebook parchment theme, Core,
  dependencies, assets, and persistence.

**Non-Goals:**
- Runtime pixel-sampling of the PNGs (colors are authored constants, sampling-informed).
- A user-facing contrast setting (the effect is fixed/tuned; only a dev command exists).
- Fired/hard-state theming or wax-specific art (wax rides the fire palette as today).
- Per-material theming when Pixel-Art is OFF (explicitly kept as global-theme).

## Decisions

### D1: Three authored per-material palettes, selected by `material`
Add `TabletRed`, `TabletBlue`, `TabletFire` `ThemeData` values in `ScribeTheme.cs`, each authored
role-by-role from the same rules as `Tablet`/`Light` (including the two semantic hover/select
inversions). Replace `ForTablet(bool pixelArt)` with `ForTablet(string? material, bool pixelArt)`:
returns the per-material palette when `pixelArt` is on, else `ThemeData.Default` (unchanged off-path).
`wax` and any unrecognized material map to the fire palette (its interim backdrop twin), mirroring
`ScribeBackdrops.ForTablet`'s default arm so theme and backdrop agree.

*Alternative considered:* runtime auto-sampling of `-soft.png` in `GetBackdropSource`. Rejected —
derived colors can land at poor legibility, need contrast guardrails and a fallback, and add
asset-load-timing risk for no benefit when the art is fixed. Authored constants are deterministic and
directly tunable.

### D2: Which roles vary per material (and which ride `Primary`/`Secondary` for free)
Author per material: `OnSurface`/`OnBackground` (ink), `Primary` (accent), `OnPrimary` (button text),
`Secondary` (pinned tint, see D2a), `SurfaceHigh` (input field background), `Border` (input/divider
border), `Background` (panel). Because `ButtonStyle.Default(colors)` derives hover = `Primary + 0.1`
and press = `Primary − 0.08` **programmatically**, and the caret/focused-input-border/selection all
read `Primary` — authoring `Primary` per material recolors buttons, their states, caret, selection, and
the focused border in one stroke. Roles not tied to material identity (error, state-overlay alphas)
keep the shared clay values.

### D2a: Remap the pinned tint from `Primary` to `Secondary`
`ScribeRowConstants.PinnedTint(colors)` currently returns `colors.Primary` at alpha `0.33`. Because the
focused-input border also reads `Primary`, a focused input inside a pinned row draws its border and the
row wash from the same hue — the focus cue reads weakly. Remap the shared helper to
`colors.Secondary with { W = PinnedTintAlpha }` so the pinned wash and the focus border come from two
different roles and stay visually distinct.

This is a **global** change (the user's explicit choice): `PinnedTint` is a single shared helper used
by the tablet, the Lectern/Notebook parchment theme, and the pinned HUD, so all pinned tints shift from
the accent to the secondary tone. The same pinned-vs-focus ambiguity exists in the Lectern today, so
this is a consistent improvement there too — but it means the readable-path pinned coloring is no
longer byte-identical (all other readable-path colors are). Each palette (parchment `Light`, and the
three clay palettes) MUST therefore author a `Secondary` that (a) is legible as a low-alpha row wash and
(b) reads clearly different from that palette's `Primary`, or the remap just relocates the clash.
`Secondary` has no other reader in the mod (only `OnSecondary` is used elsewhere), so no other surface
is affected. *Alternative considered:* a tablet-only branch in `PinnedTint` — rejected by the user in
favor of the simpler single-path global remap.

### D3: Two-pass per-stroke outer glow (halo does not self-stack)
The stacking risk is real if each stroke draws "halo then fill" individually — overlapping strokes
would darken each other's halos. Mitigate by splitting `PaintInternal`'s stroke loop into two passes
over the same reveal range: **pass 1** draws every stroke's fill in the halo color with
`SharedPaint.MaskFilter = context.GetOrCreateBlurMask(sigma)`; **pass 2** draws every stroke's crisp
ink fill (MaskFilter nulled) on top. Crisp ink always overwrites any halo, so within a glyph the halos
merge into one soft glow *behind* the letterform and show only where they extend past the ink onto the
backdrop — exactly the desired effect, and the same principle as `PaintingContext.DrawText`'s glow.
Jitter (when on) is applied identically in both passes so the halo tracks the wobbled geometry.

*Alternative considered:* offset drop-shadow. Rejected as the default — a symmetric halo separates ink
from a busy mid-tone backdrop better than a single-direction cast shadow; the glow also reads as "ink
sitting in a pressed groove" rather than floating. (A shadow variant could be added later behind the
same dev command if a material wants it.)

### D4: SharedPaint discipline
`SharedPaint` is reused across all draw ops; today `PaintInternal` saves/restores only `Color` and
`Style`. The glow adds `MaskFilter`, so the code MUST null `MaskFilter` back to null before returning
(and between pass 1 and pass 2), matching `RenderBox`'s `paint.MaskFilter = null;` after its shadow
draw. Use the **cached** `GetOrCreateBlurMask` (never dispose per-frame) per the documented
SKPictureRecorder caveat. This applies in both `CuneiformText.cs` and `ScribeCuneiformField.cs`.

### D5: Per-material glow parameters + dev console command
Glow color, blur sigma, and a light-vs-dark halo choice are per-material constants (a small table
alongside `CuneiformMetrics`). A client-registered dev command (dot-prefixed, e.g. `.cuneiformglow`)
sets these at runtime and forces a repaint of the open tablet, so values can be tuned live and reported
back for baking — the same throwaway-harness role as `.cuneiform`. The command mutates in-memory tuning
state only; nothing is persisted, and it is a developer aid, not a shipped feature.

## Risks / Trade-offs

- **Glow legibility varies by clay** → the halo light-vs-dark choice is per-material (light halo under
  dark ink on the darker clays; dark halo under the ink on the palest), tuned in-game via D5 before
  baking.
- **SharedPaint filter leak** → a forgotten `MaskFilter` reset would blur unrelated later draws;
  mitigated by nulling it between passes and before return, and by an explicit design note (D4). Also
  fix the pre-existing `IsAntialias`-not-restored minor leak while in this code.
- **Per-stroke second pass doubles path draws** → cuneiform lines are short (a title + task rows); the
  extra fills are cheap and the blur mask is cached. Acceptable.
- **Two paint sites drift** → `CuneiformText.cs` and `ScribeCuneiformField.cs` implement the same
  two-pass logic; keep the glow parameter source and the pass structure shared/mirrored so a tuning
  change touches one table, not two divergent copies.
- **Contract change for off-state expectation** → the user explicitly chose to keep `off = global
  theme`, so the off path is unchanged; the per-clay Background only shows when Pixel-Art is on.

### D6: Export the palettes to the libGUI-Theme-Library gallery
The sibling `libGUI-Theme-Library/` project is a dependency-free local browser gallery of LibGUI
themes: each theme is a `themes/<name>.json` with the 17 `ColorScheme` roles as `#RRGGBB[AA]` hex
strings, baked into `themes-data.js` by `node build.mjs` (de-dupes by base name, keeping newest mtime).
It already hosts `scribe_parchment.json` (the parchment `Light` theme), so this is an established
export path. Add `scribe_clay_red.json`, `scribe_clay_blue.json`, `scribe_clay_fire.json` by converting
each authored `Vector4` role to hex (`round(c × 255)` per channel; include the alpha byte for the
translucent roles `Border`/`OutlineVariant`/`StateHover`/`StateSelected`), then run `node build.mjs`.
If the pinned remap (D2a) changes `Light.Secondary`, re-export `scribe_parchment.json` to keep the
gallery consistent with the shipped theme.

This is a separate git repo (per the container conventions) — the export files and the `build.mjs`
rebuild land there, committed independently from the mod. The gallery visualizes only the 17 theme
roles; the cuneiform glow (D3) and backdrop art are out of its scope by design, so "observable" here
means the color palettes are browsable side-by-side, not the full in-game tablet.

*Alternative considered:* a generator in the mod that emits the JSON from the `ThemeData` at build
time. Rejected as over-engineering for three static palettes — a one-time hand conversion (verifiable
against the authored floats) is simpler and the library already expects hand-authored files.

## Migration Plan

Pure additive Mod-layer change; no persistence or asset migration. Rollout: build → Core tests (no Core
change, but keep green) → restage → in-game tune glow via the dev command → bake tuned constants →
final restage. Rollback is reverting the commit; no data implications. The dev console command can stay
in the tree (guarded/dev-only) or be removed after tuning — decided at implementation time.

## Open Questions

- Final per-material palette values and glow constants are seeded from sampling but finalized in-game
  (that is the point of the dev command); the tasks capture "tune then bake," not fixed numbers here.
- Whether to keep the `.cuneiformglow` dev command in the shipped tree or strip it post-tuning — low
  stakes, resolved during implementation.
