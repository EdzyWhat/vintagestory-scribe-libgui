## Why

Scribe's UI, handbook, hover text, and live History sentences already resolve through `Lang.Get` on the viewing client, but the zip still ships English plus a stale Brazilian Portuguese file. Players who will not use a non-native UI — including the ones El_Neuman spoke for on the ModDB thread — cannot play the mod as written. The string set is now content-complete, leftover dates already follow the viewer, and we have permission to take C4B's Spanish pack as a seed, so shipping the full locale set is a bounded file job rather than an endless pipeline.

## What Changes

- Ship a complete `scribe` locale file for each of: Russian (`ru`), Ukrainian (`uk`), Polish (`pl`), German (`de`), Simplified Chinese (`zh-cn`), Japanese (`ja`), Spanish (`es-es`), Czech (`cs`), Swedish (`sv-se`), French (`fr`), Brazilian Portuguese (`pt-br`), Italian (`it`).
- Translate everything players see through `Lang.Get`: handbook VTML, buttons, descriptors, hover text, item/block names, settings, empty states, `date-format`, and the Clockmaker worldconfig line in `assets/game/lang/`.
- Seed Spanish from C4B Traducciones-ES (author approval to take and update). Refresh the existing Arquimago `pt-br.json` against current English (it is ~half the keys and still says a tablet holds 10 tasks). **Audit every overlapping key**, not only the gaps: English has been rewritten in place (handbook tours, capacities, labels), and a translation that still matches an old key name can be the wrong sentence. Generate the other ten locales from current `en.json`, not from stale `pt-br`.
- Keep English History joke pools long. Other locales MAY ship a short contiguous flavor pool (as little as `scribe-mob-death-0` plus one literal PvP verb). Do not copy English jokes into another language, and do not copy English values for missing keys.
- Add a mechanical locale checker (placeholders, VTML tags, extra/stale keys, required flavor-pool shape, and UI-label cross-references — a handbook essay quoting a button/tab/action label must match that label's own translated lang key within the same locale) so files cannot rot the way `pt-br.json` did. Credit C4BR3R4 and Arquimago.
- **Not breaking.** No codec, network, or Core model change. Missing keys still fall back to English; a present-but-wrong key is the failure mode this change exists to prevent.

## Capabilities

### New Capabilities

- `scribe-locales`: which locale files Scribe ships, how they are authored against `en.json`, fallback vs. stale-key rules, overlapping-key meaning audit for C4B/pt-br, History flavor-pool shortness, and the mechanical check that keeps files current.

### Modified Capabilities

(none — handbook, History, and GUI specs already require `scribe:` lang keys; this change fills those keys in other languages.)

## Impact

- **Assets:** new/updated JSON under `src/Mod/assets/scribe/lang/` and matching one-key files under `src/Mod/assets/game/lang/` for the Clockmaker trait string. Canonical English stays `en.json`.
- **Core / Mod C#:** none expected. `Lang.Get` / `HasTranslation` already do the right thing for live UI, handbook, History sentences, and dates.
- **Tooling:** a small checker script under `build/` (not a GitHub workflow change). Optional hook into local `build/verify.sh`.
- **Docs:** CHANGELOG Unreleased; wiki Home credits for C4BR3R4 alongside Arquimago; TESTING.md playtest for a couple of locales including CJK glyph coverage.
- **Out of scope:** Crowdin/Weblate; `es-419` and `pt-pt`; Traditional Chinese (`zh-tw`); bundling CJK fonts; trimming English joke pools; translating player-authored notes; fighting C4B load-order if a player still runs their pack (last-loaded wins — we now ship our own `es-es.json` with their permission).
