## Why

On some Linux clients (rolling-release distros — Arch, CachyOS — reported on the `scribe` and
`libgui` ModDB pages from May 2026 through today) the game crashes natively with
`free(): invalid pointer`, at world join or on the first Scribe dialog open, before any error is
logged. Tracing the crash logs into the code (and decompiling the shipped LibGUI `Gui.dll`) found
the trigger inside Scribe's own font setup, not an incidental ordering artifact: `BuildMetrics()`
unconditionally measures the literal family `"sans-serif"`. LibGUI itself resolves that to the
generic `"sans-serif"` keyword (it self-aliases `"sans-serif"` to itself at its own startup, before
any dependent mod runs — see design.md's correction note), which is not a custom-registered
typeface, so it falls through to a live native `SKTypeface.FromFamilyName` / fontconfig lookup for
that generic family. On a system with no/broken installed fonts
(consistent with an independent report that the affected users' AUR package doesn't pull in font
packages), that live lookup — or the HarfBuzz shaping call right after it — is a plausible native
abort site. Because the same `""`/`"sans-serif"` resolution also backs the factory-default task font
(every new player, until they pick one in Settings) and Settings chrome, this isn't a rare path.

The fix is closeable entirely inside Scribe, using LibGUI's own public `FontRegistry` API — no
LibGUI fork or upstream fix needed for Scribe's own trigger.

**Scope note:** a follow-up cross-check (2026-08-30) found this is a wider LibGUI-ecosystem footgun,
not unique to Scribe — a libgui ModDB reporter (MystiVaid) crashed with the identical
`free(): invalid pointer` signature running only `libGUI` + `HudUI` + `ChatUI` (no Scribe installed
at all), and resolved it by disabling those two other LibGUI-based mods, not LibGUI itself. So this
change fixes Scribe's own trigger and removes it from the set of mods that crash the client for
affected Linux users, but does not fix the crash for users running other LibGUI-based mods without
Scribe — that requires either those mods applying the same alias pattern or an upstream fix in
LibGUI's `FontRegistry`/`TextLayoutHelper` itself. Worth relaying to ripls56 (upstream) alongside the
existing issue, but out of scope for this change.

## What Changes

- Register a font alias, `FontRegistry.RegisterFontAlias("sans-serif", <bundled family>)`, once at
  client startup, immediately after Scribe's bundled task-font registration and before
  `ScribeTaskFont.BuildMetrics(...)` runs (`ScribeModSystem.Assets.cs`).
- Pick the alias target from a fallback chain of Scribe's already-registered bundled faces (prefer
  Noto Sans; fall through Noto Serif → Scapholene → La Belle Aurore → Caudex) based on which one
  actually loaded, so the alias is never pointed at a family that isn't registered.
- Net effect: every subsequent resolution of the literal `"sans-serif"` family — Scribe's own
  `DefaultFamily` probes, ordinary task/note rendering for any player who hasn't chosen a bundled
  font, and Settings chrome — resolves through `FontRegistry.GetCustomTypeface` to a bundled TTF
  Scribe ships, and never reaches `SKTypeface.FromFamilyName` or the OS font manager.
- No change to which font families are offered in the Settings font selector, no change to task-font
  size/offset math (that stays pegged to Caudex's line-box regardless of what `"sans-serif"` now
  resolves to internally), no change to Core, network, or save format.

## Capabilities

### New Capabilities
(none)

### Modified Capabilities
- `bundled-font-rendering`: adds a requirement that the mod's default/fallback text family
  (`"sans-serif"`, used wherever no bundled family is explicitly named) SHALL NOT depend on OS/
  fontconfig name resolution either — extending the existing "no OS font install/fontconfig
  reliance" principle (currently scoped to Scribe's explicitly-named registrations) to cover the
  literal default family name too, via a `FontRegistry.RegisterFontAlias` redirect to a bundled
  face.

## Impact

- `src/Mod/ScribeModSystem.Assets.cs` — add the alias registration + fallback-chain selection in
  `RegisterCustomFonts()`.
- No Core/network/save-format surface touched. Client-only, font-registration-only change.
- Evidence trail (decompiled call chain, ModDB crash reports with dates) already written up in
  `openspec/changes/assess-libgui-decoupling/design.md` §5 ("Refined finding (2026-08-30)") — this
  change implements the fix that assessment identified; it does not alter that assessment's
  architecture recommendation (still open, separate decision).
- Verification is community-assisted: no Linux/CachyOS test machine is available locally, so
  confirmation relies on affected ModDB reporters testing a build with the fix.
