## Context

The `explore-libgui-adoption` spike (archived) cleared every make-or-break gate and the decision is GO.
This is the first migration step. Scribe's lectern GUI today is a single native
`GuiDialogScribeLectern` with two absolute-bounds views (`ComposeReadView`/`ComposeEditorView`), custom
Cairo row elements (`ScribeRowElement`), a shared layout metric (`RowTextLayout`), a floating text input
(`ScribeRowTextInput`), and a `pendingRecomposeAction` reentrancy hack. The spike proved a LibGUI dialog
(`GuiDialogBlockEntityBase`) renders on Apple Silicon, reproduces the block-entity dialog lifecycle,
renders the live document, and — the decisive gate — supports a custom multi-line editable row on
LibGUI's public API.

Work happens in the sibling fork `vintagestory-scribe-libgui` (full-history copy); the native-GUI
original is retained untouched as a fallback. The `gui-foundation-policy` spec is live and permits the
`gui` hard dependency because the spike passed. `src/Core/` (document model, codec) and the network /
persistence layer are untouched — this is a Mod-layer view swap.

## Goals / Non-Goals

**Goals:**
- De-spike the build wiring so `gui` is a bona-fide production hard dependency (no "DO NOT MERGE" framing;
  reproducible vendored DLLs documented).
- Render the lectern **read view** on LibGUI, opened through the real interaction + packet flow, with the
  block-entity dialog lifecycle intact.
- Establish the LibGUI patterns the later changes inherit: self-stateful `ValueKey`-keyed rows, and a
  code-defined parchment `ColorScheme`/`ThemeData`.
- Keep editing fully working throughout by routing "switch to editor" to the existing native editor.
- Remove the throwaway spike scaffolding that must not ship, and delete the superseded
  `own-lectern-element-bounds` stub.

**Non-Goals:**
- Migrating the **editor view** to LibGUI (custom multi-line field port, variable-height rows, keyboard
  model, autosave) — that is change 2.
- Reproducing read-view **skeuomorphic visuals** — the custom checkbox glyph and text-size scaling are
  deferred to the theme/affordance changes; the **lined-paper ruling is dropped entirely** (decision
  2026-07-23: the LibGUI lectern goes in a cleaner, more modern direction), not deferred. Relaxed in
  this change's spec delta.
- The keypress-leak fix (`CaptureAllInputs()` vs `default:` swallow) — no typing in read view; resolved in
  the editor-view change.
- Theme-JSON hot-reload (the one unchecked spike gate) — gates only the later theme-extraction change.
- Any `src/Core/`, network-packet, codec, or persistence change.

## Decisions

**D1 — One change bundles de-spike + read view, not two.** De-spiking alone produces no observable
behavior (an empty-ish spec delta); the read-view-for-real is the smallest change carrying a real
capability delta and it needs the de-spike as prerequisite plumbing. Bundling tells one coherent story.
*Alternative considered:* a standalone de-spike change first — rejected as ceremony with no delta.

**D2 — Read-only LibGUI dialog; "switch to editor" opens the existing native editor.** During the interim
the LibGUI dialog owns only the read view; its toggle re-opens the unchanged native
`GuiDialogScribeLectern` in editor mode. Editing never breaks on the fork's `main`.
*Alternative considered:* a non-functional stub editor branch inside the LibGUI dialog — rejected because
it breaks editing during the interim (only the untouched original repo would work).

**D3 — Behavior-first read view; visuals deferred.** Land a functional read view with LibGUI's stock
`Checkbox` and plain rows; defer ruling, custom checkbox glyph, and text-size scaling. Smaller, verifiable
first step; a temporary visual downgrade on the fork only.
*Alternative considered:* full visual parity now — rejected as a much larger first bite that front-loads
custom-paint work better done once the theme layer exists.

**D4 — Self-stateful, `ValueKey`-keyed rows from day one.** LibGUI's `ListView` caches children by index
and does not rebuild them on parent `SetState` (banked spike lesson). Even near-static read rows adopt the
self-stateful + keyed pattern so changes 2–3 (editable rows, drag-reorder) inherit it without a rewrite.
*Alternative considered:* controlled-component (parent-owned) rows — rejected; the spike proved they go
stale in a `ListView`.

**D5 — Code-defined parchment theme.** The `ColorScheme`/`ThemeData` is defined in C# this change; the
~60 `ScribeClientConfig` knobs stay as-is. Theme-JSON hot-reload (unproven gate) is deferred to change 4,
which verifies-or-falls-back. This unblocks changes 1–3 from the one unchecked gate.

**D6 — Production dialog is a new file, native dialog untouched.** Add
`src/Mod/GuiDialogScribeLecternLibGui.cs`; leave `GuiDialogScribeLectern.cs` in place (it still serves the
editor view via D2). The two coexist until change 2 retires the native editor.
*Alternative considered:* editing `GuiDialogScribeLectern.cs` in place — rejected; cleaner to add the
LibGUI dialog beside it and delete native pieces as each view migrates.

**D7 — Keep `SpikeScribeMultilineField.cs` as reference; delete the spike dialog.** The multi-line field
is the proven reference implementation for the change-2 editor port, so it stays (marked reference-only)
until that lands. `SpikeLibGuiLecternDialog.cs` + the `.scribespike` command are pure test scaffolding and
are deleted now.

## Risks / Trade-offs

- **First hard mod dependency** → Compliant with the live `gui-foundation-policy` (spike passed every
  gate); players install `gui` separately like ConfigLib. `Private=false` keeps `Gui.dll` out of `bin/`, so
  release/staging scripts don't ship it — verified as a task, not assumed.
- **Vendored `lib/*` DLLs are gitignored** → any build machine must re-extract them. Mitigate by documenting
  `gui_2.0.0.zip` → 7 managed DLLs in `src/Mod/lib/README.md` (the reproducibility gap the spike flagged).
- **Two dialogs coexist during the interim** → slight duplication and a visible seam (LibGUI read view vs
  native editor look). Accepted as temporary; change 2 unifies them. Editing never breaks — the upside.
- **Creative-mode walk-away auto-close** → Scribe overrides `IsInRangeOfBlock` on the native dialog; LibGUI's
  base uses `IsOutOfRange`/`InteractionRange`, a different override point. The spike did not confirm
  walk-away auto-close. Mitigate: re-check the override point here and add a survival-mode walk-away test to
  the playtest checklist; do not assume the native override transfers.
- **LibGUI's Harmony/`VanillaDialogCleanup` patches vanilla dialogs globally** (a click sound already leaked
  onto Scribe's native toggle button). Benign now; monitor as a compatibility vector with other GUI mods and
  across VS updates (LibGUI is pinned to 1.22.x and hijacks the ortho render stage).
- **Read-view visual downgrade** (no custom glyph/scaling) → temporary and fork-only; the native
  original remains the fallback and those visuals return in the theme/affordance changes. The
  **lined-paper ruling does NOT return** — it's dropped from the roadmap (decision 2026-07-23, cleaner
  modern direction), not part of the downgrade-then-restore set.

## Migration Plan

1. De-spike `Mod.csproj`; document `lib/README.md`; delete `SpikeLibGuiLecternDialog.cs` + the `.scribespike`
   command; keep `SpikeScribeMultilineField.cs` (reference-only).
2. Add `GuiDialogScribeLecternLibGui.cs` (read-only), wired into the real open path; route its "switch to
   editor" to the native editor.
3. Delete the `own-lectern-element-bounds` stub.
4. Build (`-c Release`) clean; `dotnet test tests/Core.Tests` green; `restage.sh`/`.ps1` stage without
   `Gui.dll`; in-game read-view playtest (see tasks).

**Rollback:** the untouched original repo `vintagestory-scribe` is the fallback; on this fork, reverting the
change restores the native read view (the native dialog was never removed).

## Open Questions

- Does the fork get its own GitHub remote, and does it keep modid `scribe` or eventually replace the original
  as the shipped mod? (Deferred; the inherited `origin` is already detached so no accidental push to the
  original.)
- `scribe-layout-workbench` (sibling tool) mirrors the absolute-bounds math the flex model obsoletes — mark
  dormant now, decide retire-vs-repoint during change 3/4.
