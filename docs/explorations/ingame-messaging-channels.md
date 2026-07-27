# Exploration: in-game player-messaging channels

**Status:** exploring (not yet an OpenSpec change). Created 2026-07-26.

> **Temporary holding pen.** OpenSpec has no native "exploration" artifact — the `spec-driven`
> schema defines only proposal / specs / design / tasks. This is a plain repo doc, kept only
> while the exploration is still fluid. When a change is opened, its convergent content migrates
> into that change's `proposal.md` and `design.md` and **this file is deleted**. Do not treat it
> as a durable spec. (Same convention as `lectern-row-list-rework.md`.)

## The question

How does Vintage Story surface short error/guidance messages to the player, and which channel
should Scribe use for which *intent*? Prompted by the roadmap's open item **"In-game user
feedback / error surface"** (requested 2026-07-24): the server silently rejects an oversized
edit (over `ScribeDocumentCodec.MaxBlocks`/`MaxTextLength`) and the client shows nothing.

Seed evidence (from the player, confirmed in the DLLs): the vanilla **Small Water Wheel** shows
two messages — `"Rotation blocked by another mechanical block!"` and `"An obstruction would block
the finished wheel from rotating!"` (the latter captured in a screenshot, rendered as vibrating
red center-screen text above the hotbar). These two turned out to arrive by **two different
paths** into the **same** HUD element, which is what unlocked the whole map below.

## The mechanism: one HUD family, reached two ways

Both vanilla messages resolve to the client HUD element **`HudIngameError`** (in
`VintagestoryLib.dll`, `Vintagestory.Client.NoObf`), which listens to the
`ICoreClientAPI.Event.InGameError` event.

```
                    ┌─────────────────────────────────────────────┐
                    │  HudIngameError  (client HUD element)         │
                    │  • red hover-text, center screen              │
                    │  • vibrates first 500ms, hard fade at 5000ms  │
                    │  • listens to capi.Event.InGameError          │
                    └───────────────────▲─────────────────────────┘
                                        │ InGameError event
                    ┌───────────────────┴─────────────────────────┐
                    │  ICoreClientAPI.TriggerIngameError(           │
                    │      object sender, string errorCode,         │
                    │      string text)                             │
                    └───────────────────▲─────────────────────────┘
                                        │
        ┌───────────────────────────────┴────────────────────────────┐
        │                                                             │
  ┌─────┴──────────────────────┐                    ┌─────────────────┴─────────────┐
  │ PATH A — Placement failure  │                    │ PATH B — Explicit trigger     │
  │                             │                    │                               │
  │ Block.CanPlaceBlock /       │                    │ Your code calls               │
  │ TryPlaceBlock               │                    │   capi.TriggerIngameError(...) │
  │   → ref string failureCode  │                    │   directly, any time.          │
  │                             │                    │                               │
  │ Engine (SystemMouseInWorld- │                    │ = "error-wheel-blocked",       │
  │ Interactions) auto-shows:   │                    │   fired at the wheel's own     │
  │   Lang.Get("placefailure-"  │                    │   rotation-check moment.       │
  │            + failureCode)   │                    │                               │
  │ = "rotationblocked"         │                    │                               │
  └─────────────────────────────┘                    └───────────────────────────────┘
```

- **Path A** (`placefailure-rotationblocked`): you only return a `failureCode` string from
  `CanPlaceBlock`. The engine prefixes `placefailure-`, does the `Lang.Get`, and fires the error.
  Zero HUD code on your side — but *placement-time only*.
- **Path B** (`error-wheel-blocked`): the block calls `TriggerIngameError` directly. Full control
  — any moment, any message, your own error-code namespace.

Confirmed call site for Path A (decompiled `SystemMouseInWorldInteractions`):

```csharp
if (failureCode != null && failureCode != "__ignore__")
{
    game.eventManager?.TriggerIngameError(this, failureCode, Lang.Get("placefailure-" + failureCode));
}
```

`HudIngameError` behavior (decompiled): shows for **5000ms**, **vibrates** only during the first
500ms (random ±5px jitter scaled by GUI scale), **clobbers** any currently-showing message (no
queue), always the error style.

## The graceful sibling: `TriggerIngameDiscovery`

`ICoreClientAPI` also exposes **`TriggerIngameDiscovery(object sender, string errorCode, string
text)`** → `Event.InGameDiscovery` → the **`HudIngameDiscovery`** HUD. Same shape, deliberately
different design language (decompiled):

| Property        | `HudIngameError`              | `HudIngameDiscovery`                         |
|-----------------|-------------------------------|----------------------------------------------|
| Color           | Red                           | `GuiStyle.DiscoveryTextColor` (warm off-white) |
| Font            | Standard                      | `GuiStyle.DecorativeFontName` (serif)        |
| Motion          | Vibrate 500ms                 | Gentle fade in (250ms) / fade out (last 1s)  |
| Duration        | 5000ms                        | 6000ms                                       |
| Multiple msgs   | **Clobbers**                  | **Queues** (`Queue<string>`, shows in turn)  |
| Position        | Center, above hotbar          | `CenterMiddle`, offset **-155px** (achievement-style) |
| Tone            | "you can't / it failed"       | "you discovered / it worked"                 |

So the engine already ships a **matched pair**: an alarm channel and a positive/neutral channel,
both free, both native, both reached by a one-line client call.

## LibGUI in-dialog options (the third altitude)

For messages tied to a *specific field* rather than the whole screen, LibGUI (our GUI framework,
hard dep `gui`) offers in-dialog surfaces — confirmed present in `reference/vslibgui/`:

- **Tooltip** widget (has its own `TooltipTests.cs`) — on-demand, hover-gated per control.
- **`GuiGlobalOverlay`** — for overlay content composited with the dialog.
- An **inline notice** (ordinary text/label widget placed near a field) — ambient, always
  visible while the dialog is open, not hover-gated.

## The taxonomy that falls out (candidate Scribe policy)

The channels aren't competitors; they're keyed to **intent**. A clean routing rule:

```
  Is it a FAILURE the player must notice?          → TriggerIngameError     (red HUD)
  Is it a SUCCESS / state-change confirmation?     → TriggerIngameDiscovery (warm HUD, queues)
  Is it a LIMIT / rule tied to a specific field?   → LibGUI inline notice    (in-dialog, ambient)
  Is it "what does this control do?"               → LibGUI tooltip          (in-dialog, on hover)
```

| Channel                   | Tone              | Persistence      | Best-fit Scribe case                         | Cost     |
|---------------------------|-------------------|------------------|----------------------------------------------|----------|
| `TriggerIngameError`      | Alarm (red)       | 5s, clobbers     | "Edit **rejected** — over the size limit"    | ~free    |
| `TriggerIngameDiscovery`  | Positive/neutral  | 6s, fades, queues| "Task pinned", "Note saved" (if wanted)      | ~free    |
| LibGUI inline notice      | Contextual        | you control      | "~1000-char soft limit" beside the field     | moderate |
| LibGUI tooltip            | On-demand         | while hovering   | per-control help (pin/grip/delete icons)     | moderate |

## Why this matters for the roadmap item

- The two *hard* problems both resolve to **native HUDs, not custom LibGUI work**:
  - **Rejected oversized edit** (server-authoritative) → after the existing server→client
    round-trip, the client calls `TriggerIngameError("scribe-toolong", Lang.Get("scribe:error-…"))`.
    No new HUD element, **no ToastLib** (already rejected on the roadmap), no new dialog.
  - Any **save/pin confirmation** → `TriggerIngameDiscovery`, same one-liner.
- The **soft** limit (~1000 chars) is the one case that genuinely wants **in-dialog** treatment:
  it's about a specific text field, so a center-screen flash is the wrong altitude. An inline
  notice beats a tooltip here because it's ambient (always visible near the field) rather than
  hover-gated.

## Open question / spike before committing

- **HUD z-order over an open LibGUI dialog.** Cannot be fully settled from the DLLs alone.
  `HudIngameError` uses default HUD z-order; a LibGUI lectern dialog may composite on top. **If
  the red text is occluded while a dialog is open**, then the *rejected-edit* case must also fall
  back to an in-dialog notice (and only genuinely world-context errors would use the HUD).

  **Partial finding (player observation, 2026-07-26):** the error HUD does **not** appear to
  render over other windows — an **opened Handbook covers the error message**. This is
  suggestive but **not yet conclusive**, for two reasons:
  1. *Ordering confounder.* The Handbook was opened *after* the error context, so it may simply
     be later in the composite stack (opened-last = on-top) rather than the error HUD being
     categorically behind dialogs. A clean test forces the error *while a dialog is already open*.
  2. *Vanilla never exercises this.* Every vanilla trigger for `HudIngameError` (placement
     failures, mechanical-block errors) fires from **world interaction, with no GUI open** — so
     the game itself never has to render this HUD over a dialog. That means the occlusion behavior
     is essentially untested territory in vanilla, and whatever we observe is our own new case.

  → Remaining test to make it conclusive: open the **lectern** dialog, *then* force a
  server-rejected edit, and see whether the red text shows through/over the LibGUI window. If it's
  occluded there too, treat the HUD as "world-context only" and route all *in-dialog* failures to
  an inline notice instead.

## If this ever becomes a change (not now)

A possible shape (recorded for later, deliberately *not* proposed):

- A small client-side `IScribeMessenger` seam with intent verbs — `Error(code)`,
  `Confirm(code)`, `Notice(field, text)` — mapping intent → channel per the policy above, so
  future tiers pick a verb and never re-decide the mechanism.
- First consumer: wire the rejected-edit case end-to-end (the concrete roadmap item).
- Gate on the z-order spike above.

## Key source pointers (for whoever picks this up)

- `ICoreClientAPI.TriggerIngameError` / `TriggerIngameDiscovery` — `VintagestoryAPI.dll`
  (interface + XML doc-comment: *"HudIngameError registers to this event and shows a vibrating
  red text on the players screen"*).
- `HudIngameError`, `HudIngameDiscovery` — `VintagestoryLib.dll`, `Vintagestory.Client.NoObf`.
- Path-A call site — `SystemMouseInWorldInteractions` (`VintagestoryLib.dll`).
- Vanilla lang keys seen: `game/lang/en.json` → `placefailure-rotationblocked` (line ~11227),
  `error-wheel-blocked` (line ~16988).
- LibGUI in-dialog widgets — `reference/vslibgui/` (Tooltip + `TooltipTests.cs`, `GuiGlobalOverlay`).
