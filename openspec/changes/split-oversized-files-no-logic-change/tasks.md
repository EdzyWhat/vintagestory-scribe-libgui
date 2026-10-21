## 1. Prove the checker

- [ ] 1.1 Run `build/verify-comment-only.sh` after moving one small type to a new file; if it reports a diff, add an order-insensitive mode (sort per-type/per-method IL blocks) and re-run until a pure move reports identical; verify the script exits 0 on the move and 1 on a deliberate one-token edit

## 2. Move widgets out of the large files (one commit each)

- [ ] 2.1 Clockmaker: move `ScribeGearworks` and the timer widgets to their own files; verify build and IL-identical
- [ ] 2.2 `HudScribePins`: move `HudPinRow`/`HudPinsContent`, then `ScribeFadeText`/`ScribeHudGearButton`; verify build and IL-identical
- [ ] 2.3 `ScribeEditorContent`: move `ScribeEditRow` and its state; verify build and IL-identical
- [ ] 2.4 `ScribeMultilineField`: move the render objects to their own file; verify build and IL-identical

## 3. Split by concern with partials

- [ ] 3.1 `HudScribePins`: partials for anchor/minimap clearance, tracker count engine, storm corruption; verify build and IL-identical
- [ ] 3.2 `ScribeMultilineFieldState`: partials `.Keys`, `.Pointer`, `.Reveal`; verify build and IL-identical

## 4. Verify

- [ ] 4.1 Run `build/verify.sh` (Core tests plus Atlas) and confirm green; record before/after line counts of the four files in the commit message
- [ ] 4.2 Grep the moved files for comments referring to code "above"/"below" or a file that moved, and fix them
