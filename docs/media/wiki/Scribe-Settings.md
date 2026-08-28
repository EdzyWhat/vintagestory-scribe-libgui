# Scribe Settings

Open Scribe Settings via the **gear icon** in the Lectern's or Notebook's right sidebar, or the **gear icon** on the Pinned Task HUD. All open the same panel. Settings take effect immediately — no OK button, no restart required. They are per-player and client-local.

![Scribe Settings Window](https://i.imgur.com/F8KMRpg.png)

---

## Mod Behavior

### On newly completing a task

What happens when you check off a pinned task (from the HUD, Read view, Edit view, or Pinned view).

| Policy | What happens |
|--------|-------------|
| **Keep (stay in place)** | The task stays pinned and holds its position. Nothing is removed. |
| **Keep (sink to bottom)** | The task stays pinned but moves to the bottom of the list. It stays there for the session even if you later uncheck it. |
| **Unpin (stay in place)** | Your pin is removed. The task itself is untouched in the document. |
| **Unpin (sink to bottom)** | Your pin is removed AND the task moves to the bottom of the source document. |
| **Delete task** | The task is permanently deleted from the document. Destructive — use with care. |

The policy picker is also available at the top of the **Pinned view**.

### Mute Scribe UI sounds

Silences the click sounds from Scribe's own buttons. Your game sounds and other mods are unaffected.

### Timer disappears

When on, a finished [Clockmaker's Notebook](Clockmakers-Notebook-and-Timers) timer disappears from the HUD after about 30 seconds. Turn it off to keep the fired timer showing until you click it or press **Stop Timer**.

### Alarm Volume (0–100)

Volume of the Clockmaker's Notebook alarm bell. 0 is silent, 100 is loudest. Default 65.

---

## Window Appearance

### Pixel-Art Display

Toggles the illustrated notebook art on or off. When ON, Scribe's windows render with the pixel-art notebook backdrop and light parchment theme. When OFF, they use your global game GUI theme.

### Pixel Art Size (px)

The width of the Scribe window in logical pixels (300–1000, step 10). The window's proportional layout scales with this value. Changes take effect immediately.

### Window text size (%)

Scales all text and controls inside the Scribe window (80–120%, step 5%).

### Task text font

The typeface used for task and note row text. Options include:
- **Default** — the built-in body font
- Scapholène, Caudex, La Belle Aurore, Noto Sans, Noto Serif (bundled with Scribe)
- Playfair Display, Cormorant Unicase (from LibGUI)

### Cuneiform tablets

When on (the default), Tablet text is written in the carved-wedge cuneiform script. Turn it off to render Tablet text in your selected task font instead.

### Cuneiform press-in

When on, newly typed cuneiform letters carve in stroke-by-stroke. Only has an effect while **Cuneiform tablets** is on.

---

## HUD Appearance

### Collapse the HUD

Minimizes the HUD to just its header (same as clicking the ▾ chevron or pressing P).

### Storm text corruption

During a temporal storm — or when your stability drops low — the HUD scrambles its text and its title reads "Survive the Storm". Turn off to keep the HUD fully legible.

### HUD position

Which screen edge or corner the HUD anchors to: Top-Left, Top-Center, Top-Right, Mid-Left, Mid-Right, Bottom-Left, Bottom-Right. Default is Top-Right (offset to clear the vanilla minimap).

### HUD offset (px)

Nudges the HUD away from its anchor point — positive values move it toward the center of the screen along each axis. Use this to fine-tune clearance around the minimap or other HUD elements.

### Max HUD rows

How many pinned tasks are visible at once (1–10). Tasks beyond the limit are counted in the **+N more** footer.

### HUD row width

The fixed width of each HUD row in pixels (100–1000). Long task text wraps within this width.

### HUD text size (%)

Scales the text on the Pinned Task HUD independently of the Scribe window (80–120%, step 5%).
</content>
