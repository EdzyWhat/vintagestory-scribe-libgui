## Why

The fuller presentation vision (`docs/specs/presentation-and-fonts.md` Item 3) wants Scribe's own
text drawn in bundled, tier-specific typefaces (a rustic script for notebooks, cuneiform block
letters for the clay tablet) *without* the community "global font swap" (an OS font install plus
`clientsettings.json defaultFontName`), which is game-wide and cannot be scoped to one mod.

Since this proposal was first written, Scribe's GUI was rebuilt on **LibGUI** (the `gui` hard
dependency), which renders text through **SkiaSharp**, not the native game's Cairo path. That
change makes the original spike's premise obsolete: LibGUI ships a real, cross-platform
font-registration API (`Gui.Rendering.Text.FontRegistry`), and it already bundles and registers
custom `.ttf` faces itself in production (`GuiModSystem.LoadFonts`). The mechanism this spike set
out to "prove" via a FreeType/Cairo private-surface hack is now a shipping, load-bearing code path
in a dependency Scribe already relies on. This change is retargeted to that reality: a small,
throwaway-scale spike that proves Scribe can register **one** bundled face through LibGUI's Skia
registry and route **only** its own row text at it, on the author's Apple Silicon Mac, before any
tier commits to a font system.

## What Changes

- Bundle one license-cleared TTF (Caudex, a humanist serif under the SIL OFL 1.1) as a **Scribe**
  mod asset, purely to prove the loading path.
- Load that face at client init with LibGUI's Skia asset loader
  (`SkiaAssetLoader.LoadFont(domain, path)` → `SKTypeface.FromStream`) and register it under a
  family name via `FontRegistry.RegisterCustomFont(family, weight, typeface)` — the same call
  LibGUI's own `GuiModSystem.LoadFonts` uses for its bundled faces. No FreeType P/Invoke, no
  temp-file extraction, no private Cairo surface.
- Route **only** Scribe's lectern row text at the registered family by setting the row text's
  `TextStyle.FontFamily` to that family name (the shared `FontFamily` const used by both the read
  `Text` and the editor `ScribeMultilineField`, so measured line height and drawn glyphs stay in
  lockstep). `TextLayoutHelper` consults `FontRegistry.GetCustomTypeface` before any system
  fallback, so the registered face is picked up automatically and no other GUI's text is touched.
- Prove, by running the spike **on the author's Apple Silicon Mac**, that the registered bundled
  face renders on arm64 macOS and is correctly scoped to Scribe's row text only. (The original
  FreeType-specific runtime unknowns — size-survival across a Cairo face swap, packed-zip temp-file
  extraction, `freetype6` arm64 interop — no longer apply on the Skia path; see design.md.)
- Ship the font's `OFL.txt` and credit Caudex in a `CREDITS` file (license gate).
- Correct the stale font facts in the design docs discovered during the LibGUI migration
  (documentation-only, no code): `docs/specs/presentation-and-fonts.md`'s Cairo/`FreeTypeFontFace`
  mechanism no longer describes this repo, and `GuiStyle.StandardFontName` is irrelevant to the
  LibGUI text path.

Explicit **non-goals** (this is a spike, not the font system):

- **No** per-tier faces — no cuneiform tablet face, no handwritten-notebook face. One face, one
  text surface, to prove the seam.
- **No** global-swap route (OS install + `defaultFontName`) — that path is game-wide and
  deliberately rejected.
- **No** `src/Core/` changes, no networking, no persistence/sync, no codec bump. This is
  Mod-layer client rendering only.
- **No** `ScribeFontRegistry` abstraction, config toggle, or tier→face mapping — a minimal
  client-init registration call suffices; the abstraction is designed in the parent work.
- Does **not** replace the stroke-glyph path (`docs/specs/glyph-strokes-ingestion.md`), which is a
  *different* approach aimed at the tablet's stamped glyphs; this font path is the serif/body-text
  path and the two coexist.

## Capabilities

### New Capabilities

- `bundled-font-rendering`: A mod-scoped mechanism for rendering Scribe's own GUI text in a bundled
  TTF registered through LibGUI's Skia `FontRegistry`, proven end-to-end on one surface (the
  lectern row text) with one face (Caudex), including the license-bundling requirement.

### Modified Capabilities

<!-- None. No existing spec's requirements change; the lectern row-text behavior gains a rendering
     detail but no requirement in lectern-gui-shell is altered. -->

## Impact

- **New asset:** one bundled `.ttf` (Caudex) plus its `OFL.txt` under the Scribe mod's assets, and
  a new `CREDITS` file at the repo root.
- **Touched code (Mod layer only):** a small client-init font-registration call in
  `ScribeModSystem.StartClientSide` (mirroring the existing `RegisterSvgIcon` precedent), and the
  shared row-text `FontFamily` const in `GuiDialogScribeLecternLibGui.cs` /
  `ScribeMultilineField.cs`. No change to the `TextStyle` size/color contract.
- **No** `src/Core/` impact, no network/persistence surface, no new package or mod dependency (uses
  only the already-depended-on `gui` LibGUI mod and its bundled SkiaSharp).
- **CI unaffected:** cloud runners build/test `Core` only; this is Mod-layer client render proven
  by manual playtest on the author's Mac.
- **Platform note:** the render path is SkiaSharp — already this fork's renderer, exercised by
  every LibGUI dialog Scribe draws — so the prior arm64-macOS interop risk (which was specific to
  the rejected `freetype6` P/Invoke route) is largely retired; the Mac run confirms rather than
  de-risks.
- **Shared-registry note:** `FontRegistry` is a process-global static on the shared `gui` mod;
  Scribe registers its family name into it (an alias→typeface entry other mods won't request). This
  is deliberate and mirrors how LibGUI registers its own bundled faces.
- **Docs corrected:** the obsolete Cairo/`FreeTypeFontFace` mechanism notes in
  `docs/specs/presentation-and-fonts.md` and the stale `StandardFontName` reference.
