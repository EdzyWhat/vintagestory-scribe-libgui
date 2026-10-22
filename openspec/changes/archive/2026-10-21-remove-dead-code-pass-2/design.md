## Context

Findings come from a read-only audit; each lang key was searched in every literal, prefix and concatenated form
across `src/`, `tests/`, `tools/` and non-lang assets. `build/` scripts were excluded as evidence of use.

## Goals / Non-Goals

**Goals:** remove only what is certain; keep the lang files consistent with each other.

**Non-Goals:** the 17 `item-*-desc` keys (the engine likely reads `itemdesc-*`, so they may be a naming bug, not
dead; see Risks), keys missing from some translations, and fields that are only written.

## Decisions

- **Remove the keys from all lang files in one change**, so `build/check-locales.py` parity stays intact.
- **Signature tidy is mechanical.** Each parameter is removed with its call-site arguments; a build proves no caller
  was missed. `ForTabletQuestLink` is `public static` but has one caller and no external users.
- **Leave `ScribeRowSizeDirection.Reveal`**: removing it would leave a one-member enum for no gain.

## Risks / Trade-offs

- A key read through a route the audit missed would show its raw key in game. Mitigation: run
  `build/check-locales.py` and `build/verify.sh`, and grep each key one last time before deleting.
- **Possible bug, not fixed here:** the `item-*-desc` keys (notebook, clockmaker, tasknotice, tablets) show no reader;
  vanilla looks up `itemdesc-<path>`. If tooltips lack descriptions in game, renaming them is a separate fix.
