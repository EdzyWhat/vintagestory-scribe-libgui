## 1. Confirm the mechanism (optional but recommended)

- [ ] 1.1 Read the tablet's `SlotModified`/state-resolve path (`GuiDialogScribeTablet.OnHotbarSlotModified`,
      `ItemScribeTablet.ResolveMaterialState`) to answer design Open Question: is the wet→hard/fired
      transition independent of the DocId-mismatch close, or does it rely on it? Record the answer.

## 2. Shared helper

- [ ] 2.1 Add `ActiveHandHoldsAnyScribeDocumentItem()` to `ScribeDialogBase` next to
      `ActiveHandItemHostsThisDocument()` — the presence half of the existing check (active hand stack's
      collectible is `IScribeDocumentItem`), WITHOUT the DocId comparison. Document why it exists (the
      in-place re-sync flicker) referencing this change.

## 3. Notebook + Clockmaker fix

- [ ] 3.1 In `GuiDialogScribeNotebook.OnHotbarSlotModified`, close the dialog only when
      `ActiveHandHoldsAnyScribeDocumentItem()` is false — i.e. stop routing an in-place same-slot
      modification through the DocId-strict `OnActiveSlotChanged`. Leave `OnActiveSlotChanged`
      (the real `AfterActiveSlotChanged` hand-switch) calling the DocId-strict guard unchanged.
- [ ] 3.2 Verify `GuiDialogClockmakerNotebook` inherits the fix (no own handlers) — no code change,
      just confirm it still compiles and inherits `OnHotbarSlotModified`/`OnActiveSlotChanged`.

## 4. Tablet fix

- [ ] 4.1 Apply the same `OnHotbarSlotModified` change to `GuiDialogScribeTablet`, preserving the
      wet→hard/fired in-place transition path per task 1.1's finding (gate ONLY the flicker-causing
      DocId-mismatch close behind the presence rule; keep any state-transition refresh/close intact).

## 5. Build + verify

- [ ] 5.1 Build and run the local verify loop (`./build/verify.sh Debug` with `VINTAGE_STORY` set):
      Core suite + Atlas suite must stay green. No new tests expected (client GUI-lifecycle behavior).
- [ ] 5.2 Restage the mod (`build/restage.sh Debug`) so the in-game test uses the fixed build.

## 6. In-game verification (manual)

- [ ] 6.1 Manually test in-game: obtain a Scribe item you did NOT craft (creative-give or pick one up),
      right-click to open it for the FIRST time, and confirm the dialog OPENS AND STAYS — no flicker,
      no need for a second right-click. Repeat for notebook, clockmaker's notebook, and tablet.
- [ ] 6.2 Manually test in-game (regression control): confirm a self-CRAFTED item never flickered and
      still opens cleanly; confirm dropping the item closes the dialog; confirm switching the hotbar to
      a DIFFERENT Scribe item closes the open dialog.
- [ ] 6.3 Manually test in-game (tablet transition): with a wet tablet's dialog open, let it (or force it
      to) transition wet→hard, and confirm the transition still behaves correctly (not regressed).
- [ ] 6.4 Record the verdicts in `TESTING.md` (new items under this change), and mark the RELEASE.md
      cleanup note if this closes a 1.0 blocker.
