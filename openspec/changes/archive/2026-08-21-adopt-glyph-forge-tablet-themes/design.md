## Context

The glyph-forge tool exports one readability preset per tablet view (10 files in
`~/Downloads/readability-params-*.json`): `bodyInk`, `linkInk`, `strokeWeightScale`, and
`glow{color, strength, blurFraction, offsetX, offsetY}`, plus a `backgroundMask` (opacity 0 in
every export — a no-op). These are tuned values we want as the source of truth.

Today the tablet's styling is resolved through **four parallel switches keyed differently**:

- `ScribeTheme.ForTablet(material, pixelArt)` → a full `ThemeData` per **material** (no state axis).
  `bodyInk` lives here as the `OnSurface` role and cascades to `OnBackground`, the derived
  `OnSurfaceVariant` muted text, the caret, and all page text.
- `ScribeTheme.ForTabletLink(material)` → the row `LinkColor`, per **material** only.
- `CuneiformGlowTable.For(material, state)` → glow; already **state-aware**, but hard/fired share
  ONE halo (`HardHalo`/`FiredHalo`) across all clays, and the `CuneiformGlow(Color, BlurFraction)`
  struct has **no offset**.
- Stroke weight: `GlyphBundle` carries a per-glyph `weight` (`DefaultStrokeWeight = 6.0`) in Core,
  but there is **no per-view scale knob** anywhere.

`ColorScheme` (LibGUI, `Gui.Widgets.Framework`) is a 17-role semantic palette driving every widget.
Of the JSON fields, only `bodyInk` maps to a role (`OnSurface`). The exports carry no
`Primary`/`Secondary`/`Surface`/`Border` data, so the theme's accent/panel/selection palette is
**out of scope** — it stays authored per-material in `ClayPalette`.

Two in-flight changes are part of this same effort and are superseded here:
`add-tablet-state-glow-modifier` (its "one shared light halo per state" decision is **reversed** by
per-clay halos) and `tablet-text-visibility` (per-material link ink + wet dark-halo tuning). Both are
code-complete with only in-game tuning gates pending; this change replaces their placeholder values.

## Goals / Non-Goals

**Goals:**
- Make the 10 glyph-forge exports the baked source of truth for tablet text readability.
- Model each view as one `(material, state)`-keyed bundle, resolved once and decomposed — the
  long-deferred "one bundle per view" refactor (`[[tablet-styling-fragmentation-refactor]]`).
- Add the two missing capabilities the data needs: glow **offset** and a per-view **stroke-weight
  scale**. Give ink + link a **drying-state axis**.
- Keep `src/Core/` API-free and untouched.

**Non-Goals:**
- No change to `Primary`/`Secondary`/`Surface`/`Border`/`Error` or any non-ink `ColorScheme` role —
  the exports carry no data for them.
- No runtime asset loading of the JSON; values are baked constants (matching how jitter/glow
  constants already ship). The JSON files are the authoring record, not a shipped asset.
- No change to backdrops, the non-cuneiform readable path, or non-tablet surfaces.
- Not generalizing the bundle to Lectern/Notebook/Chalkboard — tablet-family only.

## Decisions

### D1 — One `TabletReadability` bundle, resolved once, decomposed by the dialog
Introduce a small readonly record (Mod-side, e.g. in `ScribeTheme.cs` or a new
`TabletReadability.cs`) carrying `BodyInk`, `LinkInk`, `StrokeWeightScale`, and `CuneiformGlow`. A
single `TabletReadability.For(material, state)` returns the bundle. `GuiDialogScribeTablet` resolves
it **once** per build and decomposes: ink → the theme wrapper's `OnSurface`, link → `LinkColor` via
the existing `DecorateRowStyle`, glow → the `CuneiformGlowTable.For` arg it already passes, stroke
scale → a new render arg.

*Why over piecemeal edits:* the four switches drift because they key differently; a single
`(material, state)` table makes a view internally consistent and is exactly the deferred refactor.
*Alternative rejected:* patch each switch to take `state` independently — less code churn now, but
preserves the fragmentation and leaves offset/stroke homeless.

### D2 — `bodyInk` goes on the theme (`OnSurface`); theme resolution gains a state axis
`ForTablet(material, pixelArt)` → `ForTablet(material, state, pixelArt)`. The bundle's `BodyInk`
seeds `OnSurface`/`OnBackground`; `OnSurfaceVariant` keeps deriving from it via the existing HSV lift.
Only ink is state-dependent; the rest of `ClayPalette` stays per-material.

*Why:* ink cascades page-wide (caret, muted text, all labels), so it belongs on the theme, not as a
cuneiform-only param — this keeps LibGUI themes the single consistent page palette. *Note:* the
bundle owns the ink value; `ClayPalette` receives it per state. Keep the per-material accent authoring
intact.

### D3 — Glow becomes per-`(material, state)` with offset, each cell independent
Extend `CuneiformGlow` to `record struct CuneiformGlow(Vector4 Color, float BlurFraction, float
OffsetXFraction, float OffsetYFraction)` (default 0,0 = centered, backward-compatible; `Enabled`
unchanged). `CuneiformGlowTable.For(material, state)` returns the bundle's glow. Each of the 10 cells is authored
**independently** — the table permits any cell to differ, and nothing factors "wet" into a shared
constant that would couple the three wet clays. Today's values happen to coincide (the three wet clays
use the same dark halo `0.059, 0.078, 0.102`, s=0.30, blur=0.08, off=0; wax-wet is white s=0.54;
hard/fired are per-clay light halos — blue cool, fire/red warm — at higher strength/blur with a
0.04–0.05 offset), but that is data, not structure: a future retune can diverge any wet clay or any
state without a refactor. The offset threads into the Skia paint as a translation of the blurred halo
pass before the crisp ink pass (two-pass render otherwise unchanged).

*Why per-clay hard/fired (reversing `add-tablet-state-glow-modifier`):* the glyph-forge pass tuned
distinct warm/cool halos per clay; they read better than one shared halo. *Trade-off:* strengths now
exceed the old `~0.35–0.65` envelope (hard = 1.0) — accepted, the tuned export is the new authority.

### D4 — `strokeWeightScale` is a Mod-side multiplier on the existing per-stroke weight
The cuneiform render objects already read `GlyphStroke.Weight`. Multiply by the bundle's scale
(today's values happen to be 0.9 wet / 1.0 hard / 1.1 fired, aligned by state — but the scale is a
per-`(material, state)` cell like the rest of the bundle, free to diverge per view in a future retune).
Threaded as a new render arg
from `GuiDialogScribeTablet` alongside the glow. No Core change: Core keeps emitting base weights;
the Mod scales at paint time.

*Alternative rejected:* bake the scale into Core `GlyphBundle` — would push a Mod styling concern
into the API-free Core and couple stroke geometry to tablet state.

### D5 — `linkInk` uses the existing `ScribeRowStyle.LinkColor` seam, now per-state
`ForTabletLink(material)` → sourced from the bundle per `(material, state)`. Applied exactly where it
is today (`GuiDialogScribeTablet` `DecorateRowStyle`). Not promoted to a `ColorScheme` role — "link"
is a Scribe row-style concept, not a widget-framework one.

### D6 — Ignore `backgroundMask`
Opacity 0 in all 10 exports → no-op. Not modeled now; the bundle can gain it later if a future export
sets it non-zero, without touching consumers.

## Risks / Trade-offs

- **Archive-order header drift** on `cuneiform-contrast-glow` + `tablet-dialog` (both touched by the
  two superseded in-flight changes) → Mitigation: archive the two in-flight changes first (or
  `--skip-specs`) and reconcile these two spec headers to this end-state last-writer-wins, exactly as
  the v1.0.0 cut did (`[[openspec-archive-order-header-drift]]`).
- **Superseding live tuning gates** (`add-tablet-state-glow-modifier` 4.x, `tablet-text-visibility`
  5.6) → Mitigation: explicitly retire/rebaseline those gates in this change's in-game step so the
  same view isn't tuned twice; the new authoritative gate re-runs `TESTING.md` `00000016`.
- **Glow offset in the two-pass render** could double-offset or leave a seam if applied to the wrong
  pass → Mitigation: offset only the blurred halo pass; verify the crisp ink still registers over it
  (the existing overlap/reveal scenarios must still hold).
- **Values outside the old envelope** (hard glow s=1.0) may read as heavy → Mitigation: it's a gated
  in-game verification; adjust the baked constant if it reads wrong, but the export is the default.
- **State axis touches `ForTablet` callers** → Mitigation: grep confirms the callers are
  `GuiDialogScribeTablet` (`ResolveTheme`) and `GuiDialogScribeChalkboard`; the chalkboard passes its
  own fixed state/material, so the signature change is fully covered.

## Migration Plan

Visual-only, no persistence/codec impact. Bake constants from the 10 JSON files, build, restage,
verify in-game per the tasks. Rollback = revert the change; no data migration. Deferred-cleanup note:
once landed, the old `HardHalo`/`FiredHalo` shared seeds and the per-material `ForTabletLink` constants
are replaced by the bundle table.

## Open Questions

- Should the two superseded changes be **archived first then this rebased**, or should this change
  **absorb and cancel** their remaining tasks outright? (Leaning: archive them first to bank their
  confirmed code, then this modifies the specs — resolve at apply time.)
- Bundle home: a new `TabletReadability.cs` vs. folding the record + table into `ScribeTheme.cs`.
  (Leaning new file — the bundle spans theme + glow + stroke, so it reads oddly as "theme".)
