## 1. Reusable collapse widget (`src/Mod/ScribeCollapsible.cs`, new file)

- [x] 1.1 Add `ScribeHeightFactorBox` — a `RenderObjectWidget` + `RenderBox` (modeled on
  `ScribeMultilineFieldRender`) that lays its child out at full constraints, reports
  `Size = (childWidth, childHeight * factor)`, and clips paint to that box, so a parent `Column`
  reflows and slides siblings up as `factor` shrinks. Expose a settable `factor` that calls
  `MarkNeedsLayout` on change.
- [x] 1.2 Add `ScribeCollapsible` (a `StatefulWidget`) mirroring the `ScribeFadeText` self-ticking
  pattern: constructor `(bool collapsing, int durationMs, Action? onCollapsed, Widget child,
  Key? key)`. In the non-collapsing state it renders the child at natural height with no
  controller. In the collapsing state its state drives a height factor `1 → 0` and fires
  `onCollapsed` exactly once at completion.
- [x] 1.3 Add `ScribeCollapseRegistry` (host-owned, mirroring `ScribeNumericFocusRegistry`):
  one persistent `AnimationController` per departing identity key, created on first request,
  resumed (not restarted) on remount, and released when its collapse completes. Wire
  `ScribeCollapsible` to obtain its controller from the registry by key so progress survives the
  host's `ForceRebuild`. Include a `Dispose()` that disposes all controllers.
- [x] 1.4 Drive repaint via `OnValueChanged -> Element.MarkNeedsBuild()` and completion via
  `OnStatusChanged -> (Completed => onCollapsed)`; obtain the ticker via
  `Element.Owner!.GetTickerProvider()`. Choose a default duration (~200ms) and easing
  (`EaseOut`/`EaseInOutCubic`) as tunable constants.

## 2. HUD wiring (`src/Mod/HudScribePins.cs`)

- [x] 2.1 Add a host-owned `Dictionary<(Guid,Guid), HudPinRow> departing` and a
  `ScribeCollapseRegistry`; own the registry for the HUD's lifetime and dispose it with the HUD.
- [x] 2.2 On an unpin/delete window expiry (where `awaitingRemoval` is added today), snapshot the
  row's last-known `HudPinRow` into `departing` instead of dropping it from the render.
- [x] 2.3 In `Build()`, append departing rows (keyed by their existing `ValueKey<Guid>(TaskId)`)
  wrapped in `ScribeCollapsible(collapsing: true, onCollapsed: remove-from-departing + rebuild)`.
  The text is already faded to ~0 by `ScribeFadeText`, so the collapse closes the empty row.
- [x] 2.4 Clear `departing` on server-push reconciliation in `OnMyPinsChanged` (like
  `awaitingRemoval`), and guard the `sunkOrder` prune in `Build` so it never drops a `departing`
  key early. Confirm a re-pin during/after collapse reappears at full height.

## 3. Lectern editor wiring (`src/Mod/GuiDialogScribeLecternLibGui.cs`)

- [x] 3.1 Add a host-owned `Dictionary<Guid, ScribeEditRowData> departingEditorRows` and a
  `ScribeCollapseRegistry`; dispose the registry with the dialog.
- [x] 3.2 In `DeleteEditorBlock(index)`, snapshot the deleted `ScribeEditRowData` into
  `departingEditorRows` BEFORE removing it from `scratch` (keep the scratch deletion so the model
  and autosave stay correct immediately). Preserve the existing focus fix-up.
- [x] 3.3 In `BuildEditorContent`, splice each departing row back in at its old index, wrapped in
  `ScribeCollapsible`, rendered as a static, non-interactive snapshot (read-style / read-only, no
  focus node). `onCollapsed` removes the entry from `departingEditorRows` and rebuilds.
- [x] 3.4 Move `RequestClampToExtent()` out of the delete site and into the `onCollapsed`
  callback, so the scroll re-clamp runs only after the row has fully collapsed.

## 4. Build & validate

- [x] 4.1 `dotnet build src/Mod/Mod.csproj` clean (0 warnings); `dotnet test
  tests/Core.Tests/Core.Tests.csproj` green (no Core changes expected, confirm anyway).
- [x] 4.2 `openspec validate scribe-list-collapse` passes.
- [x] 4.3 `bash build/restage.sh Debug` and relaunch the client for playtesting.

## 5. In-game verification (add to `TESTING.md`)

- [x] 5.1 Complete a HUD task under Delete and under Unpin: after the fade window the row's height
  collapses smoothly and the rows below slide up (no instant vanish/snap). — Confirmed (playtest 2026-07-25T22-36-25)
- [x] 5.2 Complete/delete several HUD rows in quick succession: each collapses independently, none
  strands a half-height gap. — Confirmed (playtest 2026-07-25T22-36-25)
- [x] 5.3 Unpin a HUD task (collapse), then immediately re-pin it from the lectern: it reappears at
  full height (departing set cleared on server push). — Confirmed (playtest 2026-07-25T22-36-25)
- [x] 5.4 Delete a lectern editor row (hover → delete): it collapses smoothly and rows below slide
  up. Delete several fast; each collapses independently. — Confirmed (playtest 2026-07-25T22-36-25)
- [x] 5.5 Scroll the editor to the bottom and delete the last row: no dead-space flash; the
  viewport settles once the collapse finishes (deferred `RequestClampToExtent`). — Confirmed (playtest 2026-07-25T22-36-25)
- [x] 5.6 Delete a HUD row while another pin's fade window is still running (forces a `ForceRebuild`
  mid-collapse): the collapse still completes smoothly (validates the resume-from-elapsed registry).
  — Accepted (playtest 2026-07-25T22-36-25): too fiddly to trigger reliably; deemed an acceptable edge case, covered transitively by 5.1–5.3.
