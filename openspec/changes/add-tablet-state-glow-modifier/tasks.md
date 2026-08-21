## 1. State-aware glow lookup (CuneiformGlow.cs)

- [x] 1.1 Change `CuneiformGlowTable.For(string? material)` to `For(string? material, TabletState state)`.
      Rename the existing material switch to a private `ForWetMaterial(string? material)` helper (the four
      current dark seeds unchanged) and make `For` dispatch: `Hard` → `HardHalo`, `Fired` → `FiredHalo`,
      default (Wet) → `ForWetMaterial(material)`. Keep the tuning-envelope comment (alpha ~0.35–0.65,
      blur fraction ~0.05–0.08) and note that wax reaches this only via the Wet branch.
      — DONE: state-first switch in `CuneiformGlow.cs`; `ForWetMaterial` holds the four unchanged dark
      seeds; class doc comment rewritten to explain the wet-dark vs hard/fired-light polarity split.
- [x] 1.2 Add the two shared light-halo seeds `HardHalo` and `FiredHalo` as `static readonly
      CuneiformGlow` — near-white color, distinct from each other, alpha/blur in the tuned envelope.
      Mark the exact values as placeholder pending the §3 in-game tuning gate (mirror how the wet seeds
      are annotated).
      — DONE: `HardHalo` (0.86,0.82,0.74, α0.45) / `FiredHalo` (0.94,0.90,0.82, α0.50), blur 0.065,
      flagged PLACEHOLDER pending the §4 in-game tuning gate.

## 2. Thread tablet state into the call sites (GuiDialogScribeTablet.cs)

- [x] 2.1 Update the three `CuneiformGlowTable.For(_material)` call sites — row glow, resting title,
      editing title — to `For(_material, _state)`. Confirm no other caller of `CuneiformGlowTable.For`
      exists (grep) so the signature change is fully covered.
      — DONE: all three sites (rows 234, resting title 281, editing title 312) now pass `_state`; grep
      confirms these are the only callers.

## 3. Build + verify

- [x] 3.1 `dotnet build src/Mod/Mod.csproj -c Debug` clean (0 warnings / 0 errors).
      — DONE: Build succeeded, 0 Warning(s) / 0 Error(s).
- [x] 3.2 `dotnet test tests/Core.Tests/Core.Tests.csproj` — no Core change expected; confirm no new
      failures beyond the known pre-existing illumination-curve ones.
      — DONE: 474 passed; the same 7 pre-existing `ScribeBrightnessCurveTests`/`ScribePlayerSettingsTests`
      illumination-floor/curve failures remain (Core untouched by this Mod-only change), no new failures.
- [x] 3.3 Restage (Debug) — never while the client is running.
      — DONE: confirmed client not running, then `build/restage.sh Debug` staged 138 files.

## 4. In-game tuning gate + playtest (playtest gate)

  NOTE: there is no live glow-tuning dev command (only `.geartune`/`.scribelight`/`.scribeprobe` and
  `/scribe seed|tablet` exist). Tuning follows the same edit-constant → restage → relaunch loop the wet
  seeds used: eyeball the placeholder halos, report/adjust the `HardHalo`/`FiredHalo` constants in
  `CuneiformGlow.cs`, restage, relaunch.

- [ ] 4.1 In creative, use `/scribe tablet hard` then `/scribe tablet fired` on a written tablet of each
      clay color (blue, red, fire). Judge whether the placeholder Hard/Fired light halos make the cuneiform
      read clearly on each darker backdrop, and whether one shared halo works across all three colors per
      state. Report what needs nudging (brighter/dimmer/tighter).
- [ ] 4.2 Adjust the `HardHalo`/`FiredHalo` constants per 4.1, rebuild, restage; repeat until both states
      read well.
- [ ] 4.3 Regression: confirm WET tablets (all three colors + wax) are visually unchanged from before —
      same dark halo, same legibility.
- [ ] 4.4 Re-run `TESTING.md` `00000016` (fired/hardened cuneiform readability) and record the verdict
      there — the light halo should lift the ink instead of muddying it.
