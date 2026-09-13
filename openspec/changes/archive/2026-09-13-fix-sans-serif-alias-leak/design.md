## Context

See proposal.md - Why. Mechanically: `ScribeModSystem.Assets.cs`'s `RegisterCustomFonts()` currently
does two independent things after loading Scribe's bundled `.ttf`s: (1) `RegisterCustomFont` per
bundled family — additive, scoped to that family name, harmless to other mods; (2)
`RegisterFontAlias("sans-serif", <first bundled family that loaded>)` — a single global mutation of
LibGUI's shared `FontRegistry`, consulted by every `gui`-dependent mod's default `TextStyle`. Only
(2) is being changed. `ScribeTaskFont.DefaultFamily` (`ScribeRowConstants.cs`) is currently the
literal string `"sans-serif"`; the task-font consumers (`BuildMetrics`'s `referenceFamily` fallback,
`Resolve`, the empty-string `TaskFontFamily` fallback) read that constant rather than the literal
string directly, so retargeting the constant is a single-point change for them.

**Correction post-implementation (2026-09-12):** the "HUD/Settings chrome" half of that original
claim was wrong. HUD chrome (`HudScribePins.cs`) never read `DefaultFamily` at all — its
`TextStyle`s never set `FontFamily`, so they fell through to LibGUI's own `TextStyle` ctor
initializer directly and were never affected by retargeting the constant. Settings chrome
(`ScribeTextDefaults.WrapSettingsChrome`, `ScribeSettingsContent.Pegged`) DID read `DefaultFamily`,
and retargeting it silently rebranded Settings chrome to Scribe's bundled font — contradicting both
call sites' own doc comments and the "leave HUD + Settings unwrapped" decision from
`adopt-libgui-31-improvements`. First fix attempt: gave those two call sites their own constant,
`ScribeTaskFont.LibGuiDefaultFamily = "sans-serif"`, so Settings chrome would keep following LibGUI's
raw default like HUD chrome did.

**Second correction, same day, after in-game review (author direction):** seeing HUD chrome actually
render in LibGUI's raw/OS-dependent default side-by-side with Scribe's own Noto-Sans-rendered
Notebook text read as inconsistent, not "corrected." Direction reversed: EVERY Scribe-owned surface
that doesn't follow the player's Task Text Font choice — HUD chrome, Settings chrome, the dev-tuning
dialogs, and the Task Notice/quest-prompt popups — now explicitly names `ScribeTaskFont.DefaultFamily`
via a new `ScribeTextDefaults.WrapChrome` ancestor wrap (mirroring `WrapSettingsChrome`, minus its
`FontSize` peg). `LibGuiDefaultFamily` was removed — nothing in Scribe wants LibGUI's raw default
anymore. See VSAPI-NOTES.md's "Custom TTF fonts in the GUI" section for the full writeup.

## Goals / Non-Goals

**Goals:**
- Eliminate Scribe's only global mutation of shared LibGUI font-resolution state.
- Preserve Scribe's own reason for wanting a deterministic (non-OS-lookup) default face.
- Change nothing about the bundled-face registration mechanism itself (`SkiaAssetLoader.LoadFont` +
  `RegisterCustomFont`) — only the alias call and the constant it existed to support.

**Non-Goals:**
- Re-deciding which bundled face is Scribe's default (the fallback preference order — Noto Sans →
  Noto Serif → Scapholene → La Belle Aurore → Caudex — carries over unchanged).
- Touching `task-font-metrics`' Caudex line-box pegging behavior — pegging is keyed off
  `ScribeTaskFont.DefaultFamily` resolving to *some* stable, custom-registered typeface, which holds
  regardless of whether that constant's string value is `"sans-serif"` (aliased) or `"Noto Sans"`
  (named directly).
- Re-litigating the Linux HarfBuzz crash fix — that is fully separate, already-shipped work
  ([[linux-harfbuzz-1-4-0-regression-investigation]]).

## Decisions

**Retarget the constant instead of keeping the alias and special-casing Scribe's own call sites.**
Change `ScribeTaskFont.DefaultFamily` from `"sans-serif"` to the same family
`RegisterCustomFonts()`'s fallback chain would have aliased it to (e.g. `"Noto Sans"`), computed at
registration time and threaded into `ScribeTaskFont` the same way `caudexRegistered` already is today
(`BuildMetrics(logger, caudexRegistered)`). Every existing consumer of `DefaultFamily` keeps working
unchanged — they were never written to depend on the literal string `"sans-serif"`, only on
`DefaultFamily` naming *a* stable face. Alternative considered: keep the global alias but have
Scribe's own widgets bypass it by naming the bundled family directly, leaving the alias (and its
collateral effect on other mods) in place. Rejected — that fixes nothing for third-party mods, which
is the entire point of this change.

**Drop the alias call entirely rather than making it conditional/opt-out.** No configuration flag,
no "only alias if no other `gui` mod is present" heuristic — per
[[linux-sans-serif-font-crash-root-cause]]'s "Scope limit" finding, mod load order can't be relied on
anyway (a mod named `hudui` typically starts before `scribe`), so a conditional alias would be
unreliable in exactly the cases it would matter. Simplest correct behavior: never touch it.

**Handle the zero-bundled-faces-loaded edge case by falling back to unaliased `"sans-serif"`, not by
inventing a new fallback.** If every bundled `.ttf` fails to load (already a logged warning path
today), `DefaultFamily` has no custom family to name. Falling back to the literal `"sans-serif"`
string reproduces exactly what the mod's default text would do without Scribe's font system at all —
correct by construction, and consistent with "Scribe never overrides the shared default" even in its
own degraded case.

## Risks / Trade-offs

[Any Scribe UI surface that was silently relying on the alias's specific typeface — rather than on
"a legible bundled face" — could look subtly different once the global alias is gone] → Superseded by
the decision reversal above: rather than let those surfaces go back to the player's own OS/LibGUI
theme font (the proposal's original plan), they now explicitly pin to `ScribeTaskFont.DefaultFamily`
via `ScribeTextDefaults.WrapChrome`/`WrapSettingsChrome`, so nothing changes visually for them at all —
they render the same bundled face they always did, just without leaking that choice onto other mods.

[No local machine running HudUI to directly reproduce/verify the original bug] → The mechanism is
confirmed by the reporter identifying the exact stat (temporal stability, 0-100) and by the code
path being unconditional and global, not probabilistic — verification is "does removing the alias
change what `"sans-serif"` resolves to system-wide," which is directly observable in a Scribe+HudUI
smoke test without needing to force a 3-digit stability value specifically.

## Migration Plan

No data/save migration — this is client-side font resolution only, not persisted state. Ship as a
normal point release. No rollback concern beyond reverting the code change if a regression surfaces.
