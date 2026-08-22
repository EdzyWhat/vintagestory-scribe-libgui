# Scriptorium

The Scriptorium is a dedicated shared writing station — the place you go to **copy**, **merge**, and **import/export** Scribe documents, not just to keep one list. It hosts a full document (Read, Task Editor, Pinned, Guest Book, Settings) plus a **Transcribe** tab that no other surface has.

Anyone in reach can read it; one player edits at a time. The document survives break and re-placement, same as the Lectern.

**See also:** [Items](Items), [Crafting](Crafting-the-Lectern)

## Transcribe — copy

Place the item you want to copy **from** in the **Copy from** slot, and the item you want to copy **into** in the **Paste into** slot. Both slots accept Scribe items (Notebooks, Tablets, and picked-up Lecterns or Scriptoriums). Press **Copy** and the source's tasks and notes are written onto the target.

The two documents are fully independent afterward. The source is left untouched. A wooden stamp descends onto the slot — and thumps — when the copy lands.

If the target already has tasks, the first press of Copy turns red and warns how many it will overwrite; press again to confirm. Copying onto an empty item happens immediately.

Copy is greyed until both slots hold a Scribe item the target can accept. A **hardened or fired Tablet** can't be written to. A Tablet also has a task limit, so a source with more tasks than the target can hold is refused.

## Import / Export

The lower half of the tab moves a document through your system clipboard as plain text.

- **JSON is the exact lane** — a complete, lossless copy, best for backing a list up or moving it between items untouched.
- **TSV is the forgiving lane** — a spreadsheet-friendly table (`Type · Done · Text · Special · Count · Depth`) that's easy to edit by hand.

**Import** auto-detects JSON vs TSV from the clipboard and writes onto the slotted item (Overwrite or Append). Unknown item or link references land as plain tasks rather than failing the whole paste. Imported tasks are never pinned — an import brings the words, not anyone's HUD state.

Paste JSON into a plain-text editor (Notepad++ on Windows, CotEditor on macOS), not a word processor that may "fix" quotes.

## Opening

| Action | Result |
|--------|--------|
| **Right-click** | Opens in Read view |
| **Shift + Right-click** | Quick-adds a fresh task at the top of the editor |

For keyboard shortcuts and text-editing mechanics, see [Editor Reference](Editor-Reference).
