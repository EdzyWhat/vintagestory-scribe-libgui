## Context

Two independent Pinned-tab fixes bundled for v1.3.3.

**Pin placement.** `ScribePinOrdering.PlaceNewPin` (`src/Core/ScribePinOrdering.cs`) is pure/Core and
already handles the crafting-cluster geometry correctly:
- depth-0, no relation → append
- depth-1, parent pinned → insert right after the parent's contiguous owned-run cluster
- depth-1, parent not pinned → append
- depth-0 pin whose already-pinned children exist → append, then `GatherOwnedRunChildren` moves those
  children to sit right after it (this already covers "pin the parent later, child re-parents" — no
  new logic needed there, confirmed by reading the code and the existing
  `PlaceNewPin_PinningParent_GathersChildrenPreservingRelativeOrder` test in
  `tests/Core.Tests/ScribePinOrderingTests.cs`).

Every "append" above is a "no positional preference expressed" fallback. The new **Pin Insert**
setting (Top/Bottom) only needs to change what "append" means in those branches — it must NOT touch
the parent-cluster branch (a subtask never jumps away from its pinned parent) or the child-gathering
logic (already correct).

**Pause visibility.** Confirmed via decompiling `VintagestoryLib.dll`
(`Vintagestory.Client.NoObf.ClientMain.MainRenderLoop`) that `eventManager.TriggerGameTick(...)` — the
dispatcher for every `capi.Event.RegisterGameTickListener(...)` — runs only `if (!IsPaused)`, while
`TriggerRenderStage` (and therefore LibGUI's own per-frame render/animation pass) runs unconditionally.
Traced the pin-arrival path (`SendSetPin` → `OnServerReceivedSetPin` → push → `MyPinsChanged` →
`HudScribePins.OnMyPinsChanged` → `RebuildHudBody()`) and found it is entirely event/render-driven,
not tick-driven — no `RegisterGameTickListener` or un-flagged `RegisterCallback` in that specific path.
So the mechanism that broke `TrackerCount.OnTrackerSlotModified` under pause (documented in
VSAPI-NOTES.md) does not obviously apply here. Static reading could not find the blocker; this design
treats the fix as **unknown pending a repro trace**, not a foregone conclusion.

## Goals / Non-Goals

**Goals:**
- A new pin with no pinned-parent relationship lands at the player's chosen Top/Bottom edge.
- A pinned subtask's relationship to its pinned parent is never affected by the Top/Bottom setting.
- The HUD visibly reflects a pin-set change while the game is paused, with a concrete before/after
  confirmed by an in-game trace before landing the fix.

**Non-Goals:**
- Reworking the crafting-cluster/owned-run placement logic — it already does the right thing.
- A "Top" option for where a subtask attaches under its parent — that's always immediately after the
  cluster, never separately configurable.
- Fixing *every* `RegisterGameTickListener`-under-pause hazard in the mod — only the ones the trace
  shows are actually on the HUD pin-arrival path stay in scope here.

## Decisions

### D1: Distinct `ScribePinInsert` setting, not reuse of `ScribeNewTaskInsert`
Confirmed with the user directly. `NewTaskInsert` governs where a brand-new task block is created
inside one document's block list; `PinInsert` governs where a pin reference lands in the cross-document
pin list. Coupling them risks a newly-pinned crafting child jumping to the Top of the *whole list* if a
player sets New Task Insert to Top for editing but never considered its effect on pins. A sibling enum
(`ScribePinInsert : byte { Top, Bottom }`) and `ScribePlayerSettings.PinInsert` property, mirroring
`ScribeNewTaskInsert`/`NewTaskInsert` exactly (including a `NormalizePinInsert` fallback), keeps the two
concerns independently tunable.

**Default: Bottom**, not Top. Unlike `NewTaskInsert` (which defaults Top), this setting's default must
match today's shipped always-append behavior so existing players see zero change until they opt in —
changing the default pin order out from under players on an otherwise-invisible settings migration
would read as a bug, not a feature.

### D2: `PlaceNewPin` takes the edge as a parameter, not a global read
`PlaceNewPin` lives in `src/Core/`, which must stay Vintage-Story-API-free and is unit-tested standalone
(`ScribePinOrderingTests.cs`). It cannot read `ScribePlayerSettings` itself without a dependency
inversion; instead its signature gains a `ScribePinInsert insertEdge` parameter, and the two no-relation
append sites become `if (insertEdge == ScribePinInsert.Top) pins.Insert(0, newPin); else pins.Add(newPin);`
The Mod-layer caller (`ScribeDialogBase`'s pin-add path, near the existing `SendSetPin` call sites)
passes `modSystem.MySettings.PinInsert`. This mirrors how `ScribeDocument.InsertIndex(ScribeNewTaskInsert)`
already takes its edge as a parameter rather than reading settings itself.

### D3: Pause-visibility fix is deferred to a trace, not designed blind
Given static analysis found no blocker in the actual code path, committing to a specific mechanism
(e.g. copying `TrackerCount`'s `IsGamePaused` + `permittedWhilePaused: true` pattern) risks fixing the
wrong thing or adding dead defensive code. Instead: add temporary trace logging at each step of the pin
push → HUD rebuild path (mirroring the project's `TraceHudRows` precedent from the earlier HUD
stale-controller bug), reproduce in-game (pause via Handbook auto-pause in singleplayer, pin a task from
an already-open Notebook/Lectern), and read the trace to find where — if anywhere — it actually stalls.

**Resolved by trace (2026-08-29).** Client trace (`[scribe-pin-push]`/`[scribe-hud-rebuild]`) showed
every push during a pause window logging `paused=False` — because there simply were no pushes during
the pause window at all. Cross-checking `server-main.log`'s `[scribe] set-pin received` lines against
the client's own `Client pause state is now on/off` lines confirmed it: the server received *nothing*
while paused, then received every queued `set-pin` in a burst the instant the client unpaused. Root
cause: in singleplayer the embedded server is hosted in-process and its own tick/dispatch halts while
the Handbook holds the client paused, so `ScribePinStore.SetPin` simply doesn't run until unpause — no
matter how promptly the client sent the packet. This was already known and half-solved: the
`ScribeDialogBase.optimisticPin` overlay (doc comment, add-tracker-link-tasks 7.11b) exists *specifically*
for this reason, applied to that dialog's own rows so at least the row you clicked updates instantly. The
HUD and Pin Tab had no equivalent, so they were exactly the surfaces left stuck until unpause — which is
what the player-visible symptom actually was.

**The fix** generalizes that existing, already-shipped pattern instead of inventing a new one: the
optimistic overlay moves from `ScribeDialogBase` (per-dialog, add/remove only reflected in that dialog's
own rows) up to `ScribeModSystem` (session-wide), where `MyPins` — the single list the HUD, the Pin Tab,
*and* every dialog's row tint all already read — applies it before returning. `SendSetPin` records the
optimistic pin/unpin (with the same snapshot fields it already sends over the wire) before the packet
goes out; an optimistic ADD is placed via `ScribePinOrdering.PlaceNewPin` — the exact function the server
uses — so a subtask pinned under an unconfirmed parent still clusters correctly even before either pin is
confirmed. `ScribeDialogBase`'s own `optimisticPin` dict and `RepaintPinsOptimistically` became redundant
and were deleted; `IsPinnedForMe` now delegates straight through to the same overlay-aware
`ScribeModSystem.IsPinnedForMe`. No `IsGamePaused` check was needed anywhere — the fix is "stop waiting on
the server for display," not "detect pause and special-case it."

## Risks / Trade-offs

- [Risk] The Pin Insert default (Bottom) diverges from New Task Insert's default (Top), which could
  read as inconsistent in the Settings UI. → Mitigation: label and group the two dropdowns together in
  `ScribeSettingsContent.cs` with distinct captions ("New Task Insert" / "Pin Insert") so the difference
  reads as two separate choices, not a mismatched pair; note the differing default's rationale (no
  silent reorder of existing pins) directly in the dropdown's tooltip/help text if the settings form
  supports one.
- [Risk] The pause-visibility issue may not reproduce cleanly (intermittent, like the earlier HUD
  stale-controller bug), burning trace time with no clear signal. → Mitigation: cap the investigation —
  if a clean trace can't be captured in a reasonable session, document the attempted repro steps and
  park it (matching the project's existing "diagnose via repro, don't theorize" discipline for
  the white-flash bug), rather than shipping a speculative fix.

## Open Questions

- ~~Should `PlaceNewPin`'s no-relation fallback branches (null source, unresolvable task index) also
  respect `insertEdge`?~~ Resolved during implementation: yes, all four no-relation branches (null
  source, unresolvable index, unrelated depth-0, depth-1 with unpinned parent) go through one shared
  `InsertAtEdge` helper.
- ~~Does the settings form support per-control help text/tooltips?~~ Resolved: yes,
  `LabeledControl` already surfaces a per-setting `scribe:<key>-help` tooltip on hover; used it to
  call out the differing default directly (`scribe:settings-pininsert-help`).
- A new wire-format concern surfaced during implementation, not anticipated in the original design:
  pin placement is decided server-side (`ScribePinStore.SetPin`), but `PinInsert` is a client-local
  setting, so it has to travel in `ScribeSetPinMessage` the same way `ScribeCompleteTaskMessage`
  carries completion policy. Resolved by making `ScribePinInsert.Bottom = 0` (not `Top`, unlike
  `ScribeNewTaskInsert`) so an old client that never sends the field lands on the correct legacy
  always-append behavior by construction — see the remarks on `ScribePinInsert`.
