## Context

The Notebook and Clockmaker's Notebook features already ship in the mod (dialogs, timer, per-item
document store, history chronicle). This change is release-prep for 0.2.0: the missing survival
recipe, in-game handbook coverage, refreshed launch material, and a dev tool to produce demo
content. The only non-trivial engineering is the demo-seeding command, because it must write into
three stores that are hosted asymmetrically and are server-authoritative.

Key structural facts discovered during exploration (ground truth for the design):

- **History is hosted only on the Notebook** (`NotebookHost.History`, persisted in
  `ItemStack.Attributes["scribeHistory"]`). `BlockEntityScribeLectern` has no `HistoryStore` — no
  field, no tree attribute, no dialog reference.
- **Guestbook is hosted only on the Lectern** (`BlockEntityScribeLectern._guestbook`). The
  Notebook throws `NotSupportedException` for its guestbook (`NotebookHost.cs:76`).
- **Tasks and notes live on both** via `ScribeDocument` (`AddTask` @45, `AddTextSection` @66,
  `ToggleTask` @90 — all in `src/Core/ScribeDocument.cs`, no VS API).
- History/Guestbook are append-only logs (`HistoryStore.TryAddEntry` @51,
  `GuestbookStore.TryAddEntry` @30 / `TrySetNote` @45) — they cannot be hand-authored in a saved
  world, which is why a programmatic seeder is required.
- Chat-command precedent: `RegisterNotebookTuneCommand` (`ScribeModSystem.cs:1086`) uses the fluent
  `api.ChatCommands.Create(...).WithDescription(...).WithArgs(Parsers...).HandleWith(...)` style.

## Goals / Non-Goals

**Goals:**
- Make the plain Notebook craftable in survival (data-only recipe), completing the survival
  Notebook → Clockmaker's Notebook chain.
- Give both notebook items in-game handbook entries and refresh mod-wide handbook content.
- Provide a dev/creative-gated `/scribe seed` command that produces believable tasks, notes,
  History, and Guestbook content through the normal server-authoritative flow.
- Fix the latent bug where a held Clockmaker's Notebook never receives live history events.
- Refresh all launch material (mod page, wiki drafts, reddit, video/shot-list) and cut 0.2.0
  release mechanics.

**Non-Goals:**
- No new Notebook/Clockmaker gameplay features — they are already built.
- No changes to `src/Core` public model beyond using existing mutation methods; no new network
  message types.
- No live guestbook-tab repaint while seeding (seed then reopen is acceptable for a dev tool).
- No publishing to external sites from this change — wiki/mod-page/reddit are authored in-repo and
  published manually.

## Decisions

**1. `/scribe seed <what> [target]` is a server-side command.**
All three stores are server-authoritative; a client command cannot legitimately mutate them.
Register in `StartServerSide` (`ScribeModSystem.cs:479`) via `sapi.ChatCommands`.
- `what` = `WordRange("tasks","notes","history","guestbook","all")`.
- `target` = optional `WordRange("notebook","lectern")`, default `auto`.
- Gate: `.RequiresPrivilege(Privilege.controlserver)` + `.RequiresPlayer()`, and an in-handler
  `EnumGameMode.Creative` check that errors otherwise.
- _Alternative rejected:_ a client command like `scripttf` — wrong side for authoritative state.

**2. Target resolution mirrors existing lookups.**
`auto` → if `player.CurrentBlockSelection?.Position` resolves to a `BlockEntityScribeLectern`
(pattern from `BlockScribeLectern.cs:48`), seed the lectern; else seed the held notebook via
`FindNotebookInInventory` (`ScribeModSystem.cs:1257`). History-on-lectern and guestbook-on-notebook
combinations are skipped and reported, never errored — matching the hosting asymmetry.

**3. Reuse existing persistence paths; add three minimal additive seams.**
- Notebook: seed `Document` + `History`, then call `NotebookHost.Flush()` (writes both, marks the
  slot dirty, pushes `ScribeNotebookSaveMessage`; the client handler at `ScribeModSystem.cs:1056`
  already refreshes an open dialog). `Flush()` is currently `private` → **make it public**, matching
  the already-public `FlushHistory()`. _Alternative:_ a bespoke `SeedContent()` wrapper — rejected as
  more surface than needed.
- Lectern: seed `lectern.Document` (public getter) + guestbook, then
  `lectern.MarkDirty(redrawOnClient: true)` (triggers `ToTreeAttributes` + block-entity packet;
  client `FromTreeAttributes` at `:168` refreshes the read view).
- Guestbook seam: `_guestbook` is private and its only mutators act for the calling player. Add a
  **server-only `BlockEntityScribeLectern.SeedGuestbook(entries)`** that guards
  `Api is ICoreServerAPI`, loops `TryAddEntry`/`TrySetNote`, then `MarkDirty()` — mirroring
  `RecordVisitor` (`:561`).

**4. Widen notebook detection to both item classes (a real fix, not just a seed helper).**
`FindNotebookInInventory` (`:1257`) matches only `ItemScribeNotebook`; `ItemClockmakerNotebook` is
a sibling class, so live history recorders (deaths/storms/boss kills) currently never record into a
held Clockmaker's Notebook. Widen the match to `is ItemScribeNotebook or ItemClockmakerNotebook`
everywhere the helper feeds recording. `NotebookHost`'s constructor is collectible-agnostic (only
touches `ScribeDocumentAttributes` + `scribeHistory` bytes), so it works unchanged for a clockmaker
stack. (User-confirmed: fix the live recorders too, not just the seed path.)

**5. Believable dates via a small formatter.**
Add `FormatDateDaysAgo(sapi, daysAgo)` mirroring `NotebookHost.FormatDate` (`:164`) so seeded
History/Guestbook entries span multiple in-game days instead of all showing today. These are
display-only strings stored verbatim; plausibility, not calendar exactness, is the bar.

**6. Recipe: reuse the Lectern recipe's ingredient vocabulary.**
New `recipes/grid/scribenotebook.json` (data-only, auto-loaded) with baseline
`game:paper-parchment` + `game:leather-normal-plain`; exact grid finalized at authoring time. Also
sanity-review the two existing recipes for balance; change them only if warranted.

**7. Handbook: data-only, following the Lectern convention.**
Add `handbook.extraSections` to both notebook itemtypes referencing new `scribe:` lang keys;
refresh `handbook-scribelectern-*` and the two `config/handbook/*.json` guide pages so mod-wide docs
read coherently. No C# — the engine auto-loads these.

**8. Launch material stays in the existing `docs/media/` convention.**
`mod-page.txt` edited in place (fix stale LibGUI 2.0.0 → 3.1.0, add notebook section, bump
roadmap); wiki page drafts under `docs/media/wiki/`; a fresh 0.2 reddit feature-announcement;
updated `video-script.md`; screenshots destined for `docs/media/screenshots/0.2/`; a light
shot-list keyed to the demo seeds. User publishes wiki/mod-page/reddit manually.

## Risks / Trade-offs

- [An open guestbook tab won't repaint while seeding — `FromTreeAttributes` refreshes the read view
  but not the guestbook view] → Acceptable for a dev tool; seed first, then open the lectern. Live
  refresh (pushing a guestbook sync) is deliberately out of scope.
- [Widening notebook detection changes live recording behavior for existing Clockmaker's Notebook
  holders] → This is the intended fix; call it out in the CHANGELOG. Behavior for plain Notebooks is
  unchanged. Add/confirm Core-level coverage where feasible.
- [Making `NotebookHost.Flush()` public widens the host's surface] → Minimal and consistent with the
  already-public `FlushHistory()`; no behavior change.
- [Recipe ingredient balance is subjective] → Anchor to the existing Lectern recipe's vocabulary and
  verify craftability in-game before release.
- [Marketing/version strings drift out of sync (modinfo vs mod page vs video)] → The tasks include an
  explicit consistency check that all surfaces say 0.2.0 and LibGUI 3.1.0.

## Migration Plan

No data migration. The recipe and handbook are additive assets. The detection-widening fix only
enables recording into an item that previously received none, so there is no stored-data
compatibility concern. Rollback = revert the change; seeded demo worlds are throwaway capture
worlds, not shipped saves.

## Open Questions

- Exact grid arrangement and whether any binding item (e.g. twine) joins the parchment + leather
  baseline — finalize at recipe-authoring time and verify in-game.
- Whether to extend `RELEASE.md` with a 0.2.0 track section or start a dedicated 0.2.0 release doc —
  decide at authoring time (leaning toward extending `RELEASE.md` for continuity).
