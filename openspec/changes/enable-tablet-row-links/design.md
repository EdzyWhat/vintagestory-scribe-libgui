## Context

The Scribe GUI shares one `ScribeDialogBase` across all surfaces. Most surfaces expose a
distinct **read** view and **edit** view; the read view (`ScribeReadContent`) wraps an item row's
name label in a `GestureDetector` that calls `OnOpenLink(TaskId)`, and `ScribeDialogBase.BuildReadContent()`
supplies the dispatch (`ScribeDialogBase.Layout.cs:682-687`) that maps the task id to a Handbook
page via `ScribeItemRef.OpenHandbookPage`.

The **Tablet** dialog (`GuiDialogScribeTablet`) has no read/edit split. Its `BuildCentralRegion()`
returns `IsEditable ? BuildEditorContent() : BuildReadContent()`, and `IsEditable => _state ==
TabletState.Wet`. So a wet tablet — the normal, always-editable case — renders the **editor** row
path (`ScribeEditorContent` → `ScribeEditRow` → `BuildItemEditorContent`). The editor row wraps the
same `ScribeItemLabel` in a bare `Expanded` with **no** gesture and **no** `OnOpenLink` parameter
(`ScribeEditorContent.cs:862-863`). That is exactly why link activation never appears on a tablet,
even though the `link-task` spec already requires it "from every surface … Tablet".

A hardened/fired tablet already renders through `BuildReadContent()`, so it already has working
link activation; only the wet/editor path is missing it.

Two facts make the fix small:
1. The Handbook-open plumbing and the read-view dispatch already exist and need no change.
2. On a tablet, a Tracker/Craft row's number is **already** a live `ScribeNumericField` (`+/-`
   stepper) — that is how the target is edited on a tablet. So the user's "name opens the link,
   number edits the number" split is already the row's physical layout; only the (currently inert)
   name label needs a gesture.

## Goals / Non-Goals

**Goals:**
- Surface click-to-open-Handbook on the tablet's always-edit view for Link, Tracker, and Craft
  rows, matching the read view's behavior.
- Preserve the tablet's inline numeric editing: the name label opens the page; the numeric field
  keeps editing the target quantity — independent hit regions.
- Fix the read-view dispatch so a Craft row actually opens (it currently no-ops).
- Change nothing on the Lectern/Notebook editor views.

**Non-Goals:**
- No new capability, no Core change, no persistence/format change, no new dependency.
- No change to how the read view already resolves Link/Tracker pages (only the additive Craft
  branch).
- No visual restyle of the editor row beyond the name label becoming a clickable region (the row
  already renders the name in the link color where applicable).

## Decisions

**Decision: Add an optional `onOpenLink` to the editor path, gated by a `ScribeDialogBase` seam
that only the tablet turns on.** The editor row is shared by every surface's editor, so the
affordance must be opt-in to avoid making Lectern/Notebook editor names clickable (they have a read
view for that). Add `protected virtual bool EditorRowsOpenLinks => false;` on `ScribeDialogBase`;
`GuiDialogScribeTablet` overrides it to `true`. `BuildEditorContent()` passes the *same*
`onOpenLink` dispatch it uses in `BuildReadContent()` **only when the seam is on** (otherwise
`null`). `ScribeEditorContent`/`ScribeEditRow` take a nullable `System.Action<Guid>? onOpenLink`
(qualify `System.Action` — the VS API also defines `Action`); when null, the row renders exactly as
today (bare `Expanded`), so non-tablet editors are byte-identical.
- *Why a bool seam over a style flag*: this is dialog-level policy ("does this surface's editor
  activate links"), not per-row styling; a virtual bool matches the existing seam pattern
  (`TaskCapReachedLangKey`, `DecoratePolicyDropdownStyle`, `GetExtraNavButtons`).
- *Why not reuse the read row for the wet tablet*: rejected — editor item rows carry the
  target-quantity stepper plus drag/delete/pin authoring controls the read row lacks; swapping row
  types would regress editing. The seam adds one gesture to the existing editor row instead.

**Decision: Wrap only the name label, never the numeric field, in the gesture.** In
`BuildItemEditorContent`, wrap `ScribeItemLabel.Build(...)` in a `GestureDetector(onPress: e => {
e.Handled = true; onOpenLink(TaskId); })` — the identical shape the read row uses
(`ScribeReadContent.cs:362`) — but leave the sibling `ScribeNumericField` (the Tracker/Craft
stepper) untouched. Because the label and the field are separate widgets in the row's child list,
they are naturally independent hit regions; clicking the number reaches the field, clicking the
name reaches the gesture. Only wrap when `onOpenLink != null`, so the null (non-tablet) path is
unchanged.
- *Link rows*: a Link editor row has only the label (no inline field), so the whole label becomes
  the open region — matching "clicking any Link task always opens the Handbook."
- *Guard*: only item-kind rows (Link/Tracker/Craft) get the gesture; a plain Task/Note row's label
  is real editable text and MUST NOT become a link. Gate on the row's `IsItemKind`/kind, matching
  how the read row decides to render a link at all.

**Decision: Add the missing `IsCraft` branch to both `onOpenLink` dispatches.** The read-view
dispatch (`Layout.cs:682-687`) handles `IsLink → LinkTarget` and `IsTracker → TargetItemCode` but
omits Craft, so a Craft name (rendered as a link because Craft is an item kind) silently no-ops.
Add `else if (block?.IsCraft == true) ScribeItemRef.OpenHandbookPage(capi, block.TargetItemCode);`
— the same mapping the Pin-tab dispatch (`PinTab.cs:127-138`) already uses. Share one dispatch
lambda between `BuildReadContent()` and `BuildEditorContent()` so read and tablet stay in lockstep
and Craft is fixed in both.
- *Why fix it here*: the tablet requirement covers Craft, so Craft must resolve; the read-view
  no-op is the same one-line gap and is cheapest to close in the same dispatch.

## Risks / Trade-offs

- [A plain Task/Note editor row could accidentally gain a link gesture, hijacking clicks meant to
  focus the text field] → Gate the gesture strictly on item kinds (Link/Tracker/Craft), the same
  condition under which the read row builds a link at all. Task/Note rows keep their editable-text
  click behavior untouched.
- [The name-label gesture could swallow clicks intended for the numeric field on a Tracker/Craft
  row] → They are separate sibling widgets; the gesture wraps only the label. Verify in-game that
  clicking the number still focuses/steps the field (a listed gate).
- [Making the editor path take a new ctor parameter ripples to every caller] → The parameter is
  nullable with a null default-equivalent; all existing callers pass null (or omit) and render
  identically. Only the tablet's `BuildEditorContent` path passes a non-null dispatch.
- [Cuneiform tablet render path] → The tablet may render the row label through the cuneiform path;
  confirm the gesture wraps the label widget regardless of which font path builds it, so cuneiform
  tablets are clickable too.

## Migration Plan

Pure additive UI behavior; no data or format change, no migration. Ships in the mod DLL; takes
effect on next client launch. Rollback is reverting the seam + gesture (non-tablet paths were never
touched).

## Open Questions

- None blocking. Exact placement of the shared `onOpenLink` dispatch lambda (a private helper on
  `ScribeDialogBase` vs. inline in both build methods) is an implementation detail resolved during
  apply; the requirement is that read and editor share identical resolution including the Craft
  branch.
