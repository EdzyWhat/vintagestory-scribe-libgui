## Why

The `explore-libgui-adoption` spike passed every make-or-break gate and the decision is **GO**
(recorded in that change's proposal, now archived; the `gui-foundation-policy` spec is live). Adopting
LibGUI lets Scribe retire the imperative/absolute-bounds `GuiComposer` path — the source of nearly every
hard-won `VSAPI-NOTES.md` GUI bug — for a declarative widget tree that later tiers reuse. This change is
the first migration step: make LibGUI a real production dependency and render the lectern **read view**
on it, replacing the throwaway spike scaffolding with a production dialog. The editor view, affordance
columns, and theme extraction follow as their own changes.

## What Changes

- **De-spike the build wiring into a production hard dependency.** Remove the "SPIKE ONLY — DO NOT MERGE"
  banner and re-justify the `Gui` / `OpenTK.Mathematics` / `SkiaSharp` references in `src/Mod/Mod.csproj`
  as a production hard dep (mirroring the ConfigLib comment style). `src/Mod/modinfo.json` already declares
  `"gui": "2.0.0"`. Document `gui_2.0.0.zip` re-extraction in `src/Mod/lib/README.md` (the 7 vendored
  managed DLLs), closing the reproducibility gap since `lib/*` is gitignored.
- **Remove spike scaffolding.** Delete `src/Mod/SpikeLibGuiLecternDialog.cs` and the `.scribespike` chat
  command + `RegisterLibGuiSpikeCommand` from `src/Mod/ScribeModSystem.cs`. **Keep**
  `src/Mod/SpikeScribeMultilineField.cs` transiently as the proven reference implementation for the
  editor-view port (deleted once that change lands); mark it reference-only.
- **Render the read view on LibGUI.** Add a production dialog subclassing LibGUI's
  `GuiDialogBlockEntityBase`, opened from the real lectern interaction path (via the existing
  `ScribeRequestAccessMessage` / `ScribeEditDocumentMessage` flow — not `.scribespike`, not direct
  `Document` reuse). Widget tree: `WindowFrame` → `Column` (title + free-text block) → `ListView` of
  read rows (checkbox reflecting Done + wrapped text). The dialog is **read-only** this change; its
  "switch to editor" button opens the **existing native `GuiDialogScribeLectern` editor** (unchanged),
  so editing never breaks during migration. Change 2 replaces the native editor with a LibGUI editor view.
- **Behavior first, read-view visuals deferred.** The LibGUI read view lands functional (document renders,
  Done reflects + toggles lock-free, scrolls, rest of row inert, parchment theme) using LibGUI's stock
  `Checkbox` and plain rows. The skeuomorphic read-view visuals — lined-paper ruling, custom checkbox
  glyph, and text-size-proportional scaling — are **deferred** to the later affordance/theme changes and
  their requirements are relaxed accordingly here.
- **Establish the LibGUI row patterns now:** interactive list rows are self-stateful widgets keyed by
  `ValueKey` (LibGUI's `ListView` caches children by index and does not rebuild them on parent `SetState`),
  and the parchment `ColorScheme`/`ThemeData` is code-defined (theme-JSON extraction is a later change).
- **Delete the superseded stub** `openspec/changes/own-lectern-element-bounds/` (LibGUI is the "own bounds"
  answer from a different direction; migration supersedes it).
- **No `src/Core/` change.** The document model, codec, network packets, and `BlockEntityScribeLectern`
  persistence/sync are reused unchanged — this is a Mod-layer view swap only.

## Capabilities

### New Capabilities
<!-- None. gui-foundation-policy is already a live spec; the act of declaring the `gui` hard dep
     is recorded under Impact, not a new capability. -->

### Modified Capabilities
- `lectern-gui-shell`: the **read-view** rendering requirements change at the spec level. They currently
  mandate native mechanisms — "custom-drawn in the interactive render pass", the engine's "native clip
  region", a per-step "recompose" model, the custom `ScribeRowElement`/`GuiElementSwitch` distinction —
  which LibGUI replaces with a declarative widget tree (`WindowFrame`/`Column`/`ListView`/`Row`) whose
  scroll region does the clipping. The **observable** read-view behavior is preserved: a long document
  stays fully reachable by scrolling, rows show a lined-paper ruling that scrolls with the row, the
  checkbox reflects Done state, clicking it toggles Done without the editor lock (server-authoritative),
  the rest of a read row is inert, and read/editor views share one row-list width. Editor-view
  requirements are **left unchanged** by this change (reworked in the follow-up editor-view change).

## Impact

- **New:** `src/Mod/GuiDialogScribeLecternLibGui.cs` (production read-view dialog on LibGUI).
- **Modified:** `src/Mod/Mod.csproj` (de-spike references → production hard dep), `src/Mod/ScribeModSystem.cs`
  (remove spike command; wire the real dialog open path), `src/Mod/lib/README.md` (gui re-extraction),
  `VSAPI-NOTES.md` (`## LibGUI` — any new lessons hit in practice).
- **Deleted:** `src/Mod/SpikeLibGuiLecternDialog.cs`, `openspec/changes/own-lectern-element-bounds/`.
- **Dependency:** `gui` (modid; `Gui.dll`) becomes Scribe's first hard, always-required mod dependency —
  compliant with `gui-foundation-policy` because a spike cleared every gate. Players install the `gui` mod
  separately (like ConfigLib); `Private=false` keeps `Gui.dll` out of `bin/`, so `build/restage.sh|.ps1`
  and `package.sh` do not (and must not) ship it.
- **Unchanged:** `src/Core/` (VS-API-free invariant intact), the `scribe` network channel + 4 packets,
  codec v3, `BlockEntityScribeLectern` persistence/sync.
- **Deferred (out of scope, later changes):** editor view + custom multi-line field port + keypress-leak
  fix; affordance columns + drag-reorder; theme-JSON hot-reload extraction; retiring `ScribeInspectOverlay`.
