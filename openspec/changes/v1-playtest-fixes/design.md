## Context

Six fixes from playtest `2026-07-27T10-16-26`, all in the Mod layer (`GuiDialogScribeLecternLibGui`,
`HudScribePins`, `ScribeSettingsContent`). Research anchors:

- **Hotkey trap:** `CaptureAllInputs() => isEditorMode` (GuiDialog:330). While `isEditorMode` is true
  the dialog swallows ALL keyboard/mouse so typed keys don't leak to the game (the intended fix from
  migrate-editor-view-libgui). But it stays true even when no field is focused, so once a "New Task"
  row exists and is blurred, H/Handbook etc. are dead. The dialog already tracks the focused editor
  row via `focusedEditIndex` (GuiDialog:102-ish) and the Pin Tab via `focusedPinTaskId`.
- **Sink everywhere:** the HUD sinks a completed pin via `ScribePinOrdering.ForDisplay` (Core: not-done
  first, done last, stable) PLUS an undo-aware session overlay in `HudScribePins` (`sunkOrder`,
  `SinksForOrder`, ~HudScribePins:389-419). The Pinned view (`BuildPinnedContent`, GuiDialog:~1460)
  renders `modSystem.MyPins` in raw pin-list order with NO sink ordering. The real document reorder
  (`ScribeDocument.MoveTaskToBottom` + server `CompleteTaskForPlayer` Sink branch) already lands for
  the shared doc, so Read/Edit reflect it on the next resync for the source — the gap is (a) the
  Pinned view's own display order and (b) making the owner's Read/Edit views show it promptly.
- **Read-view pin scroll jump:** `OnReadViewTogglePinned` (GuiDialog:1441) → `SendSetPin` →
  server re-push → `OnMyPinsChanged` (GuiDialog:273) → `ForceRebuild`. The read view's virtualized
  `ListView` re-derives content height on the first post-rebuild layout and clamps the shared
  controller's offset toward 0 (the same family as `92d41071`/`7c22da1a`). The fix machinery already
  exists: `CaptureScrollForRestore()` (GuiDialog:471) snapshots `sharedScrollController.Offset` and
  `OnRenderGUI` (GuiDialog:~1060) re-applies it via `JumpTo` for up to 5 frames until it sticks.
- **Polish:** HUD text/glow style lives in `HudScribePins`; the Lectern title `Text` is in the
  title-bar band build; the settings offsets row + HUD font scale are `LabeledControl`s in
  `ScribeSettingsContent.BuildHudAppearanceSection` (offsets ~:195, hudfontscale ~:212), with the
  `PairedControls` two-column helper (~:307) already used for other rows.

## Goals / Non-Goals

**Goals:**
- Editor input capture gated on a focused field, so hotkeys work when nothing is focused.
- Sink completion reorders the Pinned view (and the owner's Read/Edit views), matching the HUD.
- Read-view pin/unpin preserves the scroll offset.
- Three polish tweaks: HUD text/glow, title padding (10px), HUD Text Size beside HUD position.

**Non-Goals:**
- No new completion policy, no `src/Core/` API growth (reuse `ScribePinOrdering` + `MoveTaskToBottom`).
- No change to WHEN the HUD sinks (its undo window is untouched); this only extends WHERE the resting
  sunk order is shown.
- No re-theming; the HUD legibility change is a color/glow tweak, not a theme toggle.

## Decisions

### Decision 1 — Gate `CaptureAllInputs()` on a focused field, not on editor mode
Return true only when an editor field (or Pin Tab field) actually holds focus, not for the whole
duration of `isEditorMode`. The dialog already knows this: `focusedEditIndex is not null` (editor) /
`focusedPinTaskId is not null` (Pin Tab). When no field is focused, return false so unhandled keys
fall through to the game's hotkey handling. The macOS Cmd-translation in `OnKeyDown` is unaffected
(it already guards on `isEditorMode` AND only rewrites when a field would consume the key).

- **Risk:** a key pressed in the gap between blurring one row and focusing another could leak to the
  game. Mitigation: the editor's row→row moves re-request focus synchronously (FocusEditorRow), so
  the unfocused state only exists after a deliberate click-away — exactly when hotkeys SHOULD work.
- **Alternative rejected:** keep capturing in editor mode but intercept only known-safe keys — more
  fragile (a denylist), and doesn't match the user's mental model ("nothing focused → hotkeys work").

### Decision 2 — Pinned view reuses the HUD's sink ordering; Read/Edit rely on the document reorder
Two halves:
- **Pinned view:** order its rows with the same sink rule the HUD uses — `ScribePinOrdering.ForDisplay`
  over `MyPins` for the resting order. If the immediate post-completion "stay put during the undo
  window, then sink" feel is wanted here too, factor the HUD's session `sunkOrder`/`SinksForOrder`
  overlay into a small shared helper both surfaces call; otherwise the Pinned view can apply the
  plain Core resting order (done sinks below not-done) and accept an immediate sink. Settle which
  during implementation (open question below) — the Core resting order is the floor.
- **Read/Edit views:** these render DOCUMENT order, and the server's Sink completion already calls
  `MoveTaskToBottom` on the shared document, so a resync shows the moved task. The gap is promptness
  for the acting player's own completion; ensure the completion's re-push/resync repaints the open
  Read view (and, for the editor, that the scratch reflects the move — already handled by
  `scribe-lectern-view-consistency`'s editor enact-in-scratch Sink branch). No new reorder path is
  invented; this is wiring existing reorders to refresh the surfaces.

### Decision 3 — Read-view pin scroll preservation reuses `CaptureScrollForRestore`
In `OnReadViewTogglePinned`, call `CaptureScrollForRestore()` before the pin send so the pending
`OnMyPinsChanged` → `ForceRebuild` has an offset to restore; the existing `OnRenderGUI` re-apply loop
(up to 5 frames) then lands it. No new mechanism — the switch-to-read path already proves this works.
Guard so it only arms in the read view (the editor/Pin Tab have their own focus-restore paths).

### Decision 4 — Polish tweaks are literal value edits
- HUD text color toward white + glow darker/tighter: adjust the `TextStyle`/glow constants in
  `HudScribePins`; pick exact values in-game (the note gives direction, not numbers).
- Title padding: 10px `padding-left` on the Lectern title `Text` (was 4px).
- Settings: wrap HUD Text Size + the HUD position (offsets) row in `PairedControls` as two columns.

## Risks / Trade-offs

- **[Hotkey gap leak]** → Decision 1 mitigation above; verify in-game that typing in a focused row
  still never leaks WASD/hotbar to the game, AND that H works after click-away.
- **[Sink ordering diverges HUD vs Pinned]** → prefer a shared ordering helper so the two can't drift;
  if the undo-window overlay isn't shared in v1, document that the Pinned view sinks immediately while
  the HUD honors the undo window, and confirm that's acceptable.
- **[Scroll restore fights a genuinely shorter list]** → the existing 5-frame `JumpTo` loop already
  clamps to the real max and stops, so a list that truly shrank still settles correctly.
- **[Polish values need eyeballing]** → HUD glow/text and padding are visual; land approximate values,
  confirm in playtest, iterate.

## Open Questions

- Does the Pinned view need the HUD's undo-window "stay then sink" overlay, or is the plain resting
  sink order enough there? Settle during implementation; the Core resting order is the minimum.
- Exact HUD text color / glow darkness / glow range values — set by eye in-game.
