## Context

See `proposal.md` for motivation. Today `OnEntityDeath` / the storm tick call `Lang.Get` on the server and store the result in `HistoryEntry.Detail`. `Lang.HasTranslation` (mob-death pool) looks only at the current locale; `TryLang` → `Lang.Get` (PvP verbs) English-falls-back, so a short locale still "hits" English weapon verbs. Kind labels in `GuiDialogScribeNotebook.KindLabel` are already live. `InGameTimestamp` already exists (`SHST v3`); `InGameDate` is a second, formatted copy of that instant.

Constraints that shape this: `src/Core/` must not reference the VS API; History is an append-only item-attribute blob (`SHST`) with a separate pending-death blob (`SPHS`) that currently duplicates the per-entry layout by hand and rejects any version ≠ 1; vanilla already exposes `Entity.GetPrefixAndCreatureName` as `prefixandcreature-<code>` lookups that do not need a live entity instance.

## Goals / Non-Goals

**Goals:**
- Persist enough facts that the History tab can format Death / PvpKill / BossKill / TemporalStorm in the viewer's locale.
- Keep English flavor catalogs intact; let other locales ship short pools without mixed-language sentences.
- One shared per-entry read/write helper so `HistoryStore` and `PendingHistoryStore` cannot drift.
- Leave recording triggers, inventory scope, queuing, and caps untouched.

**Non-Goals:**
- Live-localizing Guestbook or assignment date stamps.
- Re-parsing v1–v3 baked sentences.
- Changing English pool contents.
- Translating the rest of the mod.
- Storing a lang key per row (that key may not exist in a short locale).

## Decisions

**Store facts + a schema byte, not a lang key and args.**
A row like `scribe-mob-death-17` is wrong once `ru.json` only has `-0` and `-1`. Facts are: `SubjectName`, `OtherName`, `RefCode`, `RefCode2`, `FlavorSeed`, plus `HistorySchema { Baked = 0, Live = 1 }`. Kind decides how to interpret the refs:

| Kind | SubjectName | OtherName | RefCode | RefCode2 | FlavorSeed |
|---|---|---|---|---|---|
| Death (creature) | victim | | entity code (`game:drifter-nightmare`) | | random int |
| Death (environment) | victim | | vanilla cause (`fall`) | | variant int |
| Death / PvpKill | victim | killer | `EnumTool` name or empty | `EnumDamageType` name | killer's prior PvpKill count |
| BossKill | slayer | | boss key (`eidolon`) | | unused |
| TemporalStorm | | | strength (`heavy`) | | unused |

`Detail` / `InGameDate` stay on the record for `Baked` rows and for Manual text. Live rows leave `Detail` empty. Alternative considered: encode facts inside `Detail` with a delimiter — rejected, because Manual text is already `Detail` and Core tests should round-trip named fields.

**Format on the client with `Lang.CurrentLocale`; write path does not call `Lang.Get`.**
The History tab is the only renderer. Dedicated servers stay English; that must not stamp the chronicle. `Lang.GetL(player.LanguageCode, …)` at write time was the smaller alternative (still a stored sentence, one language per owner). Rejected: handing the notebook to someone else would keep the writer's language, which is what we are moving away from for system rows. Manual notes still do that, on purpose.

**Discover pools and PvP verbs with locale-scoped `HasTranslation`, never `Lang.Get`.**
`Lang.Get` falling back to English is exactly how "pincushioned" would leak into a Russian template. Display uses `AvailableLanguages[CurrentLocale].HasTranslation` (or `Lang.HasTranslation`, which is current-locale-only). Probe generic/mob-death keys upward until a miss, same as today's mob-death probe, but on the client at paint time. Cache pool size per locale string for the process; it is static shipped content.

**Map `FlavorSeed` with remainder, pick the seed at write without probing a pool.**
`slot = abs(seed) % poolSize` (poolSize < 1 → fallback key, not a throw). Creature deaths store `World.Rand.Next()` so we do not need the English pool size on the server. PvP stores the killer notebook's existing PvpKill count so generic verbs still rotate when the viewer has more than one. Alternative: re-roll at display — rejected, because reopening History would flicker.

**Creature names from the stored entity code, matching vanilla's key shape.**
`GetPrefixAndCreatureName` is `Lang` lookups of `{domain}:prefixandcreature-{path}` (and the no-hyphen old key), then `generic-wildanimal`. The formatter repeats that with `Lang.CurrentLocale` and does not need the dead entity. If both keys miss, use `generic-wildanimal` rather than the raw code.

**`SHST v4` appends the new fields; `SPHS` becomes v2 with progressive reads.**
Per-entry layout after the existing v3 fields: `byte schema`, `string subjectName`, `string otherName`, `string refCode`, `string refCode2`, `int flavorSeed`. v1–v3 reads default schema `Baked` and empty facts (`ApplyV3ToV4Migrations`). Extract `WriteEntry` / `ReadEntry(version)` so `PendingHistoryStore` stops copying six fields by hand; SPHS v1 blobs still load as baked entries (today's queued deaths are baked sentences and should display that way). SPHS today drops any version ≠ 1 — that must become a window or queued live deaths vanish across a restart during the upgrade. Alternative: keep two layouts — rejected as the original drift source.

**One Mod-side formatter next to `KindLabel`.**
`HistoryDisplay.Sentence(entry)` / `HistoryDisplay.Date(entry, calendar)` in the Mod project. Core only gets a tiny `HistoryFlavor.Slot(seed, poolSize)` helper. Crafted / PickedUp / Manual / `Baked` rows keep the current string concat; the formatter is not consulted. Dates for live rows rebuild via existing `NotebookHost.FormatCalendarDate` from `InGameTimestamp` (same math as `FormatDateDaysAgo`, using the client's `IWorldAccessor.Calendar`).

**Dev seeder writes live facts, not `Lang.Get` sentences.**
`/scribe seed` history should look localized when we start shipping other lang files. Storm rows that currently stuff `"Medium"` into `Detail` become `RefCode = "medium"`.

## Risks / Trade-offs

- **[Older client opens a v4 notebook]** → `HistoryStore` rejects version > current and shows empty History for that item until the player updates. Same as every prior History bump; not a world-break. Mitigation: this ships with the rest of 1.4.x, not as a silent blob change on an old minor.
- **[Entity code from a removed creature pack]** → prefixandcreature miss → `generic-wildanimal`. Acceptable; better than a raw code.
- **[Same death, different joke per locale]** → inherent. Remainder mapping is stable per locale, not semantically "the hug joke."
- **[Client pool probe on every History open]** → cheap (`HasTranslation` is a cache lookup). Cache the count per locale if a log line ever shows it hot.
- **[SPHS v1 queued death + this upgrade in one restart]** → v1 pending entries have no fact fields; they display as baked (and today's queue only holds baked `Detail`). New deaths after load are live.

## Migration Plan

1. Ship `SHST v4` / `SPHS v2` with progressive reads of every previously accepted version (History v1–v3, pending v1).
2. New Death / PvpKill / BossKill / Storm writes set `Schema = Live`.
3. Existing notebook attributes are untouched until the next event rewrites the blob; old rows stay `Baked`.
4. Rollback is "don't install the new build": v3 readers will not parse v4 blobs (empty History on that item). No data rewrite back to v3 is provided.
5. Update the HistoryStore row in `docs/CODEC-MIGRATION.md`.

## Open Questions

None. Locale-file work for other languages is a later change; this only makes History safe to translate.
