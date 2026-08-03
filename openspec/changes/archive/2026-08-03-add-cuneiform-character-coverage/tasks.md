<!-- KEY DEVIATION discovered during apply (2026-08-02): the shipped bundle already grew 47 → 54 via a
     prior "glyph-forge symbol sync" that authored + / = % # * @ (see the count assertion in
     CuneiformTests.Parse_ShippedBundle_ContainsAllAuthoredCharacters, already at 54). So Section 4's art
     — including the load-bearing `+` — HAS ALREADY LANDED. That satisfies the `& → +` gate, so `& → +`
     ships NOW alongside the bracket aliases rather than being deferred (task 4.5's precondition is met).
     Section 4's remaining art/regeneration steps are therefore already done or moot. -->

## 1. Alias map + substitution (pure Core, ships immediately)

- [x] 1.1 In `src/Core/Cuneiform/CuneiformLineLayout.cs`, add a `static readonly IReadOnlyDictionary<char, char>` alias map (aliased char → authored target char) with the immediately-shippable entries: `[` → `(`, `{` → `(`, `]` → `)`, `}` → `)`. Document in a comment that the map is data-driven and split into ship-now vs. waits-on-art tiers. — done. Also included `& → +` (see deviation note: `+` already authored).
- [x] 1.2 Insert one substitution step in `Layout`, immediately after `char c = char.ToUpperInvariant(raw);` and before `_bundle.Get(c)`: if the map contains `c`, replace `c` with its alias target. Keep the existing missing-glyph fall-through untouched for anything neither authored nor aliased. — done (in `LayoutSegment`, the shared lookup path both `Layout` and `LayoutWrapped` route through).
- [x] 1.3 Do NOT touch `Glyph`, `GlyphStroke`, `GlyphBundle`, corner math, kerning, or the migration ladder; do NOT add any `using Vintagestory.*` or VS API reference to Core. — confirmed: only the alias dictionary + one substitution line added; Core stays VS-API-free.

## 2. Core tests (`tests/Core.Tests/CuneiformTests.cs`)

- [x] 2.1 Add a test that `"["` lays out to identical positioned strokes and `TotalWidth` as `"("` (byte-for-byte equal placement), and likewise `"{"` == `"("`. — done (`Layout_BracketAliases_RenderIdenticallyToOpenParen`, uses the shipped bundle since the sample lacks `(`).
- [x] 2.2 Add a test that `"]"` and `"}"` each lay out identically to `")"`. — done (`Layout_BraceAndBracketCloseAliases_RenderIdenticallyToCloseParen`).
- [x] 2.3 Add a test that a character with no glyph and no alias entry still advances `MissingGlyphGapUnits` with no strokes and no throw (guards the fall-through remains intact). — done (`Layout_UnaliasedUnauthoredCharacter_StillDegradesToMissingGap`, uses `~`).
- [x] 2.4 Add a test asserting the shippable alias targets (`(`, `)`) are present in the shipped bundle, so the aliases render real ink from the moment they ship. — done (`ShippedBundle_ContainsAliasTargets`, also asserts `+` for the `&` alias).
- [x] 2.5 `dotnet test` green (new alias tests + full existing Core suite). — 255 pass / 0 fail (5 new alias tests).

## 3. New-glyph wishlist (publish for author approval — no art authored here)

- [x] 3.1 Confirm the recommended wishlist table in `design.md` is complete and each entry carries a rationale: `+` (required — unblocks `& → +`), `/`, `=`, `%`, `#`, `*`, `@`, and `&` (only as the author-it-directly alternative to the alias). — confirmed present in design.md.
- [x] 3.2 Surface the wishlist to the author for approve/prune; record which glyphs (if any beyond `+`) are approved, and whether `&` is authored directly or left aliased to `+`. This is the gate for Section 4. — RESOLVED by discovery rather than a fresh approval round: the wishlist glyphs `+ / = % # * @` were ALL already authored and are in the shipped 54-char bundle (the prior symbol sync). `&` is left aliased to `+` (not authored directly), matching the author's stated preference (memory: cuneiform-character-coverage-plan). No pending art remains.

## 4. Authored-glyph landing (deferred — runs only after Section 3 approval)

- [x] 4.1 Author the approved glyphs (at minimum `+`) in the `~/claude/glyph-forge` project. — already done in a prior glyph-forge symbol sync (bundle carries `+ / = % # * @`).
- [x] 4.2 Regenerate the bundle: `python3 tools/build_glyphs_bundle.py` in `~/claude/glyph-forge`. — already done (the committed bundle is the regenerated output; count is 54).
- [x] 4.3 Re-commit the regenerated `cuneiform-glyphs-1.json` to `src/Mod/assets/scribe/textures/fonts/cuneiform-glyphs-1.json`. — already present/committed (the shipped artifact this change reads).
- [x] 4.4 Update the Core test `Parse_ShippedBundle_ContainsAll47AuthoredCharacters`: bump the asserted count from 47 to the new total, rename it accordingly, and assert the newly authored characters are present. — already done by the sync: the test is `Parse_ShippedBundle_ContainsAllAuthoredCharacters`, asserts 54, and checks `+`/`@` presence.
- [x] 4.5 If `+` was authored, add the `& → +` entry to the alias map (Section 1) and a Core test that `"&"` lays out identically to `"+"`. — done: `+` is authored, so `& → +` is in the map (1.1) and `Layout_AmpersandAlias_RendersIdenticallyToPlus` asserts the identical layout.
- [x] 4.6 `dotnet test` green (updated count assertion + any new alias test + existing suite). — 255 pass / 0 fail.
