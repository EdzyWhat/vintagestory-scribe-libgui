## Why

Notebook History already localizes kind labels (`Death`, `Crafted`, `{0}'s Note`) at display time, but the sentence in `Detail` and the date in `InGameDate` are formatted on the server in the server's language and stored as opaque text. A Russian player on a typical English dedicated server therefore gets an English chronicle forever, and a short `ru.json` flavor pool cannot coexist with English's longer joke catalog. We are about to ship more languages; History has to resolve in the viewer's locale without trimming English.

## What Changes

- System-generated History rows (Death, PvpKill, BossKill, TemporalStorm) persist **facts** (names, entity/boss/cause/tool tokens, a flavor seed, timestamp) instead of a finished sentence. The History tab formats those facts with `Lang.Get` in the **viewer's** language when the row is built.
- Flavor pools stay as they are in English. A locale may ship as few as one `scribe-mob-death-*` / generic PvP verb. Display maps `seed % that locale's pool size` so a missing index never echoes English.
- PvP verb discovery uses `HasTranslation` on the viewer's locale (no `Lang.Get` English fallback), so omitted weapon verbs fall through to generic the way the lang comments already describe.
- Dates for live entries are rebuilt from `InGameTimestamp` via `scribe:date-format` and vanilla `month-*` keys. Kind labels stay as they are.
- Manual notes, Crafted, and PickedUp are unchanged: Manual `Detail` is authorship; Crafted/PickedUp already render from kind label + `ActorName`.
- **Not breaking for saves:** `HistoryStore` becomes `SHST v4` with progressive reads of v1–v3. Pre-v4 entries keep showing their baked `Detail` / `InGameDate`. New events after this change are live. The pending-death queue (`SPHS`) is bumped in lockstep so a queued v4 entry survives a restart.
- Explicitly out of scope: translating the rest of the mod; Guestbook / assignment date stamps; rewriting already-recorded v1–v3 sentences; shrinking English flavor pools.

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `notebook-history`: Death / PvpKill / BossKill / TemporalStorm persist structured facts and render in the viewer's language; flavor-pool and PvP-verb discovery is per-locale via `HasTranslation`; codec v4 + pending-store bump; legacy baked entries remain displayable.

## Impact

- **Core:** `HistoryEntry` gains a live-schema flag plus fact fields; `HistoryStore` `SHST v4`; `PendingHistoryStore` `SPHS v2`; Core stays free of `Lang` / VS API. Small pure helpers for `seed % poolSize` and key construction are Core-testable.
- **Mod:** `ScribeModSystem.History.cs` stops calling `Lang.Get` at write time (stores facts, still skips work when no relevant notebook exists). `GuiDialogScribeNotebook` (and any other History renderer) formats live rows. Creature display names resolve from a stored entity code via vanilla `prefixandcreature-*` keys in the current locale, matching `Entity.GetPrefixAndCreatureName`. Dev seeder writes facts, not baked English sentences.
- **Tests:** Core round-trip / v3-compat unit tests; Atlas scenarios that currently assert a fully-substituted English `Detail` flip to asserting stored facts; a display-formatter test that a short pool never receives an out-of-range index.
- **Docs:** `docs/CODEC-MIGRATION.md` HistoryStore row updates to v4. No gameplay or network-packet change beyond the item-attribute blob.
