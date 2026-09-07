---
name: triage-screenshot
description: Move a freshly-taken in-game Vintage Story screenshot (captured via fn+F12) from the game's screenshot folder into this repo's screenshots/ folder, renaming it with any context the user gives. Use whenever the user says something like "There's a new screenshot", "New screen", "screen", or otherwise indicates they just captured a screenshot for troubleshooting or progress purposes.
version: "1.0"
---

# Triage a new Vintage Story screenshot

The game writes screenshots (fn+F12) to `~/Pictures/Vintagestory/`, named by
capture timestamp (e.g. `2026-07-18_12-09-08.png`). This skill moves the
newest untriaged one(s) into this repo's `screenshots/` folder and appends any
context the user gives to the filename, so screenshots end up organized and
searchable instead of sitting in the game's flat folder.

## Steps

1. **Find untriaged screenshots.** List `~/Pictures/Vintagestory/*.png` and
   compare filenames (the game's own timestamp-based names) against what
   already exists anywhere under this repo's `screenshots/`. Untriaged =
   present in the source folder but not yet copied into the repo.

   - If there's exactly one untriaged file, that's the one.
   - If there are several, triage all of them (the user may have taken more
     than one before asking) — apply the same context suffix (if any) to each,
     since they don't need distinguishing on their own.
   - If there are none, say so — don't silently no-op.

2. **Pick the destination folder.**
   - Default: `screenshots/debug/` — this is the common case during active
     playtesting/bug-hunting.
   - Use `screenshots/progress/` instead only if the user's message clearly
     frames it as a progress/demo shot (e.g. mentions "progress", "showing
     off", "for the README", "demo").
   - Both folders are currently gitignored (see repo `.gitignore` —
     `screenshots/` is untracked for now, to be curated and un-ignored later),
     so there's no git action needed after moving the file.

3. **Build the new filename.** Keep the original timestamp stem (it's already
   a useful, sortable, unique identifier) and append a sanitized slug of any
   context the user gave in the same message:
   - Lowercase, spaces → hyphens, strip anything that isn't
     `[a-z0-9-]`.
   - `<timestamp>.png` if no context was given, else
     `<timestamp>_<slug>.png`.
   - Example: user says "screen, this is the crash when adding a third row" →
     `2026-07-18_12-09-08_crash-when-adding-a-third-row.png`.

4. **Move (don't copy) the file(s)** from `~/Pictures/Vintagestory/` into the
   chosen destination folder under the new filename, using `mv`. Moving keeps
   the source folder from accumulating clutter across sessions.

5. **Confirm back to the user** with the repo-relative path(s) of where each
   screenshot landed, so they can reference it in the next message (e.g. "look
   at screenshots/debug/2026-07-18_12-09-08_crash-....png").

## Notes

- Don't ask a clarifying question just to pick debug vs. progress — default to
  debug and let the user correct you; the folder is a coarse sort, not a
  commitment.
- Don't rename or touch any screenshot that's already inside the repo's
  `screenshots/` folder — only act on files still sitting in the game's
  source folder.
