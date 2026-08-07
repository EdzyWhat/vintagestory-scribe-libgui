## Context

Scribe's settings surface builds one `ScribeNumericField` (`src/Mod/ScribeNumericField.cs`) per numeric
preference. Since `scribe-settings-followups 3.4` those fields step on Up/Down arrow while focused, and
since `refine-settings-and-window-chrome` a step writes through LIVE (the +/- buttons and arrow keys are
always in range), which fires the host's `onChanged` → `Normalized()` → `ValueKey`-remount of the field.
That remount unmounts and rebuilds the field, so focus must be re-established afterward or the next key
goes nowhere.

The intended re-focus handshake (in `ScribeSettingsContent.NumericField`, `src/Mod/ScribeSettingsContent.cs`):

- Each field id has a persistent, host-owned `FocusNode` from `ScribeNumericFocusRegistry.NodeFor(id)`
  that survives the remount (an internal node would be disposed with the widget).
- On a step the field calls `onStepped` → `focus.ArmAutoFocus(id)`, setting a one-shot `armedId`.
- On the rebuild, the new field mounts with `autoFocus: focus.ShouldFocus(id)`. `ShouldFocus` returns
  true once for the matching id and clears `armedId` (self-consuming because each id builds exactly one
  field per pass). A true read makes `InitState` call `focusNode.RequestFocus()`.

**Symptom (2026-08-02 playtest).** Click a numeric field, press Up/Down: the FIRST press steps correctly,
but subsequent presses do NOT change the value — focus has silently moved to the last-touched editor row
in an open Lectern/Notebook/Tablet document, and the arrow now drives THAT row's `ScribeMultilineField`
caret.

**Where the visibility came from.** `arrow-key-line-caret-nav` (commit c767ac3, 2026-08-01) made Up/Down
LIVE keys in `ScribeMultilineField.OnKeyDown` (they were previously inert / fell through). The underlying
focus leak — focus not RELIABLY returning to / staying on the numeric field across repeated steps — was
always present, but was invisible while the leaked-to editor row ignored the arrow. Now that row consumes
it. Note that `ScribeNumericField.OnFieldKeyDown` already marks Up/Down `Handled`, so this is NOT the
numeric field mishandling the key; it is focus not being held by the numeric field when the second press
arrives.

## Goals / Non-Goals

**Goals:**
- Up/Down stepping of a focused settings numeric field works on EVERY consecutive press (3+ in a row),
  with focus staying on that field, even with a document editor open.
- Preserve `arrow-key-line-caret-nav`: a genuinely focused editor row still moves its caret by visual
  line on Up/Down.
- Preserve the +/- step buttons and the blur-commit/clamp behavior of the numeric field unchanged.

**Non-Goals:**
- No rework of the broader LibGUI focus system, the `ForceRebuild`/reconciling strategy, or the settings
  form's write-through model. Scope is strictly the focus-retention path for numeric arrow stepping.
- No `src/Core/` change, no new network packet, no new dependency.

## Decisions

### Decision: Treat this as a focus-retention bug in the re-focus handshake, not a key-handling bug
`OnFieldKeyDown` already marks Up/Down `Handled`, so the numeric field consumes the key correctly WHEN it
has focus. The failure is that on the second press the numeric field no longer has focus. So the fix must
live in the arm/consume/re-request handshake (registry + `NumericField`), not in the key switch.

### Decision (hypothesis to validate before coding): the armed one-shot does not hold across consecutive presses
The leading hypothesis, to confirm against the LibGUI source and a DEBUG focus trace before writing code:

- The re-focus arm is a ONE-SHOT: `ArmAutoFocus` sets `armedId`, and the first rebuild's `ShouldFocus`
  read consumes it. The first step therefore re-homes focus correctly (matching the "first press works"
  symptom).
- On the SECOND press the field is focused and steps, arms again, and rebuilds — but the still-mounted
  editor row (which owns its own `FocusNode` and, after `arrow-key-line-caret-nav`, actively wants
  Up/Down) can win or retain focus in the window between unmount and the remounted field's
  `RequestFocus`. Because the editor row was the last-touched focusable, focus lands there and the arrow
  drives its caret; the numeric field, now unfocused, never gets the next key.

Candidate fixes to weigh (pick the smallest that holds; validate with a frame trace):

1. **Persist the arm while the field stays focused.** Instead of a strict one-shot consumed by the first
   rebuild, keep re-arming as long as the field is the focused numeric field, so every write-through
   rebuild re-requests focus for it. (The step already calls `onStepped` each press, which re-arms — so
   the real question is whether `RequestFocus` on the remounted field reliably WINS over the editor row,
   or is being clobbered after it runs.)
2. **Have the remounted field re-assert focus more robustly** (e.g. ensure `RequestFocus` runs after the
   editor row's node has settled, or that the numeric node's `Owner` is wired before the request — the
   banked `ScribeMultilineField` lesson that `RequestFocus` needs an `Owner` to resolve its manager).
3. **Have the editor row DECLINE focus it was not given.** If focus is leaking to the editor row purely
   because it was the last-touched focusable (LibGUI's `DispatchPointerDown`/default-focus behavior), the
   editor row could refuse to treat Up/Down as caret nav unless it genuinely holds focus — but note
   `OnKeyDown` already guards on `focusNode.HasFocus`, so if the row is acting on the key it currently
   BELIEVES it has focus, which points back to the numeric field losing it (fixes 1/2) rather than the
   row misbehaving.

The design deliberately does not mandate which of these ships; the implementation task is to trace where
focus actually goes on press #2 and apply the minimal fix, favoring options 1/2 (keep focus on the
numeric field) over 3 unless the trace shows the row is the party at fault.

### Decision: verify with the DEBUG frame-trace method, not by eyeballing
Per the banked "settling loops & race diagnosis" lesson, use the DEBUG per-frame focus/`HasFocus` trace to
confirm exactly which node holds focus on each press before and after the rebuild, so the fix targets the
real transition rather than a guessed one.

## Risks / Trade-offs

- [Re-arming focus every rebuild could fight a legitimately-intended focus change, e.g. Tab away from the
  field] → Only re-request focus for the field that was just STEPPED (arm is set by `onStepped`), and only
  on its own write-through rebuild; a Tab/click that moves focus elsewhere does not step and so does not
  re-arm.
- [Timing/ordering races: `RequestFocus` on the remounted field vs. the editor row settling] → Confirm the
  ordering with the DEBUG frame trace rather than assuming; ensure the numeric node has its `Owner` wired
  before `RequestFocus` (banked lesson) so the request actually resolves its manager.
- [Regressing `arrow-key-line-caret-nav`] → Keep an explicit acceptance scenario (genuinely-focused editor
  row still line-navigates) and do not weaken `ScribeMultilineField.OnKeyDown` beyond, at most, having it
  decline focus it does not hold.
- [Regressing the +/- button unfocus fix (§8.2, the unchanged-value blur guard)] → Do not touch
  `OnFocusChanged`'s changed-value guard; the fix is confined to the re-focus arm/consume path.

## Migration Plan

Client-side behavior fix only — no data, schema, config, or persistence migration. Ships in the next
build; rollback is reverting the change. No world or save migration needed.

## Open Questions

- Which candidate fix (persist-arm vs. more-robust re-assert vs. editor-row-declines) does the frame trace
  actually justify? Resolve during implementation from the trace, not up front.
- Does the leak reproduce with any focusable last-touched (dropdown, checkbox) or only with a
  `ScribeMultilineField` editor row? If broader, the fix should still be scoped to keeping focus on the
  stepped numeric field rather than special-casing the editor row.
