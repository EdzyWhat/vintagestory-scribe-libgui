## Why

The clay tablet dialog ships one fixed earthen palette for every clay type — red, blue, and fire
tablets are colored identically, so the only per-material signal is the backdrop art. Now that the
per-type `-soft.png` backdrops render in-game, the deferred contrast decision from
`tablet-theme-contrast-vs-backdrops` can finally be made: give each clay type a palette that harmonizes
with its own backdrop color, and boost cuneiform ink legibility over the mid-tone textured clay with a
soft per-stroke glow.

## What Changes

- Replace the single `ScribeTheme.Tablet` palette with **three authored per-clay-type palettes**
  (red / blue / fire), seeded from the sampled `-soft.png` backdrop colors (red `#aa6f6d`, blue
  `#98a6af`, fire `#ccaf89`). `wax` continues to use the fire palette as its interim placeholder,
  matching its interim backdrop.
- Thread the tablet item's `material` variant into theme selection (`ScribeTheme.ForTablet` /
  `GuiDialogScribeTablet.ResolveTheme`), which today ignores it. Each palette varies the roles the
  material identity actually shows through: **ink** (`OnSurface`/`OnBackground`), the **accent**
  (`Primary`) — which programmatically drives button fill, button-text (`OnPrimary`), hover, press,
  the caret, focused-input border, and text selection — the **secondary** color (`Secondary`), which
  now drives the pinned-row tint, plus the **input field background** (`SurfaceHigh`), **input/divider
  border** (`Border`), and the **panel `Background`**.
- **Remap the pinned-row tint from `Primary` to `Secondary`** in the shared
  `ScribeRowConstants.PinnedTint` helper (all themes). Today a focused input inside a pinned row draws
  its focus border and the row wash from the same `Primary` hue, muddying the focus cue; sourcing the
  pinned wash from `Secondary` instead keeps the two visually distinct. This is a global change — it
  also shifts the Lectern/Notebook parchment pinned tint and the pinned HUD from the accent to the
  secondary tone (the same ambiguity exists there today), so the readable-path pinned coloring is no
  longer byte-identical to before (all other readable-path colors are unchanged).
- Add a **per-stroke outer glow** behind cuneiform strokes to lift the ink off the mid-tone clay. It
  renders in **two passes** (all blurred halos first, then all crisp fills on top) so overlapping
  strokes within a glyph never halo over each other — the glow shows only where it extends past the
  ink onto the backdrop. Halo color and strength are per-material, baked/tuned constants (like the
  jitter/reveal constants), not a user setting.
- Add a **client dev console command** to live-adjust the glow at runtime (strength/blur, and halo
  light-vs-dark), so tuning values can be found in-game and reported back — mirroring the existing
  `.cuneiform` dev harness pattern. No persisted setting; a tuning aid only.
- **Tablet chrome refinements (2026-08-03 playtest):** four adjustments folded in after the first
  in-game pass at the clay themes:
  - **Drop the top divider on every Tablet view.** The `Divider()` above the scroll region in the
    editor/read/pinned content views reads as an unwanted hard rule against the clay backdrop. Gate it
    off on the cuneiform/tablet path (`UseCuneiform`) only; the Lectern/Notebook readable path keeps it.
  - **Remove the glow from button labels.** The footer button labels (`BuildButtonLabel`) currently
    reuse the row glow; on the solid `Primary`-filled buttons the halo muddies the label rather than
    lifting it, so cuneiform button labels render crisp (no glow) while the rows/title keep theirs.
  - **Reduce + spread the glow.** Lower the per-material halo strength (alpha `0.55 → 0.30`) and widen
    its radius ~30% (`BlurFraction 0.09 → 0.117`) so the lift is softer and more diffuse. Still
    live-tunable via `.cuneiformglow` and finalized in the in-game tuning pass.
  - **Engrave the title-bar pencil + drag-grip.** Today `BuildTitleBar` colors these two glyphs from
    the *global* theme's `OnSurfaceVariant` (a mid-gray), so they read washed-out on clay. Color them
    from the tablet's own dark material ink (`OnSurface`) at partial alpha so the clay texture bleeds
    faintly through the strokes — a darkened, engraved impression — while the transparent icon
    background stays clear. The tint uses the normal glyph-only `SKBlendMode.SrcIn` path, via a
    `TitleChromeGlyphColor` seam on `ScribeDialogBase`; the Lectern/Notebook title bar keeps the gray
    glyphs unchanged. (A Multiply blend was tried first but `VsIcon` applies its tint as a color filter,
    which fills the whole transparent icon quad under Multiply — a pale tile — so SrcIn + a partial-alpha
    dark ink is used instead.)
- **Unchanged:** when Pixel-Art Display is OFF the tablet still follows the player's global theme
  (the current `off = global theme` contract holds); per-clay theming and the backdrop art both apply
  only when Pixel-Art is ON. Core takes no color decisions (stays VS-API-free). The Lectern/Notebook
  parchment theme is untouched.

## Capabilities

### New Capabilities
- `cuneiform-contrast-glow`: A two-pass per-stroke outer glow rendered behind cuneiform strokes to
  boost ink contrast over textured backdrops, with per-material halo color/strength and a dev console
  command for in-game tuning.

### Modified Capabilities
- `tablet-dialog`: The "Tablet dialog uses its own theme" requirement changes from a single fixed
  earthen palette to per-clay-type palettes selected by the item's `material` variant.

## Impact

- **Code (Mod layer only):**
  - `src/Mod/ScribeTheme.cs` — three per-material `ThemeData` palettes + a material-keyed
    `ForTablet(material, pixelArt)` selector; each palette authors a distinct `Secondary` (for the
    pinned tint) that reads clearly different from its `Primary`.
  - `src/Mod/ScribeRowConstants.cs` — `PinnedTint` sources `Secondary` instead of `Primary`.
  - `src/Mod/GuiDialogScribeTablet.cs` — `ResolveTheme` threads `Variant["material"]`; the cuneiform
    title ink reads the resolved per-material scheme.
  - `src/Mod/CuneiformText.cs` and `src/Mod/ScribeCuneiformField.cs` — two-pass glow in
    `PaintInternal`, with `SharedPaint.MaskFilter` set/nulled correctly per the shared-paint discipline.
  - New per-material glow constants (color/sigma/light-or-dark) and a dev console command registration.
  - `src/Mod/ScribeEditorContent.cs` — drop the button-label glow; drop the editor-view divider on the
    cuneiform path.
  - `src/Mod/ScribeReadContent.cs`, `src/Mod/ScribePinnedContent.cs` — drop the top divider on the
    cuneiform path (Lectern/Notebook readable path unchanged).
  - `src/Mod/CuneiformGlow.cs` — lower the seed alpha to `0.30` and widen `BlurFraction` to `0.117`.
  - `src/Mod/ScribeDialogBase.Layout.cs` — a `TitleChromeGlyphColor` seam; `BuildTitleBar` colors the
    pencil + grip through it. The tablet override returns a partial-alpha dark material ink (glyph-only
    `SrcIn`, so the transparent tile stays clear).
- **No new dependencies**, no asset changes (colors are authored constants informed by sampling — no
  runtime pixel-sampling code), no Core changes, no persistence/settings changes.
- **Cross-project export (final deliverable):** the three authored clay palettes are exported as
  `themes/*.json` files into the sibling `libGUI-Theme-Library/` gallery (which already hosts
  `scribe_parchment.json` for the parchment `Light` theme), converting each `ColorScheme` role from a
  `Vector4` to a `#RRGGBB[AA]` hex string, then rebuilt with `node build.mjs`. This makes the palettes
  browsable side-by-side. NOTE: the gallery renders only the 17 theme roles — it cannot show the
  cuneiform glow or backdrop art, which are not `ColorScheme` roles.
- **Regression surface:** the tablet's colors and cuneiform rendering; the readable (non-cuneiform,
  Pixel-Art-off) path and the Lectern/Notebook themes stay unchanged EXCEPT the pinned-row tint, which
  shifts from `Primary` to `Secondary` everywhere (deliberate, per the remap above).
