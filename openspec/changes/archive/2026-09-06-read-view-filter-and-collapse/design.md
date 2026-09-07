## Context

Read View is one shared widget (`ScribeReadContent`, built by `ScribeDialogBase.BuildReadContent`)
rendered identically by every `ScribeDialogBase` subclass, including the Tablet dialog — there is
no separate "tablet read view" to leave untouched. See proposal.md for why pills + collapse are
wanted and why Tablet is excluded.

Two existing precedents this design leans on:
- The Assignment Inbox filter pills (`ScribeInboxContent.cs`: `ScribeAssignmentFilterGroup`,
  `BuildFilterChip`, `ScribeAssignmentFilterGroups.LabelAndColor`) — same visual shape (radio-style
  pill row, bracketed count only when > 0), but a distinct enum/category set and a distinct color
  set for this feature.
- Per-instance persisted enum state via `ToTreeAttributes`/`FromTreeAttributes`
  (`BlockEntityScribeWritingStation.accessMode`) for block entities, and the
  `ScribeDocumentAttributes.cs` per-`ItemStack` attribute-helper pattern for portable items.

`ScribeBlock.Depth` (0 or 1, no parent/child ID) is the only structural signal for "this is a
subtask group" — a parent is any depth-0 row immediately followed by a contiguous depth-1 run.
There is no existing collapse/expand UI in the codebase to build on (`ScribeRowSizeAnimation`'s
`Collapse` is an unrelated row-removal animation).

## Goals / Non-Goals

**Goals:**
- One filter-pill row and one collapse toggle affordance, both reusable across every
  `ScribeDialogBase` surface that opts in.
- Per-instance persistence for both the active pill and each group's collapse state, surviving
  reopen/relog, following the existing Sign-block persistence pattern.
- A specific, testable rule for how filtering and collapsing interact, so it isn't left as an
  ambiguous "figure it out during implementation."

**Non-Goals:**
- No change to `src/Core/` — `ScribeDocument`/`ScribeBlock` gain no new fields; everything here is
  Mod-layer presentation state plus new attribute storage.
- No parent/child ID model — grouping stays purely structural (depth + contiguity), unchanged from
  `task-subtasks`.
- No multi-select or combined filters — exactly one pill is active at a time, matching the
  Assignment Inbox's existing radio behavior.
- Tablet is out of scope for both features entirely (see `tablet-dialog` delta spec).

## Decisions

### Filter categories and matching rule
Five categories: All, Active, Completed, Pinned, Other.
- `ScribeBlockKind.Text` and `ScribeBlockKind.QuestObjective` have no complete-state and always
  fall under **Other**.
- `Task`, `Tracker`, `Craft`, and `Link` all carry a complete-state; a row matches **Active** when
  not complete, **Completed** when complete.
- **Pinned** is evaluated independently of Active/Completed/Other: any row currently pinned
  (existing per-player pin state) matches Pinned regardless of what else it matches. A row can
  legitimately match two pills at once (Pinned + Completed) — the pill row is single-select, so a
  player sees it under whichever pill they've chosen, and its count is added to both pills'
  tallies.
- **All** applies no filtering and needs no matching rule.

### Shadow-row filtering + collapse interaction
Confirmed with the user as the exact rule to spec:
1. A subtask group (a depth-0 parent + its contiguous depth-1 owned run) is treated as a unit for
   visibility: under a non-"All" filter, the unit renders if **any** member (parent or any child)
   individually matches the active filter. If no member matches, the whole unit is hidden, same as
   any non-matching standalone row.
2. Within a visible unit, each member's opacity is independent of the others and independent of
   collapse state: a member renders at normal opacity if it individually matches the active
   filter, or at reduced ("shadow", ~50% opacity) if it doesn't. A shadow row is otherwise a fully
   normal row — same content, same interactions (checkbox, pin, edit) — just visually deemphasized
   to signal "shown for group context, not because it matches this pill."
3. Collapsing a group only removes its child rows from the rendered list; it never changes the
   parent's own visibility or opacity rule above. A collapsed group's parent still renders (at its
   own normal-or-shadow opacity) whenever any member of the group — parent or a now-hidden child —
   matches the active filter.
4. A pill's bracketed count reflects only rows that individually match that filter (rule from
   §2, "normal" rows) — shadow rows are never counted, so `"Completed [1]"` always means exactly
   one row is actually complete, regardless of how many shadow siblings render alongside it.
5. Under **All**, no filtering happens and no row is ever shadowed — shadow rendering only exists
   relative to an active non-"All" pill.

Alternatives considered and rejected:
- *Group-level filtering with no opacity semantics* (show the whole group at full opacity if any
  member matches, no visual distinction between matching/non-matching members) — simpler, but the
  user specifically wanted a way to tell, at a glance, which rows in a visible group are the reason
  it's showing.
- *Strict per-row filtering with orphan promotion* (hide non-matching rows outright, re-parent a
  lone matching child to the top level with a breadcrumb) — avoids opacity, but breaks the visual
  grouping the collapse feature exists to preserve, and needs a new "breadcrumb" UI element.

### Pill colors
Reuses five existing `ScribeRowConstants.cs` nav-button-alias colors, confirmed with the user:
- **All** → `NavActiveSettings` (warm gray) — same "no filter" meaning as `AssignmentChipAll`.
- **Completed** → `NavActiveRead` (blue).
- **Active** → `NavActivePinned` (green).
- **Pinned** → `NavActiveTranscribe` (gold).
- **Other** → `NavActiveGuestbook` (purple).

This directly follows the 2026-08-31 playtest lesson recorded on the existing `AssignmentChip*`
constants: an earlier bespoke/invented Assignment palette read as "too saturated/fancy" against
the rest of the GUI's muted nav-icon backgrounds, and the fix was aliasing to existing nav colors
instead of inventing new ones. These are a *different* alias mapping than the Assignment chips
(e.g. blue means Accepted there, Completed here) — that's fine, since the two pill rows never
appear together in the same view and each is internally consistent. `NavActiveHistory` (tan) and
`NavActiveTimer` (teal) remain unclaimed by either pill set after this change.

### Tablet exclusion
Add `protected virtual bool SupportsFilterPills => true;` (naming TBD at implementation time) on
`ScribeDialogBase`, overridden to `false` on `GuiDialogScribeTablet`. `BuildReadContent` consults
this single flag to skip building both the pill row and any subtask-collapse toggles — one flag
gates both features, since the proposal treats "exclude Tablet" as one ask, not two.

### Persistence storage
- Block entities (`BlockEntityScribeWritingStation` and subclasses, `BlockEntityInbox`,
  `BlockEntityAssignmentDesk`): a new `ToTreeAttributes`/`FromTreeAttributes` int field for the
  active filter pill (enum cast to byte, same shape as `accessMode`), and a collapsed-group-ids
  byte/string blob for collapse state (a group is identified by its parent block's stable id within
  the document, not by list index, so persisted collapse state survives reordering).
- Portable items (`ItemScribeNotebook`, `ItemClockmakerNotebook`): the same two pieces of state as
  `ItemStack` attributes, following `ScribeDocumentAttributes.cs`'s helper-class-with-const-key
  shape.
- Tablet items are exempt (see above) and carry neither attribute.

## Risks / Trade-offs

- **[Risk]** Shadow-row opacity could still read as visual clutter once real content is on screen,
  especially for the Other pill (which can shadow-render entire Quest/Craft groups whose members
  are mostly Active or Completed). → **Mitigation**: this is exactly the kind of thing to validate
  in-game per `what-to-test`/`reconcile-playtest` before considering the feature done; the rule is
  specified precisely enough to be easy to tune (e.g. adjust the shadow opacity constant) without
  changing the underlying logic.
- **[Risk]** Persisting collapse state by parent-block stable id (not list index) is new plumbing;
  if a parent block is deleted and recreated (e.g. Transcribe copy/paste), its old collapse state
  won't carry over. → **Mitigation**: acceptable — a freshly pasted document reasonably defaults to
  fully expanded, consistent with a "new" item.
- **[Risk]** Two `ToTreeAttributes` additions per host touches every existing Read-View-hosting
  block entity and item. → **Mitigation**: purely additive fields with sensible defaults (All /
  fully expanded) when absent, so existing saves deserialize unchanged — no migration needed.

## Migration Plan

Purely additive: new attribute keys default to "All" filter / fully-expanded collapse when absent,
so existing worlds and items need no data migration. No rollback concerns beyond a normal revert.
