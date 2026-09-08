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

## Second consumer (added 2026-09-07): `ScribeDialogBase.ShouldReceiveMouseEvents`

**Complaint:** clicking a Quest Link/Handbook link inside an open Scribe dialog (Notebook, Lectern,
Tablet, ...) opens a vanilla window on top of it — but a click meant for that now-visually-on-top
vanilla window still lands on Scribe underneath instead. The user's own proposed fix was to "unfocus"
Scribe's dialog whenever it opens a link, on the theory that focus is what's letting Scribe keep
intercepting the click.

**That theory was investigated and does not hold.** Decompiling `GuiManager.RequestFocus`
(`VintagestoryLib.dll`) confirms it unconditionally calls `UnFocus()` on every OTHER loaded dialog
before focusing the requester:
```csharp
internal void RequestFocus(GuiDialog dialog) {
    ...
    foreach (GuiDialog item in game.LoadedGuis.Where(d => d != dialog).ToList()) item.UnFocus();
    dialog.Focus();
}
```
Every vanilla-dialog open path this project uses (`capi.LinkProtocols["handbook"]` for Handbook pages,
`ScribeProgressionFrameworkQuestCatalog.TryOpenQuestLog`'s `dialog.TryOpen()` for Progression
Framework's Ledger) ultimately calls `GuiDialog.TryOpen(withFocus: true)`, which calls
`capi.Gui.RequestFocus(this)` — so Scribe's `Focused` flag is already flipped to `false` for free the
moment the vanilla dialog opens. There is no stale-focus bug to fix by adding a manual `UnFocus()` call.

`GuiBase.IsBlockedByFrontDialog` (LibGUI's own click-yielding check — `if (Focused) return false;`,
else scan `capi.Gui.OpenedGuis` for another focused `DialogType.Dialog` whose composer bounds contain
the click point) is exactly the mechanism that SHOULD make an unfocused Scribe yield to a focused
vanilla dialog covering it. Per this project's own `VSAPI-NOTES.md` finding (also documented at
`ScribeDialogBase.DrawOrder`'s doc comment), the deeper issue is that vanilla Cairo/GL dialogs and
LibGUI/Skia windows render through genuinely separate pipelines (`PostSkiaPipeline` flushes before
`GuiManager`) — a mismatch this codebase already diagnosed as the root cause of Scribe-vs-vanilla
stacking problems generally, and already deliberately parked as unfixable without patching the engine
or LibGUI's compositing. Whether `IsBlockedByFrontDialog`'s bounds check reliably agrees with a vanilla
dialog's actual on-screen composer bounds across that pipeline boundary was not confirmed, and isn't
worth chasing further given the parked status of the underlying mismatch.

**Decision: reuse this change's existing guard at dialog-mouse-dispatch granularity instead.**
`ScribeDialogBase.ShouldReceiveMouseEvents()` — the method `GuiManager.OnMouseDown`'s dispatch loop
checks BEFORE calling a dialog's own `OnMouseDown` at all — now overrides to
`base.ShouldReceiveMouseEvents() && !ScribeVanillaDialogGuard.IsAnyVanillaDialogOpen(capi)`. This
sidesteps the entire focus/bounds/pipeline question: while any vanilla dialog is open, Scribe simply
never gets asked to hit-test a click, so it categorically cannot swallow one meant for something else.
This is the same "decline rather than arbitrate" posture the guard's first consumer
(`GuiDialogScribeQuestPrompt`) already uses, generalized from "don't open a new modal I'd lose to" to
"don't contest any click while a vanilla dialog is open."

**Alternative considered**: add a manual `UnFocus()` call at each link-open call site. Rejected —
confirmed above to be a no-op layered on top of behavior VS's engine already performs; it would not
have changed anything.

**Alternative considered**: patch/extend `IsBlockedByFrontDialog` to explicitly special-case vanilla
dialogs. Rejected — that method lives in the `gui` mod (a hard dependency, not this project's own
code); patching it would mean forking or Harmony-patching LibGUI itself, a materially larger and
riskier change than reusing an existing, already-accepted guard this project already owns.

**Scope**: placed on `ScribeDialogBase` (not the standalone `ScribeSettingsDialog`/
`ScribeGearTuningDialog`, which have no links) so every dialog host that can render a Link/Quest Link
row — Notebook, Lectern, Tablet, Chalkboard, Task Notice, Assignment Desk — is covered by one shared
override, matching this project's established "one shared point of truth" preference over per-surface
duplication.

**Risk (same accepted trade-off as the guard's first consumer)**: any vanilla dialog open anywhere on
screen, even one not visually overlapping Scribe, makes Scribe stop receiving clicks until it closes.
No per-pixel overlap check is added, for the same complexity-vs-benefit reasoning the original design
already applied.

### Correction (2026-09-07): the blanket `ShouldReceiveMouseEvents` override made Scribe unclickable

A playtest of the above landed on `ShouldReceiveMouseEvents()` — confirmed the wrong lever. That method
takes no click position at all (`GuiManager.OnMouseDown` calls it once, per-dialog, before any hit-test
runs), so "decline while any vanilla dialog is open" was necessarily global: Scribe stopped receiving
mouse events ENTIRELY for as long as any vanilla dialog stayed open, even for a click landing squarely
on Scribe's own window, nowhere near the vanilla one. There was no way to click back into Scribe short of
closing the vanilla dialog first — the opposite of "click back on Scribe to regain focus," which the user
explicitly wants preserved.

**Fix: move the check into `OnMouseDown` instead, which DOES receive the click's `X`/`Y`.**
`ScribeDialogBase.OnMouseDown` now reads:
```csharp
public override void OnMouseDown(MouseEvent args) {
    if (args.Handled) return;
    if (ScribeVanillaDialogGuard.IsVanillaDialogAt(capi, args.X, args.Y)) return;
    base.OnMouseDown(args);
}
```
`ScribeVanillaDialogGuard.IsVanillaDialogAt(capi, x, y)` (new sibling of `IsAnyVanillaDialogOpen`) checks
whether the point falls inside any open vanilla (non-`GuiBase`, non-HUD) dialog's own composer `Bounds` —
the same `composer.Bounds.PointInside(x, y)` check `GuiBase.IsBlockedByFrontDialog` already uses for
LibGUI-vs-LibGUI arbitration, generalized to vanilla dialogs. Declining only when the CLICK POINT is
inside a vanilla dialog's bounds — rather than whenever one merely exists open somewhere — means a click
anywhere else on Scribe's own window falls through to `base.OnMouseDown` normally, hits Scribe's own
composer, and lets `GuiManager` call `RequestFocus` on Scribe exactly as it always did. Clicking back on
Scribe regains focus and interactivity immediately, regardless of whether the vanilla dialog is still open.

**Not gated on `Focused`.** Unlike `IsBlockedByFrontDialog` (which only yields to another FOCUSED LibGUI
dialog), `IsVanillaDialogAt` checks EVERY open vanilla dialog's bounds regardless of its focus state. This
is deliberate, not an oversight: per this project's own confirmed finding (`VSAPI-NOTES.md`, also at
`ScribeDialogBase.DrawOrder`'s doc comment), a vanilla Cairo/GL dialog ALWAYS paints on top of a LibGUI/
Skia surface wherever they visually overlap, regardless of focus or `DrawOrder` — so bounds alone, not
focus, correctly decide whether Scribe is visually underneath at a given point.

Tasks 3.2-3.5 are unchanged in intent (still verifying "a click on the vanilla dialog reaches it, not
Scribe"); 3.4 additionally now covers the corrected requirement directly (clicking Scribe itself must
still work immediately, vanilla dialog open or not) rather than only "after closing the vanilla dialog."
