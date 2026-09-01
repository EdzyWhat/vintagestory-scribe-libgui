## 1. Core: pin snapshot gains assignment provenance (v7 codec)

- [x] 1.1 Add `AssignerUid` (string), `AssignedDate` (string), `AcceptedDate` (string?) to
      `ScribePinnedRef`.
- [x] 1.2 Bump `ScribePinCodec.PinVersion` to 7; write the three new fields after
      `IsAcceptedAssignment` (append-only, following the existing per-field-version doc-comment
      convention); read them only when the blob's version is >= 7.
- [x] 1.3 Set the three new fields everywhere `IsAcceptedAssignment` is already set from a live
      `ScribeAssignment` (`ScribePinStore.SetPin` call sites / the resync-from-block path).
- [x] 1.4 Add `Core.Tests` coverage: round-trip a v7-written pin (fields present), and read a
      v6-shaped blob (fields absent) confirming it loads with empty/null defaults. (627/627 passing)

## 2. Mod: reposition the assignment marker

- [x] 2.1 `ScribeReadContent.cs`: move the `IsAcceptedAssignment` icon-add below the checkbox
      icon-add block.
- [x] 2.2 `ScribeEditorContent.cs`: same move in BOTH occurrences (the live row and the
      drag-collapse ghost row).
- [x] 2.3 `ScribePinnedContent.cs`: same move below the checkbox icon-add block.
- [ ] 2.4 Build + restage; confirm visually the marker now sits immediately right of the checkbox
      on all three surfaces (defer full confirmation to the manual playtest in §6).

## 3. Mod: assignment marker tooltip

- [x] 3.1 Add `CurrentShade` threading to `ScribeReadContent` (constructor param + field),
      mirroring `ScribeEditorContent`/`ScribePinnedContent`; pass `currentShade: currentShade` from
      `ScribeDialogBase.BuildReadContent()`.
- [x] 3.2 Add `AssignerName`, `AssignedDate`, `AcceptedDate` (all nullable) to `ScribeReadRowData`,
      `ScribeEditRowData`, and `ScribePinRowData`.
- [x] 3.3 Populate them at each construction site: `ScribeDialogBase.Layout.cs`'s
      `BuildReadContent()`/`BuildEditorContent()` read straight off `b.Assignment` (when Accepted)
      plus a resolved assigner name (reuse `ResolvePlayerNameForInbox`); `ScribeDialogBase.PinTab.cs`'s
      `BuildPinnedContent()` reads the pin snapshot's new fields directly (resolving the name the
      same way).
- [x] 3.4 In `ScribeAssignedTaskIcon.Build` (or its three call sites — pick one place, whichever
      keeps `ScribeRowWidgets.cs` API-free per its existing convention), wrap the icon in
      `ScribeGlobalTint.ShadedTooltip` with a two-line `Column` content (assigner + assigned date;
      accepted date) when the row's data has assignment info, built the same way
      `ScribeDocumentSlot.BuildSummaryCard` composes its lines. No tooltip when the fields are
      absent (defensive — should not happen for an `IsAcceptedAssignment` row post-§1, but never crash
      on a stale/legacy pin).
- [x] 3.5 Add lang key(s) for the tooltip's line templates (e.g. "Assigned by {0} on {1}" /
      "Accepted {0}") to `en.json`.
- [x] 3.6 Build + test; confirm Core suite still green. (Mod build 0 errors; Core.Tests 627/627)

## 4. Mod: Scriptorium — Transcribe first, right-click default

- [x] 4.1 Add `protected virtual IEnumerable<Widget> GetLeadingNavButtons() =>
      Array.Empty<Widget>();` to `ScribeDialogBase.cs`, and splice it into
      `BuildRightColNav()`'s `navChildren` ahead of `readBtn`.
- [x] 4.2 `GuiDialogScribeScriptorium.cs`: move the Transcribe `TitleButton` out of
      `GetExtraNavButtons()` into a new `GetLeadingNavButtons()` override; `GetExtraNavButtons()`
      keeps Guest Book then the conditional Inbox button.
- [x] 4.3 Add `DefaultToInventoryView()` to `ScribeDialogBase.cs` (mirroring
      `DefaultToAssignmentView`/`DefaultToInboxView`); call it from
      `GuiDialogScribeScriptorium`'s constructor.
- [x] 4.4 Override `EnterGrantedView()` on `GuiDialogScribeScriptorium` to call
      `OnClickSwitchToInventory()` instead of the base's `EnterReadMode()`.
- [x] 4.5 `BlockScriptorium.cs`: change `OpenHintLangCode` to `"scribe:scribe-tab-transcribe"`.
- [x] 4.6 Remove the now-dead `blockhelp-scriptorium-open` entry from `en.json` (confirm nothing
      else references it first). (only archived-change docs referenced it; bin/ output dirs excluded)

## 5. Mod: Lectern — Guest Book first, right-click default

- [x] 5.1 `GuiDialogScribeLecternLibGui.cs`: move the Guest Book `TitleButton` out of
      `GetExtraNavButtons()` into a `GetLeadingNavButtons()` override; `GetExtraNavButtons()` keeps
      only the conditional Inbox button.
- [x] 5.2 Add `DefaultToVisitorsView()` to `ScribeDialogBase.cs`; call it from
      `GuiDialogScribeLecternLibGui`'s constructor.
- [x] 5.3 Override `EnterGrantedView()` on `GuiDialogScribeLecternLibGui` to call
      `OnClickSwitchToVisitors()` instead of the base's `EnterReadMode()`.
- [x] 5.4 `BlockScribeLectern.cs`: change `OpenHintLangCode` to `"scribe:scribe-tab-guestbook"`.
- [x] 5.5 Remove the now-dead `blockhelp-scribelectern-open` entry from `en.json` (confirm nothing
      else references it first). (only design.md referenced it; that's the historical record)

## 6. Verification

- [x] 6.1 `dotnet build` clean (Core + Mod); `dotnet test tests/Core.Tests` green. (0/0 errors; 627/627 passing)
- [ ] 6.2 Restage and manually verify: marker position + tooltip content/shading on Read, Editor,
      and Pin Tab for an accepted assignment; Scriptorium right-click opens Transcribe with correct
      nav order and help text; Lectern right-click opens Guest Book with correct nav order and help
      text; crouch+right-click still quick-adds on both blocks. Add these as `TESTING.md` items.
