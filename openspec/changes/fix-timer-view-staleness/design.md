## Context

`GuiDialogClockmakerNotebook` rebuilds its Timer tab only when the timer status differs from `_lastKnownStatus`,
which starts as `Idle` for every new dialog instance. A dialog opened while the timer is Fired or Running builds
the right view but believes the status is `Idle`, so the later push to `Idle` compares equal and is ignored.

`HudScribePins.ApplyAnchor` returns early when its inputs match `_lastAnchorInputs`. Only `OnMyPinsChanged` resets
the cache; the timer open path (`OnMyTimerChanged`) does not. A timer-only HUD has the same window size each time,
so after close and reopen the cache can say there is nothing to do.

## Goals / Non-Goals

**Goals:** the Timer tab and HUD match the live timer in every open/close order.

**Non-Goals:** timer prefill and the clear button (a separate change); changing server timer semantics.

## Decisions

- **Seed the tracked status from the live timer when the dialog opens**, rather than rebuilding on every push. The
  status-transition rule is kept so mid-run pushes still skip the rebuild. The decision of "does a push need a
  rebuild" moves to a small pure function so a Core test can cover the transitions.
- **Reset the anchor cache when the HUD opens** rather than on every timer change, so the layout work stays cached
  during normal updates.
- **Confirm the HUD cause first.** The anchor-cache explanation is inferred from code; a debug trace of
  `ApplyAnchor` on timer open confirms it before the fix is kept. If the trace shows another cause, this part is
  re-scoped.

## Risks / Trade-offs

- Seeding the status also changes when the gearworks engage/lock timestamps are stamped; opening on a Fired timer
  must not replay the lock animation as a new transition. Check this in the playtest.
