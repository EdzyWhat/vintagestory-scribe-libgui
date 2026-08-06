## Context

Several independent defects from the 0.3 tablet playtest, bundled into one `zero-point-three-fixes` change.

**Tablet edit feedback (2026-08-05 playtest).** The playtest confirmed the firing/hardening mechanics work
but surfaced a family of missing user-feedback behaviors, all around edit restrictions being silent:
(a) adding an 11th task to a tablet (10-task cap) silently no-ops with no notice; (b) a hard or fired
tablet blocks text edits but says nothing about why or how to recover; (c) firing a tablet that has a
pinned task can strand that pin on the HUD with no way to remove it, because unpin was being treated as an
"edit" and disabled. The product decision (confirmed with the author): **checkbox completion and pin/unpin
stay live on hard and fired tablets; only *text* editing is blocked**, and on a read-only tablet the
Unpin / Delete / Unpin-and-Sink completion policies all collapse to unpin-only (no row mutation). Refused
edits surface through Vintage Story's standard ingame-error path with a material-specific message.

**Schematic recipe.** `add-clockmaker-notebook-schematic` added a second recipe for the Clockmaker's
Notebook — the same ingredients as the trait-gated recipe plus a reusable `scribe:clockmakerschematic`
(`consume: false`) — so non-Tinkerer players can craft it with a purchased blueprint. The recipe was
authored as a 1×4 row (`ingredientPattern: "BGMS"`, `width: 4, height: 1`). The vanilla crafting grid is
3×3 (`GridRecipe.Width`/`Height` default 3; no shipped grid recipe is 4-wide, confirmed by scanning
`assets/survival/recipes/grid/`). A 4-wide recipe can't be laid out in the grid, so it never crafts —
and the survival handbook's `CollectibleBehaviorHandbookTextAndExtraInfo.addCreatedByInfo`, which
enumerates every grid recipe producing the page's stack, skips/hides it too. That single defect produces
both playtest symptoms: the schematic craft fails (`e093c2ad`) and the handbook shows only the trait
recipe with no schematic path and no trait asterisk.

**Handbook dual display.** The player wanted the Sling's presentation: multiple recipes on one entry,
with a `* Requires <trait> trait` asterisk on the gated one. That rendering is entirely automatic —
`addCreatedByInfo` draws one "Created by" grid per matching recipe and appends the `gridrecipe-requirestrait`
lang note (`"* Requires {0} trait"`) for any recipe carrying `requiresTrait`. So once the schematic recipe
is a valid grid recipe, both grids appear with the asterisk on the trait one, with no custom handbook code.

**Quench rehydration.** `ItemScribeTablet` already softens a hard tablet back to wet on passive water
exposure: `OnGroundIdle` (dropped item swimming) and `OnHeldIdle` (holder swimming) both call the private
`Soften(stack, world)`, which swaps the `clay-<c>-hard` variant to its `clay-<c>` sibling, carries the
document/history via `CarryStackData`, and omits `transitionstate` so the dry-out clock re-seeds. The
gesture players expect — crouch + right-click a water container, like quenching hot metal — does not
exist. Vanilla's quench is passive (per-tick `ICoolingMedium.CoolNow` when a hot block sits under liquid),
so there is no gesture to inherit; we author the trigger and reuse `Soften` unchanged.

## Goals / Non-Goals

**Goals:**
- Make the schematic recipe craftable by fitting it in the 3×3 grid.
- Get both recipes (schematic + trait-gated) onto the Clockmaker's Notebook handbook as separate grids,
  the trait one asterisked — via recipe validity alone, no handbook code.
- Add a deliberate crouch + right-click-a-water-container quench that softens a hard tablet, reusing the
  existing `Soften` machinery, additive to the two existing passive paths.
- Surface a standard in-game error when a tablet edit is refused: an over-cap task add (11th task), and a
  text edit / row add-delete-reorder on a hardened or fired tablet (material-specific message).
- Keep checkbox completion and pin/unpin live on hard and fired tablets (only text editing is blocked), so
  a pinned task on a fired tablet is never stranded on the HUD.
- Collapse the Unpin / Delete / Unpin-and-Sink completion policies to unpin-only on a read-only tablet, so
  completing a task never mutates the locked document but still clears its pin.

**Non-Goals:**
- No change to ingredients, output, trait gating, or trader availability of either recipe.
- No `recipegroup` grouping (recipes stay separate grids, not a cycling entry).
- No temperature/`ICoolingMedium` model on the tablet — the quench is a state swap, not a thermal sim.
- No change to the passive drop-in-water / swim-while-holding paths (kept as-is).
- No new water-consumption cost (dipping doesn't drain the container) unless a later fix revisits it.
- No change to the *values* of the caps (still 10 tasks / 1 pin) or to how completion policy behaves on a
  wet/editable tablet — only the read-only collapse and the refusal-feedback are new.
- No general edit-feedback framework for notebooks/lecterns — the messaging is scoped to the tablet's
  hard/fired states and the tablet task cap. (Notebooks/lecterns are uncapped and never read-only.)
- No new blocked-interaction on checkboxes or pins — those stay live on every tablet state by design.

## Decisions

**D1 — Reshape the schematic recipe to `BGM,S__` (3×2), mirroring the trait recipe's `BGM` row.**
`ingredientPattern: "BGMS"` (unusable 1×4) becomes `width: 3, height: 2`, pattern `"BGM,S__"`: the top
row is Notebook-Gear-MetalParts (identical to the trait recipe's single row), with the reusable schematic
in the bottom-left cell (the two trailing `_` are empty cells). This makes the schematic path read as
"the same craft, plus a blueprint on the shelf below" and keeps the two handbook grids visually parallel
(both share the `BGM` row). *Alternatives:* a compact 2×2 `"BG,MS"` block (fits, but doesn't visually
echo the trait recipe); a 3×3 cross (overkill). Any layout fitting the 3×3 grid is craftable — the shape
is cosmetic — so it's chosen to parallel the trait recipe per the product ask.

**D2 — Give the two recipes DISTINCT `recipegroup` values (1 = trait, 2 = schematic).**
Corrected from the original plan (which said to leave them ungrouped): decompiling
`CollectibleBehaviorHandbookTextAndExtraInfo.addCreatedByInfo` shows the "Created by" section buckets
grid recipes into an `OrderedDictionary` keyed by `RecipeGroup` and renders ONE cycling slideshow grid per
distinct group value. Recipes that omit `recipegroup` all default to `0`, so leaving both ungrouped puts
them in the SAME bucket → they collapse into ONE cycling grid (exactly the "only one grid shows" playtest
symptom). To get TWO side-by-side grids they must have DIFFERENT group values. Vanilla confirms both
readings: planks all share `recipeGroup: 1` (one cycling grid); Sling uses `1/2/3` (separate). `RecipeGroup`
is display-only — it is not referenced in `GridRecipe.Matches`/`MatchesAtPosition`, so it never affects
craftability. *Alternative:* a single shared group to cycle both in one grid — rejected because the two
acquisition paths (trait vs. blueprint) should read as genuinely distinct, per the product ask.

**D3 — Quench trigger lives in `OnHeldInteractStart`, gated on ShiftKey + a water-container `blockSel`.**
The tablet's `OnHeldInteractStart` already branches on `byEntity.Controls.ShiftKey` to pass through to
`GroundStorable` placement. The quench must run on the SAME shift+right-click, so it takes precedence
ONLY when the aimed-at block is a water-holding container; otherwise it falls through to the existing
shift-passthrough (so ground-storage placement is unaffected when not aiming at water). Detection:
`blockSel is not null` → `world.BlockAccessor.GetBlock(blockSel.Position)` is a `BlockLiquidContainerBase`
whose `GetContent(blockSel.Position)` is a water portion (check the content stack's
`WaterTightContainableProps`/code, or `Collectible.GetCollectibleInterface<ICoolingMedium>()?.CanCool(...)`).
*Alternative:* a separate non-shift right-click — rejected because plain right-click already opens the
dialog, and reusing the crouch gate matches the metal-quench muscle memory the player asked for.

**D4 — Server-authoritative swap, client-side splash/sizzle feedback.**
`OnHeldInteractStart` fires on both sides. Mirror the existing idle-soften convention: do the
`Soften`→`slot.Itemstack = softened`→`slot.MarkDirty()` on the server only; set
`handling = EnumHandHandling.PreventDefault` so the container's own fill/pour interaction doesn't also
fire; play a splash/sizzle sound (and optional particles) on the client for feedback. Only act when
`ReadHard(stack)` is true — a wet tablet (already editable) and a fired tablet (permanent) both no-op,
so the gesture is inert on anything but a hard tablet, exactly like `Soften`'s own guard.

**D5 — No `handleLiquidContainerInteract` attribute needed (verify in-game).**
Vanilla routes a held item's interaction to the container's `OnBlockInteractStart` first, and only back
to the held item's `OnHeldInteractStart` when the collectible sets `Attributes.handleLiquidContainerInteract:
true`. Since we crouch (ShiftKey), the container's fill/pour path is typically suppressed and the held
handler runs — but if playtest shows the container swallowing the crouch-right-click, add that attribute
to `scribetablet.json`. Left out initially to avoid over-configuring; called out as the first fallback.

**D6 — Surface refused edits through the existing `capi.TriggerIngameError` path.**
The mod already surfaces transient errors with `capi.TriggerIngameError(this, "<stable-code>", Lang.Get("<key>"))`
(the lock-contention notice at `ScribeDialogBase.ViewSwitching.cs:306` and the refused editor-access reply at
`BlockEntityScribeLectern.cs:549`). The tablet dialog is a `ScribeDialogBase` and holds `capi` directly, so
the new feedback reuses that exact path — no new UI surface. Today every restriction is **silent**: the
over-cap add is a bare `return` (`ScribeDialogBase.Editor.cs:79` and `:381`; the footer button also dims via
`ScribeEditorContent.cs:472`), and the read-only checkbox/pin are inert/hidden (`ScribeReadContent.cs:240,309`).
New stable error codes + lang keys: `scribe:tablet-full` ("A tablet holds at most 10 tasks."),
`scribe:tablet-hard-locked` ("This tablet has hardened. Soften it in water to make changes."),
`scribe:tablet-fired-locked` ("This tablet was fire-hardened. It cannot be changed."). The over-cap message
fires at BOTH silent add guards (footer button click and Enter-to-insert), so the dimmed button and the
keyboard gesture both now explain themselves. *Alternative:* a chat message — rejected; `TriggerIngameError`
is the established, non-intrusive transient path and matches the two existing call sites.

**D7 — Keep completion + pin live on hard/fired tablets by scoping the read-view lock to *text*, not toggles.**
A hard/fired tablet renders the read view (`GuiDialogScribeTablet.cs:267-268` → `BuildReadContent`), which
already offers no text-edit/delete/drag affordance — so "block text editing" is satisfied structurally. The
change is to stop the read view's `ReadOnly` flag from *also* disabling the checkbox (`ScribeReadContent.cs:240`)
and hiding the pin (`:309`) on a tablet. Concretely, the tablet's read view SHALL present the completing
checkbox and the hover pin as interactive even when the tablet is hard/fired; only the tabbed Lectern/Notebook
read view keeps its existing (already `ReadOnly=false`) behavior, so nothing there changes. This directly fixes
the stranded-pin problem: a pinned task on a fired tablet stays unpinnable-from otherwise. *Alternative:*
introduce a wet-only editor with disabled text fields — rejected; far more surface than reusing the read view,
which is already the right shell for "display text, live toggles."

**D8 — Collapse Delete / Sink / Unpin-and-Sink to unpin-only for a read-only tablet at the server completion chokepoint.**
Completing a pinned task runs the policy switch server-side in `CompleteTaskForPlayer`
(`ScribeModSystem.PinOperations.cs:101-135`) and `CompleteUnpinnedTaskAtSource` (`:276-286`), after
`NormalizePolicy` (`:279`, called from `Network.cs:84`). `Delete`→`DeleteTaskFromReader` and
`Sink`/`UnpinSink`→`MoveTaskToBottomFromReader` would mutate a locked document. The collapse lives in
`NormalizePolicy`: when the target document's tablet is hard/fired, normalize `Delete`, `Sink`, and
`UnpinSink` all to `Unpin` (and `Keep` stays `Keep` — completing without unpinning is harmless and mutates
nothing). This is a single server-authoritative chokepoint that both completion paths already pass through, so
the HUD-completion path (`HudScribePins` → `ScribeCompleteTaskMessage`) is covered for free. Note the enum
member is `ScribeCompletionPolicy.UnpinSink` (not "UnpinAndSink"). *Alternative:* guard each mutating primitive
(`DeleteTaskFromReader`/`MoveTaskToBottomFromReader`) — rejected; normalizing once at the policy seam is
narrower and keeps the primitives tier-agnostic.

**D9 — The text-edit-refused message fires on tapping a task's *text* (not its checkbox) on a read-only tablet.**
Since a hard/fired tablet has no text input to click into, the discoverability gesture the author asked for
("if they try to click into the text … tell them to soften it") needs an explicit trigger. Decision: on a
read-only tablet, tapping a read-row's text region (distinct from the checkbox and the pin, which now act)
fires the D6 material message. This turns the read row's text into a "why can't I edit this?" affordance
without adding an editor. *Interpretation flag:* the author's note listed "check boxes or click into the text"
together as blocked edits, but their closing line makes checkboxes live — so only the text tap is treated as
an edit attempt. Confirm at review if tapping text should instead do nothing (no message).

## Risks / Trade-offs

- **[Container interaction precedence]** The bucket/barrel's own crouch-right-click behavior might win over
  the tablet's handler, so the quench appears to do nothing. → D5: fall back to the
  `handleLiquidContainerInteract` attribute; this is an in-game verification point, not a code unknown.
- **[Shift-passthrough collision]** Quench shares the crouch gesture with ground-storage placement. →
  D3 gates quench strictly on aiming at a water container; every other crouch-right-click still falls
  through to the existing `GroundStorable` branch unchanged.
- **[Archive-order header drift]** This change's deltas MODIFY requirements that
  `add-clockmaker-notebook-schematic` and `add-tablet-firing-mechanic`/`wire-tablet-clay-art-and-variants`
  introduce and haven't archived yet. If archived first, the delta headers won't locate their target. →
  Archive this change AFTER those; match the canon header wording they establish (documented lesson).
- **[Water detection breadth]** Different containers (bucket, barrel, tureen) expose contents slightly
  differently. → Detect via the shared `BlockLiquidContainerBase.GetContent` / `WaterTightContainableProps`
  base API rather than per-block casts, so any water-holding liquid container works uniformly.
- **[No water cost]** Dipping doesn't consume water, so a single bucket rehydrates infinitely. Accepted:
  matches the low-stakes reversibility the feature is for; a cost can be a later 0.3 fix if desired.
- **[Read-only checkbox/pin now live only on the tablet]** The read view's `ReadOnly` flag is shared with
  the tabbed Lectern/Notebook read view, which already passes `ReadOnly=false`. The D7 change must make the
  checkbox/pin interactive for the *tablet's* read-only case WITHOUT touching the tabbed view — so the
  toggle-enable is gated on "this is a tablet read view," not on clearing `ReadOnly` globally (which also
  drives text/backdrop styling). Verify the tabbed read view is visually and behaviorally unchanged.
- **[Policy collapse must cover both completion paths]** Delete/Sink also run editor-locally
  (`ScribeDialogBase.Editor.cs:161-189`) — but that path only runs for a WET tablet (the editor never opens
  on hard/fired), so the server `NormalizePolicy` seam is sufficient for the read-only collapse. Confirm the
  HUD-completion path routes through the same `NormalizePolicy` before trusting the single chokepoint.
- **[Refusal must be observable, not silent]** The over-cap add is enforced at a Core predicate
  (`ScribeDocumentPolicy.CanAdd`) but the *feedback* is a Mod concern (`capi.TriggerIngameError`). Keep Core
  pure — the predicate stays boolean; the Mod add-guards call the error path when it returns false. Do not
  push a VS-API error call into Core.
