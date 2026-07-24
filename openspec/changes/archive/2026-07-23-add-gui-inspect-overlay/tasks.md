## 1. Config toggle

- [x] 1.1 Add `InspectOverlayMode` (`int`, default `0`) to `ScribeClientConfig.cs` with a doc-comment
      explaining the `0`/`1`/`2` convention (mirrors `GuiComposer.Outlines`), that it ships in Release
      (not `#if DEBUG`), and that it applies on lectern reopen (config is re-read on open).
- [x] 1.2 **REVERTED — do NOT expose in the ConfigLib manifest.** Adding `InspectOverlayMode` as the
      manifest's first `"type": "integer"` entry broke ConfigLib's ENTIRE Mod Settings window (it wouldn't
      open, persisting across a full relaunch) — playtest 2026-07-22T17-45-13. ConfigLib parsed the patch
      without a logged error, so the fault is in its ImGui `ConfigWindow` drawing the integer control, not
      parsing. Removed the entry. Toggle `InspectOverlayMode` by editing `scribe-client-config.json`
      directly instead — the dialog re-reads config on every open, so edit + reopen the lectern is a
      complete toggle path that also doesn't depend on ConfigLib's ImGui window (dead on this Mac anyway).

## 2. Overlay helper (`src/Mod/ScribeInspectOverlay.cs`, new)

- [x] 2.1 Define the box/category model: an `InspectBox` (bounds + key/label + optional driver string
      + category) and an `InspectCategory` enum (Chrome, Viewport, Row, Affordance, Control, Gap) with
      its per-category color table (chrome=white, viewport/content=cyan, rows=green, affordances=orange,
      controls=magenta, gaps=yellow+faint fill).
- [x] 2.2 Implement the stateless `Render(capi, boxes, mode)` draw pass: stroke each box via
      `IRenderAPI.RenderRectangle` at `z≈600` in its category color; in mode `1` draw each label
      (key + `WxH`, plus driver line when present) via a cached `GenTextTexture` → `Render2DLoadedTexture`
      with an opaque `TextBackground`; in mode `2` skip labels. Gaps get a faint white-pixel-blit fill
      (RenderRectangle only strokes). Stagger affordance labels to the box bottom to reduce overlap.
- [x] 2.3 Implement the label-texture cache (keyed by label string; regen only on change) and a
      `Dispose()` that frees all cached `LoadedTexture`s + the shared white-pixel texture.
- [x] 2.4 Add the static `DriverForFixedKey` table (fixed-key drivers) plus a helper contract: per-row
      and gap drivers are formatted at the call site (`BuildInspectBoxes`) from the live
      `RowTextLayout.For(...)` / `ScribeRowElement.*Fixed(...)` values (never hardcoded), degrading to
      key+size when a key has no driver.

## 3. Dialog wiring (`src/Mod/GuiDialogScribeLectern.cs`)

- [x] 3.1 Add `BuildInspectBoxes()` that, reading `SingleComposer`/`rowListContentBounds` LIVE, resolves
      the fixed keys (`rowListScrollbar`, `rowEditInput`, `switchModeButton`, `textSizeSlider`,
      `toolPanelToggleButton`, `addTaskButton`) and per-row keys (`PinKey`/`DeleteKey`/`DragHandleKey`/
      `TextKey`) via the base `SingleComposer.GetElement(key)?.Bounds` (never the kind-specific
      getters — they throw on the wrong kind), skipping absent keys with `?.`.
- [x] 3.2 In `BuildInspectBoxes()`, add the structural bounds the dialog holds (`rowListClipBounds`
      viewport, `rowListContentBounds`) and the `ScaledRowSpacing` gap bands between consecutive rows,
      computed from the two neighboring rows' live bounds. (Per-row drivers re-derive from a live
      `RowTextLayout.For(...)` + `AffordanceButtonSizeFixed` so they can't drift.)
- [x] 3.3 Add `RenderInspectOverlay(dt)` and call it at the END of `OnRenderGUI` (after
      `base.OnRenderGUI(...)`), guarded by `clientConfig.InspectOverlayMode >= 1`; it lazily creates the
      overlay, calls `BuildInspectBoxes()`, then `ScribeInspectOverlay.Render(...)`. The focused row's
      `rowEditInput` is enumerated as its own Affordance box (it's a keyed element).
- [x] 3.4 Own the `ScribeInspectOverlay` instance on the dialog and dispose it in `OnGuiClosed`.

## 4. Optional reflection fallback (off by default)

- [x] 4.1 **Deferred by decision — not shipped.** The known-keys path (3.1) already enumerates every
      element the dialog composes plus the structural viewport/content bounds and gap bands, so nothing is
      currently unlabeled. Reflecting over `GuiComposer`'s internal `staticElements`/`interactiveElements`
      dicts adds real fragility (private-field reflection into a shipped type) for no present coverage gain,
      and the design already flags it as an optional off-by-default escape hatch. Left unbuilt until a
      concrete unlabeled element appears; recorded here so it's a conscious skip, not an oversight.

## 5. Build, test, playtest

- [x] 5.1 `dotnet build src/Mod/Mod.csproj -c Release` — clean (0 warnings/errors). `tests/Core.Tests`
      green (37 passed), unaffected as expected (no Core change).
- [x] 5.2 `bash build/restage.sh Release` — staged clean. (Config-drift guard fired, but the new
      `InspectOverlayMode` key is additive with default `0`: an absent key deserializes to off, so no
      reconcile is needed — set it to `1` in the ConfigLib panel / JSON to test.) Relaunch the client.
- [ ] 5.3 Manually verify in-game (this Mac): open a lectern, set `InspectOverlayMode = 1` in the ConfigLib
      panel, reopen → outlines + labels appear over every box; set `0`, reopen → gone. Confirm gap bands are
      labeled with the right config field; switch to editor view and confirm per-row column labels (`TextX`,
      affordance square) re-derive correctly and the focused `rowEditInput` is annotated.
- [ ] 5.4 Manually verify `InspectOverlayMode = 2` gives clean outlines-only when labels crowd at 30% text
      size; scroll a long list and change text size → outlines track live. Cross-check a couple of labeled
      numbers against `scribe-client-config.json`; edit one value in the ConfigLib panel, reopen, confirm the
      overlay reflects the new number (validates the full diagnose-and-tune loop on this Mac).

## 6. Docs / cleanup

- [x] 6.1 Recorded in `VSAPI-NOTES.md` (after the VSImGui section): the macOS-safe overlay primitives —
      `RenderRectangle` (stroke-only, `ColorUtil.ColorFromRgba`), tinted white-pixel blit for fills,
      `GenTextTexture` cache + dispose lifecycle (`TextureId != 0`, no `Loaded` property), and the
      "screen-space pass not composed child" rule (self-heals after recompose, escapes the clip).
- [x] 6.2 Design.md already folded in the exploration's Context/Decisions/Risks/Open-Questions during
      the propose step; deleted `docs/explorations/gui-inspect-overlay.md` (its explicit temporary holding
      pen — box-model reference and file table live on in the spec/tasks).
