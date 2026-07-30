## Context

`HudScribePins.ApplyAnchor()` is called from `OnRenderGUI` every frame (~60fps). It reads
two screen-dimension API calls, two settings fields, one `capi.Settings.Bool` lookup, and
runs two switch expressions to compute `WindowPos`. The result is written back to
`WindowPos` unconditionally — even if the window, settings, and screen size are all
unchanged from the prior frame.

`HudScribePins.OnTick()` fires every 250ms. Its body iterates `pendingCompletions` (a
`Dictionary<(Guid, Guid), PendingCompletion>`) to find expired entries. When no
completions are in flight the dictionary is empty and the loop body never executes — but
the iteration itself (allocation-free but not zero-cost) still runs.

VS provides no `WindowResized` event on `IClientEventAPI`. LibGUI's own
`GuiBase.OnRenderGUI` already reads `FrameWidth/FrameHeight` every frame for
`ClampWindowToScreen`, so we cannot avoid those reads at the framework level. What we
can avoid is the downstream math and `WindowPos` write when inputs are unchanged.

## Goals / Non-Goals

**Goals:**
- Reduce per-frame work in `ApplyAnchor` to a cache-key comparison when inputs are stable.
- Eliminate the `pendingCompletions` iteration in `OnTick` when no completions are in flight.

**Non-Goals:**
- Removing the `OnRenderGUI` override entirely (still needed for collapse cleanup).
- Changing tick interval or moving to an event-driven completion system.
- Any changes outside `HudScribePins.cs`.

## Decisions

### 1. Anchor cache — struct key

Cache the six inputs that determine the anchor position:

```csharp
private record struct AnchorInputs(
    float ScreenW, float ScreenH,
    ScribeHudAnchor Anchor, float OffX, float OffY, bool MinimapOn);

private AnchorInputs? _lastAnchorInputs;
```

At the top of `ApplyAnchor`, compute the key and compare to `_lastAnchorInputs`. If equal,
return immediately. If different, update the cache and proceed with the existing math.

The cache is invalidated automatically whenever any of:
- Screen dimensions change (detected by comparing `FrameWidth/FrameHeight` on every call)
- Settings change (already triggers `ForceRebuild` via `OnMyPinsChanged`, but
  `_lastAnchorInputs` simply won't match on the next frame because `Anchor/OffX/OffY` will differ)
- Minimap visibility toggles (caught by the `minimapOn` field in the key)

No explicit invalidation needed — the key comparison handles all cases.

**Alternative considered:** subscribe to a settings-changed event and call `ApplyAnchor`
only there. Rejected because VS has no `WindowResized` event, so a screen resize would
still be missed until the next settings change. The cache approach handles both.

### 2. Tick guard — early return on empty

```csharp
private void OnTick(float dt)
{
    if (pendingCompletions.Count == 0) return;
    // ... existing body
}
```

Zero behavior change. `pendingCompletions` is only non-empty during the 1.5s undo window
after a pin checkbox is tapped — the overwhelming majority of ticks see an empty dictionary.

## Risks / Trade-offs

**Stale cache on rapid successive changes**: If screen width and anchor offset both change
in the same frame, the cache misses once and self-corrects. Not a problem.

**Record struct equality**: `record struct` uses value equality by default — all six fields
compared. Floats compared with `==`; because we read `FrameWidth/FrameHeight` and divide by
the same `GUIScale` each call, equal inputs produce bitwise-identical results and the
comparison is safe.
