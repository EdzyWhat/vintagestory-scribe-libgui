## Context

Every Scribe editor surface (Lectern, Notebook, Clockmaker's Notebook, and the editable/wet Tablet) runs its text editing through one widget: `ScribeMultilineField` and its `ScribeMultilineFieldState` in `src/Mod/ScribeMultilineField.cs`. That State owns the `(text, caret, anchor)` model and is the SINGLE place all typed characters land — `OnKeyChar` → `Insert(string)` for typing, `Paste()` → `Insert` for clipboard, and the same State drives BOTH render paths (the normal TTF `ScribeMultilineFieldRender` and the tablet's `ScribeCuneiformFieldRender`) via the shared `IScribeEditableTextRender` contract. So a transform applied inside this one State reaches all four surfaces and both task and note text with no per-surface wiring.

On tablets, text is drawn as cuneiform strokes from a bundled glyph font parsed into a `GlyphBundle` (`src/Core/Cuneiform/`). `CuneiformLineLayout` folds input to uppercase, applies a data-driven `Aliases` map (e.g. `[ { → (`), then looks the character up in the bundle; a character with neither an authored glyph nor an alias degrades to a safe empty gap. The shipped bundle (`src/Mod/assets/scribe/textures/fonts/cuneiform-glyphs-1.json`) authors 54 characters (A–Z, 0–9, 18 punctuation) and has no arrow or angle-bracket art. A Core test, `CuneiformTests.Parse_ShippedBundle_ContainsAllAuthoredCharacters`, pins that count at 54 so the shipped asset can't silently drift.

This change is a v1.1 polish pairing already triaged in `docs/vnext-ideas.md` (§4.5 substitution, §1.7 arrow glyphs, §1.6 `<`/`>` glyphs). The two halves are intentionally separable so the substitution — which needs no new art — can ship even if the glyph stroke authoring in the sibling `glyph-forge` repo lags.

## Goals / Non-Goals

**Goals:**
- Typing `->` yields `→` (U+2192) and `<-` yields `←` (U+2190) in every Scribe editor, in both task and note text, at keystroke time, with the caret landing immediately after the arrow.
- The substituted arrow is stored in the document bytes (not a render-time illusion), so read view, search, and copy/paste are all consistent.
- Tablets gain real authored cuneiform glyphs for `←`, `→`, `<`, `>`, dropping in cleanly when glyph-forge's art lands.
- The substitution logic is unit-testable without a game install (keep the transform pure).

**Non-Goals:**
- A general autocorrect / emoji / text-expansion engine. The digraph table is exactly two entries.
- Vertical or bidirectional arrows (`↑`/`↓`/`↔`) and the `<->` → `↔` triple.
- Any Core-model, codec, network, or persistence change.
- Aliasing the four new cuneiform characters to existing glyphs — they are authored art, not stand-ins.
- Authoring the glyph stroke art itself (that's the glyph-forge change `add-arrow-and-comparison-characters`).

## Decisions

### D1 — Substitute in the input widget's `Insert` path, not the Core model or render layer

The transform lives in `ScribeMultilineFieldState`, applied when a keystroke completes a digraph, mutating the live buffer before `Commit()` fires `OnChanged`. This is candidate (a) from §4.5.

- **Why not Core/codec:** a codec transform would rewrite bytes the player never typed as a digraph (e.g. a legitimately-stored `->` pasted from elsewhere that the player wants literal), and it would fire on load of old saves. Keystroke-time scoping matches user intent — the arrow appears exactly when you finish typing the digraph.
- **Why not render-time-only:** the summary requirement is that stored bytes contain the real arrow so search/copy stay consistent. A render-only swap (like an alias) would show `→` but store `->`, breaking that.
- **Why this seam:** all four editors and both render paths funnel through this one State, so one change covers everything. It also keeps `src/Core/` free of the concern (the transform touches only editor UX, not the model).

### D2 — Detect the digraph on the completing keystroke, transform, then adjust the caret

The trigger is the SECOND character of the digraph arriving via `OnKeyChar`. Concretely: on inserting a character, if the just-typed char plus the character immediately before the caret form a known digraph (`-` preceded by `<` → `←`; `>` preceded by `-` → `→`), replace the two-character run with the single arrow and set the caret one position back from where two chars would have left it (net caret advance of +1 arrow char, not +2 digraph chars).

- **Fire only on completion:** typing `<` alone leaves `<` (it's a real, now-renderable character); the arrow only appears when the `-` completes `<-`. Same for `-` then `>`.
- **Anchor to the caret, not a global scan:** only the two code units immediately before the caret are examined, so the transform never rewrites a `->` sitting elsewhere in the buffer that the player isn't currently completing. This also makes a mid-word edit safe.
- **Extract a pure helper** (e.g. `ScribeArrowDigraph.TryComplete(char justTyped, char before) -> arrow?`) holding the fixed two-entry table, so the mapping is unit-testable without the game API. The table stays tiny and explicit — no regex, no config.

### D3 — Paste is left literal (typing-only trigger)

The digraph transform applies to typed characters, not to a pasted run. Pasting text containing `->` inserts it verbatim.

- **Why:** paste is a bulk insert of author-intended content (often code, paths, or copied arrows already in Unicode form). Rewriting inside a paste risks surprising clobbers and complicates the caret math for multi-occurrence runs. Scoping to single-keystroke completion keeps the behavior predictable and matches how the digraph is a *typing* affordance. (Revisit only if playtest asks for it.)

### D4 — Four cuneiform glyphs are authored, not aliased; the mod side is a drop-in

No existing glyph resembles an arrow or angle bracket, so `CuneiformLineLayout.Aliases` is the wrong tool (an alias must point at real authored ink). The glyphs are drawn in glyph-forge (slugs `arrowleft`/`arrowright`/`lessthan`/`greaterthan`), the bundle is regenerated with `tools/build_glyphs_bundle.py`, and the mod ingests the new `cuneiform-glyphs-1.json`. The only mod-side edits are: the regenerated asset, its `characterCount` 54 → 58, and the `CuneiformTests` count assertion 54 → 58. `CuneiformLineLayout` needs no code change — once the glyphs are in the bundle, the existing lookup path renders them, and the existing safe-gap path already covers them until the art lands.

### D5 — Gating: substitution ships now; glyph coverage lands with the art

Substitution has no art dependency (Unicode arrows render in the normal TTF font, and on tablets they degrade to the safe gap until glyph art exists — never a throw). So it ships ungated in v1.1. The glyph-coverage half is a separate, clean commit gated on the glyph-forge art being drawn and the bundle regenerated. Splitting them keeps v1.1 unblocked if the art slips.

## Risks / Trade-offs

- **[Caret math on the 2→1 collapse is easy to get subtly wrong]** → Extract the transform + caret adjustment into a pure helper with unit tests covering: digraph at end of buffer, digraph mid-text with trailing content, digraph immediately after another arrow, and the non-trigger cases (`<` alone, `-` alone, `- >` with a space).
- **[A player might WANT a literal `->` (e.g. writing a code snippet or ASCII art)]** → Accept as a known trade-off for v1.1; the substitution is a typing affordance and the population that types `->` meaning an arrow vastly dominates. Paste stays literal (D3), giving an escape hatch (paste the literal digraph). A toggle is deferred unless playtest asks.
- **[Cuneiform art could ship late or never]** → The separable gating (D5) means v1.1 substitution is unaffected; tablets show the safe gap for the four chars until the art lands, exactly as they do today for any unauthored character.
- **[Bundle regeneration is out-of-band (no build coupling)]** → Matches the existing convention (the bundle is a committed artifact, not built in CI). The count-assertion test is the guard that the committed asset actually reached 58, catching a stale or half-regenerated bundle.
- **[Character `→`/`←` must survive the codec round-trip]** → They already do — the codec stores UTF strings and imposes no ASCII restriction; the existing max-length clamp counts them as one char each. No codec work, but the smoke test should confirm an arrow persists across close/reopen.

## Migration Plan

No data migration — save format is unchanged and no stored bytes are rewritten on load. Deploy is two independent commits:
1. Substitution (ships in v1.1 regardless of art).
2. Glyph coverage (bundle swap + count bump + test update), landed once glyph-forge art exists.

Rollback is a plain revert of either commit; neither touches persisted data.

## Open Questions

- Should a completed digraph be undoable back to the literal in one keystroke (e.g. Backspace restores `<-`)? Default: no — Backspace deletes the arrow like any character. Revisit only if playtest finds the auto-arrow annoying when a literal was wanted.
