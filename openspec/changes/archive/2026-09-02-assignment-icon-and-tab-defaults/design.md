## Context

`ScribeAssignedTaskIcon.Build` is a single shared row-icon builder called from three row-render
sites (`ScribeReadContent.cs`, `ScribeEditorContent.cs` — twice, live row + drag-ghost —,
`ScribePinnedContent.cs`), each adding it to a `children` list BEFORE the row's checkbox. The Pin
Tab's row data comes from `ScribePinStore`'s per-player snapshot (`ScribePinnedRef`), a
versioned binary blob (`ScribePinCodec`) synced to the client and persisted with the save — it does
NOT have live access to the source document (a pin can reference a task in a document that isn't
currently loaded), so anything the tooltip needs must live in the snapshot itself, not be resolved
live from `host.Document`.

Read/Editor/Pin-Tab row content already threads a `ScribeAmbientLightSampler.Shade` down from
`ScribeDialogBase.currentShade` for illumination-correct tooltips (`ScribeEditorContent`/
`ScribePinnedContent` already carry `CurrentShade`; `ScribeReadContent` does not yet).

The Scriptorium and Lectern's nav row is built once in the shared base
(`ScribeDialogBase.Layout.cs BuildRightColNav`) as `[Read, Edit, Pinned] + GetExtraNavButtons() +
[Settings]`; each subclass only appends its own extra buttons between Pinned and Settings via the
existing `GetExtraNavButtons()` override. A plain right-click always lands on Read via
`EnterGrantedView() => EnterReadMode()`, overridden today only by the Inbox/Assignment Desk (which
have no Read view at all).

## Goals / Non-Goals

**Goals:**
- Reposition the assignment marker to render after (not before) the row checkbox everywhere it
  appears, with no other visual/behavioral change to the row.
- Give the marker a 2-line hover tooltip (assigner + assigned date; accepted date), correct in low
  light, on all three surfaces including the Pin Tab (which needs new persisted/synced fields).
- Make the Scriptorium default to Transcribe and the Lectern default to Guest Book on a plain
  right-click, with that same tab leading the nav row, while leaving every other tab (including
  Read) fully reachable and unchanged, and leaving the crouch+right-click quick-add gesture
  untouched.
- Update each block's right-click interaction-help text to name what it now opens, reusing the
  target tab's own lang key (no new translated strings to maintain).

**Non-Goals:**
- No change to the Notebook, Tablet, Chalkboard, Assignment Desk, or Inbox block's nav order or
  default view — only the Lectern and Scriptorium are in scope.
- No change to how/when an assignment marker is SHOWN (still gated on `IsAcceptedAssignment`) —
  only where it sits in the row and what hovering it reveals.
- No redesign of the pin codec beyond the one additive version bump this needs.

## Decisions

### D1: Reorder by moving the `children.Add(...ScribeAssignedTaskIcon...)` call, not by touching layout/spacing
Every one of the four call sites already adds widgets to a `children` list in strict visual order.
Moving the icon's `if (...) children.Add(...)` block to just after the checkbox's `children.Add(...)`
block (same `if` condition, same builder call, no new widget wrapper) is a pure reorder — safest
possible change, no risk of altering padding/alignment math that a wrapper or layout change could.

### D2: Tooltip via the existing `ScribeGlobalTint.ShadedTooltip`, content built like `ScribeDocumentSlot.BuildSummaryCard`
Every other hover surface in this dialog (nav buttons via `WithTooltip`, the Scriptorium's item-slot
summary card) already routes through `ScribeGlobalTint.ShadedTooltip` so the bubble matches the
body's illumination instead of "sticking out" full-bright in low light (a previously-fixed bug —
`refine-scribe-hover-tooltips` bug-1). A new unshaded `Tooltip` would reintroduce that exact bug.
Content is a small `Column` of two `Text` lines (mirroring `BuildSummaryCard`'s pattern), not a
single `\n`-joined string, so each line can carry its own style/wrapping cleanly.

Threading needed: `ScribeReadContent` gains a `CurrentShade` field (mirroring the other two, which
already have it), sourced the same way — `ScribeDialogBase.BuildReadContent()` passes
`currentShade: currentShade` alongside its existing `assignedStampBitmap:` argument.

### D3: Assigner name resolved by the dialog, dates/uid carried as plain data — mirrors `AssignedStampBitmap`/`ResolvePlayerNameForInbox`
`ScribeAssignment.AssignerUid`/`AssignedDate`/`AcceptedDate` are already plain strings on the Core
model — no resolution needed for Read/Editor (both read straight from `b.Assignment`, which the
dialog already has via `host.Document`/`scratch`). Only the assigner's DISPLAY NAME needs `capi`
(`capi.World.PlayerByUid`), so it is resolved once by the dialog (reusing the existing
`ResolvePlayerNameForInbox` helper) and passed down as a plain string, exactly like
`DisplayStack`/`DisplayName` already are for Tracker/Link rows — keeping every row-content file
API-free.

### D4: Pin snapshot gains 3 fields; pin codec bumps to v7 (append-only)
The Pin Tab cannot resolve `b.Assignment` live (see Context), so `ScribePinnedRef` gains
`AssignerUid` (string, empty when not an assignment), `AssignedDate` (string, empty when not an
assignment), and `AcceptedDate` (string?, null unless accepted) — set alongside the existing
`IsAcceptedAssignment` wherever that flag is already set (`ScribePinStore.SetPin`, the
resync/reconcile path that recomputes it from the live block). `ScribePinCodec` bumps
`PinVersion` 6 → 7, appending the three fields after `IsAcceptedAssignment`, following the same
progressive-read pattern every prior version bump used — a v6 blob (shipped) keeps loading
unchanged; new pins write v7. No migration step needed beyond "absent means empty/null," matching
how every prior optional field defaulted for pre-existing blobs.

### D5: A new `GetLeadingNavButtons()` seam, symmetric with the existing `GetExtraNavButtons()`
`BuildRightColNav()` becomes `GetLeadingNavButtons() + [Read, Edit, Pinned] + GetExtraNavButtons() +
[Settings]`, with a `protected virtual IEnumerable<Widget> GetLeadingNavButtons() =>
Array.Empty<Widget>()` default (byte-identical for every dialog that doesn't override it — Notebook,
Tablet, Chalkboard, Assignment Desk, Inbox all keep today's order). The Scriptorium overrides it to
yield its Transcribe button (removed from its `GetExtraNavButtons()`, which still yields Guest Book
then the conditional Inbox button); the Lectern overrides it to yield its Guest Book button (removed
from `GetExtraNavButtons()`, which then only conditionally yields Inbox).
Rejected alternative: overriding `BuildRightColNav()` entirely in each subclass (the Inbox/Assignment
Desk pattern) — that duplicates the shadow/size/color/alignment plumbing the base already owns, for a
change that is purely "which button comes first."

### D6: Default view via `DefaultToXView()` in the ctor + an `EnterGrantedView()` override — mirrors Inbox/Assignment Desk exactly
Both blocks keep their Read tab (unlike Inbox, which has none) — only the DEFAULT changes. Add
`DefaultToVisitorsView()`/`DefaultToInventoryView()` beside the existing
`DefaultToAssignmentView()`/`DefaultToInboxView()` in `ScribeDialogBase.cs`, called once from each
dialog's constructor (covers the dialog's very first client-side frame, before any server round
trip). Then override `EnterGrantedView()` — the method the base's `EnterReadMode()` default answers
every ordinary (non-editor) right-click grant with — to call the existing
`OnClickSwitchToVisitors()`/`OnClickSwitchToInventory()` instead, so it lands on the new default tab
on EVERY right-click open, not just the first. `LeaveEditorIfActive()` handling is already inside
those two methods, matching what `EnterReadMode()` does for the base case.

### D7: Interaction-help text reuses the destination tab's own lang key (confirmed with the user — self-match, not cross-reference)
`BlockScribeLectern.OpenHintLangCode` becomes `"scribe:scribe-tab-guestbook"` and
`BlockScriptorium.OpenHintLangCode` becomes `"scribe:scribe-tab-transcribe"` — reusing the EXISTING
nav-button title keys (already translated wherever the mod is localized) rather than adding two new
`blockhelp-*-open` string values that would need translating separately and could drift out of sync
with the tab's own label. The now-orphaned `blockhelp-scribelectern-open`/`blockhelp-scriptorium-open`
lang entries (previously both "Read") are removed from `en.json` as dead.

## Risks / Trade-offs

- [Risk] A pin codec version bump touches save-compatible binary serialization — the project's most
  sensitivity-flagged area. → Mitigation: strictly additive/append-only per the established
  `codec-migration` pattern (D4); add `Core.Tests` coverage for round-tripping a v6-shaped blob
  through the v7 reader and for a fresh v7 write/read round trip, matching existing codec test
  conventions.
- [Risk] Moving `EnterGrantedView()`'s target away from Read could surprise a player who wants Read
  by default. → Mitigation: Read remains one tap away (now the second nav button); this only changes
  what a bare right-click opens, matching the user's explicit ask, not a hidden default no one chose.
- [Trade-off] The assigned-icon tooltip is populated by a lookup on hover build, one extra
  `capi.World.PlayerByUid` call per accepted-assignment row per rebuild (Read/Editor only — Pin Tab
  reads the persisted uid string). Negligible: bounded by the number of on-screen assigned rows, same
  cost class as the existing Inbox tab's identical per-row resolution.

## Migration Plan

1. Core: add the 3 fields to `ScribePinnedRef`, bump `ScribePinCodec` to v7, add round-trip tests.
2. Mod: thread `CurrentShade` into `ScribeReadContent`; reorder the icon in all 4 call sites; add the
   ShadedTooltip content builder; thread `AssignerName`/`AssignedDate`/`AcceptedDate` into all three
   row Data records and their construction sites (`ScribeDialogBase.Layout.cs` for Read/Editor,
   `ScribeDialogBase.PinTab.cs` + `ScribePinStore` for Pinned).
3. Mod: add `GetLeadingNavButtons()` to the base; override it (and trim `GetExtraNavButtons()`) on
   the Scriptorium and Lectern; add `DefaultToVisitorsView()`/`DefaultToInventoryView()` +
   `EnterGrantedView()` overrides on those two dialogs.
4. Mod: swap `OpenHintLangCode` on both blocks; remove the now-dead `blockhelp-*-open` lang entries.
5. Build + full test pass; manual in-game verification (both blocks' right-click default, nav order,
   tooltip content/shading, crouch+right-click still quick-adds) — added to `TESTING.md`.

No rollback complexity beyond a normal revert — nothing here is a one-way migration; a v7 pin blob
read by a hypothetical older (v6-only) build would simply fail its version check and be rejected the
same way any newer-than-supported blob already is.

## Open Questions

None — the interaction-help wording ambiguity was resolved directly with the user (self-match,
recorded as D7) before writing this document.
