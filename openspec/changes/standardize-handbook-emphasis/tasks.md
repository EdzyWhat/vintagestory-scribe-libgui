## 1. Rewrite English to the new convention

- [x] 1.1 Build a working inventory of every `<strong>`/`<em>`/quoted span in `en.json`'s
      `handbook-*`/`craftinginfo-*` values, classified by design.md Decision 1's five
      categories (heading / keyboard-mouse call-out / term-of-art `<em>` / named-UI-element
      reference / incidental-no-referent). Verify the inventory accounts for all ~201 `<strong>`
      spans, the existing quoted spans, and the 4 existing `<em>` spans with no span left
      unclassified.
- [x] 1.2 Rewrite the craftinginfo essays (`craftinginfo-scribe-getting-started-text`,
      `-editor-reference-text`, `-task-types-text`, `-transcribe-text`, `-views-text`,
      `-pinned-hud-text`, `-assignments-text`, `-delivery-text`, `-quests-text`, and any other
      `craftinginfo-*` value with a category-1 or category-4 span) to the classification from
      1.1: category-1 spans stay `<strong>`, category-4 spans become plain quotes, category-5
      spans lose their markup, categories 2 and 3 are untouched. Verify each rewritten value's
      de-tagged text (strip `<strong>`/`<em>`/quote marks) is byte-identical to the de-tagged
      text of the pre-change value.
- [x] 1.3 Rewrite the per-object handbook essays (`handbook-scribelectern-*`,
      `handbook-scriptorium-*`, `handbook-chalkboard-*`, `handbook-scribeassignmentdesk-*`,
      `handbook-scribeinbox-*`, `handbook-tasknotice-*`, `handbook-scribetablet-*`,
      `handbook-scribenotebook-*`, `handbook-scribeclockmakernotebook-*`,
      `handbook-clockmakerschematic-*`, `handbook-scribe-hud-ref-text`) the same way. Verify the
      same de-tagged-text-identical check for each rewritten value.
- [x] 1.4 Run `python3 build/check-locales.py --dump-crossrefs` and read the output. Verify the
      table only contains spans that are genuinely category-4 references (a quoted or bolded
      span whose exact text equals another key's full value) — spot-check at least the filter-
      pill list, the Settings/Inbox/Editor references, and the Guest Book references that moved
      from bold to quotes.

## 2. Re-audit the two completed locales

- [x] 2.1 For every key touched in 1.2/1.3, update `es-es.json`'s markup to the same tag type
      and position as the revised English (bold stays bold, quotes stay quotes, dropped markup
      is dropped), translating no new words — only moving/removing tags around the Spanish
      words already there. Verify each touched key's de-tagged Spanish text is byte-identical to
      its own pre-change de-tagged text.
- [x] 2.2 Do the same for `pt-br.json`. Verify each touched key's de-tagged Portuguese text is
      byte-identical to its own pre-change de-tagged text.

## 3. Gate and hand back to add-player-locales

- [ ] 3.1 Run `python3 build/check-locales.py` and verify it reports clean (`✓ es-es.json`,
      `✓ pt-br.json`, `✓ assets/game/lang`) with zero problems for both locales. Currently fails:
      11 problems in `es-es.json`, 5 in `pt-br.json` — quoted spans that no longer byte-match the
      label they reference after the emphasis rewrite.
- [x] 3.2 Restage (`./build/restage.sh`) and verify the staged mod's `en.json`/`es-es.json`/
      `pt-br.json` match the repo copies (spot-check one rewritten heading and one rewritten
      quoted reference in-game or via diff against the staged files).
- [x] 3.3 Note in this change's summary (for the coordinator, not a CHANGELOG entry — this is
      an internal convention fix, not a player-facing behavior change) that
      `add-player-locales` may resume: the nine remaining greenfield locales should be written
      directly against the now-current `en.json`, and `ru.json` should be repaired against the
      now-current `en.json` rather than the version it was drafted from.
