## 1. HUD gradual fade (unpin/delete)

- [x] 1.1 In `HudScribePins.cs`, replace the static `FadingOutOpacity` target for a destructive-pending
  (Unpin/Delete) row with a time-varying opacity computed from the pending window's remaining fraction:
  `clamp01(remainingMs / PinHudWaitMs)` (1.0 at check → 0.0 at expiry). Compute it from the pending
  entry's stamped elapsed/expiry, keeping the checkbox fully opaque.
  → First tried `AnimatedOpacity` (target `0f`, Linear); playtest showed it SNAPPED to 0 instantly then
  waited 1.5s. Root cause (verified in source): the HUD's only rebuild path is `GuiBase.ForceRebuild`,
  which UNMOUNTS + recreates the tree rather than reconciling; an implicit tween only animates across a
  reconciling `UpdateWidget`, so recreated fresh it inits `Begin=End=target` → evaluates to 0 at once.
  FIXED with `ScribeFadeText` — a self-contained widget that owns an `AnimationController` started in
  `InitState` and ticks itself (repaint via `MarkNeedsBuild`), so it ramps 1→0 the moment it (re)mounts in
  the fading state, immune to the parent's ForceRebuild. The per-frame drive the original design (D1)
  called for was right after all; it just lives inside the widget, not in HUD math.
  → Follow-up (playtest 2): the ramp worked but the row FLASHED back to full opacity for a frame right at
  expiry — the destructive pin is still in `MyPins` after the send but before the server's removal push, so
  it rebuilt as not-fading (full opacity) in that gap. Fixed with an `awaitingRemoval` set: on Unpin/Delete
  expiry the identity is recorded and its row is filtered OUT of the rendered list until the removal push
  lands (cleared in `OnMyPinsChanged`). The row is already faded to ~0, so dropping it is seamless.
- [x] 1.2 Ensure the ramp is smooth: recompute the opacity on each `Build`, and if the `OnTick` cadence is
  too coarse for a smooth ramp, drive a lightweight rebuild only while a destructive window is active
  (bounded to the ≤1.5s window). No rebuild churn once no destructive window is pending.
  → Handled inside `ScribeFadeText`: its own `AnimationController` ticks the fade per-frame (the HUD renders
  every frame while open, and `GuiBase` pumps its `TickerScheduler` in `OnRenderGUI`), so the ramp is smooth
  with NO HUD-driven per-frame `ForceRebuild` (avoids the rebuild churn this task guarded against).
- [x] 1.3 Verify the undo path cancels the ramp cleanly — unchecking within the window removes the pending
  entry and the text returns to full opacity immediately (no lingering faded state).
  → Undo path: unchecking removes the pending entry → `ForceRebuild` → the row rebuilds with
  `fading: false`, so `ScribeFadeText` mounts with no controller and renders at full opacity immediately
  (no lingering faded state). Confirm live in 6.1.

## 2. Durable sink-reorder-and-stay (Sink/Keep)

- [x] 2.1 In `HudScribePins.cs`, add a client-local session set of settled-sunk pin identities
  `(DocId, TaskId)` (e.g. `sunkOrder`); populate it in `OnTick` when a `Sink`-policy pending window
  expires.
  → Added `sunkOrder` HashSet; `OnTick` adds the key on a `Sink`-policy window expiry.
- [x] 2.2 Change `SunkForOrder` to return true when the pin is in that set, independent of current
  `DisplayedDone` — so a later uncheck no longer un-sinks the row. Keep the in-window "held in place so it
  can settle" behavior (a pin with a pending completion is not yet sunk).
  → `SunkForOrder` short-circuits true on `sunkOrder` membership. Added `SunkVisual` (done AND sunk) so an
  unchecked-but-sunk row keeps its bottom slot but renders as an active row, not a muted-done one.
- [x] 2.3 Confirm unchecking a settled-sunk row clears optimistic-done / re-syncs server state as before
  but leaves the identity in `sunkOrder`, so the row holds its bottom position for the session. Keep Core
  `ScribePinOrdering.ForDisplay` unchanged (it stays the pure resting rule; the durable overlay lives in
  the Mod layer).
  → `OnToggleRow` uncheck path is unchanged (never touches `sunkOrder`); added a `Build`-time prune that
  drops identities the server has removed. Core `ForDisplay` untouched.
- [x] 2.4 (Only if the "stays at end after uncheck" ordering needs a testable rule in Core) add a
  Core-pure helper + `Core.Tests` coverage; otherwise leave ordering entirely in the Mod layer and note
  why. Do NOT add a persisted `SunkAt` (session-local per design D3).
  → No Core helper added. The durable ordering is inherently a session-local HUD overlay (it depends on
  the undo-window timing and the live pin set, both Mod-layer state), so there's no game-agnostic rule to
  extract — Core's pure `ForDisplay` remains the resting rule. No persisted `SunkAt` (per D3).

## 3. Settings-form layout & UX polish (`ScribeSettingsContent.cs`)

- [x] 3.1 Lay out HUD maximum rows + HUD row width as two `Expanded` columns in one `Row`.
  → New `PairedControls` helper; max-rows + row-width paired.
- [x] 3.2 Lay out HUD text size + window text size as two `Expanded` columns in one `Row`.
  → HUD-scale + window-scale paired via `PairedControls`.
- [x] 3.3 Put the "Collapse the HUD" checkbox in a `Row` with `MainAxisAlignment.Start` so it hugs its
  label instead of stretching full width.
  → New `HuggingCheckbox` helper (checkbox + label inline, `MainAxisSize.Min` / `MainAxisAlignment.Start`),
  keeping the hover helptext. Replaces the `LabeledControl` wrap for the collapse toggle.
- [x] 3.4 Add up/down arrow-key stepping to the numeric field: Up = value + step, Down = value − step,
  each clamped to the field's range (reuse the +/- clamp). Leave typed-entry behavior unchanged.
  → Could NOT be done on the stock `NumericField`: it owns its inner `TextField` privately (no key hook),
  and `TextField` marks every non-Alt key Handled before it bubbles to any ancestor (verified in
  `EventDispatcher.DispatchKeyDown` + `TextField.OnKeyDown`), so neither a wrapper nor the wiki's
  cross-mod Widget Transformer pipeline can inject arrows. The one public seam is `TextField`'s own
  `onKeyDown` callback (fires before it swallows). Built `ScribeNumericField` (new file) — a parity clone
  of the stock field that uses that seam to turn Up/Down into a `step±`, sharing the +/- `Adjust` path.
  Swapped it into `IntField` + `FontScaleField`. Clamping stays in the caller's onChanged (unchanged).
  → Follow-up (playtest 2): a keypress/step lost focus — the host's write-through `ForceRebuild` unmounts
  the field (and its internal FocusNode), and the value-keyed `SizedBox` remounts it anyway, so only one
  arrow press landed before needing a re-click. Fixed with the lectern's persistent-FocusNode pattern:
  `ScribeNumericField` now takes an optional host-owned `FocusNode` + `autoFocus` + `onStepped`; a new
  host-owned `ScribeNumericFocusRegistry` holds one persistent node per field id and a one-shot "which id
  to refocus" arm. A step arms its id before the write; on the rebuild the remounted field for that id
  re-requests focus in `InitState`. Wired through BOTH hosts (`ScribeSettingsDialog` + the lectern's
  `ScribeSettingsView`), which own+dispose the registry.

## 4. Lang & HUD gear sizing

- [x] 4.1 In `en.json`, rename the presented mid-edge HUD-anchor labels: `Left` → `Mid-Left`,
  `Right` → `Mid-Right`. Enum values / code keys unchanged.
  → `settings-anchor-middleleft`/`-middleright` values renamed; keys and enum untouched.
- [x] 4.2 Reduce the pinned-list HUD gear size constant in `HudScribePins.cs` by ~25% so it reads
  proportionally with the collapse chevron beside it.
  → HUD gear `VsIcon` size 16f → 12f (−25%), proportional to the 14px title/chevron.

## 5. Build & verify

- [x] 5.1 `dotnet build src/Mod/Mod.csproj` clean; `dotnet test tests/Core.Tests/Core.Tests.csproj` green
  (only if any Core helper was added in 2.4).
  → Build clean (0 warnings). Core 102/102 (no Core changes this pass, confirmed green anyway).
- [x] 5.2 `openspec validate scribe-settings-followups` passes.
- [x] 5.3 `bash build/restage.sh Debug` and fully relaunch the client (lang label rename loads at boot).
  → Restaged Debug (13 files). Client relaunch is the user's step before testing.

## 6. In-game verification

- [x] 6.1 Complete a HUD task under Unpin and under Delete: the task text fades *gradually and linearly*
  from full to zero opacity across the ~1.5s window (not an instant jump); the checkbox stays opaque;
  unchecking mid-window restores full opacity and applies nothing.
  - **Confirmed 2026-07-28** (playtest submission 2026-07-28T10-38-17): "Works." TESTING.md `f4825eb0`.
- [x] 6.2 Complete a HUD task under Sink (and Keep): when the window elapses the row settles to the END of
  the list; then uncheck it — it STAYS at the end (does not jump back to its prior slot) for the session.
  - **Backlogged 2026-07-28** (playtest submission 2026-07-28T10-38-17): tester moved to backlog — current behavior feels intuitive. TESTING.md `7a86b890`.
- [x] 6.3 In Settings, confirm max-rows + row-width sit on one row, HUD-size + window-size sit on one row,
  and the Collapse-HUD checkbox hugs its label (not full width).
  - **Confirmed 2026-07-28** (playtest submission 2026-07-28T10-38-17): "Works." TESTING.md `7361924f`.
- [x] 6.4 Focus a numeric field and press Up/Down — the value steps by that field's increment, clamped to
  range.
  - **Confirmed 2026-07-28** (playtest submission 2026-07-28T10-38-17): "Works." TESTING.md `5db8d149`.
- [x] 6.5 Open the HUD-anchor dropdown — the mid-edge options read "Mid-Left" and "Mid-Right"; the HUD gear
  is visibly smaller / proportional to the collapse chevron.
  - **Confirmed 2026-07-28** (playtest submission 2026-07-28T10-38-17): "Works." TESTING.md `6b967f10`.
