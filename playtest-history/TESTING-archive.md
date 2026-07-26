# Retired testing history

Groups moved out of the repo-root `TESTING.md` by the `what-to-test` skill when their
OpenSpec change was archived and all their items reached a terminal verdict (Confirmed /
Obsolete). The playtest app does NOT read this file -- it exists only so the live
`TESTING.md` stays lean while nothing is lost (the verdicts also survive in each change's
archived `tasks.md`).

## restore-row-affordance-columns

> Per-row delete/pin (hover-conditional) + drag-handle grip restored to the EDITOR view on the
> ScribeRowElement architecture — visuals + hit-testing + hover only; delete/pin are STUBS that log
> (real messaging is a later change). Committed `8e4733e`, restaged Debug 2026-07-22. Fully relaunch
> the client first. The whole check is one task (6.3) split into parts for scanning; the flagged risk
> parts are (e), (f), and (h). Archived 2026-07-22; the visual-refinement follow-up is
> `refine-row-affordance-visuals`.

- [x] `23b99917` **Hover shows icons.** In editor view, hover a task row → pin + delete + grip
      icons appear; move the mouse off → they hide. Confirm NO flicker/jump when they show/hide.
      Then hover a note row → delete + grip only (no pin), sitting at the same X as on task rows. *(6.3 a,b)*
      - **Confirmed 2026-07-22** (playtest 2026-07-22T11-10-44): "No flicker or jump." Icons appear on
        hover and hide on leave as designed.
- [x] `23b99917` **Read view has none.** In the plain read view, hover rows → NO icons appear;
      the checkbox still toggles a task done. *(6.3 c)*
      - **Confirmed 2026-07-22** (playtest 2026-07-22T11-10-44): "No icons in read view."
- [x] `23b99917` **Stub clicks are safe.** Click delete, then click pin → each logs (no crash);
      the pin icon flips visually but reverts to its real state on the next recompose (it's a stub,
      nothing persists yet). *(6.3 d)*
      - **Confirmed 2026-07-22** (playtest 2026-07-22T11-10-44): "Functions as described." No crash;
        stub log fires.
- [x] `23b99917` **Editing near the icons.** Focus a row and type: the text column is a bit
      narrower now (gutters reserved), and there's no jump as the label hands off to the input. While
      typing, hover another row's icons — your caret/text must be undisturbed. Then click a gutter
      icon while editing — it must NOT float the input onto that row. *(6.3 e,f — flagged risks)*
      - **Confirmed 2026-07-22** (playtest 2026-07-22T11-10-44): "Hovering doesn't disturb focus." The
        gutter click does not float the input onto the row (the critical check). Tester notes clicking a
        gutter icon takes focus OFF the input — that's the flagged blur-on-icon-click behavior the design
        anticipated; harmless for the stub, revisit if the real pin wants to preserve the caret.
- [x] `23b99917` **Scale + scroll.** Sweep the text-size slider across its range → the icon
      columns and icons grow/shrink with the row (no crash at the smallest size). With a list long
      enough to scroll and icons showing, scroll → icons track their row and clip at the box edge,
      with NOTHING bleeding below the list. *(6.3 g,h — (h) is a flagged risk)*
      - **Confirmed 2026-07-22** (playtest 2026-07-22T11-10-44): "icons clip as expected." No scroll
        bleed (the scissor-clobber risk did not materialize), scales without crashing.

## tighten-row-measure-and-trim-layering

> Behavior-preserving cleanup (Core is now trim-agnostic; one shared wrapped-text-height
> primitive). Applied + committed `587c548`; Core.Tests 37/37 and Mod Debug+Release both
> clean. This one item is the optional in-game confirmation that it's visibly a no-op.
> Restage Debug and fully relaunch first.

- [x] `3dab634a` **(5.3) Refactor is a visible no-op.** Restage Debug, open a lectern, and
      confirm two things unchanged by the cleanup: (a) the empty-list edit hint (open a fresh
      lectern with no rows) still renders at the right height/wrapping; (b) commit a task ending
      in a trailing Shift+Enter blank line and one with an interior newline — the trailing blank
      trims on commit while the interior break survives into read view, exactly as before. *(5.3)*
      - **Confirmed 2026-07-22** (playtest report 2026-07-22T09-29-44): "Empty list hint is good.
        Trailing trims on commit. All good." Both halves verified — the empty-list hint renders
        correctly and commit-time trailing-trim behaves as before. The refactor is a visible no-op.
## lectern-multiline-edit-input

> Focused editor rows now wrap and grow like their static label (rebased on
> `GuiElementTextArea`), and Shift+Enter inserts a hard newline. Code committed + restaged
> Debug 2026-07-21; these six are the in-game verification. Fully relaunch the client first.

- [x] `3df65ce4` **(4.2) Focus keeps a long row wrapped.** In editor mode, click into a task
      whose text is long enough to wrap over multiple lines — confirm it STAYS wrapped across
      lines (not collapsed to one line running off the left/right edge), and the input sits
      exactly where the static label was with no jump on focus OR on blur. *(4.2)*
      - **Confirmed 2026-07-21** (playtest 2026-07-21T23-52-34 + screenshot
        2026-07-21T23-49-01-3df65ce4.png): long rows stay wrapped across lines in the focused
        input, checkboxes aligned. Tester noted a small (couple-px) text shift on focus but
        judged it a POSITIVE — extra visual feedback that the right row is selected, given the
        caret is faint. No collapse to one line. Wrap-on-focus works.
- [x] `da9afea0` **(4.3) Typing grows/shrinks the row.** Type in a focused row until the text
      overflows onto a new line — confirm the row height grows, the rows below shift down, and
      the scrollbar updates; then delete back and confirm the row shrinks and rows below shift up. *(4.3)*
      - **Confirmed 2026-07-21** (playtest 2026-07-21T23-52-34): "Works as described." Typing that
        wraps grows the row and shifts rows below; deleting back shrinks it.
- [x] `fb36a059` **(4.4) Grow near the bottom + caret hold.** With a long list scrolled so the
      focused row is near the bottom, type until the row grows — confirm it scrolls into view and
      the caret stays where you're typing. Confirm plain Enter still commits-and-advances (NO
      newline) and Esc still closes with the edit saved. *(4.4)*
      - **Confirmed 2026-07-21** (playtest 2026-07-21T23-52-34): "Works as described." Grow near the
        bottom scrolls into view, caret holds, plain Enter commits-and-advances, Esc closes+saves.
- [x] `a15d70fd` **(4.5) Wrap parity, label vs input.** At a couple of text sizes and window
      widths, focus then blur a row whose text sits right at a wrap boundary — confirm the label
      and the input break onto a new line at the SAME word (no one-line reflow/jump on focus or
      blur). *(4.5)*
      - **Confirmed 2026-07-21** (playtest 2026-07-21T23-52-34): "Works as described." Label and
        input wrap at the same boundary; no reflow jump on focus/blur.
- [x] `52081f30` **(4.6) Shift+Enter inserts a newline.** Press Shift+Enter mid-text — confirm a
      hard line break is inserted at the caret, the row grows, the caret stays put, and typing
      continues on the new line. Confirm plain Enter still commits-and-advances (does NOT newline). *(4.6)*
      - **Still broken 2026-07-21** (playtest 2026-07-21T23-52-34): "The new line is created, but the
        row does not grow to resize as expected until another character keypress." The newline IS
        inserted (Shift+Enter works), but the row lags one keystroke before growing. Root cause: the
        height-change gate in `OnEditInputTextChanged` measures `block.Text` via `GetMultilineTextHeight`,
        which does not count a TRAILING newline (empty last line) as an added line — so a just-inserted
        trailing `\n` measures the same height, and only the next real char (making the last line
        non-empty) triggers the grow. Fix: count trailing newlines in the row-height measurement.
        Retest after the fix.
      - **Still broken 2026-07-22** (playtest 2026-07-22T00-02-12 + screenshot
        2026-07-22T00-01-46-52081f30.png): the row SLOT now grows (empty space appears below the
        text), but the **caret renders below/outside the input box** — the input element itself is
        still one line tall. Cause: two competing height measures. `RowHeightFixed` (my trailing-\n
        fix) sizes the row SLOT to 2 lines, but `GuiElementTextArea.Autoheight` re-measures the INPUT
        element in its `internal TextChanged` using the raw `GetMultilineTextHeight` (no trailing-\n),
        and that fires on every `SetValue` — shrinking the input back to 1 line after the recompose
        sized it. Caret on line 2 then draws under the 1-line box. Fix: disable the input's `Autoheight`
        (row growth is driven by our recompose, not the element self-sizing). Retest after the fix.
      - **Still broken 2026-07-22** (playtest 2026-07-22T00-17-46 + screenshot
        2026-07-22T00-17-14-52081f30.png): now the row is TOO TALL — a large empty gap below the caret
        before the ruling. Two rounds of guessing at how the engine counts a trailing newline have
        each moved the symptom without fixing it, so switching to a diagnostic-log approach to capture
        the ACTUAL measured line-count/heights in-game before the next fix. Retest after the numbers
        are captured and the real fix lands.
      - **Root cause found 2026-07-22** (user clarified: interior Shift+Enter ALWAYS worked; only a
        TRAILING newline failed): the culprit was never the height measurement — it was
        `ScribeDocument.SetBlockText`, which `Trim()`s a task's text on EVERY live keystroke. A
        just-typed trailing `"foo\n"` was stored as `"foo"` before any height code ran, so the row
        measured one line and never grew until the next real char (which made the `\n` interior and
        survived the trim). The three prior fixes were all downstream of that trim, which is why the
        symptom was identical each build. Fix: `SetBlockText` gains `trimTask` (default true; the live
        editor passes false) so the trailing newline survives live editing and the row grows; trailing
        whitespace is still trimmed at COMMIT (`NormalizeRowOnCommit`) per the 4.7 design. The round-3
        per-segment height measure (`MeasureWrappedTextHeightScaled`) and `Autoheight=false` both stay
        — they're now correct AND actually reached. Core.Tests 38/38 (3 new cases lock in the
        trim/no-trim + blank-guard behavior). Diagnostics removed. Retest.
      - **Confirmed 2026-07-22** (user, in-session): trailing Shift+Enter now grows the row by one line
        immediately, caret sits on the new empty line inside the box, typing continues. All three fixes
        compose (SetBlockText trimTask:false + per-segment measure + Autoheight off).
- [x] `bf0a3e2a` **(4.7) Trailing trim + newline round-trip.** Add a trailing Shift+Enter (blank
      last line) and commit — confirm the row does NOT stay tall/empty (trailing trimmed) while a
      newline placed BETWEEN two words survives. Switch to read view (interior newline renders as a
      hard break), then reload the world and confirm it persisted. *(4.7)*
      - **Confirmed 2026-07-21** (playtest 2026-07-21T23-52-34): "Works as described." Trailing blank
        line is trimmed on commit; an interior newline survives, renders as a hard break in read view,
        and persists across a world reload.
## lectern-edit-in-place-rows

> S2 of the row-list rework (committed `466a1a4`, restaged Release 2026-07-21). The editor
> view now uses the same custom `ScribeRowElement` as the read view, with one floating input
> on the focused row. **Known scope note carried into these tests:** moving onto the shared
> row element removed the editor's per-row **delete and pin** buttons (the shared row layout
> has no icon gutters) — reorder returns in S3, but delete/pin have no replacement yet. That's
> a flagged regression under review, not something these items test.

- [x] `8956eef7` **(6.1) Build + test gate.** Debug and Release build clean; `dotnet test`
      (Core.Tests) all green.
      - **Confirmed 2026-07-21** by re-running the gate directly this session (not just trusting
        the agent report): Debug build 0/0, Release build 0/0, Core.Tests 35/35 passed.
- [x] `8275246c` **(6.2) Caret word/line navigation.** In an editor row, press Cmd+Arrow (jump
      to line ends), Option+Arrow (skip by word), and hold Shift with each to extend the
      selection. Confirm every one moves the caret/selection as described AND none of them types
      a stray character into the text.
      - **Confirmed 2026-07-21** (playtest report 2026-07-21T13-03-17): "All these functions
        work." — word/line caret nav and shift-extend all behave, no stray characters.
- [x] `8634ee5d` **(6.3) Commit, navigate, revert, persist.** In an editor row: press Enter
      (commits the edit and moves focus to the next row down), Shift+Tab (commits and moves up),
      and Esc (throws away the in-progress edit, restoring the row's prior text); then click
      away from a row (should commit). Finally switch read↔edit view and fully reload the world —
      confirm the committed edits are all still there.
      - **Still broken 2026-07-21** (playtest report 2026-07-21T13-03-17): Enter (commit+advance)
        and Shift+Tab (commit+retreat) both work. **Esc does NOT revert** — it closes the whole
        dialog instead of restoring the row's prior text. The reload-persist half was not reached.
        NOTE: the tester argues Esc *should* close the GUI (fast escape from danger, e.g. bears)
        rather than revert — this is a spec-design question, not just a bug (see decision surfaced
        to the user). Retest after the Esc behavior is decided and the persist half is exercised.
      - **Fix staged (awaiting retest) 2026-07-21:** decision made — Esc **closes the dialog** is
        the intended behavior (not a bug). Removed the revert interception so Esc bubbles to the
        base dialog close; blur-commit saves the pending edit on the way out. On retest, confirm
        Esc closes AND the last edit persisted, and exercise the view-switch + reload persistence
        half that wasn't reached before. (tasks.md 4.4 / 6.3.)
      - **Confirmed 2026-07-21** (playtest report 2026-07-21T14-19-12): "Enter commits. Shift+Tab
        commits and moves up. ESC closes and commits. Edit→Read and leave world, the changes are
        saved." All four behaviors + the reload-persist half verified.
- [x] `2dbcaf33` **(6.4) No focus jump.** Click a row to focus it (the floating input appears),
      then click away to blur it (the static label returns). Watch the text closely — it should
      sit at the exact same position, baseline, and size in both states, with no visible jump or
      shift as the input swaps in and out.
      - **Still broken 2026-07-21** (playtest report 2026-07-21T13-03-17, general/side note):
        clicking a task row in edit mode makes the text appear to shift slightly — the tester
        attributes it to the floating input's chrome/border and asks to drop the border in favor
        of just the subtle background color. This is exactly the "input border bakes in the
        unclipped pass" bleed the S2 agent flagged as a fallback. Fix: apply the borderless
        override on `ScribeRowTextInput`, then retest.
      - **Fix staged (awaiting retest) 2026-07-21:** `ScribeRowTextInput.ComposeTextElements` now
        skips the base emboss border + dark fill, keeping only the subtle focused-highlight
        background. On retest, focus/blur a row and confirm the text no longer jumps. (tasks.md 6.9.)
      - **Confirmed 2026-07-21** (playtest report 2026-07-21T14-19-12): "focus/blur a row and I can
        confirm the text no longer jumps." Borderless-input fix verified.
- [x] `8f37a2f3` **(6.5) Editor clips and scrolls; widths match.** In editor view, add enough
      rows to overflow the box. Scroll: rows should slide continuously and clip cleanly at the
      top/bottom edge (not blink out / pop in fixed spots). Then switch between read and editor
      view and confirm the row list is the exact same width in both.
      - **Still broken 2026-07-21** (playtest report 2026-07-21T14-19-12 + screenshots): the
        **widths match** in editor and read (that half passes), and scrolling is continuous, BUT
        the **clip is not clean** — see general-note "Extra 3" and screenshot
        2026-07-21T14-17-01-general.png: row rulings/chrome render *past the top clip boundary* and
        bleed over the area above the list. Related bleed below the list in
        2026-07-21T14-12-21-general.png (a newly-added task's input drawn near screen bottom,
        outside the box). Retest after the clip-bleed fix (tracked as a follow-up, see 6.10/6.11).
      - **Confirmed 2026-07-21** (playtest report 2026-07-21T21-37-15): "Scroll is clean and widths
        are same." After the 6.10 clip fixes, both halves now pass — continuous clean scroll and
        matching read/editor widths.
- [x] `9a2eddd4` **(6.6) Read view still fine.** After all the shared-width and scroll changes,
      go back to the plain read view and confirm nothing regressed: clicking a task's checkbox
      toggles it done, the lined-paper ruling draws correctly, and a long list clips/scrolls
      properly.
      - **Confirmed 2026-07-21** (playtest report 2026-07-21T14-19-12): "All good." Read-view
        checkbox toggle, ruling, and scroll all still work after the shared-width/scroll changes.
- [x] `3ed89b7c` **(6.8) Re-click keeps focus + places caret.** Click into an editor row (caret
      appears), then click that SAME row again: the caret should stay, you can keep typing, and
      the caret should jump to where you clicked. Then click a DIFFERENT row and confirm focus
      moves there and the prior row's edit committed.
      - **Fix staged (awaiting retest) 2026-07-21** (from report 2026-07-21T13-03-17 general note):
        re-clicking the focused row used to blur it — caret vanished, typing dead, only a
        different-row click recovered it. Root cause decompile-confirmed (overlapping non-focusable
        row ate the mouse-down and `GuiComposer.OnMouseDown` blurred the input); fixed by having the
        focused row yield its text-column mouse-down to the input. This retest confirms the fix.
      - **Confirmed 2026-07-21** (playtest report 2026-07-21T14-19-12): "Confirm the caret moves
        appropriately on re-click in input." Re-click keeps focus and places the caret; fix holds.
- [x] `21041e34` **(6.10) Content bleeds past the clip boundary.** With a list long enough to
      overflow, scroll and add tasks; confirm NO row ruling, chrome, or text-input renders outside
      the dialog box — not above the title, not below the box over the buttons, not down the screen.
      - **Still broken 2026-07-21** (playtest report 2026-07-21T14-19-12, screenshots): a
        newly-added task's floating input drew near the bottom of the screen, far below the box
        (2026-07-21T14-12-21-general.png), and row rulings/chrome drew above the top clip boundary
        while scrolled (2026-07-21T14-17-01-general.png). Prime suspect: `GuiElementTextInput`'s
        `GlScissorFlag(false)` clobber defeating the dialog's `BeginClip` (already noted in
        VSAPI-NOTES for the mixed list). Needs investigation + fix.
      - **Fix staged (awaiting retest) 2026-07-21:** root cause confirmed by decompiling
        `VintagestoryLib.dll` — the base input's `GlScissorFlag(false)` is a global
        `GL.Disable(GL_SCISSOR_TEST)` that doesn't restore the `BeginClip` scissor stack.
        `ScribeRowTextInput.RenderInteractiveElements` now re-asserts the clip
        (`PushScissor(InsideClipBounds)`/`PopScissor()`) after the base renders. Recorded in
        VSAPI-NOTES.md. Retest via 6.13 (this item = 6.10).
      - **Still broken 2026-07-21** (playtest report 2026-07-21T20-58-36, screenshot
        2026-07-21T20-48-55-21041e34.png): the input clip-restore helped, but TWO distinct things
        still bleed, and the tester diagnosed both precisely:
        (1) **The `AddRowDivider` inset lines bleed** into the Text Size / Collapse / Done Editing
        area. These are `AddInset` STATIC elements — they draw in the always-unclipped static pass
        (VSAPI-NOTES "BeginClip doesn't visually clip a mixed static+interactive list"), so the
        scissor re-assert can't touch them. **Tester's call: the dividers are undesirable and should
        never be drawn at all** — so the fix is to remove `AddRowDivider`, not to clip it (the
        `ScribeRowElement` already bakes its own lined-paper ruling; these engine inset lines are
        redundant + ugly).
        (2) **A just-created task's input renders out of bounds until the next recompose.** Tester:
        it only appears right after Add Task when the new row would overflow, and "Done Editing →
        Edit removes the bugged symptoms" — i.e. it self-heals on a fresh compose. So it's a
        first-compose-only state, not a persistent clip failure — the input is composed/positioned
        before the scroll-into-view (6.11) fully settles its bounds for that first frame. Needs
        investigation of the Add-Task compose/scroll ordering.
      - **Fix staged (awaiting retest) 2026-07-21:** both residual bleeds addressed.
        (1) `AddRowDivider` removed entirely — the `AddInset` divider lines drew in the unclippable
        static pass and were redundant with `ScribeRowElement`'s own baked ruling.
        (2) `ScribeRowTextInput.RenderInteractiveElements` now skips drawing when its row is scrolled
        fully outside the clip window — the base input clips its own text to its own bounds, not the
        dialog window, so a focused input on an off-screen row painted unclipped below the box. Skip
        is focus-safe (reads live `renderY`, so it also covers the scroll-out-while-focused case, not
        just first-compose). Retest via 6.13.
      - **Confirmed 2026-07-21** (playtest report 2026-07-21T21-37-15): "No row ruling, chrome or
        input renders outside the dialog box." Both fixes hold — dividers gone, off-screen input
        no longer bleeds.
- [x] `d9602714` **(6.11) Add-task while overflowing scrolls the new row into view.** With a list
      long enough to overflow the box, click Add Task; confirm the new (focused, empty) task is
      scrolled into the visible area rather than appearing below the box / off-screen.
      - **Still broken 2026-07-21** (playtest report 2026-07-21T14-19-12, general note "Extra 1"):
        the new task is appended at the bottom out of view and the list doesn't scroll to it, so
        it appears out of bounds. Fix: scroll to the newly focused row after Add Task. (Related to
        6.10 — the out-of-bounds row is only visible because of the clip bleed.)
      - **Fix staged (awaiting retest) 2026-07-21:** a one-shot `scrollFocusedRowIntoView` flag
        (set by Add Task + Enter/Shift+Tab navigation, consumed in `ComposeEditorView`) scrolls the
        focused row fully into view before clamping; one-shot so it never overrides the user's own
        scroll on an unrelated recompose. Retest via 6.13 (this item = 6.11).
      - **Confirmed 2026-07-21** (playtest report 2026-07-21T20-58-36): "the new empty task scrolls
        into view inside the box." Scroll-into-view works.
- [x] `aa8573bd` **(6.13) Clip + scroll-into-view retest.** With a list long enough to overflow:
      scroll around and confirm NOTHING renders outside the box — no ruling/chrome/text-input above
      the title, below over the buttons, or down the screen. Then click Add Task while
      scrolled/overflowing and confirm the new empty task scrolls into view inside the box. Also
      Enter/Shift+Tab to a row near the top or bottom edge and confirm it scrolls into view.
      - Covers the staged fixes for 6.10 (clip re-assert) and 6.11 (scroll-into-view), and the clip
        half of 6.5 that was still failing.
      - **Still broken 2026-07-21** (playtest report 2026-07-21T20-58-36): the scroll-into-view half
        passes (see 6.11), but the "nothing renders outside the box" half does NOT — the tester
        submitted this as pass but explicitly tied it to the still-broken 6.10. Two residual bleeds
        remain (redundant `AddRowDivider` inset lines drawing in the unclipped static pass; a
        new-task input out of bounds until the next recompose). Retest after the 6.10 follow-up
        (remove dividers + fix Add-Task first-compose ordering).
      - **Fix staged (awaiting retest) 2026-07-21:** both residual bleeds fixed (dividers removed;
        off-screen input render-skip) — see the 6.10 entry above. This retest now covers the full
        "nothing outside the box" claim plus the scroll-into-view (6.11) that already passed. On
        retest, specifically confirm the divider lines are GONE (not just clipped) and Add Task on a
        full list shows no stray input below "Done Editing".
      - **Confirmed 2026-07-21** (playtest report 2026-07-21T21-37-15): all three sub-checks pass —
        nothing renders outside the box, Add Task scrolls the new empty task into view, and
        Enter/Shift+Tab to an edge row scrolls it into view. The 6.10/6.11 fixes and the 6.5 clip
        half are all now confirmed.
- [x] `cd69a96f` **(6.14) Checkbox-to-text margin.** In the editor, focus a task row and confirm
      there's a small, comfortable gap between the checkbox and where the text/input starts (not
      flush against the box). Check it holds for both the static label and the focused input, and
      at a couple of text sizes.
      - **Still broken 2026-07-21** (playtest report 2026-07-21T21-37-15 + screenshot
        2026-07-21T21-37-08-general.png): "functionally no distance between the checkbox and the
        text input — we need a little bit of margin." Confirmed in the screenshot (text flush to the
        checkbox on the focused row). Fix in the shared `RowTextLayout.TextX` so label + input move
        together. Purely cosmetic; not a regression from the clip fixes.
      - **Fix staged (awaiting retest) 2026-07-21:** added a `CheckboxTextGap` config knob
        (default 8, text-size-scaled) folded into the shared `RowTextLayout.TextX`, so both the
        static label and the floating input inherit the gap in lockstep (tasks only). Retest via
        6.15 — confirm the gap looks comfortable and scales with text size.
      - **Confirmed 2026-07-21** (user confirmed in-session): the checkbox-to-text margin looks
        good — comfortable gap between the checkbox and the text/input.
## skeuomorphic-lectern-gui

- [x] `9e2c1a30` **(3.5) Scrolling a long list.** Open a lectern and add enough tasks and
      notes that they don't all fit in the box and a scrollbar appears on the right. Test all
      three ways of scrolling, in **both** the plain right-click read view and the
      shift+right-click edit view:
      1. **Mouse wheel:** roll the wheel up and down. The whole list of rows should slide
         up and down smoothly as one piece — the text, checkboxes, and any icons all moving
         together. Each notch of the wheel should move the list by about one task row (not
         two). *Broken would look like:* rows blinking out and reappearing in fixed spots
         while seeming stuck, or (in edit view) the text sliding but the checkbox outlines
         and text-box borders staying frozen in place.
      2. **Dragging the scrollbar handle:** collapse the ImGui window first (see note at
         top), then click and hold the scrollbar handle and drag it up and down. The handle
         should follow your mouse the whole way, AND the rows should slide along with it
         continuously as you drag — not stay still and only jump to the new spot when you
         let go. *Broken would look like:* the handle stops moving almost immediately (after
         about one pixel), or the handle moves but the rows don't follow until release.
      3. **Reaching every row:** by whatever method, confirm you can scroll all the way to
         the very last row and it's fully visible, and back to the very first — nothing is
         permanently cut off at the top or bottom, and no row spills out below the box's
         bottom edge or above its title.
      - **Confirmed 2026-07-20** (playtest report, fresh build): wheel scroll works, dragging
        the scrollbar handle works (rows follow), clicking in the scroll track works, and all
        rows are reachable. Resolves the scroll requirement that was reopened for 3.4a/3.4b.
        (A follow-up on partial-row visibility at the scroll boundary is tracked separately —
        see the general note in ROADMAP.md, not folded into this verdict since the tested
        behavior — every row reachable, list moves as one — is met.)
- [x] `c0c0fc4d` **(4.4) Dialog fits and reads well.** Place a lectern and right-click to
      open it at normal standing distance (don't back away or step unusually close). Check
      three things, and if any looks wrong say which one and what was off (rather than just
      pass/fail):
      (a) the whole dialog fits on screen with nothing cut off at the edges and nothing
      overlapping your hotbar or other on-screen elements, at the default GUI scale;
      (b) the row text at the default text size is comfortably readable without leaning
      toward the screen;
      (c) the shape (taller than it is wide) looks deliberate, not squeezed or cramped.
      - **Confirmed 2026-07-20** (playtest report): (a) fits, (b) readable, (c) proportions
        read as intentional — all good.
- [x] `e624a788` **(5.4) Backdrop renders correctly.** With the lectern open, confirm the
      parchment backdrop image sits correctly behind the content in both read and edit view,
      and that no row is drawn underneath an opaque part of the backdrop (i.e. no text hidden
      or half-hidden behind the background).
      - **Confirmed 2026-07-20** (playtest report): backdrop renders correctly.
- [x] `805e78a7` **(6.6) Hovering icons doesn't disturb typing.** In edit view, click into a
      note and start typing. While typing, move your mouse over a different row so its
      delete/pin icons appear, then move it away again. Your typing cursor should stay exactly
      where it was and your text should be unaffected — moving the mouse over other rows must
      not interrupt what you're typing.
      - **Confirmed 2026-07-20** (playtest report): typing continues undisturbed while hovering
        over an icon with a textarea active, and while hovering other rows mid-type.
- [x] `0f961614` **(7.5) Pin survives reload.** Pin a task, switch between read and edit view,
      then fully quit the game to desktop and relaunch. The task should still be pinned.
      - **Confirmed 2026-07-20** (playtest report): pin survives view-switching and a full
        quit/relaunch, as specified.
      - *(Earlier note, still open as a separate backlog item, not this test:* the pin icon
        is currently only visible on hover, not always-shown once a task is pinned — a real
        UX gap but a design change, tracked separately.)
- [x] `88d4f7b2` **(9.3) Full scroll-and-edit pass.** With a list long enough to scroll:
      scroll down partway, then in edit view drag a row by its handle to reorder it (confirm
      it lands where you dropped it and the click lined up with the row you grabbed); drag the
      text-size slider across its range (confirm rows grow/shrink and re-wrap without
      overlapping); pin and unpin a task. Overall, confirm nothing regressed versus how the
      lectern behaved before — no rows spilling out of the box, no frozen or misplaced pieces,
      no lost clicks.
      - **Confirmed 2026-07-20** (playtest report): drag-reorder works and lands correctly, no
        regressions. Feature request logged separately (not a defect): drag-reorder currently
        moves rows only on drop; a smooth "rows spread to show the drop target" animation
        while dragging would give better feedback — tracked in ROADMAP.md.

## add-imgui-configlib-tuning

- [ ] `8a356779` **(2.6) Debug sliders recompose live.** In a Debug build, open the lectern
      and press VSImGui's toggle hotkey to show the overlay. Drag each "Lectern Layout" slider
      and confirm the dialog updates live to match. Confirm `scribe-client-config.json` on disk
      does NOT change until you press the "Save" button.
      - **Confirmed 2026-07-20** (playtest report): "All functional."
      - **Obsolete 2026-07-23:** superseded by the LibGUI rebuild. the VSImGui Debug sliders are dead on Apple Silicon and tune native `GuiComposer` layout knobs (`VisibleListHeight`/`RowSpacing`/etc.) that LibGUI replaces with its theme/flex model.
- [ ] `c2729a2d` **(5.1) Diagnose the frozen-chrome symptom.** *This item predates the scroll
      fixes and was written to diagnose them; re-evaluate whether it's still needed now that
      3.4a is in.* Using the live Debug sliders, drag `VisibleListHeight` and `RowSpacing` and
      watch the edit-view rows: the goal is to see whether the parts of a row that used to
      freeze in place (checkbox outlines, text-box borders, drag handles) now move together
      with the rest of the row as the list resizes.
      - **Obsolete 2026-07-21** (playtest report 2026-07-21T08-13-42): user confirms a row's
        elements now move together as one unit with no separate static/interactive pieces, and
        asked to disregard this item. It was a diagnostic written against the pre-rework mixed
        static+interactive architecture; the row-list rework replaces that rendering approach
        wholesale (S1 already unified the read view; S2 does the editor), so the slider-based
        frozen-chrome diagnosis no longer applies. Retired rather than deleted.
      - **Obsolete 2026-07-23:** superseded by the LibGUI rebuild. already retired; the native frozen-chrome diagnostic it ran has no analog in the LibGUI widget tree.
- [ ] `8c7c2b2a` **(4.4) No regression from the new references.** Fully relaunch the client
      after restaging and confirm the lectern still opens and behaves normally, with no errors
      from the added VSImGui/ConfigLib references.
      - **Confirmed 2026-07-20** (playtest report): opens and behaves normally.
- [ ] `171935d3` **(3.4) ConfigLib panel edits apply.** With both ConfigLib and VSImGui
      installed, open ConfigLib's in-game settings panel, change an exposed "Lectern Layout"
      field, and save. Confirm the lectern reflects the new value.
      - **Confirmed 2026-07-20** (playtest report): lectern reflects the saved value.
      - **Obsolete 2026-07-23:** superseded by the LibGUI rebuild. tunes native `ScribeClientConfig` layout fields via the ConfigLib panel; those layout knobs move to LibGUI's ThemeData/`libgui.json` under the deferred theme-extraction change.
- [ ] `d83db914` **(3.5) Loads without ConfigLib.** With ConfigLib NOT installed, confirm the
      mod still loads and the lectern opens normally, with no missing-dependency warning.
      - **Confirmed 2026-07-20** (playtest report): loads and opens normally.

## add-pinned-task-foundation

- [x] `7f3826e7` **(7.8) Per-player pin + complete loop.** Restage and relaunch first. Then, on
      a lectern with a couple of tasks: (a) pin/unpin a task in the editor and confirm the resting
      pin tint/glyph reflects YOUR pin state; (b) relog and confirm the pin state persists; (c)
      break the lectern and re-place it, and confirm the pin still shows (same document identity);
      (d) in the READ view, check a task's checkbox and confirm it completes AND (default setting)
      the pin is removed from your list. *(add-pinned-task-foundation 7.8)*
      - **Still broken 2026-07-24** (user playtest, submission 2026-07-24T15-27-15): parts (a) pin/unpin
        tint, (b) persist across relog, and (c) break→replace all **work**. Part (d) fails: checking a
        task in read view does NOT complete it or remove the pin. User's key clarification — the pin and
        checkbox visibly *activate in the UI*, but the underlying mutation (completion, pin removal) never
        takes effect, in EITHER read or edit mode; and completing sometimes resets the scroll to the top
        despite nothing being deleted. Reads as a client-optimistic toggle with the server action not
        landing (or not being reflected back): the `ScribeCompleteTaskMessage` / `ScribeSetPinMessage`
        round-trip isn't applying server-side, or its `ScribePinnedSetMessage` re-push isn't repainting.
        The Atlas suite proves the server handlers work when driven directly (SetPinForPlayer /
        CompleteTaskForPlayer), so the gap is likely in the GUI→network wiring or the client cache
        repaint, not the store logic. Needs investigation before retest.
      - **Confirmed 2026-07-24** (user playtest, after restage of the complete-vs-unpin split + `[scribe]`
        server tracing). Part (d) now works end-to-end, verified at every layer:
          - Server trace (`build/scribe-log.sh`) on a read-view checkbox click:
            `complete-task received … / complete: task … done False -> True / unpin: removed …'s pin` —
            completion and the conditional unpin both fired on the authoritative document.
          - **Completion persists**: closing and reopening the lectern shows the task still checked (the
            server `done` change synced back and stuck — not just the optimistic client flip).
          - **Pin repaint lands**: the completed task's resting pin tint/glyph disappeared in the GUI, so the
            `ScribePinnedSetMessage` re-push repainted the client cache.
        The earlier "Still broken" was against a build predating this restage; the fused op is now split into
        `CompleteTaskStep` + `ConditionalUnpinStep` (semantics unchanged) and the whole GUI→network→server→
        resync→repaint chain is closed. Parts (a)/(b)/(c) remain confirmed. (The scroll-reset-on-complete
        also reported in that submission is tracked separately as `92d41071` under
        add-lectern-row-affordances-libgui — not re-observed this session, awaiting a deliberate retest.)

## add-empty-task-lifecycle

> Retired from TESTING.md 2026-07-25 (change archived; all items terminal — 6 Confirmed + 1 Obsolete).
> Implemented 2026-07-25. New tasks start EMPTY with a dimmed "New task…" ghost hint; an empty task row
> self-destructs on blur (focus moves to the row above); Enter on an empty row is a no-op;
> switch-to-read/close/autosave never persist an empty task; the read view filters any stray empty task.
> Core model no longer rejects blank task text (cleanup moved to the editing layer). Superseded the two
> pre-implementation placeholders (`05727f66`/`f34ea553`).

- [x] `9d85da89` **Add task starts empty.** Click "Add task" (or Enter on a non-empty row) — the new
      row is empty with a dimmed "New task…" ghost hint, ready to type, no boilerplate to clear.
      *(add-empty-task-lifecycle 6.2)*
      - **Confirmed 2026-07-25** (playtest submission 2026-07-25T22-36-25, reported against superseded item
        `05727f66` "New tasks init empty"): "Works." New task rows now start empty and ready to type
        instead of pre-filled with "New task".
- [x] `8c411565` **Abandoned empty add disappears.** Add a task, type nothing, click away (or Tab off
      it) — the empty row vanishes and does not persist across reload. *(add-empty-task-lifecycle 6.2)*
      - **Confirmed 2026-07-25** (playtest submission 2026-07-25T23-02-59): "Yes this works." An abandoned
        empty add vanishes on blur and does not persist across reload.
- [x] `577159f1` **Clear-to-delete a row.** In an existing task, Cmd/Ctrl+A then Delete to empty it,
      then blur (click away / Tab) — the row is removed and focus lands on the row above.
      *(add-empty-task-lifecycle 6.3)*
      - **Confirmed 2026-07-25** (playtest submission 2026-07-25T22-36-25, reported against superseded item
        `f34ea553` "Emptied row auto-deletes on commit"): "Works." Cmd/Ctrl+A → Delete → blur now removes
        the emptied row (the pre-change no-op is fixed).
- [x] `3433d07d` **First/only empty row.** Empty the first row and blur — focus moves to the new first
      row; empty the only row and blur — no crash, editor shows the empty-state hint.
      *(add-empty-task-lifecycle 6.4)*
      - **Confirmed 2026-07-25** (playtest submission 2026-07-25T23-02-59): "Works. Both cases." First-row
        blur re-homes focus to the new first row; only-row blur shows the empty-state hint, no crash.
- [ ] `6f9ef4c2` **Empty note kept.** Leave a freeform text section empty and blur — it is NOT
      auto-removed (only task rows self-destruct). *(add-empty-task-lifecycle 6.5)*
      - **Obsolete 2026-07-25** (playtest submission 2026-07-25T23-02-59): tester — "These aren't on the
        page to test. Delete this test item, we aren't prioritizing Freeform Text." Freeform text sections
        aren't a surfaced/prioritized feature, so the empty-note-kept check no longer applies in practice.
        The code still guards it (only task rows self-destruct; text sections are untouched) — kept as an
        Obsolete record rather than deleted, per the verdict lifecycle.
- [x] `76b2a6ba` **Switch/close drops empty task.** With an empty focused task, switch to read view or
      close the dialog — no empty task is saved or shown; reload confirms none persisted.
      *(add-empty-task-lifecycle 6.6)*
      - **Confirmed 2026-07-25** (playtest submission 2026-07-25T23-02-59): "Works." Switching to read /
        closing with an empty focused task neither saves nor shows an empty task; none persists on reload.
- [x] `7bdddcd1` **Enter on empty is a no-op.** Press Enter on an already-empty task row — no second
      empty row is stacked. *(add-empty-task-lifecycle 4.5)*
      - **Confirmed 2026-07-25** (playtest submission 2026-07-25T23-02-59): "Works." Enter on an empty task
        row stacks no second empty row.
