## Why

The cuneiform script proven by `add-cuneiform-glyph-font` (Proposal A) ships with exactly **47
authored characters** (A–Z, 0–9, and 11 punctuation marks) and no `&`, no `+`, no lowercase, and no
space glyph. Missing characters already degrade **safely** — the layout folds to uppercase and
advances a small gap for anything unauthored, never throwing — so this is an **enhancement, not a
bug fix**: it makes more of a note-taker's everyday characters render as real ink instead of a blank
gap. Two cheap wins are available: (1) many characters are visually interchangeable with ones we
already drew (`[` `{` look like `(`), so they can be aliased to an existing glyph for **zero new
art**, and (2) a small, enumerated set of common symbols (starting with `+`, which the author wants
specifically so `&` can alias to it) is worth authoring in `glyph-forge` to round out the set.

This is a **follow-up to Proposal A**, sequenced independently of the still-deferred tablet
proposals (B/C/D) in the "Clay & Wax Tablets" plan.

## What Changes

- **Character substitution / aliasing (pure Core, no new art).** Add a many-to-one alias map applied
  at the **same pre-lookup layer as the existing uppercase-folding step** in
  `CuneiformLineLayout` — after uppercase folding, before glyph lookup. It maps a character with no
  authored glyph of its own onto an existing authored glyph it resembles. Confirmed initial aliases:
  `[` `{` render as the authored `(`; `]` `}` render as the authored `)`. The map is expressed as
  **data (a dictionary)** so it is trivial to extend. This adds no geometry and can ship immediately.
- **New-glyph wishlist for `glyph-forge` (art work → regenerated bundle).** Publish an explicit,
  enumerated, **RECOMMENDED** list of additional characters to author in the `glyph-forge` sister
  project, each with a short rationale, for the author to approve or prune. It MUST include `+`
  (so `&` can subsequently alias to `+`). This proposal delivers the wishlist as an output; it does
  **not** author the art. When approved glyphs land, they are regenerated via
  `python3 tools/build_glyphs_bundle.py`, re-committed to
  `src/Mod/assets/scribe/textures/fonts/cuneiform-glyphs-1.json`, and the Core test asserting the
  shipped-bundle character count (currently 47) is updated to the new total.
- **Dependency ordering (explicit).** The bracket/brace aliases (`[ { → (`, `] } → )`) depend on
  nothing and land immediately. The `& → +` alias depends on `+` being authored first (the
  wishlist half), so it lands only after that art regenerates the bundle. The alias map is designed
  so `&` is added as one more data entry once `+` exists.
- **Non-goals (explicitly deferred):** authoring the glyph art itself (manual `glyph-forge` work by
  the author); lowercase glyphs and a space glyph (uppercase-folding and the word-gap advance already
  handle those); and the tablet item, tablet dialog, and all deferred tablet mechanics (Proposals
  B/C/D).

## Capabilities

### New Capabilities
- `cuneiform-character-coverage`: broaden which characters the cuneiform script can render — a
  data-driven, many-to-one alias map applied at the layout's pre-lookup layer (reusing existing
  authored glyphs for visually-related characters with no glyph of their own), the explicit
  dependency ordering between immediately-shippable aliases and alias entries that must wait on new
  art, and the process contract for a recommended new-glyph wishlist (author in `glyph-forge`,
  regenerate + re-commit the bundle, update the shipped-bundle character-count assertion).

### Modified Capabilities
<!-- None. The `cuneiform-glyph-font` capability from Proposal A (add-cuneiform-glyph-font) is not
     yet archived into openspec/specs/, so its requirements are not modified here; this change adds a
     distinct, layered coverage capability alongside it rather than editing its (unarchived) spec. -->

## Impact

- **Modified Core code:** `src/Core/Cuneiform/CuneiformLineLayout.cs` — a new data-driven alias map
  and one substitution step inserted after `char.ToUpperInvariant` and before `_bundle.Get(c)`. No
  VS API added (Core invariant preserved). No changes to `Glyph`, `GlyphStroke`, or `GlyphBundle`.
- **New/updated Core tests:** `tests/Core.Tests/CuneiformTests.cs` — alias cases (e.g. `"["` lays
  out identically to `"("`; `"]"` identically to `")"`), plus the existing
  `Parse_ShippedBundle_ContainsAll47AuthoredCharacters` count assertion updated **only when** new
  glyphs actually land in the bundle.
- **Asset (deferred to the art step):** regenerated
  `src/Mod/assets/scribe/textures/fonts/cuneiform-glyphs-1.json` when approved wishlist glyphs are
  authored — produced by `glyph-forge/tools/build_glyphs_bundle.py` in the separate `~/claude/glyph-forge`
  repo and re-committed here.
- **Cross-repo:** the wishlist is consumed as manual authoring work in `glyph-forge`; no build
  coupling is introduced (the bundle stays a committed artifact).
- **Dependencies:** none added. **CI:** the Core suite gains the alias tests (cloud runners, no game
  install needed).
