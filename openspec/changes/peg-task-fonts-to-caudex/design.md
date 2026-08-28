## Context

Read and Edit row geometry (font 15, field pad, checkbox size, vertical padding) was unified in `unify-row-sizing-libgui` and has since been refined against **Caudex**. A single-line task is supposed to occupy the same vertical space in both views (`lectern-gui-shell`).

That contract is implemented by measuring a probe string `"Ag"` through LibGUI's `TextLayoutHelper.MeasureText`, whose Y is Skia's line-box: `metrics.Descent − metrics.Ascent + metrics.Leading` (see `VSAPI-NOTES.md`). Different typefaces at the same *nominal* point size return different Y values, so editor fields (`ScribeMultilineField.MeasureLineHeight(fontSize, fontFamily)`) grow or shrink with the player's Settings choice. Scapholene and La Belle Aurore are the obvious outliers; the rest of the lineup (Noto Sans/Serif, Playfair Display, Cormorant Unicase, Default/`sans-serif`) also diverge.

A second, quieter bug: `ScribeRowControlNudge.TextLineHeight` still measures a hardcoded `"sans-serif"` family, while the editor measures the selected family. Read reserved height and Edit field height can disagree even before any pegging.

Cuneiform already solves a related problem with `CuneiformMetrics.LineHeightRatio` and is **out of scope**. Titles and in-dialog buttons stay on unscaled Caudex (`ScribeTaskFont.ButtonFamily` / `TitleFontFamily`). The pinned HUD uses its own face and is **out of scope** for this peg (playtest: leave it alone). Settings chrome is also out: it must stay on LibGUI's default face at 100%, not Task Text Font or Window Text Size.

The existing chokepoints are `ScribeTaskFont.Resolve` (family), `ScribeTextDefaults` (per-tab inherited family + size), and `ScribeRowStyle.FromSettings` (nominal window-scaled size). None of them adjust size or baseline per face.

## Goals / Non-Goals

**Goals:**

- At a given window font scale, every selectable task font occupies the **same Skia line-box height as Caudex** on Read and Edit.
- Read and Edit single-line rows stay height-identical to each other for every selectable font (not only Caudex).
- After that line-box match, a per-family **optical scale** can shrink or grow the *drawn* letters so they read similarly sized (Default too big, La Belle Aurore too small) without changing reserved row height.
- Glyphs that sit high or low in that box can later be shifted with a per-family vertical offset (`OffsetEm`); that fill is deferred.
- One Mod-layer chokepoint owns the table and the apply math; callers do not sprinkle per-font fudge.
- Cuneiform (tablet script), Caudex chrome (title, buttons), the pinned HUD, and Settings chrome are untouched by the peg.

**Non-Goals:**

- Matching x-height, cap-height, or stroke weight across faces beyond the optical-scale knob. Line-box match is the layout lock; optical scale is a hand-tuned overlay.
- Matching wrap points or row widths across fonts (glyph advances differ; that is inherent).
- A new Settings control, codec field, or player-facing "font metrics" UI. The HTML tuner at `tools/task-font-optical-scale/index.html` is authoring-only.
- Changing `ScribeMultilineField`'s `lineHeight * 0.8f` baseline heuristic (OffsetEm compensates sit). Rewriting baseline math is a follow-up.
- Filling `OffsetEm` in this change (playtest deferred the height-offset pass).
- Tablet cuneiform rendering, jitter, glow, or `CuneiformMetrics`.
- Pegging or restyling the pinned HUD.
- Replacing or subsetting the TTF files themselves.

## Decisions

**1. Reference face is Caudex; "perceived height" is the Skia line-box.**
Layout is locked to `TextLayoutHelper.MeasureText("Ag", "Caudex", nominalSize, Normal).Y` at the current nominal *window* size. That is how the game already sizes input rows — not ink bounds, not x-height. *Alternative rejected:* matching x-height/cap-height. That would make letters look more similar but would *not* stabilize row height, which is the stated problem. Optical similarity after the lock is `OpticalScale`, not a different layout metric.

**2. Three knobs per family: `SizeScale` (auto), `OpticalScale` (hand-tuned), `OffsetEm` (hand-tuned, deferred).**

- `SizeScale[F] = caudexLineY / familyLineY` measured once at a reference size after `RegisterCustomFonts`. Dimensionless, so it holds at every window scale.
- `OpticalScale[F]` is a second multiplier so letters *read* similarly sized after the line-box match. Starts at 1; authored in `tools/task-font-optical-scale/index.html` and pasted into `OpticalScaleOf`. Caudex stays 1.
- Effective draw size: `nominalSize * (SizeScaleOverride ?? SizeScale) * OpticalScale`.
- `OffsetEm[F]` is a signed vertical draw nudge in ems of *nominal* size (`offsetPx = OffsetEm * nominalSize`; positive is down). Stays 0 until a later pass. Caudex is identity: scale 1, offset 0, optical 1.
- Optional `SizeScaleOverride[F]` (nullable) replaces the auto line-box scale entirely if needed. Absent unless playtest needs it.

*Alternatives rejected:* (a) force layout height to Caudex *without* scaling the face — tall fonts clip, short fonts float in padding; (b) only a hand-tuned scale with no auto measure — Default/`sans-serif` is OS-dependent, so a baked number would be wrong on other machines; (c) per-surface fudge in each widget — the forgot-to-thread bug class `ScribeTaskFont` already exists to prevent; (d) folding optical into `SizeScaleOverride` — that would throw away the line-box lock.

**3. Extend `ScribeTaskFont` as the only apply chokepoint.**
After fonts register, `ScribeTaskFont.BuildMetrics()` fills the table (every `KnownTaskFonts` entry plus the empty default → `"sans-serif"`). Public helpers:

- `EffectiveSize(family, nominalSize)`
- `OffsetY(family, nominalSize)` → pixels
- `LineHeight(nominalSize)` → always Caudex's line-box at that nominal size

`Resolve` stays the family mapper. `ScribeTextDefaults.Style` passes `Resolve` + `EffectiveSize` into the inherited `TextStyle`, so stock `Text` widgets pick up the scale with no per-widget threading (`gui-text-style-inheritance`). Custom paint (`ScribeMultilineField`, `ScribeGlyphFallback`) draws at `EffectiveSize` and adds `OffsetY` to the baseline Y. Layout height for the field and for `ScribeRowControlNudge.TextLineHeight` uses `LineHeight(nominalSize)` — **not** the selected family's native Y — so a slightly-off scale cannot reopen the Read/Edit gap.

**4. Read-view offset uses `Transform.Translate`; Edit adds to draw Y.**
Stock `Text` has no baseline-offset field. `Transform.Translate(0, offsetY)` is already used for optical nudges (`ScribeNumericField`, HUD). The editor's custom `PaintInternal` adds the same pixel offset next to the existing `PadY + i * lineHeight + ascent` term. Caret and selection boxes stay on the Caudex line-box (unshifted) so the field chrome does not slide with the ink.

**5. Surfaces in / out.**

In (anything that draws the player's task TTF on a document surface): Lectern / Notebook / Clockmaker / Chalkboard / Scriptorium Read and Edit rows; Guestbook *body* text; tablet rows **when** `UseCuneiform` is false (the accessibility fallback already goes through `ScribeTaskFont.Resolve`).

Out: cuneiform widgets; dialog title; buttons/radios on `ButtonFamily`; **pinned HUD** (own face, playtest: leave it alone); **Settings chrome** (`WrapSettingsChrome`: LibGUI `sans-serif` at `BaseSettingsFontSize`, never Task Text Font, never Window Text Size). Window scale still live-previews Read/Edit.

**6. Core stays font-blind.**
`KnownTaskFonts` remains a string allowlist in `ScribePlayerSettings`. The metrics table and Skia measurement live in Mod. A Core test can only assert that the allowlist is the set the Mod table is expected to cover (by duplicating the names, or by a Mod-side test if we add one later). No VSAPI/`SkiaSharp` in Core.

**7. Default/`sans-serif` is pegged too.**
The Caudex-tuned page proportions are the layout source of truth. Leaving Default on native system metrics would keep one selector option on a different geometry. This *will* shift row height for players who never left Default, if their system sans-serif line-box ≠ Caudex. That is an accepted, one-time visual change. OpticalScale then shrinks Default's *letters* so they do not read oversized inside that locked box.

**8. Settings stays on the player's LibGUI default face at 100%.**
The form used to inherit Task Text Font and Window Text Size, which made the control surface itself a live preview of those knobs. Playtest: the form must be a stable chrome face. `ScribeTextDefaults.WrapSettingsChrome` roots it at `sans-serif` / `BaseSettingsFontSize`.

## Risks / Trade-offs

- **[Risk] Auto scale makes a display/script face look tiny or huge (line-box ≫ x-height or the reverse).** → Mitigation: `OpticalScale` (primary) and `SizeScaleOverride` (escape hatch); HTML tuner for relative values; confirm in-game. Do not chase x-height as layout.
- **[Risk] Browser tuner ≠ Skia.** → Mitigation: tuner is a starting point; in-game pass still required. Values stay 1 until the author pastes from the page.
- **[Risk] Caudex failed to register (missing TTF).** → Mitigation: `BuildMetrics` falls back to measuring `"sans-serif"` as the reference and logs once; every scale becomes ~1 against the system face. Title/buttons already fall back the same way.
- **[Risk] Default/`sans-serif` row height changes for existing players.** → Mitigation: accepted (D7); called out in playtest. No settings migration.
- **[Risk] `Transform.Translate` on read `Text` moves hit-testing.** → Mitigation: wrap only the text child, not the row's checkbox/grip.
- **[Risk] Editor caret uses unshifted line-box while glyphs are offset, so the caret can look slightly off the ink.** → Mitigation: keep caret on the box (D4); OffsetEm is deferred.
- **[Trade-off] Wrap width changes with effective size.** Different fonts already wrap differently; scaling amplifies that. Out of scope.

## Migration Plan

- Client-only paint. No config key, no codec, no packet.
- Ship: register fonts → `BuildMetrics()` → existing dialogs pick it up on next build (Settings font change already rebuilds).
- Rollback: revert the Mod metrics helpers and the `EffectiveSize` / `OffsetY` call sites; players keep their `TaskFontFamily` preference.

## Open Questions

- **OpticalScale values** are unknown until the author uses `tools/task-font-optical-scale/index.html` and confirms in-game. Implementation lands every family at 1.
- **OffsetEm values** remain 0 (playtest deferred the sit pass).
- **Guestbook headers** stay Caudex Bold (chrome). Confirm no body-text site bypasses `ScribeTextDefaults` / `ScribeTaskFont` (grep at implement time).
