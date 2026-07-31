# Clockmaker's Notebook & Timers

The Clockmaker's Notebook does everything the plain [Notebook](The-Notebook) does — carried tasks, notes, pins, and an auto-logged [History](History-and-Guestbook) — and adds a built-in **Timer** tab.

## The Timer tab

Open the Timer tab to set a single countdown with an optional **label** and a duration in **hours, minutes, and seconds**. Choose one of two modes:

- **Real time** — counts down in real-world seconds.
- **In-game time** — counts down in game-world time, which runs faster than real time (and pauses when the world does).

Press **Start Timer** to begin, **Stop Timer** to clear it.

The timer belongs to **you**, not to the notebook: it keeps running while the Clockmaker's Notebook sits in a chest, and its countdown shows on your [Pinned Task HUD](Pinned-Task-HUD) above your pins.

## When it fires

When the countdown reaches zero, the HUD timer **blinks** and its icon rocks back and forth to get your attention, alongside a sound.

By default the finished timer disappears from the HUD after about 30 seconds; you can also clear it early by clicking the HUD timer or pressing **Stop Timer**. If you'd rather it stay until you acknowledge it, turn off **Timer disappears** in [Scribe Settings](Scribe-Settings) — the finished timer then remains until you click it or press Stop Timer.

## Crafting and the tinkerer trait

Craft the Clockmaker's Notebook from a finished [Notebook](The-Notebook) + a temporal gear + metal parts, in a 3×1 row — see [Crafting → Clockmaker's Notebook](Crafting-the-Lectern#clockmakers-notebook).

**The recipe requires the `tinkerer` trait**, which is granted by the vanilla **Clockmaker** character class. If you play another class, the recipe won't complete — and because the game enforces this silently, you simply won't get the output. (A world with no character classes at all is not blocked.)

### Lifting the requirement (server operators)

Scribe adds a world setting, **`scribeClockmakerRequiresTrait`** (default **on**). To let any player craft the Clockmaker's Notebook regardless of class, turn it off:

- On the **world-creation Customize screen**, or
- On a running world with `/worldconfig scribeClockmakerRequiresTrait false` (alias `/wc`).

When the setting is off, Scribe clears the trait requirement from the recipe at server startup, so the craft succeeds for everyone.
</content>
