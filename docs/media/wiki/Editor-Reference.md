# Editor Reference

This page covers keyboard shortcuts, text mechanics, and editing behaviour in the **Edit view**. The same editor powers the Lectern, the [Chalkboard](Chalkboard), the [Scriptorium](Scriptorium), the [Notebook](The-Notebook), and the [Clockmaker's Notebook](Clockmakers-Notebook-and-Timers), so everything here applies to all of them.

---

## Row navigation

| Key | Action |
|-----|--------|
| **Tab** | Commit the current row and move focus to the next row |
| **Shift+Tab** | Commit the current row and move focus to the previous row |
| **Enter** | Commit the current row and insert a new empty task directly below it |
| **Shift+Enter** | Insert a hard line break within the current row (row grows to fit) |
| **Esc** | Commit the current row and close the document |

**Enter on an already-empty row** does nothing — it won't stack a second empty task.

---

## Caret movement

| Key | Action |
|-----|--------|
| **Arrow keys** | Move the caret one character / line |
| **Alt / Option + ←/→** | Skip by whole word |
| **Cmd + ←/→** *(macOS)* | Jump to line start / end |
| **Ctrl + ←/→** *(Windows)* | Skip by whole word |
| **Home / End** | Jump to line start / end |
| **Shift + any movement key** | Extend the text selection |

---

## Selection and clipboard

| Key | Action |
|-----|--------|
| **Cmd/Ctrl+A** | Select all text in the focused field |
| **Cmd/Ctrl+C** | Copy selection |
| **Cmd/Ctrl+X** | Cut selection |
| **Cmd/Ctrl+V** | Paste |
| **Type while selected** | Replace the selection with typed text |

---

## Empty task behaviour

When you move focus away from a task row (by pressing Tab, Shift+Tab, Enter, Esc, or clicking elsewhere), the row is **committed**:

- If the row has text, it is saved and any trailing blank lines or trailing whitespace are trimmed. Interior line breaks (from Shift+Enter) are preserved.
- **If the row is empty or contains only whitespace, it is automatically deleted.** Focus moves to the row above. This keeps the document free of blank rows without needing a separate delete action.

An empty row that is still focused is not deleted — only on commit (focus leaving the row).

---

## Adding tasks

- **New Task button** — always visible at the bottom of the editor; adds a new empty task and focuses it.
- **Enter** — inserts a new empty task directly below the current row and focuses it. Faster than reaching for the button when you're mid-list.
- **Item Trackers, Links, and Crafting Tasks** — added from an item's Handbook page ("Add to Scribe"), not the Add button. A Crafting Task binds a grid recipe and builds ingredient subtasks underneath.
- **Grip tap** — tap the drag grip (without holding to reorder) to indent a row as a subtask, or tap again to promote it. One level only.

Both of the first two methods create the task empty so you can type immediately with no placeholder text to clear first.

---

## Text sections vs. task rows

Text/note sections (freeform blocks, not tasks) behave the same way for caret movement and clipboard shortcuts. The differences:

- Text sections have **no checkbox**.
- Text sections are **never auto-deleted** when empty — an empty note section is valid and is kept.
- Text sections have a **higher character limit** (10,000 characters vs. 1,000 for task rows).
- Enter in a text section inserts a line break rather than creating a new row.
</content>
