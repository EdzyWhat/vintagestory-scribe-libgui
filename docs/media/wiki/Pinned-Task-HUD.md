# Pinned Task HUD

The Pinned Task HUD is an always-on overlay showing your currently pinned tasks over the game world. It appears automatically when you have at least one pinned task and hides itself when all pins are removed.

![3 Pinned Tasks on the HUD](https://i.imgur.com/0EQ9mB0.png)

## Pinning a task

Open a Lectern or [Notebook](The-Notebook), hover a task row in **Read** or **Edit** view, and click the pin icon. The task appears on the HUD immediately. Pinning is **per-player** — your pins are yours and don't affect what other players see. Pins from every source (any Lectern, any Notebook you carry) share the one HUD.

## The Clockmaker's timer on the HUD

If you carry a [Clockmaker's Notebook](Clockmakers-Notebook-and-Timers) with a running timer, its countdown appears as a row on the same HUD, above your pins. When it fires it flashes and plays a sound; whether the fired row clears itself automatically is controlled by the **Timer disappears** setting (see [Scribe Settings](Scribe-Settings)).

## The HUD overlay

Pinned tasks display as a compact checklist with no background plate — the text uses a soft glow for legibility over any world background. The list shows up to your configured **Max HUD rows** limit; if you have more pins than that, a **+N more** indicator appears at the bottom.

Tasks are ordered: incomplete above complete. A task you've just checked off stays in place during the **1.5-second undo window**, then settles according to your completion policy.

## Checking off a task from the HUD

Click a task's checkbox to complete it. The text fades gradually over the 1.5-second window as a countdown. During that window, **unchecking the box fully cancels the action** — nothing is sent to the server. After the window elapses, the completion is applied according to your [completion policy](Scribe-Settings#completion-policy).

## Collapse

Click the **▾ Pinned** header or press the **P** key (rebindable under Controls → Scribe) to collapse the HUD to just its header. Press again to expand. The collapsed state persists across relogs and worlds.

The gear icon next to the header opens [Scribe Settings](Scribe-Settings).

## Positioning

The HUD defaults to the **Top-Right** corner, offset to clear the vanilla minimap. You can move it to any corner or edge via **HUD position** in Scribe Settings, and nudge it with the **HUD offset** fields. If you've disabled the minimap in your game settings, the extra clearance is automatically removed.
