## Why

Scribe registers `FontRegistry.RegisterFontAlias("sans-serif", <bundled family>)` at client init to
make its own default text family resolve deterministically instead of hitting a live OS/fontconfig
lookup. `"sans-serif"` is not Scribe-scoped, though — it is LibGUI's shared, framework-wide default
`TextStyle.FontFamily`, so every `gui`-dependent mod's unstyled text resolves through the same alias.
A ModDB report confirmed the fallout: installing Scribe makes HudUI's temporal-stability stat (a
fixed-width 0-100 number display) wrap "100" onto two lines, because Scribe's bundled Noto Sans
renders those digits wider than whatever "sans-serif" previously resolved to. The alias's original
justification — avoiding a Linux HarfBuzz crash theorized to be triggered by the live OS lookup — was
independently disproven by a later investigation (the real crash was an unrelated HarfBuzz ABI symbol
collision, fixed elsewhere); the alias has carried this collateral cost with no remaining benefit
since.

## What Changes

- Stop calling `FontRegistry.RegisterFontAlias("sans-serif", ...)`. Scribe SHALL NOT modify what the
  shared `"sans-serif"` family resolves to for any other mod's text.
- `ScribeTaskFont.DefaultFamily` changes from the literal string `"sans-serif"` to name one of
  Scribe's own bundled, custom-registered families directly (e.g. `"Noto Sans"`, via the same
  preference-ordered fallback chain the alias used), so Scribe's own default-family text (the
  task-font default, HUD chrome, Settings chrome, History/Timer/Guestbook metadata) keeps resolving
  to a deterministic, custom-registered typeface rather than a live OS lookup — without touching the
  shared keyword.
- Per-family `RegisterCustomFont` registrations (Caudex, task-font selector faces) are unchanged —
  they only affect text that explicitly names those families and never affected third-party mods.
- **No visual change (superseded 2026-09-12):** an earlier draft of this proposal had HUD chrome,
  Settings chrome, and History/Timer/Guestbook metadata fall back to the literal `"sans-serif"`
  sentinel (LibGUI's own raw, platform-dependent default) once the alias was removed. In-game review
  found that inconsistent with the rest of Scribe's UI, so those surfaces (plus the dev-tuning dialogs
  and Task Notice/quest-prompt popups, not originally called out here) now explicitly name
  `ScribeTaskFont.DefaultFamily` via `ScribeTextDefaults.WrapChrome`/`WrapSettingsChrome` instead —
  they keep rendering the exact bundled face they always did; only third-party mods' text is affected
  by this change.

## Capabilities

### New Capabilities
(none)

### Modified Capabilities
- `bundled-font-rendering`: replaces the "literal default text family never resolves via
  OS/fontconfig lookup" requirement (which currently mandates a global `RegisterFontAlias("sans-serif",
  ...)` call) with a requirement that Scribe's own default family resolves deterministically to a
  bundled typeface *without* aliasing the shared `"sans-serif"` keyword, so other `gui`-dependent
  mods' own default text is never affected by Scribe being installed.

## Impact

- Affected code: `src/Mod/ScribeModSystem.Assets.cs` (`RegisterCustomFonts`), `src/Mod/ScribeRowConstants.cs`
  (`ScribeTaskFont.DefaultFamily` and its consumers via `BuildMetrics`/`Resolve`).
- No `src/Core` changes; no new dependencies; no network/save-format impact.
- Downstream: fixes the reported HudUI number-wrap bug and, by construction, any other
  `gui`-dependent mod's default-font text that Scribe was silently affecting.
