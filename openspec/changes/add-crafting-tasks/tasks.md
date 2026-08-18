## 1. Core model — Craft kind + recipe binding

- [x] 1.1 Add `Craft = 4` to `ScribeBlockKind` in `src/Core/ScribeBlock.cs` (append; never renumber).
      Update the enum doc comment to mention `Craft` alongside Task/Text/Tracker/Link.
- [x] 1.2 Add a recipe-signature field to `ScribeBlock` (e.g. `public string RecipeSignature { get; set; } = ""`)
      with a doc comment noting it identifies the bound grid recipe variant and is empty for non-Craft
      blocks. Confirm `Depth` already exists and is clamped to `[0, 1]`; tighten its clamp/doc from
      "always 0 for now" to the one-level subtask contract.
- [x] 1.3 Add `public bool IsCraft => Kind == ScribeBlockKind.Craft;` and
      `public bool IsCarriedCountTracked => Kind is ScribeBlockKind.Tracker or ScribeBlockKind.Craft;`
      to `ScribeBlock`; update the `IsTracker` doc to point at `IsCarriedCountTracked` as the broader
      count-tracked predicate.
- [x] 1.4 Clamp `Depth` set to `[0, 1]` in `ScribeBlock` (drop the old always-0 assumption).

## 2. Core codecs — persist RecipeSignature (Depth already round-trips)

- [x] 2.1 In `ScribeDocumentCodec.cs` (binary), write/read `RecipeSignature` after the Tracker fields;
      bump the internal doc version comment if the format tracks one. Confirm an unknown `Kind` byte
      still degrades to `Task` on read. (Version bumped 7→8; appended as a plain string, always written.)
- [x] 2.2 In `ScribeDocumentJsonCodec.cs`, add `RecipeSignature` to the JSON DTO (default empty when absent).
- [x] 2.3 In `ScribeDocumentTsvCodec.cs`, persist `RecipeSignature` (packed into the `Special` cell per the
      codec's "fixed columns forever / richness lives in Special" invariant — NOT a new column). Depth unchanged.

## 3. Core — batch math + ingredient-list model (API-free)

- [x] 3.1 Add a Core value model for a derived ingredient (`ScribeCraftIngredient(ItemCode, PerCraftQuantity)`)
      — no VS API types. (Wildcard-ness is derivable from the code; liquid notes flow through
      `ReconcileCraftIngredients`' separate `notes` param, so the model stays minimal.)
- [x] 3.2 Add `CraftsNeeded(targetQuantity, outputQuantity) => ceil(targetQuantity / outputQuantity)`
      and per-ingredient scaling (`ingredientQty * craftsNeeded`) as pure Core functions (`ScribeCraftMath`).

## 4. Core unit tests

- [x] 4.1 `Craft` codec round-trip (binary, JSON, TSV): kind, `TargetItemCode`, quantities, done flag,
      `Depth`, and `RecipeSignature` all survive.
- [x] 4.2 `Craft` clamp rules: `TargetQuantity` ≥ 1; `Depth` clamped to `[0, 1]`.
      (`CurrentQuantity` is clamped ≥ 0 only — carried overflow is meaningful per the real Tracker semantic;
      the spec's `[0, TargetQuantity]` upper cap was aspirational and does not match shipped behavior.)
- [x] 4.3 `IsCarriedCountTracked` true for `Tracker`/`Craft`, false for `Task`/`Text`/`Link`.
- [x] 4.4 Batch math: `ceil` boundaries (target divisible, target+1, target < output-per-craft) and
      per-ingredient scaling.
- [x] 4.5 Older-version read: a `Kind = 4` block degrades to `Task`; absent `RecipeSignature` reads empty.
- [x] 4.6 `dotnet test tests/Core.Tests` — all green (24/24 Craft tests; pre-existing brightness-curve
      failures are unrelated and fail on clean `main` too).

## 5. Subtasks (task-subtasks capability) — indent + grip-tap toggle

- [x] 5.1 Row rendering: indent a `Depth` 1 row (10px + 3%×PixelArtWidth per prior layout notes) in the
      shared row path; confirm it applies to every kind and every surface (Lectern, Notebook, Tablet,
      Scriptorium, Pinned HUD). (Editor/Read/Pinned use `style.SubtaskIndent = 10 + 0.03·W`, set in the
      dialog's RowStyle getter; the HUD applies the same `10 + 0.03·rowWidth` inset in `BuildRow` +
      `BuildFrozenGhost`. Pinned/HUD required carrying `Depth` on the pin snapshot — see 5.1a.)
- [x] 5.1a Pin snapshot carries `Depth`: added `ScribePinnedRef.Depth`, bumped `ScribePinCodec` to v5
      (progressive read defaults old blobs to 0 and clamps to `[0,1]`), threaded through `SetPin`,
      `ReconcileSnapshotsForActor`, `ScribeSetPinMessage.SnapshotDepth`, `SendSetPin`, and
      `SetPinForPlayer`. Covered by 3 new pin-codec tests (round-trip, v4→default, v5 clamp).
- [x] 5.2 In `ScribeEditorContent.cs`, add `onTap: _ => Widget.OnGripTap(index)` to the grip
      `GestureDetector` (alongside the existing `onPress`/`onRelease` drag wiring).
- [x] 5.3 Implement `OnGripTap(index)` to toggle the row's `Depth` between 0 and 1 (clamped one level),
      save through the dialog's normal path, and refresh the row. Confirm press-hold-drag still reorders
      and does not toggle depth (positional tap-vs-drag discrimination via `EventDispatcher`).
      (`OnGripTap` in `ScribeDialogBase.Editor.cs` flips `scratch` block depth, sets `isDirty`, `RebuildBody`;
      the dispatcher fires `OnTap` only for a genuine tap, so a drag never toggles — grip-tap is editor-only,
      indent renders everywhere.)
- [x] 5.4 Verify depth toggle works for every kind (Task/Note/Tracker/Link/Craft) and no depth-2 is
      reachable. (Depth toggle is kind-agnostic — it operates on `ScribeBlock.Depth`, which Core clamps to
      `[0,1]`, so depth-2 is unreachable by construction; in-game confirmation is 10.8.)

## 6. Craft generator + loose self-heal

- [x] 6.1 Add a mod-side recipe probe that, given an item code, enumerates its **grid** recipe variants:
      group by recipe group, collapse wildcard-material fan-out, filter disabled recipes and pure tool
      ingredients, and compute a stable recipe **signature** (output code + pattern + WxH) plus a
      distinguishing label per variant ("Recipe N" fallback). (`ScribeCraftRecipeProbe`: dedup by signature,
      matches via `ShowInCreatedBy` + `Output.ResolvedItemStack.Satisfies`; VS pre-fans-out variants so no
      manual `{var}` substitution — resolved codes are already concrete.)
- [x] 6.2 Add `ScribeModSystem.AddCraftFromHandbook(itemCode, recipeSignature)` mirroring
      `AddFromHandbook`: resolve the surface via the existing three-tier `AddFromHandbookCore`, create
      the `Craft` parent (output code, target 1, bound signature), then generate ingredient children.
- [x] 6.3 Generator: expand the bound recipe into children — one `Tracker` at `Depth` 1 per counting
      ingredient at `ingredientQty × craftsNeeded`; substitute `{var}` bindings to concrete codes;
      leave genuine wildcards broad; emit liquid ingredients as a non-counting `Text` note at `Depth` 1
      (or omit). Place children contiguously directly below the parent. (Via `ScribeDocument.AddCraft`
      + `ReconcileCraftIngredients`; probe returns per-craft ingredients + liquid notes, reconcile scales.)
- [x] 6.4 Loose self-heal: on target-change and on document open, reconcile the contiguous `Depth` 1 run
      below a `Craft` parent — match by item code, update matched children's `TargetQuantity`, create
      missing ones, NEVER auto-delete, NEVER descend past depth 1. Cover with a Core-testable
      reconciliation helper where the matching/scaling logic can live API-free. (Wired at 3 seams:
      `ApplyCraftHandbookAppend` create, `SetEditorTrackerTargetQuantity` target-change, `SelfHealCraftTasks`
      on editor-open; unresolvable signature degrades gracefully — parent stays, children untouched.)

## 7. Ingredient matching — wildcard/family resolution

- [x] 7.1 Extend `ScribeTrackerCounter.TryResolveIngredient` to resolve wildcard/family codes (e.g.
      `linen-*`, `bowl-*-fired`) in addition to concrete codes, so family ingredient children count the
      whole family via `SatisfiesAsIngredient(..., checkStackSize:false)`. (Wildcard branch builds an
      ingredient with `MatchingType = Wildcard` and skips `Resolve` — the wildcard match path uses
      `WildcardUtil.Match`, never `ResolvedItemStack`.)
- [x] 7.2 Keep the concrete-code path byte-identical (guard the family branch additively) so existing
      plain-Tracker counts do not regress; confirm `{var}`-bound children (concrete codes) still count
      only the concrete item. (Concrete path left untouched below the additive `Contains('*')` branch.)

## 8. Kind registry + Handbook links

- [x] 8.1 Register the `Craft` kind in `ScribeAddKinds` (`RequiresItemContext: true`, counts against the
      task cap, Handbook-only) — a bare footer click no-ops on a null code, like Tracker/Link.
      **Reconciled with D5 (authoritative):** D5 explicitly REJECTED a bare footer "Crafting Task" entry
      (no item context to bind a recipe to). So Craft is deliberately NOT added to `ScribeAddKinds.Live`
      (no footer picker option); its cap-counting is enforced directly in `ApplyCraftHandbookAppend`
      (`CanAddTaskUnderPolicy`), and creation is Handbook-only via `AddCraftFromHandbook` (bespoke path
      that carries the recipe signature, which the `Func<doc,code,bool>` registry delegate can't). A
      dead/broken registry entry would be misleading, so none was added.
- [x] 8.2 In `ScribeHandbookPatch.Postfix`, append one "Add Crafting Task" `LinkTextComponent` per grid
      recipe variant (single when N==1, none when the item has no grid recipe), each labeled by its
      distinguishing ingredient, dispatching to `AddCraftFromHandbook(itemCode, signature)`.
- [x] 8.3 Add lang strings: `scribe:scribe-gui-addcraft` (link label / "Add Crafting Task"),
      a per-variant label template, the `Craft` row label framing, and the liquid-note text.
      (`scribe-gui-addcraft`, `scribe-gui-addcraft-variant`, `scribe-gui-craft-liquid-note`,
      `scribe-gui-craft-recipe-ordinal`; the Craft row label framing lands with 9.1.)

## 9. Row rendering — Craft parent

- [x] 9.1 Render the `Craft` parent like a Tracker (icon + name + `have/need` counter + shortfall/
      satisfied states) with a distinct craft-intent icon or "Craft" label framing. Reuse the Tracker
      render helper; add the distinction as the only delta. Confirm on all surfaces. (All four surface
      row-state types — `ScribeEditRowData`, `ScribeReadRowData`, `ScribePinRowData`, `HudPinRow` — gained
      `IsCraft`, broadened `IsItemKind`/`IsCarriedCountTracked`, and a `scribe-gui-craft-row-label` "Craft
      {0}" `Label` framing; the item-render branch is shared with Tracker, so the label is the only delta.
      Pin Tab / HUD `ResolvePinItem` resolve a Craft parent's output via `TargetItemCode`.)
- [x] 9.2 Extend the carried-count scan gate from `IsTracker` to `IsCarriedCountTracked` so `Craft`
      parents update alongside their children; confirm completion-behavior gating covers count-tracked kinds.
      (Dialog `RecomputeTrackers` and HUD `RecomputeHudTrackers` both now scan `IsCarriedCountTracked` /
      `Kind is Tracker or Craft`; the rising-edge completion + `ApplyTrackerCompletion` path is shared, so a
      filled Craft parent follows the Tracker Completion setting exactly like a Tracker.)

## 10. Build, restage, verify

- [x] 10.1 `dotnet build src/Mod/Mod.csproj` — 0 errors, 0 warnings.
- [x] 10.2 `dotnet test tests/Core.Tests` — all green. (Craft + pin-depth suites all pass; the 7 failing
      `ScribeBrightnessCurveTests`/`IlluminationFloor` tests are pre-existing and fail on clean `main` too —
      unrelated to this change, per 4.6.)
- [ ] 10.3 `bash build/restage.sh Debug` (only while the client is quit).
- [ ] 10.4 In-game: open an item's Handbook page with one grid recipe → one "Add Crafting Task" link;
      an item with several variants → one labeled link each; an item with no grid recipe → no link.
- [ ] 10.5 In-game: click a crafting link → a `Craft` parent plus indented ingredient subtasks appear on
      the resolved surface at correct batch quantities; verify ceil math with an output-per-craft > 1.
- [ ] 10.6 In-game: raise the parent target → ingredient subtasks rescale in place (progress preserved);
      delete one child then re-edit target → it's recreated, others untouched, nothing auto-deleted.
- [ ] 10.7 In-game: carry ingredients → children count families (wildcard) and concrete `{var}` codes
      correctly; parent counts the output; completion follows the Tracker Completion setting.
- [ ] 10.8 In-game: tap a grip → row indents/promotes (any kind); press-hold-drag still reorders; no
      depth-2 reachable. Verify on Lectern, Notebook, Tablet, Scriptorium, and the Pinned HUD.
- [ ] 10.9 In-game: verify a liquid-ingredient recipe (e.g. poultice) surfaces the liquid as a
      non-counting note (or omits it), not a broken counting row.
