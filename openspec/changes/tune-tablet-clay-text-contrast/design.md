## Context

The clay tablet dialog resolves one of three per-material palettes (`ScribeTheme.ForTablet`) when
Pixel-Art Display is ON. Each palette is built through the `ClayPalette(...)` factory in
`src/Mod/ScribeTheme.cs`, which today takes `onSurfaceVariant` as a **hand-authored `Vector4`** per clay
type:

```
onSurfaceVariant: new Vector4(0.40f, 0.28f, 0.18f, 1.0f),   // TabletFire
onSurfaceVariant: new Vector4(0.44f, 0.26f, 0.22f, 1.0f),   // TabletRed
onSurfaceVariant: new Vector4(0.30f, 0.36f, 0.40f, 1.0f),   // TabletBlue
```

That role is the "muted/secondary text (hints, placeholders)" color (per the `ClayPalette` param docs).
The multiline field renders its placeholder from it at a further alpha cut:

```
// src/Mod/ScribeMultilineField.cs (build widget)
placeholderColor: colors.OnSurfaceVariant with { W = 0.55f },
```

An in-game screenshot of a fired **red** tablet shows this placeholder is barely legible on the mid-tone
clay backdrop. The ask: darken the muted/placeholder text by a **consistent amount across all three
palettes**, maintainably.

Two relevant facts already in the codebase:
- `ScribeRowConstants.ShiftBrightness(Vector4 color, float deltaValue, float saturationScale = 1f)`
  shifts HSV **Value** (Skia 0–100 scale, clamped), scaling saturation and preserving hue + the original
  float alpha. Its own doc-comment notes it is "perceptually nicer than an RGB lerp toward white/black —
  keeps (a fraction of) the theme's chroma." It is already the codebase's blessed brightness primitive
  (used by `PinnedTint` and the drag highlights).
- The parchment `Light` theme authors its own `OnSurfaceVariant` separately and is **not** a clay
  palette — it is out of scope here.

## Goals / Non-Goals

**Goals:**
- Darken the clay tablets' muted/hint/placeholder text so it reads on the clay backdrops.
- Make the darkening **consistent by construction** across fire/red/blue — one perceptual step, one knob.
- Preserve each clay's hue identity (terracotta stays terracotta, slate stays slate).
- Keep the change Mod-layer-only, asset-free, and reversible via a single constant.

**Non-Goals:**
- **Do NOT darken the body/title `ink`** (`OnSurface`/`OnBackground`). Chosen scope is muted+placeholder
  only. Body ink is already near-black (0.12–0.24) and darkening it risks a muddy blob on mid-tone clay.
- Do NOT touch the parchment `Light` theme, the readable (Pixel-Art-off) path, or Lectern/Notebook.
- Do NOT change accent/secondary/surfaces/borders/backdrops or the cuneiform glow.
- No new user setting; contrast is an authored constant like the other clay-theme tuning values.

## Decisions

### D1 — Derive `OnSurfaceVariant` from `ink` via one shared HSV lift
Inside `ClayPalette`, compute the muted role as `ShiftBrightness(ink, +MutedTextValueLift)` (lifting the
dark ink UP toward the surface just enough to read as "muted," not lifting so far it goes faint). A single
shared `MutedTextValueLift` constant governs all three palettes, so the muted-vs-ink contrast step is
identical in perceptual (HSV Value) terms regardless of clay hue. **To darken the muted text "a bit,"
lower that one constant.** The three hand-authored `onSurfaceVariant` arguments are removed.

*Why derive from `ink` rather than from `surface`:* the muted text must contrast the **surface** it sits
on, and `ink` is already the authored "reads on this surface" anchor per clay. Lifting ink toward the
surface yields a muted tone that tracks each clay's own ink/surface relationship automatically. *Rejected:*
darkening `surface` down toward ink — that would shift the whole panel, not the text.

*Why HSV Value shift, not RGB lerp:* an RGB lerp toward black desaturates unevenly per hue (blue and red
would darken by visibly different perceptual amounts for the same lerp `t`), which is exactly the
"inconsistent across themes" problem. `ShiftBrightness` moves the same perceptual step per hue and keeps
chroma, so all three stay recognizably their clay color. This is also the helper the codebase already
standardized on.

### D2 — Raise the placeholder alpha floor — **REVISED during implementation: NO code change**
Original plan: the placeholder's `W = 0.55f` in `ScribeMultilineField` blends it ~45% into the backdrop
and was assumed the direct cause of the screenshotted faintness; raise it toward ~0.70.

**What implementation found (and why D2 becomes a no-op):** the D2 scope note asked to verify the seam is
tablet-scoped before touching it. It is NOT. `ScribeMultilineField.Build` branches on `Widget.UseCuneiform`
(`ScribeMultilineField.cs:1124`) between two render widgets:
- the **tablet** path uses `ScribeCuneiformFieldRenderWidget` (lines 1124–1159), which has **no placeholder
  parameter** — the `0.55` alpha is never applied on a tablet;
- the `placeholderColor: colors.OnSurfaceVariant with { W = 0.55f }` at line 1170 lives ONLY on the
  non-cuneiform `ScribeMultilineFieldRenderWidget` — the **readable Lectern/Notebook (Pixel-Art-off)** path.

The faint hint in the red-tablet screenshot is not a multiline placeholder at all: it is the empty-task-list
hint, a separate widget rendered at **full-alpha** `OnSurfaceVariant` (`ScribeEditorContent.cs:288` /
`ScribeReadContent.cs:100`). So the symptom is dominated by the muted *color*, which **D1 already fixes** by
deriving a darker `OnSurfaceVariant`. Raising line 1170's `0.55` would darken **only** the readable path —
exactly what this change's own "Readable path placeholder is unchanged" scenario forbids.

**Decision: skip the alpha raise; leave `ScribeMultilineField.cs:1170` at `0.55`.** D1 is the whole fix.
The ADDED spec requirement's legibility goal is met by D1's darker derived muted role, not by an alpha edit.
(No cuneiform title / single-line field carries a parallel `0.55` placeholder to centralize, so nothing
else needs touching.)

### D3 — Values are tuned in-game, not guessed here
`MutedTextValueLift` and the exact placeholder alpha are **tuning targets**, not values this proposal can
finalize from a screenshot. The implementing terminal should: build, open a red / blue / fire tablet with
Pixel-Art ON, and confirm the placeholder + any hint text read legibly on all three backdrops without the
muted text becoming as heavy as the body ink (muted must still read as *secondary*). Seed suggestions to
start from: alpha `0.55 → 0.70`; pick `MutedTextValueLift` so the resulting `OnSurfaceVariant` lands near
the current hand-authored values' Value but is defined by the shared constant (i.e. start ~matching, then
darken as the in-game read dictates). Record the finalized numbers in the change before archiving.

### D4 — Re-export the theme gallery
As in `add-tablet-clay-type-themes`, re-export the three clay palettes to `libGUI-Theme-Library/themes/
*.json` (Vector4 roles → `#RRGGBB[AA]`) and rebuild with `node build.mjs`, so the browsable gallery
reflects the new derived muted role. The gallery cannot show the placeholder alpha (not a `ColorScheme`
role) — only the `OnSurfaceVariant` color updates there.

## Risks / Trade-offs

- **Deriving from `ink` couples muted text to ink.** If a future change re-tunes a clay's `ink`, its muted
  text moves with it. This is desirable (they should track) but worth noting: the muted role is no longer
  independently dialable per clay without reintroducing a per-palette override. Acceptable given the goal
  is *consistency*; if one clay ever needs a bespoke muted tone, add an optional override arg defaulting
  to the derived value.
- **Alpha lift could over-darken hint text elsewhere** if the placeholder seam is shared beyond the
  tablet. Mitigation: D2's scope check — verify the field is tablet-scoped or gate the lift.
- **Screenshot is red-only.** The fix is applied to all three but was diagnosed from one clay. D3's
  in-game pass on all three (esp. blue, the one cool palette with a different ink hue) is the guard.
- **Muted-vs-body separation.** Lifting muted text toward legibility risks it approaching body-ink
  weight, collapsing the visual hierarchy. D3 explicitly checks muted still reads as secondary.
