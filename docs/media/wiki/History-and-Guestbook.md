# History & Guestbook

Scribe keeps two automatic, append-only logs. They are recorded for you — there's no way to write them by hand — so they read as a genuine record of what happened.

- **History** lives on the [Notebook](The-Notebook) (and Clockmaker's Notebook). It is *not* on the Lectern.
- **Guestbook** lives on the [Lectern](Using-the-Lectern). It is *not* on the Notebook.

---

## History (Notebook)

The **History** tab on a Notebook chronicles notable events that happen while you carry it. Each entry is stamped with the in-game date.

| Event | When it's recorded |
|-------|--------------------|
| **Crafted** | Once, when the Notebook is first made. |
| **Picked up** | The first time a given player opens/holds it. |
| **Death** | When you die while carrying it — with the vanilla death message. |
| **PvP kill** | When you slay another player while carrying it. |
| **Boss kill** | When a boss (e.g. the Eidolon) dies near you while you carry it. |
| **Temporal storm** | When a temporal storm begins while you carry it, with its strength. |

Older entries of the high-frequency kinds (deaths, storms, kills) roll off once their per-kind cap is reached, so the log stays a recent chronicle rather than growing without bound. Entries show newest-first.

> **Note:** because History is bound to the item you're holding, the log follows the *Notebook*, not the player. A Notebook that changes hands carries its earlier history with it.

---

## Guestbook (Lectern)

The **Guestbook** tab on a Lectern is a visitor log. The first time a player opens a given Lectern on a given in-game day, their name and the date are recorded automatically. Each visitor may leave one short **note** (up to 140 characters) on their own entry — a greeting, a status update, a signature. Entries show newest-first.

The Guestbook is a natural fit for shared or public Lecterns: a town noticeboard, a trader's stall, a base everyone passes through.

---

## For screenshots (developers)

These logs can't be hand-authored in a saved world, so Scribe ships a creative-only dev command to seed believable sample content for screenshots and video:

```
/scribe seed <tasks|notes|history|guestbook|all> [notebook|lectern]
```

- Requires the `controlserver` privilege **and** creative mode.
- With no target, it auto-picks: a Lectern you're looking at, else a Notebook you're holding.
- History seeds only a Notebook; Guestbook seeds only a Lectern (an inapplicable combination is reported, not applied). Reopen the target to see seeded Lectern content.
</content>
