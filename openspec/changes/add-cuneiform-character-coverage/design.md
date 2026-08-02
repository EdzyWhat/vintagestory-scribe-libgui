## Context

`add-cuneiform-glyph-font` (Proposal A) shipped the cuneiform script: a Core glyph model
(`src/Core/Cuneiform/{Glyph,GlyphStroke,GlyphBundle,CuneiformLineLayout}.cs`), a committed
stroke-geometry bundle (`src/Mod/assets/scribe/textures/fonts/cuneiform-glyphs-1.json`), and a
Mod-side Skia render widget. The bundle carries exactly **47 authored characters** — A–Z (26), 0–9
(10), and 11 punctuation marks (`! " ' ( ) , - . : ; ?`). There is no `&`, no `+`, no lowercase, and
no space glyph.

`CuneiformLineLayout.Layout(string)` already has a **pre-lookup normalization layer**: for each input
character it folds to uppercase (`char.ToUpperInvariant`) and then calls `_bundle.Get(c)`. A `null`
result (unauthored character) advances `MissingGlyphGapUnits` and emits no strokes — it never throws.
So coverage gaps already degrade gracefully; this change is a legibility **enhancement**, not a bug
fix.

The glyph art is authored in the separate `~/claude/glyph-forge` repo and bundled by
`python3 tools/build_glyphs_bundle.py`; the mod commits that tool's output. Core must never reference
the Vintage Story API (the load-bearing unit-testability invariant).

This is a follow-up to Proposal A, sequenced independently of the still-deferred tablet proposals
(B/C/D) in `~/.claude/plans/clay-wax-tablets-delightful-neumann.md`.

## Goals / Non-Goals

**Goals:**
- Render more everyday note-taking characters as real ink via two mechanisms living in two places.
- **Half 1 (pure Core, no art):** a data-driven, many-to-one alias map applied at the existing
  pre-lookup layer, reusing authored glyphs for visually-related characters. Ships immediately,
  unit-tested in Core.
- **Half 2 (art → regenerated bundle):** an explicit, enumerated, RECOMMENDED new-glyph wishlist for
  `glyph-forge`, including `+` so `&` can later alias to it.
- Make the dependency ordering between the two halves explicit and safe.

**Non-Goals:**
- Authoring the glyph art itself — that is manual `glyph-forge` work by the author, out of scope here.
- Lowercase glyphs and a space glyph — uppercase-folding and the `WordGapUnits` space advance already
  cover those; adding real glyphs for them is explicitly not proposed.
- The tablet item, tablet dialog, and all deferred tablet mechanics (Proposals B/C/D).
- Changing `Glyph`, `GlyphStroke`, `GlyphBundle`, corner math, kerning, or the migration ladder — the
  alias map is purely a pre-lookup redirect.

## Decisions

### The alias map is data, applied at the same pre-lookup layer as uppercase-folding
Add a `static readonly IReadOnlyDictionary<char, char>` (aliased-char → authored-target-char) in
`CuneiformLineLayout`, and one substitution step in `Layout` **immediately after** `char c =
char.ToUpperInvariant(raw);` and **before** `_bundle.Get(c)`:
if the folded character has an alias entry, replace it with its target before lookup. This is the
correct layer for the same reason folding lives there — it "absorbs the quirks of the authored set"
so callers pass ordinary text. Because aliasing happens before lookup, an aliased character produces
byte-identical positioned strokes and advance to its target (same glyph, same placement math).

*Alternative considered:* materialize alias glyphs into the `GlyphBundle` (duplicate the target
`Glyph` under the alias key at parse time). Rejected — it couples aliasing to bundle parsing, muddies
"authored character count" (the shipped-bundle test asserts 47), and spreads a display-only concern
into the data model. Keeping aliases in the layout keeps `CharacterCount` meaning "authored glyphs".

*Alternative considered:* do the substitution in the Mod widget/callers. Rejected — it would scatter
the mapping, make it untestable on CI, and duplicate the folding-layer rationale.

### Aliases are grouped by dependency: shippable-now vs. waiting-on-art
The map is authored in two conceptual tiers, documented in code and tasks:
- **Ship now (targets already authored):** `[` → `(`, `{` → `(`, `]` → `)`, `}` → `)`. Both targets
  (`(` and `)`) are in the 47-char bundle today, so these render real ink immediately.
- **Waits on art:** `&` → `+`. `+` is not authored yet, so adding this entry now would alias `&` onto
  a glyph the bundle lacks — which would just fall through `_bundle.Get('+') == null` back to the
  missing-glyph gap (harmless, but pointless). The `&` → `+` entry is therefore added only after `+`
  is authored (Half 2), at which point it becomes a one-line data addition.

This ordering is the crux the proposal calls out: Half 1 is independent; the `&` alias is gated on
Half 2.

### The new-glyph wishlist is a recommendation, delivered as a proposal artifact
The wishlist is enumerated in this design (below) and mirrored in `tasks.md` as an author-approval
step. It is explicitly advisory — the author approves or prunes it. Only `+` is load-bearing for this
change's ordering story (it unblocks the `&` alias); everything else is a nice-to-have to round out
common note-taking punctuation.

**Recommended new-glyph wishlist (for `glyph-forge`, author to approve/prune):**

| Char | Rationale |
| ---- | --------- |
| `+`  | **Required by this proposal.** Enables the `&` → `+` alias (author's explicit request); also a natural "and/plus" mark in notes. |
| `/`  | Very common in notes: dates (`3/4`), fractions, and/or, paths. High everyday value. |
| `=`  | Equals / "is" — quantities and simple relations in task notes. |
| `%`  | Percentages — quantities, progress. |
| `#`  | Numbering / counts (`#3`), tags. |
| `*`  | Bullet / emphasis / footnote marker — common ad-hoc list glyph. |
| `@`  | "at" (locations, times) — common in notes. |
| `&`  | Only if authored **directly** rather than aliased to `+`. Listed so the author can choose: author `&` as its own glyph, or leave it aliased to `+`. Not both. |

Framing: `+` is the one commitment; the rest are candidates. `&` appears here only as the
"author-it-directly" alternative to the alias — the author picks one path for `&`.

### New art regenerates the committed bundle and bumps the count assertion
When approved glyphs are authored, run `python3 tools/build_glyphs_bundle.py` in `~/claude/glyph-forge`,
re-commit the output to `src/Mod/assets/scribe/textures/fonts/cuneiform-glyphs-1.json`, and update the
Core test `Parse_ShippedBundle_ContainsAll47AuthoredCharacters` (in `tests/Core.Tests/CuneiformTests.cs`)
from 47 to the new total (and its name/asserted members accordingly). The bundle stays a committed
artifact — no build-time Python coupling is introduced (matching Proposal A's decision).

## Risks / Trade-offs

- **[An alias whose target isn't authored silently does nothing]** → If someone adds an alias to an
  absent target (e.g. `&` → `+` before `+` exists), `_bundle.Get(target)` returns null and the char
  falls through to the missing-glyph gap — no crash, but no ink either. Mitigation: the design gates
  `&` → `+` on `+` landing first, and a Core test asserts the shippable aliases match their targets.
- **[Aliasing hides that a character has no true glyph]** → `[` looking like `(` is intentional and
  legible for a wedge script, but a reader can't distinguish them. Accepted: this is the whole point
  (visual reuse), and the pairs chosen are conventionally interchangeable bracket forms.
- **[Character-count assertion drift]** → The shipped-bundle test hard-codes 47; forgetting to bump it
  when art lands turns a green intent into a red build. Mitigation: the tasks and this design call the
  bump out explicitly as part of the art step, so the failing test is the reminder, not a surprise.
- **[Wishlist scope creep]** → Authoring many glyphs is real art effort. Mitigation: only `+` is a
  commitment; the rest are explicitly prunable, so the change is complete with just Half 1 + the `+`
  entry approved.

## Migration Plan

No data migration. Rollout is staged by the dependency ordering:

1. **Step 1 (this change, ships immediately):** add the alias map with the four bracket/brace entries
   plus the substitution step in `CuneiformLineLayout`; add Core alias tests. No art, no bundle
   change, no count-assertion change.
2. **Step 2 (after author approves + authors glyphs in `glyph-forge`):** regenerate and re-commit the
   bundle; bump the shipped-bundle count assertion to the new total; if `+` was authored, add the
   `& → +` alias entry and a test for it.

Rollback: the alias map is additive and data-only — removing an entry (or the whole map + step)
restores the prior behavior exactly, since unaliased characters already degrade safely.

## Open Questions

- Which wishlist glyphs beyond `+` the author wants authored (and whether `&` is authored directly or
  left aliased to `+`) — resolved by author approval in Step 2, not up front.
- Whether any further visually-interchangeable aliases are worth adding now (e.g. other bracket-like
  forms) — the map is trivially extensible, so more can be added if the author identifies them.
