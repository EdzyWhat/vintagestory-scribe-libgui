## Why

Over two months of incremental work, the nine non-tablet dialog tabs (Read, Edit, Pinned,
Notebook History, Guest Book, Inbox, Sent Assignment History, Create Assignments, Scriptorium
Transcribe) each grew their own ad hoc header-to-content transition, leaving four different
spacing/divider patterns in the codebase. Nothing about this was ever decided on purpose, so it's
visibly inconsistent: some tabs' dividers hug their header content, others float mid-gap; Create
Assignments has no divider at all. Worse, every tab's nav button is icon-only with a hover-only
tooltip, so there is currently no persistent way to tell which tab you're looking at — most
visibly, the Assignment Inbox and Sent Assignment History tabs render byte-identical panels below
the nav bar. Unify the pattern now, before more tabs are added on top of the inconsistency.

## What Changes

- Every non-tablet `ScribeDialogBase` view/tab adopts one shared header anatomy:
  - **Row 1** — the existing title bar (drag handle, title, edit/close), with its padding
    tightened to help offset the height of the new Row 2.
  - **Row 2 (new)** — a persistent subtitle: a small-caps label plus an italic descriptor (e.g.
    "GUEST BOOK: who has visited"), reusing existing nav-tooltip lang strings as the label where
    they read naturally, with new short descriptors otherwise.
  - **Row 3** — each tab's existing per-tab controls, unchanged in content: filter pills
    (Read/Inbox/Sent History), the completion-policy picker (Pinned), the "Visitor/Note" column
    headers (Guest Book), or a tab's drafting/form controls (Create Assignments, Transcribe). Not
    every tab has one.
  - **One divider**, always present, separating the header block above from the scrollable/general
    content below, at a fixed 8px-above / 4px-below spacing (the numbers already used by the
    Inbox/Sent History pattern).
- Removes the uniform `Column(spacing: 8)` gap approach from the Read view, Pinned view, Editor
  view, and Notebook History, which is what let their dividers drift away from the header content
  they're supposed to sit against.
- Guest Book keeps and formalizes its ledger-style flourish: an extra divider directly above its
  "Visitor / Note" column headers, in addition to the one universal divider below them. This is
  the one tab-specific exception to the single-divider rule, kept because it reads as a real log
  entry.
- Create Assignments and Scriptorium's Transcribe tab treat their entire non-scrollable
  form/zone content as Row 3, landing on the same single-divider rule as every list-based tab.
  Create Assignments gains a divider it previously had none of; Transcribe's existing
  title-bar-adjacent divider moves to sit after the new Row 2 instead, and its existing
  divider between its two content zones is kept as an internal content separator (not a header
  separator).
- Extends the shared header to two tabs missed in the original pass: the Inbox block's own
  "Inbox Inventory" tab (`GuiDialogScribeInbox`) and the Clockmaker's Notebook's Timer tab
  (`GuiDialogClockmakerNotebook`). Both get the Row 2 subtitle; the Inbox Inventory tab's 12-slot
  grid becomes Row 3, the Timer tab has no Row 3 (mirrors the Editor/History tabs).
- Flips which side of the divider owns the 4-unit gap: the divider now sits flush against the
  scrollable content below it (zero gap outside the scroll region), and each tab instead adds 4
  layout units of padding at the very top of its own scrolling content, inside the scroll
  viewport — so the gap scrolls away with the content instead of sitting as permanent chrome
  between the divider and the viewport.
- Relocates Row 1's window drag-grip from the trailing button group (right side) to a new
  leading slot to the left of the title text, matching the original exploration mockup, and
  reduces the title bar's left inset (dropping the extra 10px of "breathing room" that padding
  carried, now that the grip itself occupies that space) to match the right inset. A follow-up
  playtest pass then halved both the title's own inner left padding and that outer left inset a
  second time, and vertically aligned the grip's baseline with the title text's baseline (it read
  as visually dropped lower before).
- Row 2's small-caps treatment applies per WORD (each word's own first letter at full cap size,
  not just the label's first word), and every text run in the header — cap letters, small-cap
  tails, the colon, the italic descriptor — is baseline-corrected via computed font-metrics
  padding, fixing a playtest-caught bug where the smaller runs floated above the line's baseline
  instead of sitting on it.
- Tightens the gap between Row 1 and Row 2 by another 6px (on top of the divider/padding rule
  change below), across all ten tabs.
- The Tablet dialog renders neither Row 2 nor Row 3 at all, via a new `SupportsTabHeader`
  capability flag — reversing an earlier pass that let it inherit Row 2 purely because it reuses
  the same `ScribeReadContent`/`ScribeEditorContent` widgets every other surface does.
- **Round 3 (2026-09-09 playtest):** Create Assignments' Row 3 is pared back to ONLY the send-to
  row (player picker + Send button) — the staging hint, delete-checkbox, delivery toggle, and
  notice slots move back into the scrollable/general content below the divider (an earlier pass
  had put the entire drafting form in Row 3), with the delivery toggle specifically placed BELOW
  the scrollable stage tray. The redundant "Assign Tasks" heading is removed outright (heading
  text + lang key), and the two notice-slot hints are simplified to just their item names ("Task
  Notice" / "Assigned Notice").
- The Inbox Inventory tab's durable divider now sits directly under its Row 2 subtitle (it had
  been deferred behind the whole slot grid, since the grid was previously passed as Row 3
  content) — establishing a general rule that any tab sharing the Row 1/Row 2 header anatomy
  always shows its divider there.
- Small-caps size increased from 75% to 87.5% of the cap-letter size; the drag-grip's baseline
  nudge is halved (the Round 2 fix overshot, reading as too high); the established
  padding-before-first-scrollable-element idiom doubles from 4 to 8 units; and the title-edit
  `TextField`'s fixed-height default (which visibly grew the title band on entering edit mode) is
  replaced with a height matched to the title font's own line height.
- **Round 4 (2026-09-10 playtest):** Small-caps size pulled back from 87.5% to 82.5% of the
  cap-letter size (87.5% read as too close to the cap size). Row 1's multi-line growth no longer
  reserves a static max-height band regardless of the current title's actual length — it now
  measures the title's actual wrapped line count and grows the band DOWNWARD only as far as that
  needs, pushing Row 2 and the rest of the tab's content down with it (a short title's layout is
  unchanged). Create Assignments' delivery-info button is resized to match the height of the
  Local Inboxes/Send a Notice buttons beside it (computed from their own font metrics, mostly via
  padding growth rather than icon growth), the notice-inventory slots move to sit directly above
  that delivery-toggle group, and the delete-from-source checkbox merges into the same row as the
  toggle group (trailing side) instead of its own line.
- **Round 5 (2026-09-10, 2nd playtest pass):** Create Assignments' delivery-info button loses 1
  more pixel of padding on every side (on top of Round 4's button-matching resize). The
  delete-from-source checkbox un-merges from the delivery-toggle row and moves back onto its own
  full-width line, now at the very top of the content section (above the staging area) rather than
  near the bottom. Row 1's multi-line title growth gets a real fix for a bug in Round 4's own
  attempt: the band now grows by the exact same amount as the inner content box (instead of being
  clamped to "at least the single-line band height," which in practice almost never grows at all),
  so wrapping to a 2nd line no longer visibly slides the title upward — it only pushes content down.
- **Round 6 (2026-09-10, 3rd playtest pass):** Row 1's title-band sizing switches from a
  precomputed-estimate approach (a line-count guess forcing an exact `SizedBox` height, which
  produced two distinct bugs across Round 5/6 as the estimate drifted from what LibGUI's real text
  layout actually rendered) to genuine self-sizing via `ConstrainedBox`, borrowing the same
  intrinsic-measurement mechanism the Tablet's cuneiform title band already relies on: a minimum
  floor (the original single-line height) with no maximum, so the real, measured title row decides
  its own height — a short title is pixel-identical to before, a wrapping title grows to exactly
  what it needs, with no separate estimate to fall out of sync. Applies uniformly to every dialog
  (Row 2/Row 3/dividers untouched); the title-edit pencil's existing Edit-tab-only visibility was
  traced and reaffirmed as already correct, not changed.
- **Round 6 correction (2026-09-10, 4th playtest pass):** Round 6's first cut wrapped its
  `ConstrainedBox`es in `Align(Alignment.BottomCenter)`, which regressed to a much worse bug — the
  entire dialog's content collapsed to the very bottom of the whole window. Root cause: this subtree
  is a non-`Expanded` `Column` child inside a `SizedBox` pinned to the dialog's fixed total height,
  so the "unbounded" `maxHeight: float.PositiveInfinity` was actually clamped down to that fixed
  height, and `Align` fills whatever space it's handed rather than passively respecting a floor.
  Fixed by dropping `Align` entirely in favor of a fixed top `Padding` (reproducing the
  `TitleBarH`/`TitleBtnsH` gap directly) wrapping a single min-only `ConstrainedBox` around ordinary,
  non-fill-behaving widgets — same growth behavior, no widget in the chain ever fills to an ambient
  bound.
- **Round 7 (2026-09-10, 5th playtest pass — REVERT):** the Round 6 correction's `Align` removal also
  deleted the only step that horizontally CENTERED the title row within the full-width band, since
  `Align(Alignment.BottomCenter)` centers on both axes, not just bottom-anchors — regressing the
  whole grip+title+trailing-buttons group flush against the left edge ("ruined the orientation left
  and right"). Rather than patch a third variant of the `ConstrainedBox`/`Align` chain (two
  regressions in two rounds), Round 6 and its correction are reverted wholesale back to Round 5's
  two-nested-`SizedBox`-plus-`Align(BottomCenter)` structure — restoring centering and keeping the
  outer box's height explicit/bounded (so `Align` only ever fills that bounded height, never the
  whole dialog). To avoid reintroducing Round 5's own disclosed "jump on wrap" bug, `titleLineH` is
  fixed to use the title's real font metrics instead of the mismatched `CuneiformMetrics` ratio Round
  5 used.
- Purely visual/structural — no change to any tab's underlying data, network messages, or save
  format.

## Capabilities

### New Capabilities

None — this formalizes a shared layout rule inside the existing `scribe-dialog-base` capability
rather than introducing a new one.

### Modified Capabilities

- `scribe-dialog-base`: adds the shared tab-header anatomy requirement (Row 1 padding, the new
  Row 2 subtitle row, Row 3 placement, the universal single-divider spacing rule, and the
  Guest-Book-only extra divider) that every non-tablet view/tab built on `ScribeDialogBase` follows.

## Impact

- `src/Mod/ScribeDialogBase.Layout.cs` — title bar (Row 1) padding, new shared subtitle-row (Row 2)
  builder.
- `src/Mod/ScribeReadContent.cs`, `ScribeEditorContent.cs`, `ScribePinnedContent.cs` — drop
  `Column(spacing: 8)` for explicit Pattern-A-style padding; add Row 2 subtitle.
- `src/Mod/ScribeInboxContent.cs` — add Row 2 subtitle (used by both the Inbox and Sent Assignment
  History tabs, which currently share this class and are visually identical).
- `src/Mod/GuiDialogScribeNotebook.cs` (History tab), `src/Mod/ScribeDialogBase.Guestbook.cs`
  (Guest Book), `src/Mod/ScribeAssignmentFormContent.cs` (Create Assignments), and
  `src/Mod/GuiDialogScribeScriptorium.cs` (Transcribe) — apply the unified anatomy to each tab's
  existing Row 3 content.
- `src/Mod/GuiDialogScribeInbox.cs` (Inbox Inventory tab) and
  `src/Mod/GuiDialogClockmakerNotebook.cs` (Timer tab) — apply the same unified anatomy, closing
  the gap left by the original pass.
- Every existing `ScribeTabHeader.Build` call site — drop the caller-owned fixed top padding
  between the divider and the scroll region, and add 4-unit top padding inside each tab's own
  scrollable content instead.
- `src/Mod/ScribeDialogBase.Layout.cs` (`BuildTitleBar`) — move the drag-grip into a new leading
  slot, adjust the title row's left inset, vertically align the grip to the title baseline.
- `src/Mod/ScribeDialogBase.Layout.cs` (`ScribeTabHeader.Build`) — per-word small-caps splitting,
  baseline-correcting `WidgetSpan` wrapping for every header text run.
- `src/Mod/ScribeModSystem.Assets.cs` (`RegisterCustomFonts`) — bundle and register Caudex's real
  italic cut under the `Italic` weight slot.
- Every tab's outer content `Padding` (all ten tabs) — top inset `10` → `4`, tightening the Row
  1-to-Row 2 gap by 6px.
- `src/Mod/ScribeDialogBase.Layout.cs` (new `SupportsTabHeader` flag, mirroring
  `SupportsFilterPills`), `src/Mod/GuiDialogScribeTablet.cs` (override to `false`),
  `src/Mod/ScribeReadContent.cs`/`ScribeEditorContent.cs` (thread the flag through, skip the
  `ScribeTabHeader.Build` call and related padding when false) — Tablet Row 2/Row 3 exclusion.
- `src/Mod/assets/scribe/lang/en.json` — new subtitle descriptor strings (label reuses existing
  nav-tooltip keys where they read naturally); Round 3 removes `scribe-assignment-form-heading`
  and rewords `scribe-delivery-notice-supply-hint`/`scribe-delivery-notice-output-hint`.
- `src/Mod/ScribeAssignmentFormContent.cs` (Round 3) — Row 3/content rebalance, stage-tray padding
  reverted to symmetric now that it's no longer flush against the divider.
- `src/Mod/GuiDialogScribeInbox.cs` (Round 3) — Inbox Inventory's divider moved to sit under the
  subtitle instead of after the slot grid.
- `src/Mod/ScribeDialogBase.Layout.cs` (Round 3) — small-caps size, grip baseline-nudge, title
  `TextField` height.
- `src/Mod/ScribeDialogBase.Layout.cs` (Round 4) — small-caps size (87.5%→82.5%); `BuildTitleBar`'s
  band/content-box height switched from a static `TitleMaxLines` reservation to one driven by the
  title's actual measured wrapped line count (new `CountWrappedLines` helper).
- `src/Mod/ScribeAssignmentFormContent.cs` (Round 4) — info button sized from the delivery
  buttons' own font metrics + solved-for `IconScale`; notice slots repositioned above the delivery
  toggle; delete-checkbox merged into the delivery-toggle row (with a standalone fallback for
  non-Hybrid delivery mode).
- `src/Mod/ScribeAssignmentFormContent.cs` (Round 5) — info button padding reduced another 2px
  (1px/side); delete-checkbox un-merged back to a standalone row and moved to the top of the
  content section.
- `src/Mod/ScribeDialogBase.Layout.cs` (Round 5) — `BuildTitleBar`'s band-height formula fixed so
  it actually grows in lockstep with the content box instead of being clamped to a constant that
  rarely changes; single-line titles are unaffected.
- `src/Mod/ScribeDialogBase.Layout.cs` (Round 6) — `BuildTitleBar`'s two nested `SizedBox`es (exact,
  precomputed heights) replaced with `ConstrainedBox`es (minimum-only, self-sizing); `titleLineH`
  and the `contentBoxH`/`bandH` estimate formulas removed entirely; `CountWrappedLines` demoted to
  choosing only the cosmetic cross-axis alignment.
- `src/Mod/ScribeDialogBase.Layout.cs` (Round 6 correction) — `BuildTitleBar`'s `Align(BottomCenter)`
  wrappers removed (they filled to the ambient bounded max instead of respecting a floor, collapsing
  the whole dialog to its bottom edge); replaced with a fixed top `Padding` reproducing the
  `TitleBarH`/`TitleBtnsH` gap directly, around a single min-only `ConstrainedBox`.
- `src/Mod/ScribeDialogBase.Layout.cs` (Round 7 — revert) — Round 6 and its correction both reverted;
  `BuildTitleBar`'s return statement is back to two nested, exactly-sized `SizedBox`es plus
  `Align(Alignment.BottomCenter)` (Round 5's structure), restoring horizontal centering; `titleLineH`
  now derives from the title's real font metrics instead of `CuneiformMetrics.LineHeightRatio`.
- No changes to `src/Core/`, network messages, or persisted document state.
