## Why

Players write arrows into task/note text all the time — "mine ore -> smelt -> forge", "return <- from cellar" — but they can only type the ASCII digraphs `->` / `<-`, which look like typos and don't read as arrows. Substituting them for real Unicode arrows (`→` / `←`) as you type is a cheap, universally-understood polish win that costs nothing at the model/codec layer. On tablets, though, the cuneiform script can't render those arrows (or the literal `<` / `>` a player might type) — the shipped glyph bundle carries only 54 ASCII characters and has no art for them — so tablet text would show gaps where a lectern/notebook shows the arrow. This change is the v1.1-polish pairing that closes both halves.

## What Changes

- **Auto-substitute typed arrow digraphs in every Scribe editor.** As the player types in the Lectern, Notebook, Clockmaker's Notebook, and the editable (wet) Tablet — in BOTH task rows and freeform note text — completing the digraph `->` rewrites it to `→` (U+2192) and `<-` rewrites to `←` (U+2190). The substitution is a keystroke-time transform on the editor's live text buffer, so the **stored bytes contain the real Unicode arrow** (search, copy/paste, and the read view all stay consistent). The caret is adjusted for the 2-char→1-char collapse so it stays put after the arrow.
- Substitution is a **fixed, exactly-two-entry digraph table**, deliberately NOT a general autocorrect/emoji engine. Out of scope: `↑` / `↓` / `↔`, and the `<->` → `↔` triple. It fires only when the digraph completes and never rewrites text the player is editing elsewhere in the buffer.
- **Add four authored cuneiform glyphs so tablets can render `←`, `→`, `<`, `>`.** The glyph stroke art is authored out-of-band in the sibling `glyph-forge` project (cross-repo, art-gated; tracked there as its own change). This change is the MOD side: ingest the regenerated `cuneiform-glyphs-1.json` bundle, bump its `characterCount` 54 → 58, update the Core test that asserts the shipped authored-character count, and smoke-test the render via the `.cuneiform <text>` client harness. These are **real authored glyphs, not alias stand-ins** — no existing glyph resembles an arrow or angle bracket, so `CuneiformLineLayout.Aliases` is not the mechanism here.
- **The two halves are separable and independently gated.** Substitution ships UNGATED on lectern/notebook/tablet immediately — Unicode arrows render in the normal TTF font with no new art. The cuneiform glyph coverage lands only when glyph-forge's stroke art exists; the mod side is designed as a clean drop-in (bundle swap + count bump + test update) once it does. Typing `<` / `>` is independent of substitution — you type them literally; they just gain cuneiform art.
- No Core-model or codec change; no new dependency; save format unchanged.

## Capabilities

### New Capabilities
- `typed-arrow-substitution`: keystroke-time rewriting of the ASCII digraphs `->`/`<-` into `→`/`←` in the editable text buffer of every Scribe editor, in both task and note text, with correct caret handling and a fixed two-entry table (no general autocorrect).

### Modified Capabilities
- `cuneiform-character-coverage`: the authored glyph set gains four real glyphs (`←`, `→`, `<`, `>`); the bundle-regeneration + character-count-assertion requirement moves from its current shipped total (54) to 58, and the new art is explicitly NOT aliased.

## Impact

- **Code (substitution):** `src/Mod/ScribeMultilineField.cs` — the editor input widget's insert path (`ScribeMultilineFieldState.Insert` / `OnKeyChar`), where a completed digraph is detected and collapsed before commit. Shared by all four editor surfaces (both normal and cuneiform render paths run through this one State), so all editors pick it up from one seam. No Core reference added.
- **Code (glyph coverage):** `src/Mod/assets/scribe/textures/fonts/cuneiform-glyphs-1.json` (`characterCount` 54 → 58, regenerated bundle) and `tests/Core.Tests/CuneiformTests.cs` (`Parse_ShippedBundle_ContainsAllAuthoredCharacters` count assertion 54 → 58). `src/Core/Cuneiform/CuneiformLineLayout.cs` is unchanged — the new characters are authored, not aliased.
- **Cross-repo dependency:** the four glyph stroke files in `~/claude/glyph-forge/` (slugs `arrowleft`/`arrowright`/`lessthan`/`greaterthan`) must be drawn and the bundle regenerated (`tools/build_glyphs_bundle.py`) before the coverage half can land. Tracked as glyph-forge change `add-arrow-and-comparison-characters`.
- **Tests:** existing Core `CuneiformTests` count assertion; add a Core-level (or Mod-level, since the seam is in `src/Mod`) test for the digraph transform. No game install needed for the substitution logic if the transform is extracted to a pure helper.
- **No change:** codec version, network packets, persistence, dependencies (`game 1.22.0`, `gui 3.1.0`).
