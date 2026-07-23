## 1. Config toggle

- [ ] 1.1 Add `InspectOverlayMode` (`int`, default `0`) to `ScribeClientConfig.cs` with a doc-comment
      explaining the `0`/`1`/`2` convention (mirrors `GuiComposer.Outlines`), that it ships in Release
      (not `#if DEBUG`), and that it applies on lectern reopen (config is re-read on open).
- [ ] 1.2 Add `InspectOverlayMode` to `src/Mod/assets/scribe/config/configlib-patches.json` as an
      int/dropdown (0–2) so it's toggleable in the in-game ConfigLib panel on this Mac.

## 2. Overlay helper (`src/Mod/ScribeInspectOverlay.cs`, new)

- [ ] 2.1 Define the box/category model: an `InspectBox` (bounds + key/label + optional driver string
      + category) and an `InspectCategory` enum (Chrome, Viewport, Row, Affordance, Control, Gap) with
      its per-category color table (chrome=white, viewport/content=cyan, rows=green, affordances=orange,
      controls=magenta, gaps=yellow+faint fill).
- [ ] 2.2 Implement the stateless `Render(capi, boxes, mode)` draw pass: stroke each box via
      `IRenderAPI.RenderRectangle` at `z≈600` in its category color; in mode `1` draw each label
      (key + `WxH`, plus driver line when present) via a cached `GenTextTexture` → `Render2DLoadedTexture`
      with an opaque `TextBackground`; in mode `2` skip labels. Stagger affordance labels to the box
      bottom to reduce overlap.
- [ ] 2.3 Implement the label-texture cache (keyed by label string; regen only on change) and a
      `Dispose()` that frees all cached `LoadedTexture`s (and dispose superseded entries on regen).
- [ ] 2.4 Add the static `key-pattern → driver-string` table and a helper that formats a per-row
      key's driver live from `RowTextLayout.For(...)` / `ScribeRowElement.*Fixed(...)` (never hardcoded),
      degrading to key+size when a key has no table entry.

## 3. Dialog wiring (`src/Mod/GuiDialogScribeLectern.cs`)

- [ ] 3.1 Add `BuildInspectBoxes()` that, reading `SingleComposer`/`rowListContentBounds` LIVE, resolves
      the fixed keys (`rowListScrollbar`, `rowEditInput`, `switchModeButton`, `textSizeSlider`,
      `toolPanelToggleButton`, add-task) and per-row keys (`PinKey`/`DeleteKey`/`DragHandleKey`/
      `ToggleKey`/`TextKey`) via the base `SingleComposer.GetElement(key)?.Bounds` (never the kind-specific
      getters — they throw on the wrong kind), skipping absent keys with `?.`.
- [ ] 3.2 In `BuildInspectBoxes()`, add the structural bounds the dialog already holds
      (`rowListContentBounds`, clip bounds, `bgBounds`) and the gap bands (`TopContentGap`,
      `ElementToDialogPadding`, `ScaledRowSpacing` between consecutive rows, `ListToControlsGap`,
      `ControlRowGap`) computed from config values + neighboring row bounds.
- [ ] 3.3 Add `RenderInspectOverlay(dt)` and call it at the END of `OnRenderGUI` (after
      `base.OnRenderGUI(...)`), guarded by `clientConfig.InspectOverlayMode >= 1`; it calls
      `BuildInspectBoxes()` then `ScribeInspectOverlay.Render(...)`. Annotate the focused row / `rowEditInput`
      specially (it suppresses its static label and hosts the input).
- [ ] 3.4 Own the `ScribeInspectOverlay` instance on the dialog and dispose its label cache in
      `OnGuiClosed`.

## 4. Optional reflection fallback (off by default)

- [ ] 4.1 Add a `IncludeUnknownElements` path (off by default, try/catch-guarded) that reflects over the
      composer's internal `staticElements`/`interactiveElements` dicts to outline unkeyed elements — kept
      off the main path so the known-keys route stays reliable.

## 5. Build, test, playtest

- [ ] 5.1 `dotnet build src/Mod/Mod.csproj -c Release` — clean (0 warnings/errors). No Core change, so
      `tests/Core.Tests` should be unaffected; run it to confirm still green.
- [ ] 5.2 `bash build/restage.sh Release` (Release is the point — Debug/ImGui is dead here) → fully quit
      and relaunch the client.
- [ ] 5.3 Manually verify in-game (this Mac): open a lectern, set `InspectOverlayMode = 1` in the ConfigLib
      panel, reopen → outlines + labels appear over every box; set `0`, reopen → gone. Confirm gap bands are
      labeled with the right config field; switch to editor view and confirm per-row column labels (`TextX`,
      affordance square) re-derive correctly and the focused `rowEditInput` is annotated.
- [ ] 5.4 Manually verify `InspectOverlayMode = 2` gives clean outlines-only when labels crowd at 30% text
      size; scroll a long list and change text size → outlines track live. Cross-check a couple of labeled
      numbers against `scribe-client-config.json`; edit one value in the ConfigLib panel, reopen, confirm the
      overlay reflects the new number (validates the full diagnose-and-tune loop on this Mac).

## 6. Docs / cleanup

- [ ] 6.1 Record anything non-obvious learned building this in `VSAPI-NOTES.md` (e.g. `RenderRectangle`
      z-ordering vs the dialog, `GenTextTexture` lifecycle, `GetElement` vs kind-specific getters if it bit).
- [ ] 6.2 Migrate any still-relevant Decisions/Open-Questions from `docs/explorations/gui-inspect-overlay.md`
      into this change's `design.md`, then DELETE the exploration file (it's an explicit temporary holding pen).
