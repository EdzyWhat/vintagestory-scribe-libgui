## Why

Playtesting turned up two rough edges. First, the accepted-assignment marker sits to the LEFT of a
row's checkbox, reading as its own separate leading column rather than a decoration on the checkbox
itself, and it carries no information — a player can see a task is assigned but not by whom, when,
or when it was accepted. Second, the Scriptorium and Lectern both default their plain right-click to
the Read tab, but each block's actual primary purpose (transcribing/copying documents on the
Scriptorium; recording visitors on the Lectern) lives on a different tab a player must navigate to
manually every time.

## What Changes

- Move the accepted-assignment marker icon from before the row checkbox to after it (inside, to its
  right) on every surface that renders it: Read view, Editor view, and the Pin Tab.
- Add a two-line hover tooltip to that icon: who assigned the task and when, and when it was
  accepted — reusing the same shaded-tooltip mechanism every other row/nav tooltip already uses.
- The Pin Tab's pinned-task snapshot (`ScribePinnedRef`) gains the assigner uid and the
  assigned/accepted dates so its tooltip works without needing the source document loaded
  (**pin-list codec version bump, v6 → v7**).
- The Scriptorium's Transcribe tab becomes its first nav button (ahead of Read/Edit/Pinned/Guest
  Book/Inbox/Settings) and its default view on a plain right-click, replacing Read. Crouch
  (shift)+right-click is unchanged (still the quick-add-a-task gesture). The block's right-click
  interaction-help text now reads "Transcribe" (reusing the Transcribe tab's own lang key) instead
  of "Read".
- The Lectern's Guest Book tab becomes its first nav button (ahead of Read/Edit/Pinned/Inbox/
  Settings) and its default view on a plain right-click, replacing Read. Crouch+right-click is
  unchanged. The block's right-click interaction-help text now reads "Guest Book" (reusing the
  Guest Book tab's own lang key) instead of "Read".

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `assignment-state-machine`: the accepted-assignment marker's position and its new tooltip content
  (assigner + assigned date + accepted date) are added as a requirement on how an accepted
  assignment renders on the assignee's task rows.
- `player-pins`: a pin's snapshot gains assigner-uid/assigned-date/accepted-date fields so the Pin
  Tab can render the same tooltip without resolving the source document.
- `scriptorium-block`: the Transcribe tab's nav position and its role as the default/right-click
  view are now specified requirements, not incidental layout.
- `lectern-guestbook`: the Guest Book tab's nav position and its role as the default/right-click
  view are now specified requirements, not incidental layout.

## Impact

- `src/Core/ScribePinnedRef.cs`, `src/Core/ScribePinCodec.cs` (version bump + migration), and
  `tests/Core.Tests` codec coverage.
- `src/Mod/ScribeReadContent.cs`, `ScribeEditorContent.cs`, `ScribePinnedContent.cs`,
  `ScribeRowWidgets.cs` (icon position + tooltip), `ScribeDialogBase.cs`/`.Layout.cs`/
  `.ViewSwitching.cs`/`.PinTab.cs` (data threading, new leading-nav-button seam, new
  `DefaultToVisitorsView`/`DefaultToInventoryView`, `EnterGrantedView` overrides).
- `src/Mod/GuiDialogScribeScriptorium.cs`, `GuiDialogScribeLecternLibGui.cs` (nav reorder, default
  view, `EnterGrantedView` override).
- `src/Mod/BlockScribeLectern.cs`, `BlockScriptorium.cs`, `src/Mod/assets/scribe/lang/en.json`
  (interaction-help lang-key reuse).
- No breaking change to existing pins from a v6 client/save — v7 is an append-only, backward-reading
  codec version per `codec-migration`'s established pattern.
