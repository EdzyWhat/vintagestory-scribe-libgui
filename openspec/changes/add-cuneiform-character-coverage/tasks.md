## 1. Alias map + substitution (pure Core, ships immediately)

- [ ] 1.1 In `src/Core/Cuneiform/CuneiformLineLayout.cs`, add a `static readonly IReadOnlyDictionary<char, char>` alias map (aliased char → authored target char) with the immediately-shippable entries: `[` → `(`, `{` → `(`, `]` → `)`, `}` → `)`. Document in a comment that the map is data-driven and split into ship-now vs. waits-on-art tiers.
- [ ] 1.2 Insert one substitution step in `Layout`, immediately after `char c = char.ToUpperInvariant(raw);` and before `_bundle.Get(c)`: if the map contains `c`, replace `c` with its alias target. Keep the existing missing-glyph fall-through untouched for anything neither authored nor aliased.
- [ ] 1.3 Do NOT touch `Glyph`, `GlyphStroke`, `GlyphBundle`, corner math, kerning, or the migration ladder; do NOT add any `using Vintagestory.*` or VS API reference to Core.

## 2. Core tests (`tests/Core.Tests/CuneiformTests.cs`)

- [ ] 2.1 Add a test that `"["` lays out to identical positioned strokes and `TotalWidth` as `"("` (byte-for-byte equal placement), and likewise `"{"` == `"("`.
- [ ] 2.2 Add a test that `"]"` and `"}"` each lay out identically to `")"`.
- [ ] 2.3 Add a test that a character with no glyph and no alias entry still advances `MissingGlyphGapUnits` with no strokes and no throw (guards the fall-through remains intact).
- [ ] 2.4 Add a test asserting the shippable alias targets (`(`, `)`) are present in the shipped bundle, so the aliases render real ink from the moment they ship.
- [ ] 2.5 `dotnet test` green (new alias tests + full existing Core suite).

## 3. New-glyph wishlist (publish for author approval — no art authored here)

- [ ] 3.1 Confirm the recommended wishlist table in `design.md` is complete and each entry carries a rationale: `+` (required — unblocks `& → +`), `/`, `=`, `%`, `#`, `*`, `@`, and `&` (only as the author-it-directly alternative to the alias).
- [ ] 3.2 Surface the wishlist to the author for approve/prune; record which glyphs (if any beyond `+`) are approved, and whether `&` is authored directly or left aliased to `+`. This is the gate for Section 4.

## 4. Authored-glyph landing (deferred — runs only after Section 3 approval)

- [ ] 4.1 Author the approved glyphs (at minimum `+`) in the `~/claude/glyph-forge` project.
- [ ] 4.2 Regenerate the bundle: `python3 tools/build_glyphs_bundle.py` in `~/claude/glyph-forge`.
- [ ] 4.3 Re-commit the regenerated `cuneiform-glyphs-1.json` to `src/Mod/assets/scribe/textures/fonts/cuneiform-glyphs-1.json` (committed artifact; no build-time Python coupling).
- [ ] 4.4 Update the Core test `Parse_ShippedBundle_ContainsAll47AuthoredCharacters` in `tests/Core.Tests/CuneiformTests.cs`: bump the asserted count from 47 to the new total, rename it accordingly, and assert the newly authored characters are present.
- [ ] 4.5 If `+` was authored, add the `& → +` entry to the alias map (Section 1) and a Core test that `"&"` lays out identically to `"+"`.
- [ ] 4.6 `dotnet test` green (updated count assertion + any new alias test + existing suite).
