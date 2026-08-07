## 1. Confirm the mechanism (optional but recommended)

- [x] 1.1 Read the tablet's `SlotModified`/state-resolve path (`GuiDialogScribeTablet.OnHotbarSlotModified`,
      `ItemScribeTablet.ResolveMaterialState`) to answer design Open Question: is the wet→hard/fired
      transition independent of the DocId-mismatch close, or does it rely on it? Record the answer.
      **Answer: independent.** The Harden/Smelt transitions swap the material variant
      (`clay-red` → `clay-red-hard`/`-fired`) but (a) carry the document bytes forward via
      `CarryStackData`/`DoSmelt`, so the `DocId` is UNCHANGED, and (b) every variant is the same
      `ItemScribeTablet` collectible, which is an `IScribeDocumentItem`. So on an in-place transition the
      strict `ActiveHandItemHostsThisDocument()` already returns true (matching DocId) and the dialog does
      NOT close today. Moving `OnHotbarSlotModified` to a presence-only check is purely additive — presence
      is likewise true across the transition — so it cannot regress the transition. (The tablet's `_state`
      is fixed at construction; the dialog doesn't live-swap wet→read-only either way, which is out of scope
      here.)

## 2. Shared helper

- [x] 2.1 Add `ActiveHandHoldsAnyScribeDocumentItem()` to `ScribeDialogBase` next to
      `ActiveHandItemHostsThisDocument()` — the presence half of the existing check (active hand stack's
      collectible is `IScribeDocumentItem`), WITHOUT the DocId comparison. Document why it exists (the
      in-place re-sync flicker) referencing this change.

## 3. Notebook + Clockmaker fix

- [x] 3.1 In `GuiDialogScribeNotebook.OnHotbarSlotModified`, close the dialog only when
      `ActiveHandHoldsAnyScribeDocumentItem()` is false — i.e. stop routing an in-place same-slot
      modification through the DocId-strict `OnActiveSlotChanged`. Leave `OnActiveSlotChanged`
      (the real `AfterActiveSlotChanged` hand-switch) calling the DocId-strict guard unchanged.
- [x] 3.2 Verify `GuiDialogClockmakerNotebook` inherits the fix (no own handlers) — no code change,
      just confirm it still compiles and inherits `OnHotbarSlotModified`/`OnActiveSlotChanged`.
      Confirmed: `GuiDialogClockmakerNotebook : GuiDialogScribeNotebook` declares neither handler; it
      inherits both and compiles clean.

## 4. Tablet fix

- [x] 4.1 Apply the same `OnHotbarSlotModified` change to `GuiDialogScribeTablet`, preserving the
      wet→hard/fired in-place transition path per task 1.1's finding (gate ONLY the flicker-causing
      DocId-mismatch close behind the presence rule; keep any state-transition refresh/close intact).

## 5. Build + verify

- [x] 5.1 Build and run the local verify loop (`./build/verify.sh Debug` with `VINTAGE_STORY` set):
      Core suite + Atlas suite must stay green. No new tests expected (client GUI-lifecycle behavior).
      Green: build succeeded, Core 286/286, Atlas 25/25.
- [x] 5.2 Restage the mod (`build/restage.sh Debug`) so the in-game test uses the fixed build.
      Done as the final stage of `verify.sh` (93 files staged into the Mods folder).

## 6. In-game verification (manual)

- [x] 6.1 Manually test in-game: obtain a Scribe item you did NOT craft (creative-give or pick one up),
      right-click to open it for the FIRST time, and confirm the dialog OPENS AND STAYS — no flicker,
      no need for a second right-click. Repeat for notebook, clockmaker's notebook, and tablet.
      Confirmed 2026-08-06 (playtest): notebook, clockmaker's notebook, wet tablet, wax tablet, and lectern
      all open and stay on first right-click; Shift+RC quick-add on a fresh item also opened cleanly.
- [x] 6.2 Manually test in-game (regression control): confirm a self-CRAFTED item never flickered and
      still opens cleanly; confirm dropping the item closes the dialog; confirm switching the hotbar to
      a DIFFERENT Scribe item closes the open dialog. All three CONFIRMED 2026-08-06 (playtest): self-crafted
      opens cleanly (no regression); dropping closes; switching to a different Scribe item closes.
- [x] 6.3 Manually test in-game (tablet transition): with a wet tablet's dialog open, let it (or force it
      to) transition wet→hard, and confirm the transition still behaves correctly (not regressed).
      Confirmed 2026-08-06 (playtest): wet→hard with the dialog open behaves correctly, not disrupted.
- [x] 6.4 Record the verdicts in `TESTING.md` (new items under this change), and mark the RELEASE.md
      cleanup note if this closes a 1.0 blocker. TESTING.md items added under a new
      `## fix-item-dialog-first-open-flicker` group (`f991b645`, `582eaeb0`, `341ebeae`, `eaa0965b`,
      `0b36d79f`, `5d0e9322`, `45462079`) — ALL confirmed 2026-08-06 via live playtest. RELEASE.md V.5
      updated: fix applied, verify green, and in-game confirmed — ready to archive in Track A.
