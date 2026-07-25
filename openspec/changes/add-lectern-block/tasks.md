## 1. Solution & project scaffolding

- [x] 1.1 Create the solution `Scribe.slnx` and a `Directory.Build.props` setting `TargetFramework=net10.0`, `LangVersion=latest`, `Nullable=enable` (+ `VintageStoryPath` fallback)
- [x] 1.2 Create `src/Core/Core.csproj` (a plain classlib, NO game references)
- [x] 1.3 Create `tests/Core.Tests/Core.Tests.csproj` (xUnit, references `Core`)
- [x] 1.4 Create `src/Mod/Mod.csproj` referencing `Core` and `VintagestoryAPI.dll` via `<HintPath>$(VintageStoryPath)/VintagestoryAPI.dll</HintPath>` (Private=false)
- [x] 1.5 Add all three projects to the solution; confirm Core, Core.Tests, AND Mod all build

## 2. Core: block-based document model (test-first)

Document = an ordered sequence of blocks; each block is a task (text + done) or a text
section (freeform), interspersable and reorderable, with a reserved depth for future nesting.

- [x] 2.1 Write failing xUnit tests for the document structure (new doc is empty; block order + kinds preserved)
- [x] 2.2 Write failing tests for add task / add text section (adds to end; task trims + rejects blank; text section allows empty)
- [x] 2.3 Write failing tests for edit block text (keeps done flag/kind; task rejects blank; text section allows empty)
- [x] 2.4 Write failing tests for toggle completion (both directions; fails on a text section; invalid position fails safely)
- [x] 2.5 Write failing tests for delete + reorder (delete preserves order; move up/down; move-to-same is no-op; invalid position fails safely)
- [x] 2.6 (covered by 2.2/2.3 — text sections replace the single note)
- [x] 2.7 Write failing tests for the serialization round-trip (order/kind/text/done/depth preserved; malformed/empty bytes fail safely without throwing)
- [x] 2.8 Implement `ScribeBlock` (Kind, Text, Done, Depth) and `ScribeDocument` (ordered blocks) with mutations returning success/failure
- [x] 2.9 Implement the byte-array codec (serialize/try-deserialize) used by both persistence and networking
- [x] 2.10 Run `dotnet test` — all Core tests pass (27)

## 3. Mod: assets & mod metadata

- [x] 3.1 Add `src/Mod/modinfo.json` (`type: code`, `side: Universal`, `requiredOnServer: true`, id/name/version, game 1.22 dependency)
- [x] 3.2 Confirm (in-game creative search / `.blockcode`) the vanilla lectern shape+texture codes to reuse; record them
      -> confirmed: vanilla `clutter` block, type `bookshelves/lecturn-book-open`
         (switched from the "aged" variant: aged implies scavenged ancient material,
         not something crafted from ordinary wood); shape
         `block/clutter/bookshelves/lecturn-book-open`, textures verified on disk.
- [x] 3.3 Add the lectern block JSON under `assets/scribe/blocktypes/` reusing that shape, wiring it to the custom block/block-entity classes; creative-inventory only (no crafting recipe in v1)
- [x] 3.4 Add `assets/scribe/lang/en.json` for the block name, GUI labels, and hotkey name

## 4. Mod: block, persistence, networking (server-authoritative)

Atlas tests interleave here rather than batching at the end: each test lands right after
the implementation task that makes it possible, so persistence/networking/the lock get a
fast, automated check before we ever touch the GUI or manual playtesting (group 7). Atlas
needs the game install (`VINTAGE_STORY`) and runs via `dotnet test` against a real headless
server — a LOCAL suite only, not run on cloud CI.

- [x] 4.1 Implement `ScribeModSystem` (register block/block-entity classes and the network channel + message type in `Start`; per-side handlers in `StartClientSide`/`StartServerSide`)
- [x] 4.2 Implement `BlockScribeLectern` (placement/break; `OnBlockInteractStart` opens the GUI client-side)
- [x] 4.3 Implement `BlockEntityScribeLectern` holding a `ScribeDocument`; `ToTreeAttributes`/`FromTreeAttributes` serialize via the Core codec (persist + initial sync)
- [x] 4.3a Add a `tests/Integration.Tests` project referencing `Pixnop.Atlas.XUnit`, loading the built mod via `[AtlasMods(...)]` — the first point there's something real to boot a server against
- [x] 4.3b Atlas test: persistence — place a lectern, edit its document, reload the world, assert the document survives (`RollbackWorld`/`RestartWorld` isolation)
- [x] 4.4 Define the `[ProtoContract]` edit message (Core-serialized document bytes + block position) and register it identically on both sides
- [x] 4.5 Server handler applies the incoming document to the block entity and calls `MarkDirty(true)` to persist + re-sync to all clients
- [x] 4.5a Atlas test: server-authoritative edit — send an edit packet, assert the block entity's stored document updates and re-syncs
- [x] 4.6 Implement the single-editor lock: server tracks position→holder UID; refuse a second opener with the "one person at a time" message; release on close and on disconnect/leave
- [x] 4.6a Atlas test: the lock — first opener acquires it; a second opener is refused; lock releases on close/disconnect
- [x] 4.7 Document how to run the Atlas suite locally in the README (it's excluded from cloud CI)

## 5. Mod: editor GUI

- [x] 5.1 Implement `GuiDialogScribeLectern` (a `GuiDialogBlockEntity`) rendering the document's ordered blocks
- [x] 5.2 Each block row: task rows have a complete-toggle + editable text + delete; text-section rows have editable text + delete; plus "add task" / "add text section" controls
      -> PARTIALLY SUPERSEDED by the LibGUI rebuild: the per-row **delete** affordance was dropped
         during `migrate-editor-view-libgui` (deferred as a "per-row icon control", never rebuilt)
         and does NOT exist in `GuiDialogScribeLecternLibGui`. The complete-toggle + editable text
         survive. Text-section creation was intentionally removed per 8.18. Per-row delete is being
         rebuilt under a NEW dedicated change (delete + reorder + pin affordances on LibGUI). Core
         `ScribeDocument.DeleteBlock` remains and is unit-tested.
- [x] 5.3 Collapsible tool panel with a per-option **visibility-predicate hook** (the gating mechanism); wire NO real gates in v1 (all options visible)
      -> SUPERSEDED by the LibGUI rebuild: the collapsible tool panel has no LibGUI equivalent and
         was not carried over (silently dropped in migration). Orphaned config knobs
         (`ToolPanelToggleWidth`, the "Editor toolbar" block in `ScribeClientConfig`) linger unused
         and are being removed under task 8.15's cleanup. The visibility-predicate gating hook is
         not currently present; re-introduce it with the future affordances change if still wanted.
- [x] 5.4 Reorder mode: mouse-drag reordering of block rows in the list (consistent with VS's mouse-driven crafting grid/blacksmithing/clayforming interactions), calling Core `MoveBlock(from, to)` on drop
      -> SUPERSEDED by the LibGUI rebuild: mouse-drag reorder was NOT ported to
         `GuiDialogScribeLecternLibGui` (deferred at migration, explicitly planned to return under
         LibGUI). There is currently NO way to reorder rows. Being rebuilt under the NEW dedicated
         delete/reorder/pin change. Core `ScribeDocument.MoveBlock` remains and is unit-tested.
- [x] 5.5 Text size control as a **client-side display preference** (scales GUI font; stored in local mod config, NOT in the document, NOT synced)
      -> REDESIGNED (not regressed) by the LibGUI rebuild: the in-GUI text-size SLIDER was
         intentionally replaced by config-file scaling. `ScribeClientConfig.TextSizeScale` is read
         once per dialog-open and applied at the single `ScribeRowStyle.FromConfig` chokepoint
         (`unify-row-sizing-libgui`). The preference still lives in local mod config, not the
         document, not synced -- it's just edited via JSON / a ConfigLib panel now, not a slider.
- [x] 5.6 Edit-mode toggle keybind: GUI opens in a resting state; pressing the key enters edit mode with an immersive "pull out the pen/stylus" beat
      -> superseded during planning: no hotkey. Plain right-click opens a lock-free read
         view; shift+right-click (or the in-GUI toggle button) opens/switches to the
         lock-holding editor view. See design.md-equivalent plan notes for the full
         two-view rationale.
- [x] 5.7 On save, send the edited document to the server over the channel; reflect the server-synced state on reopen
      -> superseded during planning: no explicit Save action. Editor-view edits autosave
         via a throttled (1s) dirty-flag tick, force-flushed on mode-switch/close/
         walk-away; the server acks failures (e.g. lost lock) back to the client.

## 6. Build & release automation

- [x] 6.1 Add `.github/workflows/ci.yml`: on push/PR, `dotnet test` the Core project (cloud runners have no game DLL) — document this scope in the README
- [x] 6.2 Add release packaging (`.github/workflows/release.yml` on tag `v*`) that builds the mod locally-style and zips `modinfo.json` + assets + the compiled DLL into `Releases/`
- [x] 6.3 Verify CI is green on a pushed branch

## 7. In-game verification (local, this Mac)

Written for the two-view design: plain right-click opens a lock-free **read view**;
shift+right-click (or the in-GUI toggle button) opens/switches to the lock-holding
**editor view**.

- [x] 7.1 Build the mod and copy it into `~/Library/Application Support/VintagestoryData/Mods`; launch the game
      -> Done: the build+restage+relaunch loop is exercised every session (`build/restage.sh` → full
         client relaunch); the mod loads and runs in-game, which every later group-7/8 verdict relies on.
- [x] 7.2 Place a lectern (from creative inventory); plain right-click opens a read view with no edit controls; shift+right-click opens the editor view; add tasks, complete one, edit the note, and confirm edits autosave (no explicit Save button — check a moment after typing, before closing, that the change already round-tripped)
      -> Confirmed 2026-07-21 (TESTING.md `c9c26fc3`).
- [x] 7.3 Save and reload the world; confirm the lectern's tasks and note persist
      -> Confirmed 2026-07-21 (TESTING.md `97938f33`).
- [x] 7.4 Toggle check: from the editor view, click the in-GUI toggle to switch to read view (confirm the just-typed edit is reflected, not stale); from the read view, click the toggle to request the editor view back
      -> Confirmed 2026-07-21 (TESTING.md `89290239`).
- [ ] 7.5 Multiplayer check: run a local headless server (`dotnet ".../VintagestoryServer.dll" --dataPath ~/vsdata`) with the mod, connect a second client, confirm an edit by one session is seen live in the other session's *read* view, and that two separate lecterns hold independent documents
- [ ] 7.6 Lock check: with the editor view open in one session, confirm a second session's shift+right-click (or toggle-to-editor) is refused with the "one person at a time" message but still shows current content read-only; confirm a second session's plain right-click (read view) is granted normally even while the editor lock is held elsewhere; confirm closing the editor view or disconnecting releases the lock for the next requester
- [ ] 7.7 Reorder + tool panel check: in the editor view, mouse-drag a row to reorder it; collapse/expand the tool panel; adjust the text-size slider and confirm the font scales and the preference persists across reopen
      -> OBSOLETE (as written): this was Confirmed 2026-07-21 (TESTING.md `32876056`) against the NATIVE
         GUI, but all three features it exercised are gone/redesigned in the LibGUI rebuild, so the task
         no longer applies as phrased. Coverage moved elsewhere: drag-reorder was re-verified and
         Confirmed under `add-lectern-row-affordances-libgui` (archived 2026-07-24, task 5.4 / TESTING.md
         `08cae46d`); the tool panel does not exist in `GuiDialogScribeLecternLibGui`; the text-size
         slider was replaced by config-file scaling, covered by the unify-row-sizing work. Left unchecked
         deliberately (obsolete is a terminal non-done state), not pending.
- [x] 7.8 Walk-away check: open the editor view, make an edit, walk out of interaction range without closing the GUI; confirm the dialog auto-closes and the edit was flushed (reopen and see it persisted) rather than lost
      -> Confirmed 2026-07-21 in Creative (TESTING.md `9c04c5c7`); the LibGUI dialog pins
         `InteractionRange` to `DefaultPickingRange + 0.5` so walk-away auto-close fires in every
         game mode. Survival open/walk-away not yet exercised in-game (verified by analysis).

## 8. Playtesting bugfixes (found during group 7)

UI pass already landed (icon buttons + hover tooltips, wider rows, `Scribe.Core.dll`
packaging fix) — these three are what's left from the first real playtest.

- [x] 8.1 Fix crash: `ArgumentException: Image surface width and hight must be above 0` in
      `Cairo.SurfaceTransformBlur.BlurPartial`, reached via
      `GuiElementDialogBackground.ComposeElements` <- `GuiComposer.Compose` <-
      `GuiDialogScribeLectern.ComposeEditorView` <- `OnClickAddTask`. Repro: add a task, add a
      text/note block, then add another task -> crash.
      -> done. Real root cause (confirmed via decompiled vsapi source, not the earlier
         hover-text/FlatCopy hypothesis, which a standalone isolated-harness repro disproved):
         `ScribeBlockRowCell.Compose` called `GetTextInput(...).SetValue(...)` /
         `GetTextArea(...).SetValue(...)` on a row's text element *during* composer construction
         -- before the composer's own `.Compose()` had ever run `CalcWorldBounds()` on the tree.
         At that moment `Bounds.InnerWidth` is still its field default, `0`. `SetValue` ->
         `LoadValue` -> `TextChanged()` computes the element's auto-height/line-wrap against that
         0-width box, corrupting the value baked into the tree; `GuiComposer.Compose()` swallows
         any resulting `CalcWorldBounds()` exception (log-only, no rethrow) and falls through to
         build a Cairo surface from whatever `OuterWidth`/`OuterHeight` were left at (0), which
         `BlurPartial` then rejects. This also explains a second crash trace found in the user's
         own logs (`EnterMode`/`SwitchMode` -> `ComposeEditorView`, on entering editor mode with
         persisted note text, unrelated to row *count*) -- same defect, different entry point.
         Fix: split `ScribeBlockRowCell.Compose` (adds elements only, no `SetValue`/`On` writes)
         from a new `ApplyValues` (seeds toggle state + text), called once per row from
         `ComposeEditorView` immediately after `SingleComposer.EndChildElements().Compose()`, so
         every `SetValue` now runs against real, already-calculated bounds. Mod.csproj builds
         clean; Core.Tests still 27/27.
- [x] 8.2 Make drag/drop reordering always-on in the editor view (`isReorderMode` should not be
      a togglable mode) and remove the "reorder" toggle button from the tool panel entirely
      (`GuiDialogScribeLectern.ToolbarOptions()`/`OnClickToggleReorder`).
      -> done: removed `isReorderMode`/`OnClickToggleReorder`/the toolbar entry/the dead
         `scribe-gui-reorder` lang key; `ScribeBlockRowCell.Compose` always gets
         `showDragHandle: true`. Mod.csproj builds clean.
- [x] 8.3 Investigate the read view's walk-away auto-close at extreme distance in creative mode:
      user confirmed the editor view's auto-close is explained by creative's
      `PickingRange = 100`, but reported the read view specifically still does not close even
      past 100 blocks. Confirm whether this is a real read-view-specific bug or the same
      expected-distance behavior, and fix if it's real.
      -> LIKELY OBSOLETE under the LibGUI rebuild: this described a NATIVE two-dialog artifact
         (separate read vs. editor dialogs with divergent range handling). The LibGUI rebuild uses a
         SINGLE dialog for both views with one `InteractionRange => DefaultPickingRange + 0.5`
         override (`GuiDialogScribeLecternLibGui`), so a read-view-specific range difference can't
         exist as described -- the read view auto-closes on the same pinned distance the editor view
         does (which 7.8 confirmed). Needs a quick in-game confirm (creative, walk past the close
         threshold in read view -> dialog closes); if confirmed, mark Obsolete in TESTING.md. No code
         change expected.
- [x] 8.4 CRITICAL, blocks 8.2: `HitTestRowIndex` (`GuiDialogScribeLectern.cs`) calls
      `SingleComposer.GetTextInput(...)` unconditionally for every row, but text-section rows are
      registered as `GuiElementTextArea` (via `AddTextArea`), not `GuiElementTextInput` --
      `GetTextInput`'s cast throws `InvalidCastException`. Repro: any document containing a
      text/note block, then drag-reorder any row -> crash. Once 8.2 makes reorder always-on,
      this fires on nearly every drag once a note block exists. Fix `HitTestRowIndex` to look up
      the row's bounds without assuming task-vs-text kind (e.g. branch on `block.IsTask`, or read
      bounds via a kind-agnostic accessor).
      -> done: switched to `SingleComposer.GetElement(key)?.Bounds` (base `GuiElement`, no
         kind-specific cast). Mod.csproj builds clean.
- [x] 8.5 Every structural editor-view mutation (add/delete/toggle-reorder-mode/panel-collapse/
      slider-change) rebuilds `SingleComposer` from scratch and calls the default `.Compose()`,
      which focuses element 0 -- typing in row 3 then clicking delete on row 5 (or touching the
      slider/panel toggle) silently yanks focus/caret to whatever is index 0 in the new layout.
      Pass `focusFirstElement: false` (or otherwise preserve focus) on recompose.
      -> LARGELY RESOLVED by the LibGUI rebuild (the native `SingleComposer`/`focusFirstElement`
         mechanism this describes no longer exists). `ForceRebuild` PRESERVES keyboard focus: the
         dialog owns its `FocusNode`s in `editorFocusNodes` (outside the widget tree) and the
         `FocusManager` is a `GuiBase` field untouched by rebuild, so the focused node stays focused
         across a recompose. Verified against `reference/vslibgui`. Residue: `ForceRebuild` recreates
         each field's `State`, so `ScribeMultilineFieldState.InitState` re-seeds the caret to
         end-of-text on the focused row. Checkbox toggles use incremental `SetState` (no
         `ForceRebuild`), so they don't disturb the caret at all. The remaining caret-to-end residue
         is minor; if it proves bothersome in play, preserve/restore the caret offset across
         `ForceRebuild` (as `EnterEditorMode`/add-task already track the focused index). Confirm
         in-game during the Part-B (delete/reorder) testing, since those paths rebuild too.
- [x] 8.6 A failed autosave (lock lost mid-edit) only shows a one-time toast
      (`scribe-gui-save-failed`) with no retry/re-request path; `isDirty` is already cleared by
      the time the failure ack arrives, so further edits that don't happen to coincide with a
      *new* mutation are silently never resent. Low priority (currently hard to reach in normal
      play), but worth a recovery path eventually.
      -> done: added a bounded recovery path. `GuiDialogScribeLecternLibGui.HandleSaveFailed()`
         (called from `BlockEntityScribeLectern.HandleServerReply` only for the
         `scribe:scribe-gui-save-failed` ack) re-requests the editor lock while keeping the unsaved
         scratch intact (via a `recoveringLostLock` flag). A recovery re-grant lands back in
         `EnterEditorMode`, which detects the flag, keeps the scratch (instead of reseeding from the
         authoritative doc, which would discard the edit), and re-flushes the pending edit. Bounded
         by `MaxSaveFailureRetries` (3) so a lock genuinely held by another player can't spin the
         re-request/resend loop forever -- after the cap it stops with the edits still visible and
         the one-time toast already shown. The counter resets on lock re-acquire and on leaving the
         editor. A bare edit resend alone can't work (the server's `ApplyEdit` requires holding the
         lock), which is why recovery goes through re-requesting access, not just re-setting
         `isDirty`. Mod.csproj builds clean.
- [x] 8.7 `BlockEntityScribeLectern.ApplyEdit` accepts client-submitted `DocumentBytes` with no
      server-side cap on block count or per-block text length beyond the codec's weak sanity
      check. Add a reasonable size/length bound server-side before persisting, since this is
      trusted client input from whoever holds the edit lock.
      -> done: added `ScribeDocumentCodec.MaxBlocks` (1000) and `MaxTextLength` (10 000 chars)
         constants enforced in `TryDeserialize` -- the single chokepoint for BOTH the network edit
         path and world-persistence load, so one check covers both. An over-cap payload fails
         deserialization; `ApplyEdit` then returns false and the existing save-failed ack fires (no
         separate server-side check needed). Kept entirely in Core (no VS API dependency). Added 4
         Core.Tests (`TryDeserialize_At/OverBlockCountCap`, `.../TextLengthCap`); Core.Tests now
         47/47 green.
- [x] 8.8 Editor-view (and read-view) top spacing: the title bar sat right on top of the first
      row with no gap (screenshot: `screenshots/debug/2026-07-18_12-22-56_*.png`) -- content
      inside `BeginChildElements` started at y=0, flush against the title bar.
      -> done: added a `TopContentGap` (12px) constant, both compose methods now start their
         `y` cursor there instead of 0. Mod.csproj builds clean.
- [x] 8.9 "Add Task" placeholder text is a bad user-facing default (currently literally the lang
      key `scribe-gui-newtask-placeholder`, but even resolved it reads awkwardly). Pick a
      friendlier placeholder (e.g. "New task" is already the intended value -- confirm it reads
      well now that 8.12 is fixed, or pick something better like an empty string with a grey
      placeholder-style hint instead of literal seeded text).
      -> partial: the raw-key text seen in the screenshot was NOT a stale-build issue as
         originally guessed here -- see 8.12, the actual (and only) root cause. Still open:
         consider switching to `GuiElementTextInput.SetPlaceHolderText(...)` (a real faded-hint
         API, confirmed via decompile) instead of seeding literal "New task" content, so a new
         task starts genuinely empty -- this needs a Core semantics change to `AddTask`
         (currently rejects blank text, a tested invariant) and wasn't done solo without user
         sign-off.
      -> RESOLVED (option a, user sign-off 2026-07-23): keep the seeded "New task" text and close
         this. The awkward display 8.9 originally flagged was 8.12's domain-prefix bug (now fixed);
         "New task" reads fine. The faded-hint alternative (option b) doesn't port cleanly anyway --
         the LibGUI editor uses the custom `ScribeMultilineField`, which has no placeholder-hint
         support, and it would require relaxing Core `AddTask`'s tested blank-text invariant for a
         marginal gain. Not worth it. No code change.
- [x] 8.10 Text-size slider changes the font but not the text input/text area's own height --
      at larger scales, the bottom half of each row's letters gets clipped. Fix: grow row height
      in step with `TextSizeScale` rather than the current fixed `TaskRowHeight`/
      `TextSectionRowHeight` constants (`ScribeBlockRowCell.RowHeight`).
      -> done: `RowHeight(block, textSizeScale)` now multiplies by scale; both compose methods
         pass `clientConfig.TextSizeScale`. Mod.csproj builds clean; Core.Tests still 27/27.
- [x] 8.11 Text-size slider drag behavior is broken: click-and-hold should let you freely drag
      from one end to the other, but it currently only moves one step (e.g. 100% -> 80%) per
      press-and-hold, requiring release-and-reclick to move further.
      -> done: root cause was `OnTextSizeSliderChanged` calling `ComposeEditorView()` on every
         intermediate value, which rebuilds a brand-new `GuiElementSlider` mid-drag -- the fresh
         instance never saw the original mouse-down, so the drag effectively restarts each step.
         `GuiElementSlider.TriggerOnlyOnMouseUp` (the API's own fix for exactly this) turned out
         to be `internal`, inaccessible from mod code -- deferred the recompose ourselves instead
         via a `textSizePendingRecompose` flag, flushed in the dialog's own `OnMouseUp` override.
         Mod.csproj builds clean.
- [x] 8.12 CRITICAL, was mistaken for a staging/stale-DLL issue across several earlier rounds:
      every single `Lang.Get(...)` call in the mod (dialog title, every button/label/hint, the
      crosshair interaction help text, the new-task placeholder) rendered as its own raw lang
      key, even on a freshly-placed block with a confirmed-correct, freshly-staged `en.json` --
      screenshots `screenshots/debug/2026-07-18_13-4[4-5]-*_helptext-issue-fresh-lectern.png`
      prove this reproduces with no staging involved at all.
      -> done. Real root cause (confirmed via decompiled `Vintagestory.API.Config.TranslationService`
         source, and independently corroborated by a real third-party mod's own code
         (`xlib:levelup`) doing the same prefix): every lang entry loaded from
         `assets/scribe/lang/en.json` gets registered keyed by its owning domain --
         `"scribe:scribe-gui-title"`, not `"scribe-gui-title"` (`TranslationService.LoadEntry` ->
         `KeyWithDomain(entry.Key, item.Location.Domain)`). `Lang.Get(key)` looks up
         `KeyWithDomain(key)`, which defaults to domain `"game"` when the key contains no `:` --
         it does NOT infer "the calling mod's own domain" from context. Every call site in this
         mod called `Lang.Get("scribe-gui-...")` with no domain prefix, so every lookup was
         actually hitting `"game:scribe-gui-..."`, which never existed -> silent fallback to
         printing the raw key (`Lang.Get`'s documented behavior on a missing key). Same mechanism
         for `WorldInteraction.ActionLangCode` (the crosshair hover-help) and the network-carried
         `RefusalReason` string. Fix: prefixed every lang key with `scribe:` at every call site
         across `BlockScribeLectern.cs`, `GuiDialogScribeLectern.cs`, `ScribeBlockRowCell.cs`,
         `BlockEntityScribeLectern.cs` (including the server-set `refusalReason` literals, so the
         wire format itself carries the correct domain). `TriggerIngameError`'s `errorCode`
         param (`"scribe-lectern-locked"`) is a separate opaque code, not lang-resolved, and was
         left as-is. Mod.csproj builds clean; Core.Tests still 27/27.
- [x] 8.13 Read-view and editor-view rows overlap the content below them once text wraps past
      a fixed row-height constant: confirmed for the read-view empty-state hint
      (`screenshots/debug/2026-07-18_13-57-29.png`), for a long task wrapping in read view
      (`screenshots/debug/2026-07-18_14-28-05_*.png`), and for a long note wrapping to 4 lines in
      BOTH editor view (overlaps "Text Size"/"Collapse") and read view (overlaps "Edit")
      (`screenshots/debug/2026-07-18_14-32-1[3-6]_editor-note-normalwords.png`). Root cause:
      `ScribeBlockRowCell.RowHeight` returns a fixed constant scaled by text size, not a
      measurement of the actual text -- fine until content wraps past it. Task rows
      (`GuiElementTextInput`) never wrap (confirmed via decompile and
      `screenshots/debug/2026-07-18_14-28-0[1,5]_long-task-text-scrolls-not-wraps.png`: long task
      text scrolls left-right instead), so editor-view task rows were correctly left at fixed
      height.
      -> done: added `ScribeBlockRowCell.TextWidth` (factored out of `Compose`'s existing inline
         math, no behavior change) and `MeasureWrappedHeight` (floors at the existing fixed
         constant, grows via `capi.Gui.Text.GetMultilineTextHeight` -- the engine's own
         wrap-aware measurement, same mechanism `GuiElementTextArea.TextChanged()` already uses
         internally). `ComposeReadView` now measures every row (tasks and notes alike, since
         `AddStaticText` wraps regardless of block kind) plus the empty-state hint.
         `ComposeEditorView` measures only text-section/note rows (task rows stay fixed-height,
         correctly, since `GuiElementTextInput` never wraps). Both recompute automatically on
         every text-size-slider change since both compose methods already fully rebuild from
         scratch. Mod.csproj builds clean; Core.Tests still 27/27.
- [x] 8.14 Follow-ups from 8.13 live playtesting (screenshots
      `screenshots/debug/2026-07-18_14-4[2,5,6]-*_verify-item[1-4].png`):
      1. The 8.8 top-content gap (12px) was still tight enough that placeholder/hint text visibly
         touched the title bar.
      2. 8.13's pre-measurement fixes layout at compose time, but a note's `GuiElementTextArea`
         also grows live on every keystroke (`Autoheight`) -- `OnRowTextChanged` only set
         `isDirty`, never recomposed, so rows below a growing note stayed put and visibly
         overlapped it while actively typing (not just at compose time).
      3. Not a bug -- switching a long note to read view already recomposes fresh via 8.13's fix.
      4. The text-size slider's 200% max has no accompanying scrollable/clipped region (rows are
         stacked by absolute Y with no scrollbar) -- past a certain scale + block count, content
         renders below the screen with no way to reach it.
      -> done, 1-2-4 fixed here, 3 confirmed no bug: (1) `TopContentGap` raised from 12 to 20,
         shared by both views so a single bump fixes it everywhere the row stack starts. (2)
         `OnRowTextChanged` now measures the note's new wrapped height on every keystroke against
         the height last composed for that row (tracked in a `composedNoteRowHeights` dict,
         cheap since `MeasureWrappedHeight` is self-contained/no live rendering context needed);
         on a real change it recomposes via a new `RecomposeEditorViewPreservingFocus`, which
         captures the focused note's index + caret position beforehand and restores both via
         `GuiComposer.FocusElement`/`GuiElementEditableTextBase.SetCaretPos` after the recompose
         creates fresh element instances -- this is a scoped fix of the general "recompose resets
         focus to element 0" issue (8.5's general form still open, but no longer reachable via
         normal note-typing). Scoped to notes only; tasks' `GuiElementTextInput` never
         wraps/grows, so no recompose-on-keystroke is needed there. (4) Capped the slider's max
         at a new `MaxTextSizePercent = 150` constant; also clamps any pre-existing saved config
         value down to the new cap on load. Real scrollable-region support is a separate,
         larger follow-up -- not done here, tracked below as 8.15. Mod.csproj builds clean;
         Core.Tests still 27/27.
- [x] 8.15 No scrollable/clipped region exists in either view -- rows are stacked by absolute Y
      with no scrollbar, so content can render off-screen with no way to reach it once enough
      rows/text-size combine to exceed the visible dialog height. 8.14 capped the text-size
      slider as a stopgap; the underlying missing-scrollbar issue is unaddressed. Investigate
      `GuiComposer`'s clipped-region support (see `GuiDialogTrader`'s scrollbar usage, referenced
      in this file's own class doc comment) and add a real scrollable list region.
      -> PREMISE CORRECTED + done under the LibGUI rebuild. The "absolute-Y stacking, no scroll
         region" premise was NATIVE-GUI-only: the LibGUI read view already used a scrollable
         `ListView` and the editor a `SingleChildScrollView`, both of which wheel-scroll to
         off-screen content (verified against `reference/vslibgui`). What was genuinely missing was a
         VISIBLE scrollbar. Fix: wrapped both scroll regions in LibGUI's opt-in `Scrollbar` widget
         sharing an owned `ScrollController` (the read content State and editor content State each
         own one, disposed with the State) -- `Scrollbar` requires the child scroll view to share its
         controller (LibGUI `ScrollPage` pattern). `Scrollable.EnsureVisible` still works (it walks
         up to the nearest scrollable ancestor; the added Stack/Padding wrappers are above the scroll
         view, not between it and the rows). Also removed the now-moot 150% stopgap: deleted the dead
         `MinTextSizePercent`/`MaxTextSizePercent` fields and the whole orphaned "Editor toolbar"
         config block (`ControlRowHeight`..`HoverTextWidth`, all native-GUI-only, unreferenced by
         LibGUI) from `ScribeClientConfig`, and fixed `TextSizeScale`'s stale XML doc that still
         pointed at the deleted `GuiDialogScribeLectern.OnTextSizeSliderChanged`. Mod.csproj builds
         clean. In-game confirm of the visible/draggable bar in both views is a manual playtest item.
- [x] 8.16 Rename `scribe-gui-addtext` label from "Add Text" to "Add Note" (matches the rest of
      the UI's "note" terminology) -- done in `en.json`; no code changes needed, just tracking
      the UX-copy fix alongside the others found in this playtesting pass.
      -> done: `en.json`'s `scribe-gui-addtext` value changed to "Add Note".
- [x] 8.17 Investigated: only the bottom-right sliver of the lectern dialog's close (X) button
      was clickable, per live playtesting. Confirmed via decompile + a live hover-position
      diagnostic (`OnMouseMove` logging mouse coords against the title bar's live bounds,
      screenshotted mid-hover) that our hit-test math is correct and consistent with the
      logged bounds. Reproduced the identical tight/shifted-feeling hitbox on a plain vanilla
      dialog (a chest) -- confirming this is a general engine (likely Retina-display-scale)
      quirk affecting `GuiElementDialogTitleBar`'s close icon everywhere, not a bug introduced
      by this mod. No fix needed or possible from mod code. See `VSAPI-NOTES.md` for the full
      writeup and the diagnostic technique (hover-log + screenshot beats click-based testing,
      which is ambiguous once a successful click closes the dialog mid-test).
- [x] 8.18 Removed the "Add Note" toolbar button from the lectern's editor GUI (per explicit
      user direction: freeform text-section creation is earmarked for a different, not-yet-
      designed recipe/item later, not the lectern). `ToolbarOptions()` now yields only
      `addTaskButton`; the now-unused `OnClickAddText` handler was removed alongside it.
      Core's text-section capability (`ScribeBlockKind.Text`, `ScribeDocument.AddTextSection`,
      the codec, and `ScribeBlockRowCell`'s kind-agnostic row rendering) is untouched and still
      fully functional -- any pre-existing text-section block continues to render, edit, and
      delete normally in both read and editor view; only the lectern's own creation entry
      point is gone. `en.json`'s `scribe-gui-addtext` string ("Add Note") is left in place for
      the future recipe to reuse. `lectern-block/spec.md`'s "Edit the document through the
      GUI" requirement updated to match (dropped the lectern-specific "Add a text section"
      scenario; `task-note-document/spec.md`'s own requirement is unaffected). Mod.csproj
      builds clean; Core.Tests still 27/27 (no Core changes).
