# Using the Lectern

## Opening the Lectern

| Action | Result |
|--------|--------|
| **Right-click** | Opens the Lectern in Read view |
| **Shift + Right-click** | Opens the Lectern in Edit view (requests the editor lock) |

The Lectern's views are switched via the icon buttons in the right-hand sidebar: **Read**, **Edit**, **Pinned**, **Guestbook**, and **Settings**.

---

## Read view

The default view. Shows all tasks and notes in the document as a scrollable list. You can:

- **Check a task off** by clicking its checkbox. This applies your current completion policy (see [Scribe Settings](Scribe-Settings)).
- **Pin a task** using the pin icon that appears on hover. Pinning adds the task to your personal [Pinned Task HUD](Pinned-Task-HUD).
- **Scroll** through the list with the mouse wheel or the scrollbar.

Read view is available to any player, even while another player holds the editor lock.

---

## Edit view

Full document editing. Only one player can hold the editor lock at a time — if another player is already editing, your Edit affordance will be greyed out and you'll receive a message when you try to enter.

In Edit view you can:

- **Type** into any task or note row to edit its text. Text wraps and the row grows to fit.
- **Add a new task** with the **New Task** button, or press **Enter** in a focused row to insert a task directly below it.
- **Check a task off** — the completion policy applies immediately in the editor scratch (the task may sink to the bottom or be removed from the document).
- **Pin / unpin** a task using the hover pin button.
- **Delete a row** using the hover delete (×) button.
- **Reorder rows** by dragging the grip handle on the far left of each row.
- **Done editing** commits your changes and releases the lock, returning you to Read view.

For keyboard shortcuts, caret movement, text mechanics, and empty-task behaviour, see [Editor Reference](Editor-Reference). (The same editor powers the [Notebook](The-Notebook), so the shortcuts are identical there.)

---

## Pinned view

Shows all of your pinned tasks **across every Lectern and Notebook**, not just the one currently open. Each row is fully editable in place — you can edit the text, check it off, unpin it, delete it, or drag-reorder it.

A **completion policy picker** at the top of the list lets you change your policy without opening Settings. Changes here stay in sync with the Settings window.

Completions in the Pinned view apply **immediately** with no undo delay (unlike the HUD's 1.5-second undo window).

---

## Guestbook view

An automatic **visitor log** unique to the Lectern (the Notebook has no Guestbook). The first time a player opens a Lectern on a given in-game day, their name and the date are recorded. Each visitor may leave a short **note** on their own entry. See [History & Guestbook](History-and-Guestbook) for the full description.

---

## Settings view

Opens the Scribe Settings panel. This is the same panel opened by the gear icon on the [Pinned Task HUD](Pinned-Task-HUD). See [Scribe Settings](Scribe-Settings) for a full description of every option.

---

## Multiplayer

- Any number of players can have the Lectern open in **Read**, **Pinned**, or **Guestbook** view simultaneously.
- Only **one player** can hold the editor lock at a time. Others see the Edit affordance as unavailable while the lock is held.
- Read views update **live** — when the editor saves, other players see the change immediately without reopening.
- The editor lock is released when the editing player clicks **Done editing**, closes the dialog, or moves out of range.
</content>
