## 1. Core model: Tracker & Link kinds (no VS API)

- [x] 1.1 Append `Tracker = 2` and `Link = 3` to `ScribeBlockKind` (never renumber `Task`/`Text`).
- [x] 1.2 Add `TargetItemCode` (string?), `TargetQuantity` (int), `CurrentQuantity` (int), and
      `LinkTarget` (string?) to `ScribeBlock`; extend the constructor with defaulted params so
      existing callers compile unchanged.
- [x] 1.3 Implement clamping in Core: `TargetQuantity` clamped to ≥ 1 on set/create;
      `CurrentQuantity` clamped into `[0, TargetQuantity]` whenever set.
- [x] 1.4 Add `ScribeDocument.AddTracker(itemCode, targetQuantity)` and `AddLink(target)` ops
      (append a block, assign a distinct `TaskId`, mirror the existing add-op return contract).
- [x] 1.5 Add a `ScribeDocument` op to set a tracker's `CurrentQuantity` by `TaskId` (clamped,
      no-op/failure for non-tracker or missing id).
- [x] 1.6 Core.Tests: add-tracker/add-link create the right kind + fields; target-quantity and
      current-quantity clamping; ordering + distinct `TaskId` for the new kinds.

## 2. Core codec: v5 → v6 with named migration step

- [x] 2.1 Bump `ScribeDocumentCodec.Version` to 6 and `PriorVersion` to 5; append the four new
      fields to the per-block serialize/deserialize layout in field order.
- [x] 2.2 Add `ApplyV5ToV6Migrations` that defaults the new fields for v5 blobs
      (`TargetItemCode`/`LinkTarget` = null, `TargetQuantity` = 1, `CurrentQuantity` = 0); wire it
      into the read path at the version branch. Ensure v4 (two versions back) fails safely.
- [x] 2.3 Update the `ScribeDocumentCodec` class doc-comment version table (current v6, prior v5,
      fields added in the v5→v6 transition).
- [x] 2.4 Core.Tests: current-version round-trip preserves tracker/link fields; replace the v4
      older-blob test with a v5 older-blob test asserting the new fields default; assert v4/older
      bytes fail to deserialize.
- [x] 2.5 Update `docs/CODEC-MIGRATION.md` with the v5→v6 transition as the newest worked example.

## 3. Mod: Handbook "Add to Scribe" entry point (Harmony)

- [x] 3.1 Add a Harmony patch class with a postfix on
      `CollectibleBehaviorHandbookTextAndExtraInfo.GetHandbookInfo` that appends "Add to Scribe"
      `LinkTextComponent`(s) (Tracker and Link paths) carrying the page's collectible code. Register/
      unregister the Harmony patch in the client mod-system lifecycle.
- [x] 3.2 Track the last-opened Scribe item: set a client-side field wherever an item-hosted Scribe
      dialog opens (Notebook/Tablet).
- [x] 3.3 Implement three-tier target resolution on click (`ScribeModSystem.AddFromHandbook`):
      (1) open `ScribeDialogBase` via
      `capi.Gui.OpenedGuis.OfType<ScribeDialogBase>().FirstOrDefault(d => d.IsOpened())`; (2) else
      open the last-opened carried Scribe item (fallback: first carried Scribe item) via the new
      `IScribeDocumentItem.OpenScribeDialog` seam; (3) else
      `TriggerIngameError("You need a Scribe item to do that.")` and create nothing.
- [x] 3.4 **(REVISED — no new packet).** REUSE the existing per-dialog save path instead of a new
      `ScribeCreateTaskFromHandbookMessage`: `ScribeDialogBase.TryAddFromHandbook(kind, itemCode)`
      appends to the resolved live dialog's `scratch` document and flushes via its existing
      (possibly-overridden) `SendFlushPacket`. Case A (already editing) appends + flushes at once;
      Case B (not editing) stashes a `pendingHandbookAppend` and calls `TryEnterEditor()` — a
      deferred-append hook at the end of `EnterEditorMode` applies it once editor access lands
      (synchronous for items, async grant for blocks). Backwards compatible: `ScribeAddKind` is never
      serialized, and the only persistence change is the v6 document bytes already shipped in Group 2.
- [x] 3.5 **(REVISED — no new server handler).** The reused `SendFlushPacket` path already routes
      through the server-authoritative `ScribeEditDocumentMessage`/`ScribeNotebookSaveMessage` edit +
      re-sync; no dedicated handbook server handler is added. Locked-by-other blocks surface the
      generic `scribe:scribe-gui-locked` error (reused, no new key) via `TryEnterEditor`; a
      read-only/refused surface clears the stale `pendingHandbookAppend`.
- [x] 3.6 Register a new Handbook explainer entry (registration JSON + lang copy) describing the
      Tracker and Link task types and pointing at the per-item "Add to Scribe" link.
- [x] 3.7 Add Tracker and Link entries to `ScribeAddKinds.Live`; extend `ScribeAddKind` /
      `OnClickAdd` so these dispatch a non-mutating guide action: Handbook closed → open the
      explainer entry; Handbook open → `TriggerIngameError` telling the player to scroll to the
      current entry's bottom and click the "Add to Scribe" link.
- [x] 3.8 Add lang keys for the button label(s), the footer guide entries + their error text, the
      "no Scribe item" error, and task-type labels.
- [x] 3.9 Add a `VSAPI-NOTES.md` entry recording the exact `GetHandbookInfo` type/signature, the
      append-only postfix approach, and the handbook open API used to jump to an entry.

## 4. Mod: Tracker count engine (carried-only)

- [x] 4.1 Build a carried-inventory matcher: construct a `CraftingRecipeIngredient` from
      `TargetItemCode` and sum matching stack sizes across hotbar + backpack via
      `SatisfiesAsIngredient(stack, checkStackSize:false)`.
- [x] 4.2 Recompute on `IInventory.SlotModified` (debounced) + a ~1s edge-case poll, active only
      while the open document contains at least one Tracker; recompute on dialog open.
- [x] 4.3 Route `CurrentQuantity` updates through the server edit path (synced like `Done`);
      server persists, viewers converge.
- [x] 4.4 On target-met, apply the per-player completion setting (completes / deletes / nothing) by
      issuing the matching edit; guard against resurrecting a deleted task on later shortfall.

## 5. Mod: row rendering & completion setting

- [x] 5.1 Render a Tracker row: target item icon + name + `have/need` counter, with a progress
      state (none / partial / satisfied); shortfall reads unsatisfied, met reads like a completed row.
- [x] 5.2 Wire the inline arrow-stepper numeric control to edit a Tracker's `TargetQuantity` on the
      row (reuse the Settings numeric / `typed-arrow-substitution` control); re-clamp on change.
- [x] 5.3 Render a Link row: item icon + name; clicking the label opens the referenced Handbook page
      (parse `LinkTarget` → `AssetLocation` → handbook open API) and does NOT change completion,
      distinct from the row's completion control.
- [x] 5.5 Wire Link-task hyperlink activation on the pinned-task HUD: a pinned Link's click opens
      its Handbook page (reuse the existing HUD row-click plumbing, gated on kind == Link).
- [x] 5.4 Add the completes/deletes/nothing completion setting to `ScribeClientConfig` +
      `ScribeSettingsContent`/`ScribeSettingsDialog` (default: completes) with a lang label.

## 6. Verification & docs

- [x] 6.1 `build/verify.sh` green (Core suite incl. new tests + Atlas suite).
- [ ] 6.2 Manually test in-game — three-tier resolution: "Add to Scribe" appears on an item page;
      (1) with a Scribe surface open (test a block AND an item surface) it creates the task there;
      (2) with none open but a Scribe item carried, it opens that item's UI and creates the task;
      (3) with no Scribe item at all, it shows "You need a Scribe item to do that."
- [ ] 6.8 Manually test in-game — footer guide: click the footer Tracker/Link entry with the
      Handbook closed → the explainer entry opens; click it with the Handbook open → the
      scroll-and-click instruction error fires; neither creates a block.
- [ ] 6.9 Manually test in-game — Link hyperlink: click a Link task in a Scribe UI and again as a
      pinned task on the HUD; both open the linked Handbook page and leave completion unchanged.
- [ ] 6.3 Manually test in-game: create a Tracker, set N via the arrow-stepper, collect/drop matching
      items and confirm the `have/need` counter tracks carried inventory only (chest items ignored).
- [ ] 6.4 Manually test in-game: verify each completion-setting mode (completes / deletes / nothing)
      behaves correctly when a Tracker reaches its target.
- [ ] 6.5 Manually test in-game: create a Link, confirm tapping it opens the item's Handbook page and
      leaves its completion state unchanged.
- [ ] 6.6 Manually test in-game: load a pre-v6 (v5) world/save and confirm existing documents open
      cleanly with the new fields defaulted.
- [x] 6.7 Update `CHANGELOG.md` (Unreleased → Added: Tracker & Link task types, Handbook entry) and
      `ROADMAP.md` (mark the v1.2 task-types cluster progress).

## 7. Playtest refinements (2026-08-15 feedback)

- [x] 7.1 (feedback 6.3) Editor Tracker row layout: move the inline target stepper to the LEFT of the
      icon/name (it sat under the hover delete/pin buttons on the right, unreachable — which also made
      the pin untappable, feedback 6.9), and shrink its width from ~6 characters to ~3.
- [x] 7.2 (feedback 6.2) Handbook add tier-2: skip read-only carried items (hardened/fired tablets) and
      land the task on the next WRITEABLE Scribe item; add an `IScribeDocumentItem.IsSlotWriteable` seam
      (Notebook always true, Tablet ⇔ wet). If carried Scribe items exist but none are writeable, show a
      single generic error (`scribe-gui-all-locked`); the "no Scribe item at all" error is unchanged.
- [x] 7.4 (feedback 6.5) A Tracker row's name becomes a hyperlink that opens its target item's Handbook
      page (like a Link), in the read view; extend the `onOpenLink` handler to resolve a Tracker's
      `TargetItemCode`. (Future Crafting tasks inherit this.)
- [x] 7.5 (feedback 6.4) Focus the caret into the numeric stepper when a Tracker is freshly created from
      the Handbook, so the player can type the target immediately (reuse `ScribeNumericField.autoFocus`).
- [x] 7.6 (feedback 6.8) Inject "Add Link" on non-item Handbook Guide/explainer entries (today the
      Harmony postfix hooks only the collectible builder, so links appear on item pages only). Requires
      VS-internals research into how non-item Handbook pages are built + whether an injection seam exists;
      teach the Link model + `OpenHandbookPage` to store/open a raw page code, not just an item code.
      Tracker does NOT apply to guide pages (nothing to count) — Add Link only.
      DONE: second Harmony postfix `ScribeGuidePageHandbookPatch` on `GuiHandbookTextPage.Init` (public
      method; `comps` reached by field-ref) appends one "Add Link" on `CategoryCode=="guide"` pages →
      `ScribeModSystem.AddGuideLinkFromHandbook(pageCode, title)` → shared `AddFromHandbookCore` 3-tier
      resolution → `ScribeDialogBase.TryAddGuideLinkFromHandbook` → `scratch.AddGuideLink`. Model:
      `ScribeLinkTarget` (`page:` prefix) + dedicated `LinkLabel` field (guide title has no item to
      resolve from) → doc codec v6→v7 + pin codec v3→v4, both switched to progressive reads so shipped
      v5 docs / v1 pins are never dropped. `OpenHandbookPage` opens a `page:`-prefixed code directly.
      Display: new `scribebook` open-book glyph + `LinkLabel` name via shared `ScribeLinkIcon` /
      `ScribeItemRef.ResolveDisplay` across read/editor/Pin-Tab/HUD. Core 375 green; needs in-game playtest.
- [ ] 7.7 Manually re-test in-game after 7.1/7.2/7.4/7.5: (a) Tracker stepper sits left of the icon, ~3
      chars wide, and the hover pin/delete are now reachable on a Tracker row; (b) "Add to Scribe" while
      carrying only a fired/hardened tablet shifts to a writeable notebook, or shows the single locked
      error when none is writeable; (c) tapping a Tracker's name opens the item's Handbook page and leaves
      completion unchanged; (d) creating a Tracker from the Handbook drops the caret in the stepper.
- [ ] 7.8 (feedback 6.9, now unblocked by 7.1) Manually test the HUD: pin a Tracker and a Link, confirm
      both appear on the pinned-task HUD and behave (Link click opens its page; Tracker shows progress).
- [x] 7.9 (feedback 6.9 root cause) Render pinned Tracker/Link rows item-shaped on the HUD **and** the Pin
      Tab (they had shown blank — the pin row data was Task-shaped, but Tracker/Link carry empty text and
      resolve their label from the item). "Full treatment": bump the pin-store codec v2→v3 (named
      progressive-read migration — accept v1/v2/v3, read each version's trailing fields by threshold, so
      shipped v1 pins are never dropped) to snapshot the Tracker's `TargetItemCode` + target/current
      quantities in the pin; plumb the snapshot through `ScribeSetPinMessage`/`SetPinForPlayer`/
      `ScribePinStore.SetPin` and refresh it in `ReconcileSnapshotsForActor`; render icon + name on both
      surfaces, with the have/need counter on the **LEFT** for a Tracker (matching the read view; future
      Crafting tasks inherit it). Name is a Handbook hyperlink on both surfaces; never touches completion.
## 7b. Playtest refinements — round 2 (2026-08-15 feedback)

Confirmed PASS this round (no action): guide-page "Add Link" appears + works; guide Link opens the
page; old (v5/v1) saves load clean; HUD pins Tracker + Link (from an already-open surface); Tracker
rows reachable (7.1 regression closed).

- [x] 7.11a (feedback: article formatting) VTML does NOT decode HTML entities — the task-types article
      rendered literal `&amp;` and `12&#47;20`. Replaced with literal `&` and `/` in the two
      `craftinginfo-scribe-task-types-*` keys. Rule (add to VSAPI-NOTES): use literal `&`/`/` in VTML
      copy; other article strings already do (lines 217/223) and render fine. DONE.
- [x] 7.11b (feedback: pin-from-handbook) When a task is created from a Handbook link and the player
      tries to PIN it while the Handbook is still open, the pin request only QUEUES (single-player server
      time is paused while the Handbook is open) and gives no feedback until the Handbook closes. Editing/
      completing/adjusting the tracker number all work because they apply optimistically; pinning does
      not. Fix: apply the pin OPTIMISTICALLY on the local row (color it the pinned color immediately) and
      reconcile when the server + HUD + Pin Tab catch up — mirror the existing optimistic-edit path
      (`ApplyLocalOptimisticEdit` / `RefreshReadView`), don't rely on the server round-trip for feedback.
      DONE: added a dialog-scoped `optimisticPin` overlay (`Dictionary<Guid,bool>` keyed by TaskId). The
      dialog's `IsPinnedForMe` wrapper consults it first (so the read/editor row's pin tint flips at once);
      `TogglePinWithPolicy` (and the tablet swap-out in `ReleasePinsToFitPolicy`) record the intended state
      and call `RepaintPinsOptimistically` → the existing per-view `OnMyPinsChanged` reconcile+focus-rehome
      path. `OnMyPinsChanged` now drops overlay entries the authoritative pushed set already agrees with
      (no-op until the queued packet is processed on Handbook close, then the server cache resumes driving);
      `OnGuiClosed` clears the overlay so a close-before-catch-up can't leak a stale entry into the reopen.
- [ ] 7.11c (feedback: Handbook nav friction) The editor Add → Tracker/Link footer action (Handbook-
      CLOSED path) currently opens our explainer entry, which dead-ends: to actually add a Tracker/Link
      the player must return to search, type the item, open its page, scroll, and click. Confirmed a
      search bar canNOT coexist with an entry page (two separate composers — `overviewGui` has the
      "searchField", `detailViewGui` does not). Shortcut available: the `handbooksearch://<text>` link
      protocol opens the Handbook overview with the search box already focused. DECISION (2026-08-15):
      **open focused search** — speed-to-entry wins over the explainer dead-end. So the Add → Tracker/Link
      footer action (Handbook-closed) opens the Handbook overview with the search box focused (drop the
      explainer open). Discoverability handled elsewhere: cross-link the task-types explainer entry from
      the other top-level Scribe guides (Getting Started / Editor Reference / Pinned HUD) so the teaching
      page is still reachable — just not on the critical add path.
- [x] 7.11d (feedback: book/guide glyph color) The `scribebook` guide-page-Link glyph renders too harsh
      (near-black `OnSurface`) inside the Notebook. Render it `Primary` on the in-Notebook surfaces
      (read/editor/Pin Tab); confirm the HUD tint still reads. DONE: read/editor/Pin-Tab call sites now pass
      `colors.Primary` as the book tint; the HUD keeps `textStyle.Color` (near-white). The item icon ignores
      the color, so only the guide-page glyph changes.
- [x] 7.11e (feedback: glyph size) Shrink the `scribebook` guide-page-Link glyph ~20% in the Notebook
      AND the HUD. DONE: `ScribeLinkIcon.BookGlyphScale = 0.8f` applied inside the shared builder, so every
      surface (incl. HUD) shrinks the glyph uniformly.
- [x] 7.11f (feedback: item-icon size + row height) Grow the Tracker/Link item icon (`ItemStackDisplay`)
      ~10%, but make it row-height-NEUTRAL: perceived layout height 0, vertically centered, so a
      Tracker/Link row is the same height as a single-line Task/Text/Note row (today icon rows are
      taller). Applies to read/editor/Pin Tab/HUD. DONE: `ItemIconScale = 1.1f`; `ScribeLinkIcon.Build`
      wraps the (larger) icon in a `Stack` sized by a single-text-line `SizedBox` spacer with the icon a
      `Positioned` child forced to full size and offset up to center — RenderStack doesn't clip, so it paints
      larger while contributing only one line of height. Line height via new
      `ScribeRowControlNudge.TextLineHeight`. (The editor Tracker row is still stepper-tall by design.)
- [x] 7.11g (feedback: tracker emphasis swap) Invert the Tracker counter emphasis: an IN-PROGRESS
      (unsatisfied) Tracker should read STRONG (Primary / bold — the thing you're still working on), and a
      SATISFIED (met) Tracker should read FADED (muted). Today it's backwards. Apply on read view + HUD +
      Pin Tab counters. DONE via shared `ScribeTrackerCounterText.Build`: unsatisfied → strong (Primary/bold,
      or HUD near-white/bold), satisfied → muted (OnSurfaceVariant, or HUD grey).
- [x] 7.11h (feedback: tracker strikethrough) When a Tracker is satisfied, draw a VERY FAINT strikethrough
      over ONLY the "N / N" counter section (e.g. "1/1 Notebook" → the "1/1" struck through, the name not).
      LibGUI `TextDecoration` has no strikethrough (only None/Underline) → implement a small custom
      thin-line overlay widget centered over the counter Text. DONE: `ScribeTrackerCounterText` wraps the
      satisfied counter Text in a `Stack` with a `Positioned` thin `Container` line (left:0/right:0 spans the
      counter width, centered on its line, ~0.6·muted alpha) — sizes to the counter, so it never strikes the
      name.
- [x] 7.11i (playtest) Re-test in-game after 7.11b–7.11h: pin-from-Handbook gives immediate feedback;
      the Add-Tracker/Link nav shortcut; article renders `&`/`/` correctly; book glyph is Primary + smaller;
      item icons larger but rows single-line height; in-progress Trackers strong / satisfied faded + struck.
      DONE: 2026-08-15 playtest — 7.11d/e/f/g/h all confirmed good; 7.11b pin-from-Handbook gives immediate
      feedback as designed; article `&`/`/` render correctly. All round-2 tests PASS.
- [x] 7.11j (feedback: optional HUD icons) Make the HUD's Tracker/Link icon display (item icon OR guide-page
      book glyph) optional via a client-local boolean Scribe Setting, default ON (icons shown = original
      behavior). DONE: added `ScribePlayerSettings.HudShowIcons` (plain bool, default true, no Normalized
      change); threaded through `HudPinsContent` as a `showIcons` ctor param (resolved from
      `modSystem.MySettings.HudShowIcons` at the build site), so `BuildHudItemContent` omits the icon Widget
      when off (counter + name still render). Added a "Show HUD icons" hugging checkbox at the top of the HUD
      Appearance section in `ScribeSettingsContent` + `settings-hudshowicons`(-help) lang keys. Notebook/Pin-Tab
      icons are unaffected (HUD-only, as requested). Restaged; needs a quick in-game toggle check.

- [ ] 7.10 Follow-up (deferred): make the HUD Tracker counter fully LIVE (recompute have/need from the
      viewer's carried inventory continuously) rather than the persisted snapshot refreshed on edit. The
      count engine is dialog-bound today (freezes when no Scribe dialog is open); a live HUD counter needs
      the HUD to subscribe to inventory changes + handle the completion trigger itself. Out of the 7.9
      snapshot scope; the snapshot already keeps the HUD/Pin-Tab counters correct across edits.
