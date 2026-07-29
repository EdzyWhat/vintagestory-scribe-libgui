## Context

`docs/specs/presentation-and-fonts.md` Item 3 ("custom fonts per tier") depends on being able to
render Scribe's own GUI text in a bundled typeface without touching any other GUI in the game. This
spike de-risks that before a real tier commits to a font system.

**The premise changed with the LibGUI migration.** The original version of this change was written
against the native game's **Cairo** text path, where `CairoFont.SetupContext` resolves fonts by
name via fontconfig/OS and no managed font-registration API exists — forcing a FreeType-direct load
(`Cairo.Util.FreeTypeFontFace.Create` + `SetContextFontFace`) on the row's private Cairo surface.
That entire mechanism is gone from this repo: `ScribeRowElement.cs` and `GuiDialogScribeLectern.cs`
were retired in commit `a7ad139` ("Migrate lectern editor view to LibGUI; retire the native
editor"). Scribe's GUI now renders through **LibGUI/SkiaSharp**.

**What LibGUI provides (verified in `reference/vslibgui/`, treat as ground truth):**

- `Gui.Rendering.Text.FontRegistry` (`Rendering/Text/FontRegistry.cs`) is a cross-platform,
  process-global font registry. `RegisterCustomFont(string familyName, FontWeight weight,
  SKTypeface typeface)` (`:29`) registers a typeface under a family+weight key; `GetCustomTypeface`
  (`:41`) looks one up.
- `TextLayoutHelper` (`Rendering/Text/TextLayoutHelper.cs`) — LibGUI's text layout/measure path —
  resolves a `TextStyle.FontFamily` via `FontRegistry.ResolveFontFamily`, then checks
  `FontRegistry.GetCustomTypeface(resolvedFamily, weight)` (`:106`) **before** falling back to
  `SKTypeface.FromFamilyName` (`:122`). So a registered family name is picked up automatically by
  both measurement and drawing — no per-surface draw hook needed.
- `SkiaAssetLoader.LoadFont(string domain, string path)` (`Rendering/SkiaAssetLoader.cs:61`) loads a
  `.ttf` asset by `AssetLocation` and returns an `SKTypeface` via `SKTypeface.FromStream` over the
  asset bytes — no filesystem path, no temp file, works identically packed or unpacked.
- **LibGUI already does exactly this in production:** `GuiModSystem.LoadFonts` (`GuiModSystem.cs:117`)
  bundles Cormorant Unicase, JetBrains Mono, and Playfair Display as `gui`-domain `.ttf` assets and
  registers each via `RegisterCustomFont`. The spike is proving a shipping, load-bearing path.

**Where Scribe's title specifies its font:** LibGUI text is styled with
`Gui.Rendering.Text.TextStyle`, whose `FontFamily` defaults to `"sans-serif"` (`TextStyle.cs:107`).
The lectern dialog's title is a single `Text` widget built in `BuildTitleBar`
(`GuiDialogScribeLecternLibGui.cs`, the `scribe:scribe-gui-title` widget). Setting that one
`Text`'s `TextStyle.FontFamily` to the registered family is the whole seam — the title is a single
draw with no read/edit duality, so there is no line-metric lockstep to maintain. The task-row text
(read view + editor field) is deliberately left on the default `"sans-serif"` family.

**Scope note (2026-07-27):** an earlier pass targeted the task-row text instead of the title; that
was reversed at the author's request. Rows are on the default family; only the title carries Caudex.

**The chosen face:** Caudex — a humanist serif under the SIL Open Font License 1.1, chosen only
because it is unambiguously redistributable inside a mod `.zip` and legible as body text. The spike
proves the *seam*, not the final aesthetic. (An alternative worth noting: LibGUI already bundles
serif faces — Playfair Display, Cormorant Unicase — under family names Scribe could simply reference
with zero new assets. We still bundle Caudex here to also prove Scribe's own asset+register+license
path, which the parent font work needs; see Decision 1.)

## Goals / Non-Goals

**Goals:**

- Prove a **Scribe-bundled** `.ttf` loads via `SkiaAssetLoader.LoadFont`, registers via
  `FontRegistry.RegisterCustomFont`, and is picked up by `TextLayoutHelper` for the lectern row
  text, and on **no** other game GUI.
- Confirm it renders correctly on the author's Apple Silicon Mac.
- Establish the license-bundling discipline (ship `OFL.txt`, credit in `CREDITS`) that the parent
  font work will reuse.
- Leave the codebase clean: one registration call at client init, one `TitleFontFamily` const set on
  the title `Text`; trivially reverted if a finding kills the path.

**Non-Goals:**

- Per-tier faces (cuneiform tablet, handwritten notebook) — the parent Item 3 vision, not this.
- The global-swap route (OS install + `defaultFontName`) — game-wide, not mod-scopable, rejected.
- The FreeType-direct / Cairo `SetContextFontFace` mechanism — obsolete on the LibGUI/Skia path.
- Any `src/Core/` change, networking, persistence/sync, or codec bump — Mod-layer client render
  only.
- A `ScribeFontRegistry` abstraction, config toggle, or tier→face mapping — over-engineering for a
  one-face proof. A single client-init call suffices; the abstraction is designed in the parent
  work.
- Replacing the stroke-glyph path (`docs/specs/glyph-strokes-ingestion.md`).

## Decisions

### Decision 1 — Register via LibGUI's Skia `FontRegistry`, not FreeType-direct

Load the bundled `.ttf` with `SkiaAssetLoader.LoadFont("scribe", "fonts/caudex-regular.ttf")` and
register it with `FontRegistry.RegisterCustomFont("Caudex", FontWeight.Normal, typeface)` at client
init.

**Why:** This is LibGUI's shipping mechanism (`GuiModSystem.LoadFonts` does the same for its own
faces), it is cross-platform via SkiaSharp, it needs no filesystem path or temp file, and
`TextLayoutHelper` already consults the registry before system fallback so the registered family
"just works" for both measure and draw.

**Alternatives considered:**
- *FreeType-direct on a private Cairo surface* (the original design) — obsolete: the Cairo
  `ComposeElements` seam and `GuiDialogScribeLectern.cs` no longer exist; text renders through Skia.
- *Global font swap* (OS install + `clientsettings.json defaultFontName`) — rejected: game-wide,
  requires an OS-level install, exactly what this proposal avoids.
- *Reuse a LibGUI-bundled serif* (Playfair Display / Cormorant Unicase) by just naming it in
  `TextStyle.FontFamily`, bundling nothing — genuinely viable and zero-asset, BUT it would not
  exercise Scribe's own bundle→load→register→license pipeline, which is the discipline the parent
  work must inherit. We bundle Caudex to prove that pipeline; noted as an option the parent work can
  still take.

### Decision 2 — Route only the title `Text` at the registered family

Add a `TitleFontFamily = "Caudex"` const on `ScribeRowControlNudge` and set the title `Text`'s
`TextStyle.FontFamily` to it in `BuildTitleBar`. Leave the task-row text (read `Text`, collapsing
ghost, and the editor `ScribeMultilineField`) on the default `"sans-serif"`.

**Why:** Scoping is inherent — only the widget whose `TextStyle.FontFamily` names the registered
family resolves it; every other dialog, menu, tooltip, and the task rows keep their own family and
are untouched. No per-surface draw override is needed because `TextLayoutHelper` does the lookup
centrally. The title is a single `Text` (no read/edit duality), so unlike the abandoned row-text
approach there is no cross-view line-metric lockstep to maintain.

### Decision 3 — Register once at client init; no per-frame work, no explicit dispose needed

Register the face exactly once in `ScribeModSystem.StartClientSide` (mirroring the existing
`RegisterSvgIcon` precedent at `~:271`), not per-row or per-frame.

**Why:** `RegisterCustomFont` stores the `SKTypeface` in the shared registry once; `TextLayoutHelper`
caches resolved typefaces thereafter. The `SKTypeface` lives for the client session in a
process-global registry that outlives Scribe's dialogs, so there is no per-dialog handle to leak and
no Scribe-owned dispose hook to add (LibGUI owns the registry lifetime, exactly as it does for its
own bundled faces). This is deliberately simpler than the old FreeType path's manual
cache-and-dispose.

### Decision 4 — License gate is part of "done"

Ship Caudex's `OFL.txt` alongside the `.ttf` and credit Caudex in a `CREDITS` file. SIL OFL 1.1
permits bundling/redistribution inside the mod `.zip`; do not rename the font files if modified
(they are not modified here). The spike is not complete until the license artifacts are in place —
this bakes the discipline in before the parent font work adds more faces.

## Risks / Trade-offs

- **[Registered family not picked up]** → `TextLayoutHelper` checks `GetCustomTypeface` before system
  fallback (`:106`), so a correctly-registered family resolves; if the title still renders in
  sans-serif, the likely cause is a family-name mismatch (registration name vs. `TitleFontFamily`
  literal) or registration running after first layout. Mitigation: register in `StartClientSide`
  before any dialog opens, and keep the registration name and the const as one string.
- **[arm64 macOS render]** → SkiaSharp is already this fork's renderer (every LibGUI dialog Scribe
  draws exercises it), so this is largely retired vs. the old `freetype6` P/Invoke risk. The Mac run
  confirms rather than de-risks; still worth an explicit check since a bundled TTF is a new asset
  path. Mitigation: the change is one registration call + one const, trivially revertible.
- **[Shared-registry collision]** → `FontRegistry` is a process-global static on the `gui` mod;
  registering `"Caudex"` writes a global alias→typeface entry. Collision is implausible (no other mod
  requests "Caudex"), and this mirrors how LibGUI registers its own faces. Noted so a future reviewer
  isn't surprised Scribe writes into a shared registry.
- **[Title size/wrap under a serif]** → Caudex has different metrics than the default sans, so the
  title could measure wider or clip in the title-bar band at some font scales. Mitigation: the title
  is short and bold; verify at a couple of window font scales during the Mac run. (No read/edit
  line-metric lockstep concern here — that was the abandoned row-text approach; the title is a single
  draw.)
- **[Scope creep into the full font system]** → Non-Goals fence it: one face, one family const, no
  registry abstraction, no config, no tier mapping.

## Migration Plan

Not applicable — no data model, persistence, or wire-format change. Deploy is a local dev build run
on the author's Mac; "rollback" is reverting the client-init registration call and the two
`FontFamily` const edits and removing the bundled asset. No release ships from this spike unless the
findings say the path is sound.

## Open Questions

- **Bundle Caudex vs. reuse a LibGUI-bundled serif?** Decision 1 bundles Caudex to prove Scribe's own
  asset+license pipeline. If the parent work decides the proof of the *pipeline* isn't needed and any
  serif will do, it could instead just name Playfair Display / Cormorant Unicase (already registered
  by LibGUI) in `TextStyle.FontFamily` with zero new assets. Not blocking for the spike; recorded for
  the parent work.
- The `ScribeFontRegistry` shape, per-tier face selection, a client config toggle, and the sourcing
  of the actual production faces (rustic script, cuneiform) are deferred to the parent font work
  (`docs/specs/presentation-and-fonts.md` Item 3), explicitly not this spike.
