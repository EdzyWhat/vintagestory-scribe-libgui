## Why

New tasks land in three different places depending on how you create them: the footer Add button and Handbook "Add to Scribe" append at the bottom; Shift+right-click quick-add inserts at the top. Players who work the live list from the top have to scroll after every Handbook or Add click. v1.3.2 is the right cut to make that one client-local setting, defaulting to top so capture-style adds stay at the working edge.

## What Changes

- **New Task Insert** setting (Mod Behavior dropdown): **Top** (default) / **Bottom**. Client-local on `ScribePlayerSettings`; missing JSON key → Top.
- **One insert index for document-level creates.** Footer Add (Task and Note), Shift+right-click quick-add, and Handbook Add to Scribe (Link, Tracker, Craft, guide-page Link) all insert at that index. Top = index 0 (newest first); Bottom = append. A Crafting Task still expands its ingredient run immediately under the parent, so the whole group sits together at the chosen edge.
- **Focus.** Footer Add and quick-add still focus the new empty row. Handbook adds stay unfocused (existing cross-window rule).
- **Out of scope:** Enter = insert-below-caret (relative, not document-level); Transcribe copy/import; pin-list order; nested grip indent.

Not codec-breaking. Default Top **does** change footer Add and Handbook from today's append — that is the product change.

## Capabilities

### New Capabilities

- `new-task-insert-position`: The Top/Bottom policy, which create-gestures honor it, and how a Craft parent+owned-run lands as one group.

### Modified Capabilities

- `settings-tab`: Mod Behavior dropdown for New Task Insert (Top / Bottom), with helptext.
- `lectern-gui-shell`: Footer Add places the new Task/Note at the setting's edge and focuses it. Enter insert-below is unchanged.
- `handbook-scribe-entry`: "Add to Scribe" inserts at the setting's edge instead of always appending.

## Impact

- **Core:** `ScribeNewTaskInsert` enum + settings field + `Normalize`; `Insert*` (or index-taking) variants of AddTask/AddTextSection/AddTracker/AddCraft/AddLink/AddGuideLink. No VS API in Core.
- **Mod:** Settings dropdown + lang keys; `OnClickAdd`, `QuickAddTopTask`, and Handbook apply paths share one insert-index helper; focus index follows the new row.
- **Tests:** Core unit tests for Top vs Bottom on each kind, including Craft parent+children clustered at the top. In-game: Add, quick-add, Handbook Link/Tracker/Craft.
- **Saves:** client settings JSON only. No packet, no codec bump.
