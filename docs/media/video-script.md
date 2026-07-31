# Scribe — Feature Showcase Video Script

**Target length:** ~90–120 seconds  
**Format:** screen capture of the game, no face cam needed. Narration optional (could be
text overlays + in-game UI doing the talking). Background music: ambient/quiet.

---

## Shot list / beat sheet

### Beat 1 — The problem (0:00–0:10)
**Shot:** a wide in-world shot, player standing in a partially-built base. No UI visible.  
**Overlay / narration:** *"Vintage Story is a long game. Between sessions, I forget everything I was doing."*  
*Optional: quick cuts of scattered ore piles, half-built structure, empty crafting grid.*

---

### Beat 2 — Place the Lectern (0:10–0:22)
**Shot:** player opens inventory, shows the Lectern item. Places it in the world. It snaps to face the player.  
**Overlay:** *"Scribe adds a Lectern — a craftable notebook you write directly on."*  
*Cut to: the placed block from a slight angle, looking good in the world.*

---

### Beat 3 — Open and write (0:22–0:40)
**Shot:** player right-clicks the Lectern. The GUI opens — notebook art, task list, clean layout.  
Shift + right-click to enter edit mode. Type two or three tasks in real time:
- "Smelt copper ingots"
- "Build the cellar door"
- "Find clay deposits"
**Overlay:** *"Right-click to read. Shift + right-click to edit."*  
*Let the typing speak for itself — don't rush this.*

---

### Beat 4 — Check a task off (0:40–0:48)
**Shot:** hover over a task, click the checkbox. The row dims/checks. Simple, satisfying.  
*No narration needed — let the UI do it.*

---

### Beat 5 — Pin to HUD (0:48–1:05)
**Shot:** hover over a task. The pin icon appears. Click it. Cut to: the world view — the
Pinned Task HUD appears in the corner with the task text glowing on screen.  
Player walks around the world with tasks visible.  
**Overlay:** *"Pin tasks to your HUD. They stay on screen while you play."*  
*Then: player checks off the task from the HUD. The text fades during the undo window, then disappears.*  
**Overlay:** *"Check them off from the HUD. Short undo window before it's final."*

---

### Beat 6 — Settings (1:05–1:20)
**Shot:** open the Settings panel (gear icon). Quick cuts showing:
- Theme toggle (pixel-art notebook on/off)
- Font selector (scroll through a couple options)
- HUD anchor picker (move it to a different corner)
**Overlay:** *"Customize the theme, font, HUD position, and size. Everything updates live."*

---

### Beat 7 — Multiplayer (1:20–1:30)  *(optional, include if you have a 2nd client handy)*
**Shot:** two players, one edits the Lectern, the other reads. Show the read-lock behavior briefly.  
**Overlay:** *"Multiplayer-safe. Edits sync live — one editor at a time, everyone else reads."*

---

### Beat 8 — Outro / CTA (1:30–end)
**Shot:** pull back to the world. Lectern in the environment. HUD with a couple tasks pinned.  
**Overlay:**
- *"Scribe v0.2 — available on the Vintage Story Mod DB"*
- *"Requires LibGUI (free, one-click install)"*  
*Fade to title card / mod DB URL.*

---

## 0.2 additions — carried notes, timers, and logs

Insert these beats after Beat 6 (Settings) and before the Multiplayer/Outro beats, or cut a shorter
0.2-focused clip from just these. They lead with the headline change: *you can take it with you.*

### Beat 2.1 — The Notebook (carry it with you)
**Shot:** open inventory, pull out the **Notebook**, right-click to open it in-hand. It opens on the
same clean task list — no block, no walking back to a Lectern.  
**Overlay:** *"New in 0.2 — a carried Notebook. Your tasks, in your pocket."*  
*Type a task or two, then close and walk away — make the point that it's on you now, not on a block.*

### Beat 2.2 — The Clockmaker's Notebook & Timer
**Shot:** open the **Clockmaker's Notebook**, go to the **Timer** tab. Set a short label ("Pull the
crucible") and a duration, pick Real time, press **Start Timer**. Cut to the world — the timer sits on
the HUD above the pins, counting down. Let it fire: it blinks and rocks.  
**Overlay:** *"The Clockmaker's Notebook adds timers — real time or in-game time, right on your HUD."*

### Beat 2.3 — History & Guestbook (the logs write themselves)
**Shot:** open the Notebook's **History** tab — show a lived-in chronicle (crafted, a storm, a death,
a boss kill). Then a Lectern's **Guestbook** tab with a few signed visitors.  
**Overlay:** *"Every Notebook keeps a History. Every Lectern keeps a Guestbook. Written automatically."*

---

## Shot-list & demo-seed cheat sheet

The History and Guestbook logs can't be authored by hand, and empty task lists look unconvincing on
camera. Use the creative-only seed command to stage each shot, then capture. All commands require
creative mode + the `controlserver` privilege.

| Shot / beat | What it needs | Seed command | Notes |
|-------------|---------------|--------------|-------|
| Read view w/ full task list | Tasks + notes on a Notebook | hold Notebook → `/scribe seed all notebook` | 12 mixed done/undone tasks + 2 notes |
| History tab (Notebook) | A varied, dated chronicle | hold Notebook → `/scribe seed history notebook` | spread across recent in-game days |
| Guestbook tab (Lectern) | Several signed visitors | look at Lectern → `/scribe seed guestbook lectern` | some visitors leave short notes; **reopen the lectern** to see them |
| Lectern Read view | Tasks + notes on a Lectern | look at Lectern → `/scribe seed all lectern` | reopen to refresh the read view |
| Timer on HUD | A running / fired timer | set it live in the Clockmaker's Notebook Timer tab | not seedable — it's a live countdown; use a short Real-time duration for the fire shot |
| Pinned HUD | A few pins | seed tasks, then pin 2–3 from Read view | pins aren't seeded (they're per-player state) |

`/scribe seed all` with no target auto-picks the Lectern you're looking at, else the Notebook you
hold. Screenshots land in `docs/media/screenshots/0.2/`.

## Production notes
- Keep the world tidy — a clean, mid-game survival base is more relatable than a Creative sandbox.
- Capture at native resolution; don't zoom in via video editor (the pixel art theme especially benefits from clean native pixels).
- If doing narration: keep it sparse and matter-of-fact. The UI is self-explanatory; narration should add *why*, not describe *what*.
- If skipping narration: text overlays in a neutral sans-serif work fine.
- Ideal posting: embed on the mod DB page (F5) and link from the reddit announcement post.
