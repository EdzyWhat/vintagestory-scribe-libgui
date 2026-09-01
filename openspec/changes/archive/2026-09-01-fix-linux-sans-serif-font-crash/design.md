## Context

`ScribeModSystem.Assets.cs:84` `RegisterCustomFonts()` runs once at `StartClientSide`, for every
player, before any dialog opens:

1. Loads Caudex (bold cut) and registers it under every `FontWeight` via
   `FontRegistry.RegisterCustomFont("Caudex", weight, bold)`.
2. Loads the 4 task-text faces (Scapholene, La Belle Aurore, Noto Sans, Noto Serif) and registers
   each under every `FontWeight`.
3. Calls `ScribeTaskFont.BuildMetrics(...)` (`ScribeRowConstants.cs:275`), which measures every known
   family **plus** `DefaultFamily = "sans-serif"` (`ScribeRowConstants.cs:178`) against Caudex's
   line-box, via `TextLayoutHelper.MeasureText("Ag", family, ...)`.

Decompiling the shipped `Gui.dll` (LibGUI 3.1.0, `src/Mod/lib/Gui.dll`,
`Gui.Rendering.Text.FontRegistry`/`TextLayoutHelper`) shows `MeasureText` resolves a family in two
steps: `ResolveFontFamily(name)` looks up a small alias table before `GetCustomTypeface(resolvedName,
weight)` is tried; a miss there falls through to `SKTypeface.FromFamilyName(resolvedName, ...)` — a
live call into the OS's native font manager (fontconfig on Linux) — followed immediately by
`TextShaper.Shape(...)` (HarfBuzz).

**Correction (checked against decompiled `GuiModSystem.StartClientSide`, which runs strictly before
any consuming mod's, since `gui` is a hard dependency):** the alias table's *initial* static value is
`sans-serif → Arial`, but LibGUI overwrites this itself at its own `StartClientSide`, before Scribe
(or any dependent mod) ever runs: `FontRegistry.RegisterFontAlias("sans-serif",
GuiStyle.StandardFontName)`. `GuiStyle.StandardFontName` (`VintagestoryAPI.dll`) is itself the
literal string `"sans-serif"` — so this is a **self-referencing alias**: by the time Scribe's
`BuildMetrics` probes `"sans-serif"`, `ResolveFontFamily` returns `"sans-serif"` right back, and
`GetCustomTypeface("sans-serif", weight)` misses (nothing is registered under that literal name), so
the live lookup requests the OS's *generic* `"sans-serif"` alias directly — not `"Arial"`. Practically
this is a stronger match for Jack_Frost's "missing font packages" report: fontconfig natively
understands `"sans-serif"` as a generic family keyword that resolves via its own `<alias>`/default
rules to whatever's configured as the system's default sans font — with zero fonts installed, it has
nothing to resolve *that* to either, which is a very plausible corruption/abort site in the native
resolver. (LibGUI also self-aliases `"serif" → GuiStyle.DecorativeFontName` = `"Lora"`, a real,
not-self-referencing family — but nothing in `Gui.dll` or Scribe ever sets `FontFamily = "serif"`, so
that particular alias is dormant and not a practical crash risk.) The fix here is unaffected by this
correction — aliasing `"sans-serif"` to a bundled face overwrites whatever the current mapping is,
self-referencing or not — but the "Arial" framing in earlier notes was based on the *static default*,
not the *actual runtime value* by the time Scribe probes it, and should not be repeated.

Scribe never registers anything under the literal `"sans-serif"` family, so probing it (after
LibGUI's own self-referencing alias, and before Scribe's fix) always takes the live-lookup branch. On
Linux systems with no/broken installed fonts, that branch is a plausible `free(): invalid pointer`
native-abort site — matching every reported crash log (last line is always one of Scribe's own
font-registration notifications, then straight to the abort, no further log). Full evidence trail
(ModDB reports with dates, the traced call chain) is in
`openspec/changes/assess-libgui-decoupling/design.md` §5.

Because `ScribePlayerSettings.TaskFontFamily`'s factory default is `""` (`ScribePlayerSettings.cs:274`)
and `ScribeTaskFont.Resolve` maps that straight to `"sans-serif"`, this same live-lookup path is also
on the hot path for ordinary task/note rendering (any player who hasn't picked a bundled font) and
for Settings chrome, which renders literal `"sans-serif"` unconditionally.

A follow-up cross-check found the same crash signature on a libgui ModDB reporter (MystiVaid) running
only `libGUI` + `HudUI` + `ChatUI` — no Scribe. So this is a wider LibGUI-ecosystem footgun (any mod
that measures/draws an unstyled or literal system-family text string at startup can hit it); this
change closes Scribe's own trigger only.

**Scribe+LibGUI-only case (no other LibGUI mods): this fix likely covers it, including LibGUI's own
default-styled text, not just Scribe's.** `Gui.Rendering.Text.TextStyle.FontFamily` defaults to
`"sans-serif"` for *every* stock `Text` widget, including LibGUI's own — but checking what LibGUI
itself opens at `StartClientSide` (`GuiGlobalOverlay`, whose `Build()` returns an empty `SizedBox()`
with no text at all; and a first-run `SettingsDialog`, opened via `EnqueueMainThreadTask` rather than
synchronously) shows neither renders any text *synchronously within LibGUI's own `StartClientSide`*.
Mod loading calls every mod's `StartClientSide` synchronously, in one batch, strictly before the
first render frame; `gui`'s `StartClientSide` runs before `scribe`'s (hard dependency, not a
tie-broken guess like the HudUI ordering) and any `EnqueueMainThreadTask` work — including LibGUI's
first-run Settings dialog — fires later still, after every mod's `StartClientSide` (including
Scribe's) has finished. Scribe's own `BuildMetrics` probe, by contrast, is a direct synchronous
`MeasureText` call inside `RegisterCustomFonts` — which is exactly why it crashes *immediately*,
before LibGUI's deferred rendering ever gets a chance to. Net effect: with Scribe installed, its
alias registers before *anything* — Scribe's or LibGUI's own — ever actually measures/draws
`"sans-serif"` text, so LibGUI's own default text is incidentally protected too, for this specific
"only Scribe + LibGUI" case. This is inferred from decompiled code and the observed log timing (both
of Scribe's font-registration lines always appear before the crash, which rules out LibGUI's own
`StartClientSide` crashing first), **not verified on a real CachyOS machine** — treat it as a strong
hypothesis pending community confirmation, not a guarantee.

## Goals / Non-Goals

**Goals:**
- Eliminate every code path where Scribe's own text rendering forces a live OS/fontconfig font
  lookup, by making `"sans-serif"` always resolve to a bundled typeface Scribe already ships.
- Keep the existing task-font line-box-pegging behavior intact — task rows keep matching Caudex's
  line-box regardless of which concrete face `"sans-serif"` now resolves to internally.
- Degrade gracefully if the preferred alias target itself failed to load (corrupt/missing asset),
  rather than silently re-introducing a live lookup under a different family name.

**Non-Goals:**
- Fixing the crash for players running other LibGUI-based mods without Scribe (confirmed to exist
  independently — see Context). That needs either those mods adopting the same pattern or an
  upstream LibGUI fix; tracked as a follow-up communication, not part of this change's code.
- Changing which fonts the Settings font selector offers, or any task-font metrics/offset/scale
  values.
- Touching `assess-libgui-decoupling`'s architecture recommendation (Option C) — unrelated decision.

## Decisions

**Alias target: prefer "Noto Sans", with a fallback chain.**
`FontRegistry.RegisterFontAlias(alias, systemFamily)` rewrites the `FontMappings` entry so
`ResolveFontFamily("sans-serif")` returns the new target instead of `"Arial"`; `GetCustomTypeface`
then hits on that target (already registered under every weight), and the live-lookup branch is
never reached. "Noto Sans" is the natural choice — it's the general-purpose sans body face among the
bundled task fonts (vs. Noto Serif, a serif face, or Scapholene/La Belle Aurore, which are stylized
script faces not meant as a neutral default).

Guard: only set the alias to a face that actually loaded. The registration loop already tracks
per-font load success (logs a warning and `continue`s on failure). Build a small ordered candidate
list — `["Noto Sans", "Noto Serif", "Scapholene", "La Belle Aurore", "Caudex"]` — and alias
`"sans-serif"` to the first candidate that successfully registered. If literally none of Scribe's
bundled fonts loaded (a catastrophic packaging failure, already logged loudly for each one), skip the
alias entirely — at that point the mod's own text is already falling back to a live lookup for its
*named* families too, so this change can't make things worse, and forcing an alias to an unregistered
name would just move the crash risk rather than remove it.

**Where the alias call happens: once, after the task-font loop, before `BuildMetrics`.**
`RegisterFontAlias` must run before `BuildMetrics()` probes `DefaultFamily`, so the very first
`"sans-serif"` measurement already resolves through the alias. Since `FontRegistry` is a static
registry (its dictionaries are `static readonly`), registering the alias once at `StartClientSide` is
sufficient for the whole client session — no per-dialog or per-frame re-registration.

**No try/catch around the probe.** `free(): invalid pointer` is a native (glibc) heap-corruption
abort, not a managed `.NET` exception — it terminates the process regardless of any `try`/`catch` in
managed code. The only real fix is preventing the dangerous native call from happening at all, which
the alias does structurally (removes the call site), not defensively (doesn't attempt to survive it).

**Considered and rejected: hardcode `DefaultFamily`'s `SizeScale` to skip measurement entirely.**
Instead of aliasing, `ScribeTaskFont.SeedFamily` could special-case `DefaultFamily` and skip
`ProbeY` altogether, using a fixed scale (matching the existing `familyY <= 0f` fallback: scale 1).
Rejected: this only closes `BuildMetrics`'s one-time probe, not the other `"sans-serif"` call sites
(Settings chrome, and any ordinary task-row render for a player still on the default/empty
`TaskFontFamily`) — those still reach `TextLayoutHelper.MeasureText`/`DrawText` with the literal
`"sans-serif"` family, so the live lookup (and the crash) would still fire the moment such text
renders. The alias fixes the resolution itself, so every call site is covered by one registration.

**Considered and rejected: alias `"serif"`/`"monospace"` too.** Nothing in Scribe's own code
requests those families today, so there's no known trigger to close. Left out to keep this change
narrowly scoped to the reported crash; can be added later as cheap extra hardening if a future
surface ever uses them.

## Risks / Trade-offs

- **[Risk] The alias changes what "Default" task font visually renders as** (previously whatever the
  OS substituted for "Arial"; now always Noto Sans) → **Mitigation:** this is an intentional,
  disclosed side effect, not a regression — rendering was already platform-dependent and unverifiable
  cross-platform; a bundled face makes it deterministic. Task row height stays pegged to Caudex's
  line-box either way (`task-font-metrics` spec's contract is about the pegging outcome, not which
  concrete face `"sans-serif"` resolves to internally), so no spec requirement is violated.
- **[Risk] `FontRegistry` is shared process-wide, so this alias also affects any other LibGUI-based
  mod's default-styled `Text`, in either direction** → **Mitigation:** this is a *beneficial* side
  effect for other mods loaded after Scribe on the same client (their default text also stops
  triggering the live lookup); it cannot make another mod render less safely than it already does
  before Scribe's `StartClientSide` runs. Worth flagging in the ModDB replies as an incidental benefit,
  not something to design around.
- **[Risk] No local Linux/CachyOS machine to reproduce or verify on** → **Mitigation:** verification is
  community-assisted (ask affected ModDB reporters to test a build); the Core test suite and a manual
  macOS/Windows smoke test (Settings font selector still works, task rows still render, no line-box
  regression) cover everything that's locally testable.
- **[Risk] This does not fix the crash for players running other LibGUI mods without Scribe** (see
  Context) → **Mitigation:** out of scope by design; flagged to the maintainer/upstream separately.

## Migration Plan

Pure additive client-side code change — one new call in `RegisterCustomFonts()`, no data migration,
no save-format or network change. Ship in the next release; no feature flag needed (the alias only
ever redirects an already-undefined-behavior resolution path onto a face Scribe already ships).
Rollback is a plain revert if it somehow regresses default-font rendering.

## Open Questions

- Does the alias alone fully resolve the crash on an affected machine, or is the AUR/missing-fonts
  condition doing something else too (e.g. a *different* unstyled-text call site we haven't found)?
  Only answerable by a reporter testing a build — tracked as a task, not blocking implementation.
- **Open, and more fundamental than the above: is the crash actually about family resolution at
  all?** `BuildMetrics` measures Caudex (line 284, a fully custom-registered face) *before* it ever
  touches `DefaultFamily`/`"sans-serif"` (line ~300) — both calls do real text shaping
  (`MeasureText` → `TextShaper.Shape`/HarfBuzz), and neither logged anything on success in the
  original code, so an existing crash log cannot distinguish "crashed measuring Caudex" from
  "crashed measuring sans-serif." If the true bug is the *original* ABI-mismatch theory
  (ripls56/vslibgui#2 — bundled HarfBuzzSharp vs. system libharfbuzz, crashing on the first shape
  call regardless of which font), the crash would be at the Caudex probe, and this fix — which only
  changes what `"sans-serif"` resolves to — would not help at all, since Caudex is never touched by
  the alias. Added two diagnostic `Notification` log brackets in `BuildMetrics`
  (`ScribeRowConstants.cs`, around both the Caudex probe and the `DefaultFamily` probe) so the next
  crash log will show exactly which measurement was in progress. If a future report shows the
  "measuring 'Caudex'..." line but not "measured reference line-box...", that's decisive evidence
  for the ABI-mismatch theory over this fix's theory, and this change would need to be revisited
  (or the maintainer should treat that as new evidence for `assess-libgui-decoupling`'s Option D
  weighing, per its §5 "on keeping LibGUI at all").
- Should we also report this pattern upstream to ripls56 (vslibgui) as a general hardening
  suggestion (e.g. LibGUI shipping its own non-OS-dependent fallback for `"sans-serif"`/`"serif"`/
  `"monospace"` instead of hardcoding real OS family names)? Recommended, but a communication task,
  not part of this change.

**Resolved (2026-08-30) — the question above is answered, and the answer is "no, this fix does not
address the real bug."** SnuwWulfie tested `scribe_1.3.4-rc.1.zip` (this fix, built) on the same
CachyOS setup. Client log:
```
[scribe] bundled font 'Caudex' (bold cut) registered under all weights for the lectern dialog title
[scribe] bundled task-text fonts registered for the settings font selector
[scribe] "sans-serif" aliased to bundled font 'Noto Sans' (avoids a live OS font lookup)
[scribe] measuring 'Caudex' as the task-font line-box reference
free(): invalid pointer
```
The alias registered and logged successfully — this fix's own mechanism worked exactly as designed.
The crash is one line later, at the Caudex probe, with no "measured reference line-box" line after
it — i.e. it crashed *before* `BuildMetrics` ever reaches the `DefaultFamily`/`"sans-serif"` probe
this change targets. Caudex is a fully custom-registered face loaded from Scribe's own bundled TTF;
measuring it never calls `SKTypeface.FromFamilyName` or touches fontconfig at all — there is no
OS/family-resolution step in that call path for this fix to have affected. The only remaining live
native call in that probe is `TextShaper.Shape` (HarfBuzz) on an already-in-memory, already-correct
typeface. So this crash is not a family-resolution problem, full stop — it's the very first
HarfBuzz shape call of the client session, unconditionally, regardless of which font is being
shaped. That is exactly the *original* ripls56/vslibgui#2 theory (bundled `libHarfBuzzSharp` 8.3.1
ABI-mismatched against the system's `libharfbuzz`), which this change explicitly was not designed to
fix (see Non-Goals). **This change's alias fix is confirmed working on its own terms but does not
resolve the reported Linux crash for at least this reporter.** See
`assess-libgui-decoupling/design.md` §5 for the updated architecture-decision implications — this
finding raises that document's urgency rather than lowering it.
