## Context

See `proposal.md` - Why. Nine tabs across five dialog hosts (Lectern/Notebook family: Read,
Edit, Pinned, History; Assignment Desk: Create Assignments, Inbox, Sent Assignment History;
Lectern: Guest Book; Scriptorium: Transcribe) each currently build their own header-to-content
transition inline, inside their own content-state `Build()` method. There is no shared helper for
this today — every tab hand-rolls `Padding`/`Column`/`Divider` itself, which is how the four
divergent patterns surveyed during exploration (see the change's exploration notes) came to exist.

The dialog-wide title bar (Row 1) is already shared: it's built once per dialog in
`ScribeDialogBase.Layout.cs`'s `BuildTitleBar`, as a sibling above whichever tab's content is
currently selected — it is not part of any per-tab `Build()` method.

## Goals / Non-Goals

**Goals:**
- One shared code path produces the Row 2 subtitle + Row 3 placement + durable-divider spacing,
  so the 8-above/4-below numbers are enforced structurally rather than copy-pasted across nine
  files (the root cause of the current drift).
- Every affected tab gets a persistent, distinguishing subtitle with no added tooltip-only
  dependency.

**Non-Goals:**
- The Tablet dialog's header/chrome is out of scope (it was explicitly excluded from this change).
- No change to any tab's underlying content or behavior — filter-pill categories, the
  completion-policy picker's options, guestbook recording logic, assignment staging/delivery
  logic, and Transcribe's copy/import/export logic are all unchanged.
- No change to `BuildRightColNav`, the three-column dialog skeleton, or nav-button icons/tooltips.
- No save-format, network message, or `src/Core/` changes — this is presentation-only.

## Decisions

### A shared `BuildTabHeader` helper replaces each tab's inline header code

Add one new method (name TBD at implementation time, sketched here as
`ScribeDialogBase.BuildTabHeader(labelLangKey, descriptorLangKey, row3Content, hasLeadingDivider)`)
that returns the Row 2 subtitle, the optional Row 3 content, the Guest-Book-only leading divider
when requested, and the durable divider at the fixed 8-above/4-below spacing — as one widget each
tab's `Build()` method places at the top of its own content, in place of the ad hoc code it has
today.

**Alternative considered:** leave each tab's `Build()` method to hand-build its own header per the
new spec's numbers. Rejected — that is exactly how the current inconsistency happened; a shared
helper is the only way the rule stays enforced as new tabs are added later.

### Row 1's padding tightens once, at its single existing call site

Because `BuildTitleBar` is already shared dialog-wide (not per-tab), tightening its padding is a
single, well-contained edit — it does not need to be threaded through the per-tab change at all.

### Subtitle copy: reuse existing nav/tab-name lang keys as the label, add new descriptor keys

The label half of each subtitle reuses the tab's existing nav-tooltip lang key (no new copy):
`scribe-gui-nav-read`, `scribe-gui-nav-edit`, `scribe-gui-nav-pinned`, `scribe-gui-nav-history`,
`scribe-tab-guestbook`, `scribe-tab-inbox`, `scribe-tab-senthistory`, `scribe-tab-assignment`,
`scribe-tab-transcribe` all already exist. The descriptor half is new copy, added as sibling keys
following each family's existing naming (`scribe-gui-subtitle-<tab>` / `scribe-tab-subtitle-<tab>`),
using the wording already agreed during exploration (e.g. `scribe-tab-subtitle-guestbook` =
"who has visited").

**Alternative considered:** write entirely new label text instead of reusing the tooltip keys.
Rejected for tabs where the existing tooltip text reads naturally with a descriptor appended
(all of them, per the agreed drafts) — reuse avoids duplicate translation work with no
readability cost.

### Typography: small-caps label, italic descriptor — approximated with mixed-size spans + a real italic face

The agreed look (from the HTML mockup, option C) is a small-caps label plus an italic descriptor.
LibGUI/Skia has no true small-caps OpenType feature field on `TextStyle`/`SpanStyle`, so it's
approximated with two uppercase `TextSpan`s sharing one family/weight/color: EACH WORD's first
letter at the full cap size, that word's remaining letters at ~75% of that size — the classic
small-caps look (a bigger capital leading smaller capitals per word), not a uniform ALL-CAPS block
and not just the label's first word (2026-09-08 playtest feedback: "Read View" needs both the R
and the V at full cap size).

`RenderRichText` does **not** baseline-align mixed-size spans sharing one line the way an initial
read of the framework suggested — every run draws at `y = line.Y - run's own font.Metrics.Ascent`
(confirmed against both the 2.0.0 reference clone and the shipped 3.1.0 `Gui.dll` via `ilspycmd`),
which TOP-aligns each run to the line regardless of its own size. Left uncorrected, the smaller
"remaining letters" runs floated above the baseline instead of sitting on it (2026-09-08 playtest
feedback). Fixed by wrapping every text run — cap letter, small-cap tail, the literal colon, and
the descriptor — in a `WidgetSpan` whose box is forced to the tallest run's own natural line
height, with just enough top padding pushing that run's OWN baseline down to the shared reference
baseline (a local `BaselineRun` helper in `ScribeTabHeader.Build`, using font metrics queried via
`TextLayoutHelper.GetFont`). A plain space between words needs no such wrapping (no glyph to
misalign).

The descriptor's italic previously wasn't rendering as a real slant: Caudex ships as a single bold
`.ttf`, historically registered under all four `FontWeight` slots (Normal/SemiBold/Bold/Italic) to
sidestep an old font-resolver bug, so an Italic request against it silently drew the same upright
bold glyphs. Fixed by bundling Caudex's real (lighter-weight) italic cut and registering it under
just the `Italic` slot, leaving Normal/SemiBold/Bold on the bold cut as before
(`ScribeModSystem.RegisterCustomFonts`).

### Two tabs missed in the original pass adopt the same shared helper, no new mechanism

`GuiDialogScribeInbox.BuildInboxInventoryContent` and `GuiDialogClockmakerNotebook.BuildTimerContent`
were not touched by the original nine-tab sweep even though both are ordinary `ScribeDialogBase`
tabs. They adopt `ScribeTabHeader.Build` exactly as every other tab does — the Inbox Inventory
tab's existing 12-slot grid becomes Row 3 (mirroring how the Pinned tab's policy picker became Row
3), the Timer tab has no Row 3 (mirroring the Editor/History tabs), and the Timer tab's pre-existing
bare `new Divider()` (previously sitting right after the outer `Padding(10)`) is replaced by the
helper's own durable divider instead of living alongside it.

### The divider-to-content gap moves from outside the scroll region to inside it

Originally the durable divider sat 8 units below the header content, and each caller added its own
4-unit `Padding(EdgeInsets.Only(top: 4f))` around the `Expanded`/scroll-region widget — a fixed gap
that lives OUTSIDE the `SingleChildScrollView`, so it never scrolls away. Playtest feedback wanted
the divider to sit flush against the content with no lingering permanent gap. The fix keeps the same
4 units of breathing room but relocates it: each tab now applies that padding as top padding on the
`Column`/content living INSIDE its `SingleChildScrollView`, so it reads as the top margin of the
first row and scrolls out of view once the player scrolls past it, rather than as fixed chrome
between the divider and the viewport.

**Alternative considered:** drop the 4 units entirely (divider flush against the first row with zero
gap at all times). Rejected — the divider would visually collide with the first row's own top edge
at rest (scroll offset 0), which was not the feedback; the ask was specifically to stop that gap
being *permanent* chrome, not to remove the breathing room itself.

### Row 1's drag-grip relocates to a new leading slot, left of the title

The grip icon currently lives in the trailing button group (pencil · expand/collapse-all · grip ·
close), on the right, even though the window's whole title band is already a drag zone via
`WindowConfig.DragHandleHeight` — the grip is a discoverability affordance with its own duplicate
drag `GestureDetector`, not the only way to drag the window. The original exploration mockup placed
this grip to the LEFT of the title instead. This change moves it there: a new leading slot before
`titleSlot` in `BuildTitleBar`'s `titleRow`, carrying the same `GestureDetector`/`Tooltip`/glyph
unchanged (mechanically a reposition, not a new drag mechanism — the band-drag-vs-tooltip-vs-own-
gesture interplay documented at `BuildTitleBar`'s existing comment block is untouched). The title
row's left inset drops its extra flat `10px` (previously `10 + 0.04·W`, now just `0.04·W`, matching
the right inset) — that 10px was general "breathing room," not narrowly tied to any one element, but
with the grip now occupying the leading slot it is no longer needed on top of the symmetric 0.04·W
inset both sides already carry.

**Alternative considered:** leave the grip on the right and only shrink the left inset. Rejected —
the whole point of the ask was to restore the left-side grip from the original mockup, not just
recover padding.

**2026-09-08 playtest refinement:** the initial left-of-title placement still left too much left
padding available to both the grip and the title, and the two didn't read as vertically aligned —
the grip sat visibly lower than the title text. Fixed by (a) halving the title's own inner left
padding (`titleBtnSpacing * 1.5f` → `* 0.75f`) and halving the outer left inset a second time
(`0.04·W` → `0.02·W`, right inset unchanged at `0.04·W`, so the two sides are deliberately
asymmetric now that the grip lives on the left), and (b) wrapping the grip in a bottom-`Padding`
computed from the title font's own metrics (`descent + leading`) so its baseline lines up with the
title text's baseline instead of both simply sharing a `Row`'s cross-axis alignment.

### Row1-row2 gap tightened by another 6px, on top of the §7 divider-to-scroll-content move

Separately from §7's flush-divider change, playtest feedback (2026-09-08) wanted the gap between
Row 1 (title bar) and Row 2 (the new subtitle) tightened by 6px across every tab. Each tab's outer
content `Padding(EdgeInsets.All(10))` — the wrapper around the whole tab body, header included —
changes its top inset from `10` to `4`, left/right/bottom staying `10`. Applied to all ten tabs
(the nine from the original sweep plus the Inbox Inventory tab from §6, which previously had no
outer padding at all and needed one added).

### The Tablet renders neither Row 2 nor Row 3 — reversing an earlier decision to let it inherit Row 2

An earlier implementation pass let the Tablet's Read View/Editor keep Row 3 gated off (via the
pre-existing `SupportsFilterPills` flag) but still render Row 2's subtitle, on the reasoning that
it reuses the exact same `ScribeReadContent`/`ScribeEditorContent` widgets every other surface
does. Playtest feedback (2026-09-08) called this out as the unification going further than
intended — the Tablet's own title-bar chrome (cuneiform strokes or the readable fallback) is
already its whole header; a second, generic subtitle line underneath it is redundant chrome the
Tablet never asked for.

Fixed with a new `SupportsTabHeader` capability flag on `ScribeDialogBase` (mirroring the existing
`SupportsFilterPills` pattern): defaults `true`, overridden `false` only on
`GuiDialogScribeTablet`. Threaded into `ScribeReadContent`/`ScribeEditorContent` as a constructor
parameter; when false, the call to `ScribeTabHeader.Build` (and its durable divider) is skipped
entirely rather than passed empty Row 2/Row 3 content, and the surrounding padding reverts to its
pre-header values (outer top inset stays `10` rather than the tightened `4`; the inner 4px
scroll-content top padding from §7 is skipped too) so the Tablet's spacing is unaffected by any of
this change's other numbers.

**Alternative considered:** keep gating only Row 3 (the existing `SupportsFilterPills` behavior)
and simply hide Row 2's text via an empty label. Rejected — an empty-but-present subtitle row still
reserves its line height and draws its own durable divider, which is exactly the extra chrome the
feedback objected to; only skipping the `Build` call entirely removes both.

## Risks / Trade-offs

- **[Risk]** Nine call sites change in one pass; a mistake in the shared header helper affects
  every tab at once instead of just one. → **Mitigation**: the existing local playtest loop
  (`what-to-test` / manual TESTING.md checklist) already covers opening every dialog and tab;
  this change adds no new save/network surface, so a regression is purely visual and easy to spot
  per tab during that pass.
- **[Trade-off]** Row 2 adds a small amount of vertical height to every tab. Row 1's padding is
  tightened specifically to offset this, but the two are not guaranteed to net to exactly zero for
  every tab — some net height change is expected and was accepted during exploration (the HTML
  mockup's live height readout was used to confirm this was acceptable per tab).
- **[Risk]** Registering Caudex's italic cut under a distinct `FontWeight.Italic` slot on the same
  "Caudex" family reintroduces the same shape of risk as the historical Normal-vs-Bold resolver bug
  (two distinct real faces on two weight slots of one family) — see
  `ScribeModSystem.RegisterCustomFonts`'s comment. → **Mitigation**: verify in-game after restaging
  that the title still renders bold and the subtitle descriptor renders genuinely italic (not the
  bold face at both slots); if the title regresses, the fallback is the family-name-split approach
  considered and rejected earlier in this session (a distinct "Caudex Italic" family).

### Round 3 (2026-09-09 playtest): Create Assignments' Row 3 pared back to just the send-to row

An earlier pass (§4.1) had moved the WHOLE drafting form — heading, staging hint, delete-
checkbox, delivery toggle, notice slots, and send-to — into Row 3, on the reasoning that it's all
"the form." Playtest feedback called this out as too much header chrome above the divider: only
the send-to row (player picker + Send button) is genuinely header-shaped (a persistent control the
player revisits regardless of scroll position); everything else is drafting detail that belongs in
the scrollable/general content area below the divider, same as every other form-shaped tab's
non-header controls.

Fixed by reducing Row 3 to `sendToRow` alone and moving `stagingArea`/`deleteFromSourceRow`/
`noticeSlotsRow` into the content Column that follows the header, with the delivery toggle
(`deliveryRow`, the "Local Inboxes"/"Send a Notice" buttons) specifically placed AFTER the
scrollable stage tray rather than grouped with the other pre-scroll controls — it reads as a
send-time decision, not drafting setup. The stage tray's own top padding, previously grown by 4
units specifically because it sat flush against the divider, reverts to a plain symmetric 6 now
that it's no longer the first thing after the header.

The redundant "Assign Tasks" heading (a bare bold `Text` row) is deleted outright along with its
lang key — the tab's own Row 2 subtitle already names it, so the heading was pure duplication.

**Alternative considered:** keep the delivery toggle grouped with the notice slots (both are
delivery-related) rather than moving it below the scroll region alone. Rejected — the ask was
specifically about the "Local Inboxes" button group's position, and splitting delivery-adjacent
controls across two spots is an acceptable minor inconsistency next to actually following the
explicit placement request.

### Round 3: any tab sharing the Row 1/Row 2 header anatomy always shows its divider right there

The Inbox Inventory tab (§6.2) passed its 12-slot grid as `row3Content` into
`ScribeTabHeader.Build`. Since `Build` always appends the durable divider AFTER `row3Content`,
this put the divider below the ENTIRE slot grid — at the very bottom of the tab, functioning as
nothing (there's no further content beneath it) rather than as the header/content separator every
other tab has. Playtest feedback framed this as a general rule, not a one-off: any tab with the
same Row 1 (title)/Row 2 (subtitle) construction should show the divider directly under Row 2,
matching the no-Row-3 tabs (Editor, History, Timer).

Fixed by calling `Build` with no `row3Content` (so the divider lands directly under the subtitle)
and treating the slot grid as ordinary general content placed after the returned header widget —
the same shape every other tab already uses, just previously misapplied here.

### Round 3: small-caps size, grip baseline-nudge, and the scroll-content top padding all needed re-tuning after seeing them in-game

Three purely numeric refinements, each a direct response to how the Round 2 fixes actually looked
once restaged:

- **Small-caps size:** 75% of the cap-letter size (§5.1) read as too small a jump from the capital.
  Raised to 87.5% — the midpoint between the old 75% and the full 100% cap size — so the
  "small-caps" letters stay visibly smaller than the capital but not cramped.
- **Grip baseline-nudge:** the §8.4 fix (a bottom-padding of the FULL `descent + leading`,
  producing a `/2` net upward shift per the Center-alignment math) overshot — the grip read as
  sitting too high relative to the title's own visual center. Halved to a `/4` net shift.
- **Scroll-content top padding:** the §7 idiom (4 units of top padding inside each tab's
  scrollable/general content, so the durable divider stays flush against the viewport) read as too
  tight once seen in-game. Doubled to 8 units, applied uniformly everywhere the idiom already
  existed (including the two non-`Build`-generated 2026-09-09 uses in the restructured Create
  Assignments content and the Inbox Inventory slot grid).

No alternative approaches were considered for any of these three — they are direct numeric
tunings of already-agreed mechanisms, not new decisions.

### Round 3: the title-edit field's visible "growth" was `TextFieldStyle`'s fixed 40px `Height` default, not a padding property

Clicking the title's edit-pencil swaps a display `RichText` for a live `TextField`, and that swap
visibly grew the title band. Investigation (decompiling the shipped `Gui.dll`'s
`Gui.Widgets.Framework.TextFieldStyle`) found the struct has no `Padding` field at all — its
vertical size is fixed by a `Height` property that defaults to `40f` when unset, and
`TextFieldState.Build` uses it as a hard `MinHeight = MaxHeight` constraint (not an auto-fit), so
any font size smaller than that renders inside a much taller box than the display text occupied.
`BorderThickness` was already explicitly `0` in `BuildTitleField`'s style, so the border half of
the ask was already satisfied.

Fixed by computing the title font's own natural line height (`TextLayoutHelper.GetFont(...)
.Metrics`, `Descent - Ascent + Leading` — the same metrics helper the grip-alignment fix already
uses) and setting `Height` to that value explicitly, instead of leaving the struct's 40px default.

**Residual gap, disclosed rather than silently accepted:** `RenderTextField.PaintInternal` also
bakes in a literal `10f` horizontal text inset that is NOT exposed on `TextFieldStyle` anywhere —
it cannot be zeroed without forking `gui`. The Tablet's cuneiform title field achieves true
zero-padding because it's a bespoke `RenderObjectWidget`
(`ScribeCuneiformFieldRenderWidget`) with `padX`/`padY` as real constructor parameters, not the
stock `TextField`. Matching the Tablet's horizontal padding exactly would require the same kind of
bespoke render widget for the readable (non-cuneiform) title field — out of scope for this fix,
since the dominant visible bug was the vertical growth, not the horizontal inset (which reads as
minor next to the title row's own leading/trailing insets). Documented in `VSAPI-NOTES.md` so this
isn't re-discovered from scratch later.

### Round 4 (2026-09-10 playtest): small-caps pulled back to 82.5%

Round 3 raised the small-caps size from 75% to 87.5% of the cap-letter size — too small a jump
originally, but 87.5% then read as too CLOSE to the cap size (barely readable as "small" caps at
all). Pulled back to 82.5%, still above the original 75% but with more contrast against the full
cap letter than 87.5% left. A direct numeric re-tuning, not a new decision.

### Round 4: Row 1's multi-line growth changes from a static max-height reservation to an actual-content-driven one, so it only ever pushes content down

`BuildTitleBar`'s title band and its inner content box were both sized from `TitleMaxLines` (a
static, per-dialog-type constant — 2 by default since `wrap-titles-all-surfaces`), NOT from
whether the CURRENT title actually needs a second line. Concretely, `contentBoxH` was always
computed as if the title might wrap to the configured max, and the whole content box was
bottom-anchored inside that always-max-sized band — so even a short, single-line title's chrome
sat inside a band pre-sized for two lines, with the slack simply invisible. Playtest feedback
(2026-09-10) described the visible effect of this as the band "growing up and down" whenever a
title reached its wrap point, and asked instead for wrapping to only ever push Row 2 and the rest
of the tab's content DOWN.

Fixed by measuring the CURRENT title's actual wrapped line count (a new `CountWrappedLines`
helper — the same greedy word-wrap `ScribeMultilineField` already uses for its own caret math,
since LibGUI's own line-breaking is internal) against an estimate of the title slot's available
width (`layout.TitleBtnsW` minus the fixed insets, the grip's width, and the trailing button
group's width — all already-known constants at that point in `BuildTitleBar`, not a new layout
pass). The content box now grows ONLY as far as that actual line count requires
(`layout.TitleBtnsH + (actualLines - 1) * titleLineH`), and the outer band is
`Math.Max(layout.TitleBarH, contentBoxH)` — so a short title's band is pixel-identical to the old
single-line layout (no behavior change for the common case), while a title that genuinely needs a
2nd line grows the band beyond `TitleBarH`. Because `BuildOuterArtBox` stacks the title band above
`BuildSectionInnerBox` in a plain `Column` (no `Expanded`), growing the band's actual returned
height pushes everything below it — Row 2, Row 3, the durable divider, the tab's own content —
down by exactly the extra height, with nothing shifting upward.

The title-edit `TextField` (`BuildTitleField`) has no wrap/multiline support at all (confirmed
during the Round 3 `TextFieldStyle` decompile — no such flag exists on the struct), so it stays
fixed at its single-line height regardless of what's typed; the line-count measurement is skipped
entirely while editing (`_isTitleEditing`), keeping the band at its single-line size for the
duration of an edit rather than reserving room the field can't actually use.

**Alternative considered:** keep the static max-lines reservation but switch the cross-axis
alignment to top-anchor (`CrossAxisAlignment.Start`) instead, so a short title's slack sits below
the title text (pushing Row 2 down by a CONSTANT amount at all times, whether or not the title
ever wraps) rather than only when actually needed. Rejected — that would move Row 2 down on every
single-line-titled dialog, a much bigger and less justified layout change than what was asked for
(this change's whole premise is that a short title should look untouched).

**Alternative considered:** leave the band static and instead measure/react inside
`BuildSectionInnerBox` (i.e., have the CONTENT read the title's line count and add its own
top margin). Rejected — that duplicates the wrap measurement in two places for one fact ("does the
title need 2 lines") and couples an unrelated method to `BuildTitleBar`'s internals; computing the
band's own real height once, in the one place that already owns every number involved, is simpler
and keeps the "content follows the band" invariant `BuildOuterArtBox`'s `Column` already provides
for free.

**Residual imprecision, disclosed:** the available-width estimate used for the wrap measurement
approximates the title slot's rendered width from already-known constants rather than an actual
post-layout measurement (LibGUI does not expose one before building the widget tree here) — it can
be off by a few pixels at the exact wrap boundary in principle, but decides only a 1-vs-2 discrete
line count, not a pixel position, so a small estimate error only risks the wrap point differing
from LibGUI's own by a character or two of title text, not a visibly wrong band height.

### Round 4: Create Assignments' info button sized from the buttons beside it, not a hardcoded size

The delivery-info `ScribeRowButton` next to the Local Inboxes/Send a Notice `Button` pair
(`BuildDeliveryRow`) used `style.ControlSize`, sized independently of those two buttons and
noticeably smaller than them. Rather than hand-picking a new fixed size, its target height is
computed from the same inputs that determine the two `Button`s' own rendered height: the button
label font's line height (`TextLayoutHelper.GetFont(ScribeTaskFont.ButtonFamily, 12.5f,
FontWeight.Normal).Metrics`) plus Gui's stock `ButtonStyle.Default` vertical padding (`6+6`, from
the decompiled `Gui.Widgets.Framework.ButtonStyle.Default`) plus its ~1px border each side — so
the info button tracks any future font-size/theme change to the buttons beside it automatically,
matching the project's existing precedent for metrics-derived sizing (the grip's baseline nudge).

`ScribeRowButton`'s drawn box and icon glyph both scale proportionally with its `Size` (glyph =
`(Size - padding*2) * IconScale`, padding itself a flat 18% of `Size`) — so naively growing `Size`
alone to hit the target box height would grow the icon by the same proportion, contradicting the
explicit ask ("mostly padding growth, only slight icon growth"). Fixed by solving for an
`IconScale` that lands the new glyph at ~15% larger than its OLD glyph size (computed at the old
`style.ControlSize`, scale 1) rather than proportional to the new, larger `Size` — so growth is
almost entirely absorbed by the padding around the icon, with the icon itself only nudged up.

**Alternative considered:** hand-pick a fixed pixel size + `IconScale` by eye. Rejected — every
other numeric fix in this change that touches font-driven chrome (the grip nudge, the title field
height) is computed from metrics rather than a magic number, specifically so it survives a future
font or scale change; there's no reason to break that precedent here.

### Round 4: notice slots move to sit directly above the delivery toggle; the delete-checkbox merges into that same row

Two further Create Assignments placement adjustments, both continuing Round 3's "form controls
live in general content, ordered by how they're actually used" framing:

- The notice-slot row (`noticeSlotsRow`) moves from its Round-3 position (above the scrollable
  stage tray) to directly above the Local Inboxes/Send a Notice toggle (`deliveryRow`) — the two
  are both delivery-time concerns, so grouping them reads more coherently than separating them
  across the scroll region.
- The delete-checkbox row (`deleteFromSourceRow`), previously its own full-width line, now shares
  a `Row` with `deliveryRow` via `MainAxisAlignment.SpaceBetween` (delivery buttons+info left,
  checkbox+label right) — it's a small, secondary toggle that doesn't need a dedicated line. This
  only applies when `deliveryRow` actually renders (Hybrid delivery mode); on a non-Hybrid server
  (`ScribeDeliveryPolicy.ShowsToggle` false, no toggle to share a row with) the checkbox row falls
  back to standing alone in the same slot, so it's never silently dropped.

**Alternative considered:** always render the checkbox on its own line, even when `deliveryRow`
exists, to avoid the two-shapes-of-layout fallback. Rejected — the explicit ask was to place it in
the SAME row as the toggle group, and the non-Hybrid case is a real, already-existing mode
(`ScribeDeliveryPolicy`) that must keep the checkbox visible somehow; a small fallback shape is
simpler than inventing a different shared row for a control that isn't there.

### Round 5 (2026-09-10, 2nd playtest pass): info button 1px smaller, checkbox split back out onto its own top-of-content line, and the real fix for Row 1's upward slide

Three more numeric/placement refinements on top of Round 4, the third correcting a bug in Round 4's
own fix rather than re-tuning a number:

- **Info button padding:** Round 4 sized the delivery-info button's box to exactly match the two
  `Button`s beside it. Playtest feedback wanted it a touch smaller again — 1px less padding on
  every side — so `infoSize` is reduced by 2px (1px/side) from Round 4's `buttonTotalH +
  ScribeRowButton.BoxShrink`. The already-solved-for `IconScale` mechanism (Round 4) is re-derived
  against this smaller `infoSize`, so the icon's target size is untouched — the 2px comes entirely
  out of the button's own padding, exactly matching the request.
- **Delete-checkbox split back onto its own line, moved to the top:** Round 4 merged
  `deleteFromSourceRow` into the same `Row` as `deliveryRow` (`SpaceBetween`, trailing side).
  Playtest feedback reversed this — the checkbox read as too easy to overlook sharing a line with
  the toggle buttons. It's un-merged back into its own full-width row, AND relocated to the very
  TOP of the content section (above `stagingArea`), rather than its previous position near the
  bottom. The Round 4 non-Hybrid fallback concern is now moot — a standalone row is once again the
  ONLY shape the checkbox renders in, so there is no longer a fallback to reason about.
- **Row 1 still visibly slid upward when wrapping — Round 4's fix didn't actually take effect:**
  Round 4's stated intent was "grows downward only," implemented as
  `bandH = Math.Max(layout.TitleBarH, contentBoxH)`. In practice `layout.TitleBarH` (0.115·H) has
  ~0.05·H of slack over `layout.TitleBtnsH` (0.065·H) — historically reserved for exactly the
  2-line case — so `contentBoxH` almost never actually exceeds `TitleBarH`, and `bandH` stayed
  pinned at the CONSTANT `TitleBarH` regardless of the title's real line count. Only the *inner*
  bottom-anchored content box (height `contentBoxH`) was actually growing, and growing a
  bottom-anchored box inside a fixed-height outer box pushes its TOP edge UP as it grows — which is
  exactly the "slides upward from the center" symptom reported.

  Fixed by growing `bandH` by the *same delta* as `contentBoxH`, both measured from their
  single-line baselines (`layout.TitleBarH` / `layout.TitleBtnsH`) instead of clamping `bandH`'s
  growth to "at least `TitleBarH`":
  ```
  extraLines = actualTitleLines - 1
  contentBoxH = layout.TitleBtnsH + extraLines * titleLineH
  bandH       = layout.TitleBarH  + extraLines * titleLineH
  ```
  Because both grow in lockstep, `bandH - contentBoxH` (the inner box's Y-offset from the band's
  page-fixed top) stays CONSTANT at `layout.TitleBarH - layout.TitleBtnsH` regardless of line
  count — so the inner box's top edge never moves; only its bottom (and the band's bottom) extends
  further down as more lines are needed. Single-line titles are unaffected (`extraLines = 0`
  reduces both formulas to their original single-line values).

**Alternative considered (info button):** leave the padding at Round 4's exact button-matching
size and treat "appear smaller" as already satisfied by Round 4. Rejected — the user explicitly
asked for 1px less padding on top of Round 4's sizing, i.e. deliberately no longer exactly matching
the two buttons' height.

**Alternative considered (checkbox):** keep the checkbox merged into `deliveryRow` but move the
COMBINED row to the top of the content section. Rejected — the explicit ask was for the checkbox to
have its own line, not merely to relocate the shared row.

### Round 6 (2026-09-10, 3rd playtest pass): Row 1's title band switches from a precomputed estimate to genuine self-sizing, borrowing the Tablet's own mechanism

Round 5's fix for the upward-slide bug (growing `bandH` in lockstep with `contentBoxH`) traded that
bug for a new one: wrapping to a 2nd line now made the whole Row 1 construction visibly JUMP down
by a number of pixels, rather than growing smoothly. Root cause: `titleLineH` was computed as
`titleFont * CuneiformMetrics.LineHeightRatio` — a ratio tuned for the Tablet's CUNEIFORM glyph
rendering, reused (incorrectly) as the assumed per-line growth for the ordinary Latin/Caudex
`RichText` every other title uses. Because that assumed line height didn't match the REAL line
height `RichText` actually renders at, the precomputed `contentBoxH`/`bandH` delta didn't match
what the real, wrapped `titleRow` actually needed — the discrepancy is what read as a discrete
jump the instant the estimate and reality parted ways.

This is the second bug in a row caused by the same root pattern: `BuildTitleBar` was pre-computing
an estimated box height (via `CountWrappedLines` + an assumed per-line height) and then forcing
that exact estimate onto the tree via nested `SizedBox`es — a top-down guess, rather than letting
LibGUI's own layout measure the title's real, wrapped height and report it back (bottom-up,
intrinsic sizing). The Tablet's cuneiform title already avoids this whole bug class: its resting
title (`BuildTitleDisplay` override) is a bespoke `RenderObjectWidget`
(`ScribeCuneiformFieldRenderWidget`) that computes its OWN true size during layout — there is no
separate estimate for it to drift out of sync with, which is why the user found the Tablet's title
"worked the way I'd expect" even though it runs through this exact same shared `BuildTitleBar`.

Fixed by replacing BOTH nested `SizedBox`es (which force an EXACT, precomputed height) with
`ConstrainedBox`es carrying only a MINIMUM height (the original single-line `TitleBarH`/`TitleBtnsH`
values) and an unbounded maximum:
```
new ConstrainedBox(new LayoutConstraints(minWidth: layout.W, maxWidth: layout.W,
        minHeight: layout.TitleBarH, maxHeight: float.PositiveInfinity),
    child: new Align(Alignment.BottomCenter,
        child: new ConstrainedBox(new LayoutConstraints(minWidth: layout.TitleBtnsW, maxWidth: layout.TitleBtnsW,
                minHeight: layout.TitleBtnsH, maxHeight: float.PositiveInfinity),
            child: /* same titleRow tree as before */)))
```
`ConstrainedBox` (confirmed via the decompiled `Gui.dll`'s `RenderConstrainedBox.PerformLayout`)
lays its child out against `additionalConstraints.Enforce(ambientConstraints)` — i.e. it enforces
the MINIMUM as a floor but otherwise passes the ambient (here, effectively unbounded) max through —
then reports the CHILD's real resulting size upward. So a short, single-line title's box is
pixel-identical to before (its real content never exceeds the floor); a wrapping title's box grows
to EXACTLY what the real, measured `titleRow` needs — no separate estimate involved at all. The
existing `Align(Alignment.BottomCenter)` between the two boxes is unchanged and still does the same
job it did in Round 5 (keeping the inner box's top pinned so growth only ever extends the bottom) —
it's just now aligning a box whose height is real instead of estimated.

`CountWrappedLines`/`titleAvailableW` are KEPT, but demoted to a purely cosmetic role: choosing
`titleCrossAlign` (`Center` for a single line, `End` — bottom-anchored to the wrapped title's last
line — for multiple). Since sizing no longer depends on this estimate at all, an occasional wrong
guess here can only produce a slightly-off alignment choice, never a wrong size or a jump. This also
retires the two disclosed imprecision risks from Round 4's design (the wrap-width estimate, and by
extension the whole "does the estimate match reality" question) as a sizing concern — they're now
strictly cosmetic.

**Pencil-icon visibility, reaffirmed (not changed):** the request asked to make sure the title-edit
pencil only draws on the actual Edit tab once this mechanism is shared across every dialog. Tracing
it: `LeaveEditorMode` (`ScribeDialogBase.ViewSwitching.cs`) nulls `scratch` and lands `viewMode` back
on `Read` the instant any OTHER tab is selected on a tabbed dialog (Lectern/Notebook/Scriptorium/
Chalkboard), so the pencil's existing `scratch is not null` gate was ALREADY exactly the Edit-tab
gate — no functional change was needed. The Tablet has no separate tabs at all (its one always-edit
view functions as "the Edit tab" for this purpose), so it correctly keeps the pencil throughout.
Documented explicitly in `BuildTitleBar`'s pencil comment so this reasoning isn't re-derived if the
gate is ever questioned again.

**Alternative considered:** keep the precomputed-estimate approach but fix `titleLineH` to use the
title's real font metrics (`TextLayoutHelper.GetFont(...).Metrics`, already computed a few lines
below for the grip-baseline nudge) instead of `CuneiformMetrics.LineHeightRatio`. This would likely
also have fixed THIS specific jump. Rejected in favor of the `ConstrainedBox` approach because it
only patches the one metric that happened to be wrong this time — the underlying pattern (an
external estimate that must stay in lockstep with LibGUI's real text layout, forever) is what
produced two distinct bugs in two rounds, and the user explicitly asked to adopt the Tablet's
actually-self-sizing mechanism rather than keep patching the estimate. `ConstrainedBox` removes the
whole estimate-vs-reality class of bug for sizing, not just this instance of it.

### Round 6 correction (2026-09-10, 4th playtest pass): the `Align(BottomCenter)` regressed to "everything drops to the bottom of the whole interface"

Round 6's `ConstrainedBox` + `Align(Alignment.BottomCenter)` pairing shipped a real regression: in
practice the ENTIRE dialog content (Row 2, Row 3, the scrollable body — everything) rendered
collapsed against the bottom edge of the whole window, not just the title band growing. Root cause,
confirmed by reading `BuildOuterArtBox`: `BuildTitleBar`'s returned widget is a plain (non-`Expanded`)
child of a `Column` that itself lives inside a `SizedBox(width: layout.W, height: layout.H)` — i.e.
the dialog's fixed total height. The ambient max-height constraint flowing into `BuildTitleBar`'s
tree is therefore bounded by `H` (the whole dialog), not actually unbounded. `ConstrainedBox`'s
`maxHeight: float.PositiveInfinity` gets clamped DOWN to that ambient `H` by its own
`additionalConstraints.Enforce(ambientConstraints)` — so the constraints handed to the `Align` child
were effectively `(min: TitleBarH, max: H)`, not `(min: TitleBarH, max: ∞)` as the Round 6 writeup
above assumed. `Align` — unlike a passive box that just clamps its intrinsic size up to a floor —
actively FILLS however much space it's given before positioning its child within that filled space.
Handed a max of `H`, it filled the entire dialog height and bottom-anchored the title content at the
very bottom of the window, pushing every subsequent Column child (`BuildSectionInnerBox`, i.e.
literally everything else) down below it.

The Round 6 writeup's claim that `ConstrainedBox` "passes the ambient (here, effectively unbounded)
max through" was the flaw — the max was never actually unbounded in this tree position, and `Align`
is exactly the wrong widget to pair with a not-really-unbounded max, since it fills rather than
passively floors.

**Fix:** drop `Align` entirely from both nesting levels. The `TitleBarH`-vs-`TitleBtnsH` gap (the
~0.05·H "historically reserved slack") was always just a fixed top margin above the title content —
reproduced directly with a plain `Padding(EdgeInsets.Only(top: TitleBarH - TitleBtnsH))` instead of
"reserve a big box and bottom-align within it." The remaining single `ConstrainedBox` (min:
`TitleBtnsH`, max: unbounded) wraps a plain widget chain (`FlatPanel` → `Padding` → `Row`) with no
fill-to-max behavior anywhere in it, so it reports the CHILD's real intrinsic height clamped up to
the floor — never forced to fill whatever ambient max it's handed:
```
new SizedBox(width: layout.W,
    child: new Padding(EdgeInsets.Only(top: layout.TitleBarH - layout.TitleBtnsH),
        child: new ConstrainedBox(new LayoutConstraints(minWidth: layout.TitleBtnsW, maxWidth: layout.TitleBtnsW,
                minHeight: layout.TitleBtnsH, maxHeight: float.PositiveInfinity),
            child: /* same titleRow tree as before */)))
```
Single-line: the inner box's intrinsic content height ≈ `TitleBtnsH` by design, so total height =
`(TitleBarH - TitleBtnsH) + TitleBtnsH = TitleBarH` — pixel-identical to every prior round. Multi-line:
the inner box grows to exactly the real, measured `titleRow` height, with the fixed top margin
ensuring growth only ever extends the bottom — the same intended behavior, achieved without any
widget that fills to an ambient bound.

**Lesson for future self-sizing work in this dialog tree:** any non-`Expanded` child of
`BuildOuterArtBox`'s `Column` receives a MAX height bounded by the dialog's fixed total `H`, not an
unbounded one — `maxHeight: float.PositiveInfinity` on a `ConstrainedBox` in this position is not a
true "no ceiling," it's clamped down to `H`. Combining that with any widget that fills-to-max
(`Align`, `Expanded`, `SizedBox.expand`, etc.) will make it fill the whole dialog. A bare
`ConstrainedBox`(min-only) wrapping ordinary intrinsic-sizing boxes is safe; anything that
deliberately fills is not.

### Round 7 (2026-09-10, 5th playtest pass): reverted Round 6 + its correction entirely — self-sizing broke horizontal centering too

The Round 6 correction's `Padding`-based fix removed `Align` to stop it filling the whole dialog
height, but that removal ALSO deleted the only thing doing horizontal centering:
`Align(Alignment.BottomCenter)` centers its child on BOTH axes, not just bottom-anchoring it
vertically. Without it, a plain `Padding`/`ConstrainedBox` chain has no centering step at all — a
`ConstrainedBox` reports its own width as the fixed `TitleBtnsW` regardless of the wider ambient
width it's laid out against, and with nothing to center that narrower box within the full-width
(`layout.W`) outer box, it landed flush against the LEFT edge instead. Playtest feedback: "ruined
the orientation left and right" — the whole grip+title+trailing-buttons group had shifted off its
centered position.

This is the second distinct regression from the Round 6 rewrite (the first was the whole-dialog
bottom-collapse fixed by the Round 6 correction), so rather than patch a third variant of the
ConstrainedBox/Align chain, Round 6 and its correction are reverted wholesale back to Round 5's
structure: two nested, EXACTLY-sized `SizedBox`es (an outer `W × bandH` band, an inner
`TitleBtnsW × contentBoxH` box) with `Align(Alignment.BottomCenter)` centering the inner box within
the outer one — restoring the horizontal centering AND keeping the outer box's height explicit and
bounded (so `Align` fills exactly `bandH`, never the dialog's total `H`, which is what made `Align`
safe to use here throughout Round 4/5 in the first place).

Reverting to Round 5's structure would also reintroduce Round 5's own known bug (the discrete
downward "jump" on wrap that Round 6 was created to fix) if left as-is — Round 5's `titleLineH` was
computed as `titleFont * CuneiformMetrics.LineHeightRatio`, a ratio tuned for the Tablet's cuneiform
glyph rendering and never actually correct for the ordinary Latin/Caudex `RichText` every other
title uses. Rather than reintroduce that disclosed bug, `titleLineH` is fixed directly: it now
reads the title's own real font metrics (`titleFontMetrics`, already computed earlier in
`BuildTitleBar` for the grip-baseline nudge) —
`titleFontMetrics.Descent - titleFontMetrics.Ascent + titleFontMetrics.Leading` — instead of the
mismatched cuneiform ratio. This is exactly the "Alternative considered" Round 6 rejected in favor
of the self-sizing rewrite; given the rewrite caused two separate regressions across two rounds, the
simpler, disclosed, metrics-corrected estimate is the more reliable fix in practice.

```
float titleLineH = titleFontMetrics.Descent - titleFontMetrics.Ascent + titleFontMetrics.Leading;
int extraLines = actualTitleLines - 1;
float contentBoxH = layout.TitleBtnsH + extraLines * titleLineH;
float bandH = layout.TitleBarH + extraLines * titleLineH;

return new SizedBox(width: layout.W, height: bandH,
    child: new Align(Alignment.BottomCenter,
        child: new SizedBox(width: layout.TitleBtnsW, height: contentBoxH,
            child: FlatPanel(new Padding(EdgeInsets.Only(left: 0.02f * layout.W, right: 0.04f * layout.W),
                child: titleRow)))));
```

Single-line titles are unaffected (`extraLines = 0` reduces both formulas to their original values,
byte-identical to every prior round). A wrapping title now grows `bandH`/`contentBoxH` by an amount
derived from the SAME real font metrics `RichText` itself uses, closing the estimate-vs-reality gap
that caused Round 5's jump — without carrying forward either of Round 6's two regressions.

**Alternative considered:** keep Round 6's `ConstrainedBox` self-sizing structure and patch in a
separate horizontal-centering step (e.g. wrap the whole `ConstrainedBox` chain in a `Center` widget
instead of `Align`, reasoning that `Center` might not fill-to-max the way `Align` does). Rejected —
`Center` is itself implemented as `Align(Alignment.Center)` in this framework (same
fills-to-ambient-max behavior the Round 6 correction already diagnosed for `Align`), so it would
reintroduce the exact bottom-collapse regression the correction fixed. Any fill-behaving widget is
unsafe in this tree position regardless of which alignment it's configured with (see the "Lesson for
future self-sizing work" callout above) — the two nested `SizedBox`es avoid the whole hazard by
never handing a fill-behaving widget an unbounded ambient max in the first place.

## Open Questions

- Exact grip icon size/spacing in its new leading-slot position (Row 1 drag-grip relocation) —
  resolved empirically during implementation; does not change the spec, the approach, or the task
  breakdown either way.
