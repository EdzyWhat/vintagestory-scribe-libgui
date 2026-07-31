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
`ItemClockmakerNotebook` is a *sibling* of `ItemScribeNotebook` (both extend `Item`), so every
`is ItemScribeNotebook` type-check silently excludes the Clockmaker's Notebook. In-game verification
(2026-07-31) confirmed this breaks more than history: **closing a Clockmaker's Notebook dialog does
not persist its task/note edits at all**, and changing the active hotbar slot force-closes the open
dialog. So the fix spans four sites, all widened to `is (ItemScribeNotebook or ItemClockmakerNotebook)`:
- `FindNotebookInInventory` — live history recorders (deaths/storms/boss kills) can record into a
  held Clockmaker's Notebook (the originally-scoped fix).
- `OnServerReceivedNotebookSave` (`ScribeModSystem.cs:1088`) — the server was **rejecting** the
  Clockmaker's Notebook's `ScribeNotebookSaveMessage`, dropping every task/note edit. This is the
  data-loss bug confirmed in-game.
- `TryResolveDocHost` inventory scan (`ScribeModSystem.cs:981`) — server-side DocId→host resolution
  for pin/edit routing must find a held Clockmaker's Notebook.
- `OnActiveSlotChanged` (`GuiDialogScribeNotebook.cs:153`, inherited by the Clockmaker dialog) — the
  dialog must stay open while a Clockmaker's Notebook is the active hand item.
The `/scripttf` dev transform-tuner (`ScribeModSystem.cs:1153`) stays Notebook-only by design (it
mutates the plain Notebook's model transforms). `NotebookHost`'s constructor is collectible-agnostic
(only touches `ScribeDocumentAttributes` + `scribeHistory` bytes), so it works unchanged for a
clockmaker stack. (User-confirmed after in-game verification: fix the whole sibling-exclusion family.)

**5. Believable dates via a small formatter.**
Add `FormatDateDaysAgo(sapi, daysAgo)` mirroring `NotebookHost.FormatDate` (`:164`) so seeded
History/Guestbook entries span multiple in-game days instead of all showing today. These are
display-only strings stored verbatim; plausibility, not calendar exactness, is the bar.

**6. Recipe: reuse the Lectern recipe's ingredient vocabulary.**
New `recipes/grid/scribenotebook.json` (data-only, auto-loaded). Finalized after an in-game balance
pass (user-directed) to sit closer to the Lectern's writing-set cost: a 3×2 grid `FRN,BL_` =
`game:feather` + `game:paper-parchment` + `game:metalnailsandstrips-*` (the buckle, top-right) over a
fired bowl of black ink + `game:leather-normal-plain`. The ink is a `liquidContainerProps` fill
(`requiresContent: game:dye-black`, `requiresLitres: 1`, `consumeContainer: true`), mirroring the
Lectern recipe exactly. Also sanity-review the two existing recipes for balance; change them only if
warranted. **Recipe corrected in review (user-directed):** the Clockmaker's Notebook recipe is a
single recipe consuming exactly **three ingredients, one each** — 1 `scribe:scribenotebook` +
1 `game:gear-temporal` + 1 `game:metal-parts` — laid out in a 3×1 row (`GMB`). The shipped recipe had
two problems: it was two separate variants (metal-parts OR temporal-gear as four corner pieces around
the notebook), and its metal-parts reference used the non-existent `{ type: "item", code:
"game:metalparts-*" }` wildcard, which resolves to zero items and **crashes the handbook**
(`SlideshowGridRecipeTextComponent.ResolveIngredients` throws when a wildcard matches nothing) the
moment anyone opens the Clockmaker's Notebook's "Created by" page. The correct metal-parts reference is
the block `{ type: "block", code: "game:metal-parts" }` (the `metalpartsandscraps` block, code `metal`,
`type` variant `parts`/`scraps`) — the old one was wrong on both `type` (item→block) and `code`.

**6b. Clockmaker's Notebook craft is gated by the `tinkerer` trait, data-only, with a worldconfig bypass.**
Verified from the game DLLs + vanilla assets (do not re-derive):
- `GridRecipe` (via `Vintagestory.API.Common.RecipeBase`, `VintagestoryAPI.dll`) has a native
  `public string? RequiresTrait { get; set; }` field. Setting `"requiresTrait": "tinkerer"` in the
  recipe JSON is fully enforced by the game with **no mod code** — the vanilla survival
  `Vintagestory.GameContent.CharacterSystem` (`Mods/VSSurvivalMod.dll`) subscribes to
  `IEventAPI.MatchesGridRecipe` and denies the match for players lacking the trait. Vanilla precedent:
  `assets/survival/recipes/grid/{sewingkit,linen}.json` use `requiresTrait: "clothier"`.
- There is **no "Tinkerer" class**; `tinkerer` is a *trait* granted by the `clockmaker` class
  (`assets/survival/config/characterclasses.json`: clockmaker → [..., "tinkerer"]). `tinkerer` in
  `config/traits.json` has empty attributes — a pure gating flag. So the correct token is the trait
  `"tinkerer"`, NOT a class code. Thematically apt: the Clockmaker's Notebook wants the clockmaker's
  tinkerer trait.
- Enforcement short-circuits to **allow** when the player has no `characterClass` watched attribute
  (classless / no character system) — matching vanilla; documented as a scenario, not a bug.
- **Scope:** only `recipes/grid/scribeclockmakernotebook.json` gets `requiresTrait`. The plain
  Notebook and Lectern recipes stay ungated. (User-confirmed.)
- **Bypass = worldconfig + startup null-out.** Declared **data-only** in a `worldconfig.json` at the
  mod root next to `modinfo.json` (Scribe is loaded as a folder mod with a `modinfo.json`, so this is
  the correct form — the `[ModInfo].WorldConfig` attribute form is only for pure code/DLL mods without
  a `modinfo.json`). One `worldConfigAttributes` entry, mirroring vanilla `survivalchallenges` bools:
  `{ "category": "scribe", "code": "scribeClockmakerRequiresTrait", "dataType": "bool", "default": "true" }`.
  Localize its label via lang key `worldattribute-scribeClockmakerRequiresTrait`. **Read** server-side
  with `sapi.World.Config.GetBool("scribeClockmakerRequiresTrait", true)` — always pass the `true`
  default explicitly, since worlds created before this key existed won't have it baked into the savegame
  (`GetBool` does NOT consult the registered attribute default; that default is only written into
  `World.Config` at world *creation*). When the toggle is off, enumerate `sapi.World.GridRecipes`
  (the `List<GridRecipe>` on `IWorldAccessor`), match the Clockmaker's Notebook recipe(s) by
  `r.Name?.Path` / resolved output code, and set `RequiresTrait = null` so `CharacterSystem` allows all.
  **Timing:** grid recipes are registered by vanilla's `RecipeLoader` at `ExecuteOrder 1.0`, so the
  null-out must run *after* that — do it in `AssetsFinalize` or `StartServerSide` (both run after all
  `AssetsLoaded`), NOT in an early `AssetsLoaded`. `/worldconfig scribeClockmakerRequiresTrait false`
  (alias `/wc`) mutates the same key operators would toggle. (Verified from `VintagestoryLib.dll`
  `ModContainer.LoadModInfo`/`WorldConfig`/`CmdWorldConfig`, `VintagestoryAPI.dll`
  `ModWorldConfiguration`/`WorldConfigurationAttribute`/`IWorldAccessor.Config`+`.GridRecipes`/
  `TreeAttribute.GetBool`, and `VSSurvivalMod.dll` `RecipeLoader.AssetsLoaded`.)
  - _Alternative rejected — a second `MatchesGridRecipe` handler returning `true`:_ verified unsafe.
    `Vintagestory.Server.ServerEventAPI.TriggerMatchesRecipe` (`VintagestoryLib.dll`) returns only the
    **last** subscriber's bool (last-writer-wins, undefined ordering vs. the survival mod's handler),
    so a bypass handler cannot reliably override the vanilla `false`. Null the field instead.
  - _Alternative noted — granting `tinkerer` via a player's `extraTraits` watched attribute:_ works
    per-player (the vanilla handler honors `extraTraits`), but the user asked for a world-wide on/off,
    so the worldconfig null-out is the chosen mechanism.
- **No ConfigLib dependency.** The toggle is a vanilla worldconfig setting; ConfigLib stays an
  optional soft dep unrelated to this gate. (User-confirmed.)

**7. Handbook: data-only, following the Lectern convention.**
Add `handbook.extraSections` to both notebook itemtypes referencing new `scribe:` lang keys;
refresh `handbook-scribelectern-*` and the two `config/handbook/*.json` guide pages so mod-wide docs
read coherently. No C# — the engine auto-loads these. The Clockmaker's Notebook handbook entry SHALL
mention the `tinkerer`/clockmaker-class crafting requirement so players understand why the recipe may
not be craftable for them (the engine's own denial is silent).

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
- Whether to surface the toggle on the world-creation Customize screen (default `onCustomizeScreen:
  true`) or hide it (`onCustomizeScreen: false`) and leave it `/worldconfig`-only — decide at
  authoring time. Key name, DECLARE mechanism, and READ call are now settled (see Decision 6b):
  `scribeClockmakerRequiresTrait`, default `"true"`, declared in `worldconfig.json`, read via
  `World.Config.GetBool`.
