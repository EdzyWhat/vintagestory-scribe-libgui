## 1. Diagnose

- [ ] 1.1 Reproduce in-game: open Scribe Settings with a Lectern/Notebook/Tablet document editor also open, touch an editor row, then focus a numeric field (e.g. Window Text Size) and press Up/Down 3+ times; confirm only the first press steps and the arrow then drives the editor row's caret.
- [ ] 1.2 Add a DEBUG per-frame focus trace (per the banked "settling loops & race diagnosis" method) that logs which `FocusNode`/widget holds focus before and after each write-through rebuild, and capture the exact transition on press #2.
- [ ] 1.3 From the trace, confirm the root cause: whether the armed one-shot is consumed by the first rebuild and not re-held, whether the remounted numeric field's `RequestFocus` is clobbered by the editor row, or whether the editor row acts on Up/Down while not genuinely focused. Record the finding.

## 2. Fix focus retention

- [ ] 2.1 Apply the minimal fix indicated by 1.3 in the re-focus handshake — `ScribeNumericFocusRegistry` (`src/Mod/ScribeNumericField.cs`) and/or the `NumericField` handshake in `src/Mod/ScribeSettingsContent.cs` — so a stepped numeric field re-holds focus across CONSECUTIVE write-through rebuilds, not just the first. Ensure the numeric field's `FocusNode.Owner` is wired before `RequestFocus` (banked `ScribeMultilineField` lesson).
- [ ] 2.2 Only if the trace shows the editor row is acting on Up/Down without genuinely holding focus, have `ScribeMultilineField` decline focus/keys it was not given; otherwise leave `ScribeMultilineField` untouched.
- [ ] 2.3 Do NOT modify `ScribeNumericField.OnFieldKeyDown` (Up/Down already `Handled`) or `OnFocusChanged`'s unchanged-value blur guard (the §8.2 +/- button fix); keep the change scoped to focus retention.

## 3. Verify

- [ ] 3.1 Confirm consecutive arrow presses (3+) step a focused numeric field every time, focus staying on it, WITH a document editor open (the primary repro from 1.1).
- [ ] 3.2 Confirm the same with no document open, and across several different numeric fields (rows, width, offsets, font scales).
- [ ] 3.3 Confirm no regression: a genuinely focused editor row (Lectern/Notebook/Tablet, and Pin Tab) still moves the caret by visual line on Up/Down per `arrow-key-line-caret-nav`.
- [ ] 3.4 Confirm no regression to the +/- step BUTTONS (they still step and keep focus) or the blur-commit/clamp behavior (select-all-and-retype, out-of-range clamps on blur).
- [ ] 3.5 Remove the DEBUG focus trace added in 1.2.

## 4. Land

- [ ] 4.1 Run the Core suite (`dotnet test`) and confirm green; run the Atlas suite via the local pre-push gate.
- [ ] 4.2 Update CHANGELOG.md with the fix and record any new API/focus finding in `VSAPI-NOTES.md` (LibGUI section) if one was learned.
