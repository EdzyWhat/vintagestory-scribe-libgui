## Context

See proposal.md - Why. The precise mechanism, confirmed by decompiling `VintagestoryLib.dll`
(`Vintagestory.Client.NoObf.GuiManager`):

```csharp
public override void OnMouseDown(MouseEvent args)
{
    foreach (GuiDialog item in game.LoadedGuis.ToList())
    {
        if (!item.ShouldReceiveMouseEvents()) continue;
        item.OnMouseDown(args);
        if (args.Handled) { RequestFocus(item); break; }
    }
}
```

Dispatch is a plain walk of `LoadedGuis` in list order, stopping at the first dialog that sets
`Handled`. This is **unrelated to what is actually painted on top** (rendering separately walks
`Enumerable.Reverse(OpenedGuis)`, and vanilla Cairo/GL dialogs paint after LibGUI's shared Skia
surface regardless of either list's order — the pipeline-ordering fact already documented in
`VSAPI-NOTES.md`, confirmed not fixable within LibGUI 3.1.0 and explicitly not being re-attempted
here per the user's direction). So there are two independent orderings (paint order, and
`LoadedGuis` list order for clicks) with no guarantee they agree, and no per-click way for Scribe to
know it's "under" something without inspecting the other open dialogs itself.

Patching `GuiManager.OnMouseDown` directly (Harmony) was considered and rejected: it is the single
dispatch point for every mouse click in the entire game, for every dialog of every mod — a defect
there has an unbounded blast radius, categorically different from this codebase's existing Harmony
patches (each scoped to one behavior/type, e.g. `ItemSlotOverlay`, `CollectibleBehaviorHandbookTextAndExtraInfo`).

## Goals / Non-Goals

**Goals:**
- Scribe can reliably ask "is a vanilla dialog open right now" and use that to avoid contesting a
  click or opening a new modal-style surface it would visually lose to.

**Non-Goals:**
- Correct z-order arbitration in general (Scribe winning a click when it's genuinely on top of a
  vanilla dialog) — not achievable without touching the engine's dispatch loop (rejected above) or
  LibGUI's own compositing (already spiked and parked, per `VSAPI-NOTES.md`, and explicitly not being
  re-attempted per the user's direction).
- Patching `GuiManager` or any other engine-internal dispatch/render method.
- Retroactively fixing every existing Scribe click-handling site to consult this guard — this change
  only adds the check; consuming it is each caller's own decision (Change 4 is the first consumer).

## Decisions

**A pure, read-only, static check over `capi.Gui.OpenedGuis`, not a Harmony patch.** `OpenedGuis` is
public (`IGuiAPI.OpenedGuis : List<GuiDialog>`); filtering it to "not `Gui.GuiBase`, not
`DialogType == HUD`" needs no reflection and no engine patch. This mirrors the exact filtering
precedent already in `VSAPI-NOTES.md` for finding the Handbook dialog by type
(`capi.Gui.OpenedGuis.FirstOrDefault(d => d.ToggleKeyCombinationCode == "handbook")`), generalized
from "find one specific dialog" to "is there any dialog NOT of a known-safe category."

**"Vanilla" is defined by exclusion (not `GuiBase`, not HUD), not by an allow-list of known dialog
types.** An allow-list (Handbook, Inventory, chest, PF's Ledger, ...) would need updating for every
new vanilla dialog Scribe might ever coexist with (any future quest mod, any block's own GUI, etc.).
Exclusion is self-maintaining: anything that isn't LibGUI and isn't a HUD overlay is, by definition,
something Scribe cannot out-paint.

**A standalone static helper, not a `ScribeDialogBase` method.** Change 4's Popup notification is
HUD-driven (`HudScribePins`), not a `ScribeDialogBase` subclass — putting the check on the dialog base
class would make it unreachable from exactly the first real consumer.

## Risks / Trade-offs

- **[Risk]** The exclusion filter could misclassify a dialog neither LibGUI nor HUD but also not
  visually blocking (e.g. a dialog anchored to a corner that doesn't overlap Scribe's surface at all)
  — the guard would report "vanilla open" and a caller might over-suppress. → **Mitigation**: accepted
  as a deliberate false-positive bias — declining to open a modal a little too often is a minor UX
  cost; consuming a click meant for something else is the actual hazard this change exists to avoid.
  Per-pixel bounds-overlap checking was considered and rejected as unnecessary complexity for the
  first consumer's need (see design's Non-Goals).
- **[Risk]** `Gui.GuiBase` type-checking requires a compiled reference to the `gui` mod — already a
  hard dependency of this project (modid `gui`), so no new dependency risk.

## Testing Note

Tasks originally called for Atlas coverage of the guard. Confirmed during implementation that
Atlas is a headless **server-only** harness — no `ICoreClientAPI`, no `OpenedGuis`, no rendering
of any kind ("No client, no window" per its wiki; real network/rendering clients are explicitly
out of its roadmap). `Core.Tests` can't cover it either, since the helper is Mod-layer and needs
the VS client API. This check has no automatable coverage path in this project's current tooling;
verification is the manual playtest (tasks 2.3-2.5) only.

## Migration Plan

Purely additive, client-only, no persisted state. Rollback is a plain revert.

## Open Questions

None — this change's scope is deliberately narrow (the check itself); how each future caller uses it
is that caller's own design question.
