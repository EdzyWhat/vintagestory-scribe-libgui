## Why

Row geometry on Read and Edit has been locked down against **Caudex**, but each Settings task font has its own Skia line-box (`ascent − descent + leading`). At the same nominal point size, Scapholene and La Belle Aurore (and the rest) produce visibly different input-row heights, so the proportional relationships between page elements cannot hold once the player picks another face. That layout work is wasted unless every selectable TTF is drawn so the game *perceives* the same height as Caudex.

After that line-box lock, letters still *look* differently sized (Default too big, La Belle Aurore too small). A second, optical multiplier is needed on top of the auto scale. Settings must stay a stable chrome surface (LibGUI default face at 100%), and the HUD must keep its own font.

## What Changes

- Peg every selectable **task-text** font's layout line-box to Caudex's, at the current **window** font scale, on Read and Edit. "Perceived height" means LibGUI/Skia's line-box (`TextLayoutHelper.MeasureText("Ag").Y` = `metrics.Descent − metrics.Ascent + metrics.Leading`), not x-height or ink bounds.
- Apply a per-family **size scale** (so that line-box matches Caudex), a per-family **optical scale** (so letters read similarly big after that match), and a per-family **vertical draw offset** (so glyphs can later sit optically like Caudex inside that box; OffsetEm stays 0 this change). Caudex itself is the identity (scale 1, optical 1, offset 0).
- Route both Read (`Text`) and Edit (`ScribeMultilineField`) through the same chokepoint, so a single-line row stays the same height across a view switch *and* across a font change. Today's Read path measures a hardcoded `"sans-serif"` while the editor measures the selected family — that mismatch goes away.
- Cover document surfaces that draw the player's task font (Lectern, Notebook, Clockmaker's Notebook, Chalkboard, Scriptorium, Guestbook bodies, tablet **fallback** when cuneiform is off).
- **Exclude** the tablet's cuneiform script (`CuneiformText` / `ScribeCuneiformField` / title cuneiform). It already has its own `CuneiformMetrics.LineHeightRatio`. Titles and in-dialog **buttons** stay on unscaled Caudex (`ButtonFamily` / `TitleFontFamily`).
- **Exclude** the pinned HUD: it keeps its own face and is not pegged.
- **Exclude** Settings chrome: LibGUI default face at 100%. Window Text Size still live-previews Read/Edit, not the settings form.
- Authoring aid: `tools/task-font-optical-scale/index.html` to pin OpticalScale values (not a player-facing control).
- No new Settings control. No codec or settings-schema change.

## Capabilities

### New Capabilities

- `task-font-metrics`: per-family optical metrics for selectable TTF task fonts, pegged to Caudex's Skia line-box, applied at one Mod-layer chokepoint for measure and draw.

### Modified Capabilities

- `font-selector`: choosing a different task font MUST NOT change single-line row height (or Read/Edit parity) relative to Caudex at the same font scale.
- `lectern-gui-shell`: the existing Read/Edit single-line height-parity requirement holds for every selectable task font, not only the face the layout was tuned against.
- `gui-text-style-inheritance`: the per-tab `DefaultTextStyle` ancestor on task-text tabs carries the Caudex-pegged *effective* font size (and still the resolved family). Settings uses a separate chrome ancestor.
- `settings-tab`: the settings form stays on LibGUI's default face at 100%; Window Text Size live-updates Read/Edit, not the form itself.

## Impact

- **Mod only.** `ScribeTaskFont` (and callers: `ScribeTextDefaults`, `ScribeRowControlNudge.TextLineHeight`, `ScribeMultilineField` measure/draw). Metrics table lives next to font registration after `RegisterCustomFonts`. Core stays free of font/Skia types; `KnownTaskFonts` stays a string allowlist.
- **Tests:** Core cannot measure Skia fonts. Assert the table covers every `KnownTaskFonts` entry plus the empty default, identity for Caudex, and that `NormalizeTaskFontFamily` still maps unknowns to default. In-game: cycle every selector option on Read and Edit and confirm single-line rows stay Caudex-height. HUD unchanged. Settings chrome unchanged by those knobs.
- **Saves / network:** none. Client-local paint only.
- **Dependencies:** none. Uses already-registered typefaces and `TextLayoutHelper`.
