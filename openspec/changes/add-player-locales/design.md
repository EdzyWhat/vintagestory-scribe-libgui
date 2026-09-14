## Context

See `proposal.md` for why. Player-facing strings already go through `Lang.Get("scribe:…")` at paint time (UI, handbook, hover, live History sentences, live dates). `Lang.HasTranslation` looks at the **current locale only**, which is load-bearing for History flavor pools: probing `scribe-mob-death-0`, `-1`, … must stop at the first missing key in **that** language, not count an English fallback as a hit. Canonical English is `src/Mod/assets/scribe/lang/en.json` (~494 real keys, ~55k characters; handbook VTML is ~76% of the text). `pt-br.json` is a stale Arquimago file (253 keys, tablet still says 10). C4B's Spanish lives in `~/Downloads/C4B_Traducciones-ES_MODS_246.10.2.zip` at `assets/scribe/lang/es-es.json` (441 keys vs current English; ~66 missing; stale extras and JSON comment-keys at the bottom). Vanilla language codes that matter: Swedish is `sv-se`, Chinese is `zh-cn`, Spanish is `es-es`, Portuguese is `pt-br`. The one `game`-domain string is `src/Mod/assets/game/lang/en.json`.

## Goals / Non-Goals

**Goals:**

- One shared process so twelve locale files stay coherent: same key set, same glossary of proper names, same placeholder/VTML rules, then one file (or agent) per language in parallel.
- Seed `es-es` from C4B and refresh `pt-br` from Arquimago, including a **meaning audit of every overlapping key** against current English (rewrites in place, not just new keys). Generate the rest from current `en.json`.
- A checker that fails on the `pt-br` failure mode (extra/stale keys, missing required keys, copied English, broken `{n}` / VTML).

**Non-Goals:**

- Crowdin / Weblate / a community translation website. Ignore until a useful file shows up.
- `es-419`, `pt-pt`, `zh-tw`. Latin-American Spanish and European Portuguese fall back to English; that is accepted for this change.
- Bundling Noto CJK or changing LibGUI fonts. Ship `ja` / `zh-cn` files; glyph coverage is a playtest risk, not a font project.
- Trimming English History joke pools. Other locales may be short; English stays long.
- Changing C# History / date / `Lang.Get` call sites (already live-localized).
- Translating player-authored notes, Guestbook messages, or baked pre-timestamp dates.
- Editing `.github/workflows/*.yml`. Wire the checker into local `build/verify.sh` only.

## Decisions

### 1. Filenames are vanilla `languages.json` codes

| Language | File | Seed |
|---|---|---|
| Russian | `ru.json` | greenfield |
| Ukrainian | `uk.json` | greenfield |
| Polish | `pl.json` | greenfield |
| German | `de.json` | greenfield |
| Chinese (Simplified) | `zh-cn.json` | greenfield |
| Japanese | `ja.json` | greenfield |
| Spanish | `es-es.json` | C4B zip → take and update |
| Czech | `cs.json` | greenfield |
| Swedish | `sv-se.json` | greenfield |
| French | `fr.json` | greenfield |
| Portuguese (Brazil) | `pt-br.json` | existing Arquimago file → update |
| Italian | `it.json` | greenfield |

**Alternatives considered:** `sv.json` / `zh.json` / `es.json` / `pt.json` — those are not what the game loads. `es-419` as a copy of `es-es` — a second dialect file without a native pass; skip. `pt-pt` — user asked to update `pt-br`, not add European Portuguese.

### 2. Shared glossary, then parallel files

El_Neuman's pipeline (diff English → translate blob → merge without clobbering human keys) is the right shape, run **once per language** because the string set is now content-complete.

Apply order:

1. Write `build/locale-glossary.md` (locked proper names) and `build/check-locales.py` (the mechanical gate).
2. Ingest C4B → draft `es-es.json`; refresh `pt-br.json`. These two prove the checker.
3. Generate the ten greenfield files **in parallel**, each from current `en.json` + the glossary. Do **not** start from `pt-br.json` or from each other.

Glossary locks the in-world names so twelve agents do not invent twelve Lecterns. Spanish **keeps C4B's names**. Portuguese **keeps Arquimago's names** where English meaning is unchanged. Greenfield languages pick one natural equivalent and stick to it.

| English | Notes |
|---|---|
| Scribe | Keep as the mod name unless the language already has an established rendering |
| Lectern | ES: C4B *Atril de Escritura* / *Atril*. PT: Arquimago *Atril* |
| Scriptorium | ES: C4B *Escritorio* (keep; do not "correct" to a calque) |
| Chalkboard | |
| Assignment Desk | |
| Inbox | |
| Task Notice | |
| Notebook / Clockmaker's Notebook | |
| Clay Tablet / Wax Tablet | |
| Guest Book | |
| Pinned Task HUD | "HUD" may stay |
| History | |
| Transcribe | |

Button labels that C4B mistranslated (DeepL "Start Timer" → "Hora de Inicio") get fixed on ingest; they are not glossary.

This table locks in-world nouns for translators to read; it is not the enforcement mechanism for the much larger surface of UI action/tab/button labels that handbook and craftinginfo essays quote inline (`<strong>Add Item Tracker</strong>`, `<strong>Task Editor</strong>`, `<strong>Settings</strong>`, `<strong>Timer</strong>`, `<strong>Read</strong>`, and so on — dozens of labels, not ~14 nouns). Hand-maintaining a glossary entry per label would drift the moment a label's English text changes. That surface is covered by a generated cross-reference table instead — see Decision 7.

**Alternatives considered:** GUI-only first, handbook later — rejected; user wants everything. One language at a time — slower, and the checker + glossary exist specifically so parallel is safe.

### 3. Overlapping keys are audited for meaning, not only coverage

C4B and Arquimago both have keys whose **names** still exist in `en.json` but whose **English sentences were rewritten** after the translation was made (handbook tours, tablet capacity 10 → 15, labels). Filling the missing-key gap would ship fluent Spanish/Portuguese for an old draft. Before adding new keys, apply MUST:

1. List overlapping keys (present in both the seed and current `en.json`).
2. Build a changed-English priority list from `git log -p -- src/Mod/assets/scribe/lang/en.json` since each seed's vintage (C4B: 1.4.0-rc.1 / early Sep 2026; Arquimago: last commit that was a real pt-br translation, not an English-fallback key mirror).
3. Re-translate every priority-list key whose seed value no longer matches current English meaning.
4. Read the remaining overlap — especially every `handbook-*` / `craftinginfo-*` essay — and re-translate any other drift.

The mechanical checker cannot prove meaning (it only catches copied English and structural breakage). This audit is an LLM/human pass with the git list as the queue. Known examples that MUST come out of the audit: `tablet-full`, `craftinginfo-scribe-getting-started-text`.

**Alternatives considered:** only fill missing keys and fix the two known leftovers — rejected; those two are examples of a class, not the whole class.

### 4. C4B ingest is take-and-update, not drop-in

From `~/Downloads/C4B_Traducciones-ES_MODS_246.10.2.zip` extract only `assets/scribe/lang/es-es.json`. Do not commit the pack.

Ingest steps:

1. Parse JSON (C4B uses comment-keys like `"// Traducido por C4BR3R4 …"`).
2. Keep keys that exist in current `en.json`.
3. Drop extras, comment-keys, and the stuffed `game:worldattribute-…` key (that string goes in `assets/game/lang/es-es.json`).
4. Run the overlapping-key meaning audit (Decision 3) on the remaining C4B values. Re-translate drift; keep C4B wording where English is unchanged.
5. Strip injected `<font color='burlywood'>` (and similar) unless English has the same tag. Keep C4B's Spanish **wording and names** where the English meaning is unchanged.
6. Fix known false friends (timer start/stop).
7. Translate the ~66 missing 1.4.x keys (Task Notice, delivery, Inbox inventory, tab subtitles, quest-prompt variants, …).
8. Optionally keep C4B's translated joke pool (they did all 21). Contiguous from `-0` is required if extras are kept.

If a player still runs C4B's pack, both mods ship `assets/scribe/lang/es-es.json` and last-loaded wins. We now have permission to ship ours; we do not try to detect or disable theirs.

**Alternatives considered:** leave Spanish to C4B (load-order truce) — rejected; user got approval to take and update. Strip C4B naming to match a fresh calque — rejected; honors the person who already did the work.

### 5. `pt-br.json` is a seed, never a template

Keep Arquimago values whose English meaning is unchanged after the same overlapping-key audit as Spanish. Re-translate changed meanings (`tablet-full` 10 → 15, rewritten handbook, any other key whose current English is a different sentence). Fill the 242 missing keys from current English. Drop the one extra key. Never copy `pt-br.json` as the skeleton for `ru.json` / `de.json` / etc.

### 6. History flavor is short by default in new locales

English keeps `-0..-20` jokes and the tool-verb catalog. Greenfield locales ship:

- `scribe-mob-death-0` — literal ("{0} was slain by {1}." equivalent)
- optionally `scribe-mob-death-1` — second literal if it reads naturally
- `scribe-pvp-verb-generic-0` — a verb that works in both the kill template and the death template (prefer a language where active ≈ passive, e.g. a past-tense "killed", so no participle override is needed)
- `scribe-pvp-death-message` / `scribe-pvp-kill-message` with `{0}{1}{2}`

Omit tool verbs, extra generics, and jokes. `HasTranslation` on the viewing client then reports pool size 1 (or 2), which is exactly what History already does. Copying English jokes into `ru.json` would leak English into a Russian template — that is forbidden.

C4B already translated the long pool; Spanish may keep it.

### 7. Checker before any parallel translation is "done"

`build/check-locales.py` (Python, no new package) against `src/Mod/assets/scribe/lang/en.json`:

- JSON parses; keys are strings
- No extra keys vs `en.json`
- Every required key present (all non-comment English keys minus the optional flavor set in the spec)
- Optional flavor keys, if present, contiguous from `-0`
- Same `{n}` placeholder indexes as English
- Same VTML tag names and `handbook://` targets (href quote style may differ)
- No `_comment-*` keys in non-English files
- Values whose English source is longer than 40 characters MUST NOT be identical to English (catches copy-paste of handbook essays; short tokens like "HUD" are allowed to match)
- Every generated UI-label cross-reference (below) matches within the same locale file

**UI-label cross-reference, generated from `en.json` only (not hand-maintained):**

Handbook and craftinginfo essays quote real button/tab/action labels inline as plain text (`<strong>Add Item Tracker</strong>` duplicates `scribe-gui-addtracker`'s own value; `Task Editor` duplicates `scribe-gui-switch-to-editor`; `Settings` duplicates `scribe-hud-questprompt-settings-button`; `Timer` duplicates `scribe-gui-nav-timer`; similarly for `Read`, `Add Task`, `Add Link`, `Add Crafting Task`, and others). These are separate lang keys from the label's own string, not composed at paint time — vanilla `GuiHandbookTextPage.Init` resolves handbook text via the zero-arg `Lang.Get(Text)` (`vssurvivalmod/Systems/Handbook/Gui/GuiHandbookTextPage.cs:38-40`), so there is no format-arg seam to inject a label's translated value into an essay at render time. Twelve locales translating the label key and its inline quotes independently can drift within a single language even though every other checker rule and the glossary both pass.

The checker closes this without touching rendering code:

1. Walk every `<strong>…</strong>` / `<em>…</em>` / quoted span inside a `handbook-*` / `craftinginfo-*` value in `en.json`.
2. If that span's exact English text equals another key's full English value (a label key — `scribe-gui-*`, `scribe-hud-*`, `blockhelp-*`, and similar), record `(handbook key, span index, label key)`.
3. Spans that don't exactly match any other key's full value are left alone — free prose, not a label reference; this avoids false positives from partial phrase overlap.
4. For each recorded triple, in every non-English locale: the locale's translated span at that position SHALL equal that locale's own translated value of `label key`. Mismatch fails the check.

Hook it as a stage in `build/verify.sh` (local). Do not add a GitHub Actions step in this change.

`assets/game/lang/<code>.json` is a second, tiny check: one key, all twelve codes plus `en`.

**Alternatives considered:** xUnit in Core.Tests reading Mod assets — layering smell; a script next to `build/verify.sh` is the local-verification pattern. Cloud CI — would need a workflow edit; ask later. Runtime `Lang.Get` interpolation of label text into handbook essays at paint time (single source of truth by construction, no cross-reference needed) — rejected; it requires a Harmony prefix on `GuiHandbookTextPage.Init` to intercept resolution before vanilla's zero-arg `Lang.Get(Text)` runs, which is a Mod C# / rendering-lifecycle change this proposal's Impact section rules out ("Core / Mod C#: none expected") for what is meant to stay a translation-content change.

### 8. Authoring rules for every locale file

- UTF-8, tab-indented object, trailing-comma-free, same key order as `en.json` (required keys in English order, then any extra flavor the locale chose to keep).
- Bare keys, no `scribe:` prefix.
- Do not translate `_comment-*`.
- Preserve `{0}` / `{1}` / `{2}` even when grammar wants a different order.
- Preserve `<hotkey>…</hotkey>` ids (`rightmouse`, `Shift`, …).
- Wildcard keys (`block-chalkboard-*`, `block-scribeinbox-*`) are real key strings; copy the `*` literally.
- `date-format` may reorder `{0}{1}{2}`; vanilla already localizes month names.

## Risks / Trade-offs

- **[Machine-translated handbook is uneven]** → Accept for greenfield; C4B/Arquimago prove native cleanup can arrive later. Checker catches structural breakage, not tone. Known C4B false friends get a pass on ingest.
- **[Meaning audit is judgment, not a test]** → Git-changed English keys are the required queue; handbook overlap is a required read. The checker will not fail a fluent-but-wrong Getting Started. Task 2.0/2.1/2.2 are the gate.
- **[CJK tofu in LibGUI]** → Scribe's bundled Noto Sans is not Noto CJK. Ship the files anyway; playtest `ja` and `zh-cn` on a dialog and a handbook page. If glyphs missing, record a follow-up — do not block `ru`/`de`/`fr`. Cuneiform is a Latin-script toy; CJK players may want it off (existing setting, not this change). Japanese/Chinese inherit vanilla `linebreakBehavior: AfterCharacter`.
- **[C4B pack still installed]** → last-loaded `es-es.json` wins. Document in CHANGELOG; we do not detect their mod.
- **[es-419 / pt-pt players see English]** → accepted; do not silently copy `es-es` into `es-419`.
- **[Twelve agents diverge on names]** → glossary file is a gate: no greenfield locale starts until it exists.
- **[Checker too strict on short identical strings]** → 40-character threshold. Too loose on handbook → that is the volume that matters.
- **[UI-label cross-reference check is exact-match, not paraphrase-aware]** → A translator may legitimately rephrase a handbook mention without quoting the button verbatim (describing behavior instead of naming it); the checker only flags spans that exactly equal a label's English text and then diverge in translation. It does not force every mention to be a literal quote, and it cannot catch a paraphrase that drifts in meaning — that stays part of the human/LLM read in Decision 3.

## Migration Plan

No save / codec migration. Players get new files on next mod zip. Rollback is deleting the new JSON files and reverting `pt-br.json`. Restage (`build/restage.sh`) after lang edits so the client reloads them.

## Open Questions

None that affect specs or tasks. Glyph coverage for `ja` / `zh-cn` is answered in playtest, not in this design.
