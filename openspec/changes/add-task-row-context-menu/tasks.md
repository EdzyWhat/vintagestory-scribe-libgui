## 1. Menu widget

- [ ] 1.1 Add `ScribeRowContextMenu`, a floating action menu on the `ScribeAddKindPicker` overlay pattern (barrier plus anchored follower), taking a list of entries and closing on entry choice, outside click and Escape; verify it builds and opens from a temporary dev hook
- [ ] 1.2 Ensure only one row menu is open per dialog and that it closes when its row's state is disposed; verify by opening two in sequence in-game

## 2. Wire the rows

- [ ] 2.1 Read rows: open the menu on right-click with Edit, Pin/Unpin and Delete, each gated by the same flags as the row's existing controls and the dialog's edit permission; verify on a Notebook and a read-only Lectern
- [ ] 2.2 Read-view Edit: enter the editor through the existing path (editor lock grant on block surfaces), then focus the row; verify locked-editor feedback when another player holds the lock
- [ ] 2.3 Editor rows: open the menu with Pin/Unpin and Delete wired to the existing index callbacks; verify no Edit entry appears
- [ ] 2.4 Pin Tab rows: open the menu with Unpin and Delete wired to the existing identity-addressed callbacks; verify the server messages are the same as the hover controls'
- [ ] 2.5 Skip Text note rows and the HUD; verify right-click does nothing there

## 3. Verification

- [ ] 3.1 Run `build/verify.sh` with the client closed and verify Core and Atlas are green
- [ ] 3.2 Add playtest items to `TESTING.md` for the menu in each dialog type (Notebook, Lectern in each access mode, Chalkboard, Scriptorium, Assignment Desk, Tablet, Pin Tab), outside-click and Escape closing, and the hover controls still working
