## Context

The cuneiform outer-glow system (`cuneiform-contrast-glow`) paints a soft halo behind each ink stroke
to lift the text off the clay backdrop. `CuneiformGlowTable.For(string? material)` returns a
`CuneiformGlow(Vector4 Color, float BlurFraction)` seed keyed on the clay material only:

```csharp
"clay-blue" => BlueDefault,
"clay-red"  => RedDefault,
"wax"       => WaxDefault,
_           => FireDefault,   // clay-fire
```

All four seeds are **dark** halos (`Color.W` ≈ 0.40 alpha, blur ≈ 0.060 of em), tuned on **wet**
tablets, where a dark halo behind dark ink separates the text from a mid-tone backdrop. That polarity
is only correct on the lighter wet backdrop. Hardened and fired tablets render on **darker** backdrops
(playtest `00000016`), so the dark halo is dark-on-dark and *reduces* contrast. The glow lookup never
sees the tablet's life-cycle state, so all three states get the wet halo.

The dialog already tracks both dimensions independently: `GuiDialogScribeTablet` holds `_material` (the
base clay color: `clay-blue`/`clay-red`/`clay-fire`/`wax`) and `_state` (`TabletState.Wet/Hard/Fired`),
and calls `CuneiformGlowTable.For(_material)` at three sites (row glow, resting title, editing title).
So the missing input is already in hand — it just isn't threaded into the lookup.

## Goals / Non-Goals

**Goals:**
- Make cuneiform legible on hardened and fired tablets by flipping their halo to light polarity.
- Keep wet tablets byte-identical to today (their halo is already validated in-game).
- Keep the change small and readable: one added parameter, one added branch, no new abstractions.

**Non-Goals:**
- No change to ink color, backdrop, or any theme value (`ScribeTheme.ForTablet` stays material-only).
- No per-color halo for hardened/fired — one shared light halo per state, distinct between the two.
- No new persisted setting; final halo values are baked constants (in-game tuning gate).
- No `src/Core/` change, no dependency, no persistence/schema change.

## Decisions

**Decision 1 — Add `TabletState` as a second key to `CuneiformGlowTable.For`, not a new table.**
Signature becomes `For(string? material, TabletState state)`. Wet dispatches to the existing
per-material seeds (unchanged). Hard and Fired short-circuit to a single shared light-halo seed each,
*before* the material switch, since they don't vary by color:

```csharp
public static CuneiformGlow For(string? material, TabletState state) => state switch
{
    TabletState.Hard  => HardHalo,
    TabletState.Fired => FiredHalo,
    _                 => ForWetMaterial(material),   // existing per-material dark seeds
};
```

Wax reaches this method only in the Wet branch (it has no Hard/Fired sibling item, so the dialog never
opens a hardened/fired wax), so `ForWetMaterial` still owns the wax seed and no special-casing is
needed. *Alternative considered:* a full `(material × state)` table — rejected as more surface area for
no behavioral gain, since hardened/fired are deliberately color-uniform. Reading the state switch first
makes the "state wins over color for the set states" rule obvious at a glance.

**Decision 2 — Light polarity via halo color only, reusing the existing two-pass renderer.**
The glow renderer already draws all blurred halos first, then crisp ink on top, and the halo color is
arbitrary (`Vector4`). A light halo is just a high-value color with the same alpha/blur envelope — no
renderer change. `HardHalo` and `FiredHalo` are near-white seeds (distinct values so the two states
read differently), with alpha/blur kept in the same tuned envelope as the wet seeds (alpha ~0.35–0.65,
blur fraction ~0.05–0.08). Exact values are placeholders pending the in-game tuning gate.

**Decision 3 — Thread `_state` into the three existing call sites.**
`GuiDialogScribeTablet` changes `For(_material)` → `For(_material, _state)` at the row glow, resting
title, and editing title. No other caller of `CuneiformGlowTable.For` exists. The dev glow command
continues to mutate the in-memory tuning state and force a repaint as today; because the open dialog's
`_state` is fixed, tuning naturally targets the current tablet's state.

## Risks / Trade-offs

- **[Placeholder halo values may not be right on first paint]** → The values are explicitly a tuning
  gate: iterate with the runtime glow dev command on real hardened/fired tablets (all three colors) and
  bake, exactly as the wet seeds were found. The `/scribe tablet hard|fired` dev command already makes
  every state reachable in creative for this.
- **[A future hardened/fired wax path would fall through to a light halo]** → Acceptable and arguably
  correct: no such item exists today (wax has no `-hard`/`-fired` variant), and if one were ever added a
  light halo on a darker wax is the same fix, not a regression. Documented, not guarded.
- **[Contrast could regress on one clay color if a single shared light halo doesn't suit all three]** →
  The three fired/hardened backdrops are close in value (all darkened clay); a shared halo is the design
  intent (per the locked decision). If tuning shows one color genuinely needs its own seed, the switch
  in Decision 1 is the single place to split it later — cheap to revisit.
