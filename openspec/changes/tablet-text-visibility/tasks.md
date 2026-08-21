# Tasks — tablet-text-visibility

> DESIGN PROPOSAL. These are the steps a future implementer follows AFTER the user picks a direction
> in `design.md`. Nothing here is implemented yet. The values below are the recommended (hybrid A +
> C-for-links) set; swap them if the user chooses differently.

## 0. Gate on the decision

- [x] 0.1 Confirm the user has chosen a direction. Default assumed here: **hybrid** — dark glow
      (Option A) for all four palettes + a dedicated per-material link ink (Option C for links); body
      ink unchanged; no Option B. If a different pick, adjust tasks 1–3 accordingly.

## 1. Per-material link ink (Option C for links) — `ScribeTheme.cs`

- [x] 1.1 Add four `internal static readonly Vector4` tablet link inks, documented like the existing
      `ChalkboardLinkText`, with the recommended values:
      - fire: `new(0.42f, 0.18f, 0.07f, 1.0f)` (`#6B2E12`, ~4.7 : 1)
      - red:  `new(0.44f, 0.11f, 0.11f, 1.0f)` (`#701C1C`, ~4.8 : 1)
      - blue: `new(0.15f, 0.33f, 0.46f, 1.0f)` (`#265475`, ~5.1 : 1)
      - wax:  `new(0.44f, 0.28f, 0.06f, 1.0f)` (`#70470F`, ~4.9 : 1)
- [x] 1.2 Add `public static Vector4 ForTabletLink(string? material)` mirroring `ForTablet`'s switch
      (`clay-blue`/`clay-red`/`clay-fire`/`wax`, default → fire). It always returns a link ink (no
      Pixel-Art gate here — the caller gates; see 3.1), so the selector stays a pure material→color map.
- [x] 1.3 Note in the XML doc why these are decoupled from `colors.Primary`: Primary is a mid-value
      *fill* color that fails AA as small text on the same-value clay ground (measured 2.3–3.7 : 1) —
      same decouple-from-Primary reasoning the Chalkboard's `ChalkboardLinkText` already documents.

## 2. Dark glow (Option A) — `CuneiformGlow.cs`

- [x] 2.1 Retune the three `CuneiformGlowTable` seeds from light halos to dark, ink-derived halos with
      a tighter blur:
      - `FireDefault = new(new Vector4(0.20f,0.10f,0.05f, 0.55f), 0.060f)`
      - `RedDefault  = new(new Vector4(0.24f,0.10f,0.09f, 0.55f), 0.060f)`
      - `BlueDefault = new(new Vector4(0.12f,0.16f,0.20f, 0.55f), 0.060f)`
- [x] 2.2 Add a NEW `WaxDefault = new(new Vector4(0.28f,0.22f,0.12f, 0.55f), 0.060f)` and route `"wax"`
      to it in `For(...)` (wax no longer rides the fire seed).
- [x] 2.3 Update the `CuneiformGlowTable` header comment: the halos are now DARK (a soft dark outline
      lifts dark ink off a light-mid clay ground); the old "a light halo lifts dark ink" reasoning was
      the inverted-polarity bug this change fixes. Keep the two-pass note (halos first, crisp ink
      overwrites overlaps) — unchanged and still correct for a dark halo.

## 3. Wire the link ink into the tablet — `GuiDialogScribeTablet.cs`

- [x] 3.1 In `DecorateRowStyle`, on the Pixel-Art path (same condition the grip/glow use), set
      `style = style with { LinkColor = ScribeTheme.ForTabletLink(_material) }`. With Pixel-Art OFF the
      tablet follows the global theme over a flat panel, so leave `LinkColor` unset there (falls through
      to `colors.Primary` as today, which is correct on a flat themed panel).
- [x] 3.2 Confirm no other tablet code path needs touching: `ScribeEditorContent`/`ScribeReadContent`/
      `ScribePinnedContent` already resolve `style.LinkColor ?? colors.Primary`, and
      `ScribeItemLabel.Build` / `ScribeLinkIcon.Build` already pass the color + `style.CuneiformGlow`
      through to the renderer. No renderer change.

## 4. Build

- [x] 4.1 `dotnet build src/Mod/Mod.csproj` — 0 errors, 0 warnings. (Copy the gitignored vendored
      `src/Mod/lib/*.dll` LibGUI deps into a fresh worktree first if needed.)
- [x] 4.2 `bash build/restage.sh Debug` (client not running) so the change is testable in-game.

## 5. In-game verification gates (Pixel-Art Display ON)

- [x] 5.1 On a wet **fire** tablet with a Link/Tracker/Craft row: the item name reads as a distinct,
      clearly-legible warm-rust link, obviously not the body ink and no longer washed into the clay.
  - Confirmed 2026-08-20: TESTING.md `00000011` "Works." — fire link reads as a distinct warm rust.
- [x] 5.2 On a **red** tablet: link reads as a deep wine, distinct from the rosy ground (the worst
      camouflage case before).
  - Confirmed 2026-08-20: TESTING.md `00000012` "Works." — red link reads as a deep wine on the rosy ground.
- [x] 5.3 On a **blue** tablet: link reads as a lively deep steel-blue.
  - Confirmed 2026-08-20: TESTING.md `00000013` "Works." — blue link reads as a deep steel-blue.
- [x] 5.4 On a **wax** tablet: link reads as a deep amber-bronze, legible on the pale honey ground (the
      2.3 : 1 worst case before).
  - Confirmed 2026-08-20: TESTING.md `00000014` "Works." — wax link reads as a deep amber-bronze (closes the 2.3:1 worst case).
- [x] 5.5 Body task text on all four: strokes read crisp and firmly seated (the dark halo reads as a
      soft engraved shadow, NOT as grime/dirt over the clay). If grime, drop glow alpha toward 0.40; if
      still smearing, raise toward 0.65 before widening blur (keep blur fraction 0.05–0.08).
  - Confirmed 2026-08-20: TESTING.md `00000015` "Works." — the 0.40-alpha body glow reads as a seated shadow, not grime.
- [ ] 5.6 Repeat 5.1/5.5 on a **fired/hardened** (read-only) tablet of the same clay — confirm the
      colors match the wet form (state must not change palette) and read-only rows are equally legible.
  - Still broken 2026-08-20: TESTING.md `00000016` — fired/hardened cuneiform readability, retested via /scribe tablet fired|hard, still fails. The fix is owned by add-tablet-state-glow-modifier §4 (state-aware light halo); this box unblocks when that ships.
- [x] 5.7 Title (cuneiform) and tracker "N / N" counters: confirm the dark glow reads well on them too
      (they share `CuneiformGlowTable`).
  - Confirmed 2026-08-20: TESTING.md `00000017` "Works." — dark glow reads well on the cuneiform title and N/N counters.
- [x] 5.8 Pixel-Art Display OFF: tablet follows the global theme over a flat panel; links use
      `colors.Primary` and no glow — confirm unchanged from before.
  - Confirmed 2026-08-20: TESTING.md `00000018` "Works." — Pixel-Art OFF renders unchanged (flat panel, Primary links, no glow).
- [x] 5.9 Record verdicts in `TESTING.md` via the what-to-test skill.
  - Done 2026-08-21: verdicts for 5.1–5.8 recorded in TESTING.md (items 00000011–00000018) via the what-to-test skill.
