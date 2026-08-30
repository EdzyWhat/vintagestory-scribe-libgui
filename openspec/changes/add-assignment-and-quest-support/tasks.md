## 1. Core: assignment data model

- [x] 1.1 Replace `ScribeBlock.AssignedToUid` (`src/Core/ScribeBlock.cs`) with a `ScribeAssignment?`
      value type carrying assigner UID, current state, in-game assigned-date, and the Seen flag.
- [x] 1.2 Add a `ScribeAssignmentState` enum (`Unaccepted`, `Accepted`, `Declined`, `Cancelled`,
      `Discarded`, `Completed`) to `src/Core/`.
- [x] 1.3 Add pure transition-validation logic (which actor may move which state to which state)
      in `src/Core/`, matching the matrix in `design.md` exactly.
- [x] 1.4 Add `ScribeQuestAcceptPolicy` and `ScribeQuestCompletionPolicy` enums (`Always`/`Never`/
      `Prompt`) to `src/Core/`, mirroring `ScribeCompletionPolicy`'s shape.
- [x] 1.5 Add a Quest-namespaced `LinkTarget` convention (e.g. a `quest:` prefix) alongside the
      existing `page:` guide-page convention, staying within the existing `Link` block kind (no
      new `ScribeBlockKind`).

## 2. Core: unit tests

- [x] 2.1 Unit-test every legal and illegal transition in the assignment state matrix (both
      actors, every state).
- [x] 2.2 Unit-test that Completed is only reachable via the underlying task's done-flag, never
      directly.
- [x] 2.3 Unit-test Delete-on-Accepted performing the Discard transition.
- [x] 2.4 Unit-test the Seen flag's default-unseen and mark-seen-on-view behavior independent of
      state transitions.
- [x] 2.5 Unit-test codec round-trip of the new `ScribeAssignment` field (present and absent
      cases) and of the Quest-namespaced `LinkTarget`.

## 3. Codec/serialization

- [x] 3.1 Update `ScribeDocumentJsonCodec`/binary codec to serialize/deserialize the new
      `ScribeAssignment` type in place of the old bare `AssignedToUid` string.
- [x] 3.2 Confirm import (`ScribeDocumentJsonCodec.cs`'s existing "assignment is place-bound, not
      shareable" rule) still strips assignment state on import — update the comment to reference
      the new type instead of the old bare UID.
- [x] 3.3 Update `docs/CODEC-MIGRATION.md` and any codec version-window tests/comments affected by
      the field shape change.

## 4. Networking

- [x] 4.1 Add client→server messages for: send assignment, Accept, Decline, Cancel, Discard.
- [x] 4.2 Add server→client sync for assignment state changes (both Assigner's and Assignee's open
      dialogs, if any, update live). Added `ScribeAssignmentStore` (Core, append-only, one canonical
      record per assignment keyed by its `TaskId`/`AssignmentId`) + `ScribeAssignmentSyncMessage`
      (server→client push of a player's Sent+Received views) + server handlers for the two 4.1
      messages, wired into `ScribeModSystem` start/save/load/join like `ScribePinStore`. Open-dialog
      live refresh itself lands with the Assignment Desk/Inbox GUI (section 5-7), which doesn't
      exist yet — the sync plumbing and the `MyAssignmentsChanged` event it's built to drive are
      ready for those views to subscribe to.
- [x] 4.3 Add the Quest Accept/Completion policy preferences to the existing player-settings sync
      message, following `ScribeCompletionPolicy`'s existing wire pattern. **Resolved as no-op**: the
      `settings-tab`/`quest-auto-detect` specs mark both policies as per-player, CLIENT-LOCAL only
      (unlike `CompletionPolicy`, which the server needs at completion time) — there is no
      server-side consumer, so no wire message was added. The Core enums + `ScribePlayerSettings`
      fields already exist (task 1.4).

## 5. Assignment Desk block

- [x] 5.1 Add the Assignment Desk block/blocktype, item, crafting recipe, and block entity
      (reusing the writing-station block-entity base). Added `BlockAssignmentDesk`/
      `BlockEntityAssignmentDesk` (thin subclasses, mirroring `BlockScriptorium`/
      `BlockEntityScriptorium`), `assignmentdesk.json` blocktype + `assignmentdesk.json` grid
      recipe, registered in `ScribeModSystem.Start()`. Shape/textures/recipe are a PLACEHOLDER
      clone of the Scriptorium's/Lectern's, pending real art/balance (§13.1) — the same
      "placeholder now, restyle later" precedent the Scriptorium set for its own GUI backdrop.
      `CreateDialog` returns a new minimal `GuiDialogScribeAssignmentDesk` stub (see 5.4 note).
- [x] 5.2 Implement `IScribeDocumentHost.GetLayout` for the Assignment Desk: `W = PixelArtSize`,
      `AspectH = 1.2`, with the active tab's content region laid out as a 1:1 square within that
      box (design.md Decision 8) — a real layout, not a placeholder pending art. Done via
      `BlockEntityAssignmentDesk.PageAspect => 1.2f`, reusing the base class's existing
      `GetLayout(w) => new ScribeLayout(w, PageAspect, LayoutProportions)` forwarding — no new
      dimension mechanism needed. The 1:1-square-content-region half of Decision 8 is realized
      when the Assignment/Inbox tab content itself is built (5.5/7.x), via `LayoutProportions`.
- [x] 5.3 Add `Assignment` and `Inbox` members to `ScribeLecternView`
      (`ScribeDialogBase.cs:88`) and the corresponding `IsAssignmentView`/`IsInboxView` exposers.
- [x] 5.4 Build the Assignment Desk's GUI dialog class, defaulting to the Assignment tab, with a
      nav button pair switching between Assignment and Inbox — reusing Lectern/Scriptorium widget
      parts per `design.md` Decision 1. Added `ScribeDialogBase.DefaultToAssignmentView()` (called
      from the ctor, before `TryOpen`'s first `Build()`) and `OnClickSwitchToAssignment()`
      (mirroring `OnClickSwitchToInbox()`) to the base class. `GuiDialogScribeAssignmentDesk`
      overrides `BuildRightColNav()` wholesale (the `GuiDialogScribeTablet` precedent for replacing
      this seam) to a 3-button column — Assignment, Inbox, Settings — dropping the base's
      Read/Editor/Pinned buttons, which don't apply to this 2-tab-only dialog. **Layout
      simplification, documented in the dialog's own doc-comment**: the spec's "title bar + a
      tab-switcher nav row" 2-tab chrome implies dropping the vertical `SectionRightCol` nav column
      entirely; this reuses the existing vertical column with just these 3 buttons instead, so
      Settings stays reachable without inventing a new layout mechanism. Revisit when the tab
      content itself (5.5/7.x) is built. Assignment tab icon is a placeholder reuse of the edit
      glyph (no dedicated asset — §13).
- [x] 5.5 Build the Assignment tab's create-and-send form (task text entry, target player
      picker, send action) — the only surface in the mod with this capability. Added
      `ScribeAssignmentFormContent` (`src/Mod/ScribeAssignmentFormContent.cs`): a target-player
      `Dropdown<string>` (online players only — the client has no UID→name directory for an
      offline player, a documented MVP scope, not a hidden limitation), a task-text `TextField`,
      and a Send button that mints a fresh `AssignmentId` client-side and sends
      `ScribeSendAssignmentMessage` (§4.1's existing networking). Below the form it renders this
      player's own Sent history via the SAME `ScribeInboxContent` the Inbox tab uses (viewed as
      `ScribeAssignmentActor.Assigner`), realizing design.md Decision 3 ("the Assigner keeps
      read-only visibility past Unaccepted") without a divergent one-off (Decision 5).
      `ScribeDialogBase.BuildAssignmentContent()` (previously an alias to `BuildInboxContent()`)
      now builds this directly — safe in the base since only the Assignment Desk ever routes
      `viewMode` to `Assignment` (Decision 1).

## 6. Inbox block

- [x] 6.1 Add the Inbox block/blocktype, item, crafting recipe, and block entity (reusing the
      writing-station block-entity base). Added `BlockInbox`/`BlockEntityInbox` (thin subclasses),
      `inbox.json` blocktype + `inbox.json` grid recipe, registered in `ScribeModSystem.Start()`.
      Same placeholder-shape/recipe rationale as 5.1. `CreateDialog` returns a new minimal
      `GuiDialogScribeInbox` stub (see 6.3 note).
- [x] 6.2 Implement `IScribeDocumentHost.GetLayout` for the Inbox block: same `W = PixelArtSize`,
      `AspectH = 1.2`, 1:1 square content region as the Assignment Desk's Inbox tab
      (design.md Decision 8). Done via `BlockEntityInbox.PageAspect => 1.2f`, same mechanism as
      5.2.
- [x] 6.3 Build the Inbox block's GUI dialog class, opening directly to the shared Inbox tab with
      no Assignment tab present. `GuiDialogScribeInbox` calls the new
      `ScribeDialogBase.DefaultToInboxView()` from its ctor and overrides `BuildRightColNav()` to a
      single Settings button — no Read/Editor/Pinned (this block has none) and no Inbox nav button
      either (Inbox is the only view, so there is nothing to switch away from and back to). Since no
      button here ever changes `viewMode`, the dialog stays on Inbox for its whole open lifetime.

## 7. Shared Inbox tab UI

- [x] 7.1 Build the Inbox row `StatefulWidget` with a per-row `expanded` bool, collapsed/expanded
      renderings per `design.md` Decision 6. Added `ScribeInboxRow`/`ScribeInboxRowState`
      (`src/Mod/ScribeInboxContent.cs`), keyed by `ValueKey<Guid>(TaskId)` inside the new
      `ScribeInboxContent`/`ScribeInboxContentState` container (mirroring `ScribeReadContent`'s
      keyed-row reconcile discipline) so a row's expand state and the container's filter selection
      both survive a data-only refresh (`RebuildBody`, not `ForceRebuild` — see 7.5).
      **Deferred**: a row has no completable checkbox/tracker control (the spec's "if applicable"
      carve-out) because `ScribeAssignmentStore` doesn't track a Done flag at all yet — that only
      exists once accepted content is placed into the Assignee's own document (§9.1, not built).
- [x] 7.2 Build the leading chevron disclosure control (reusing `ScribeRowButton` chrome),
      chevron-only trigger, leading-edge placement before the checkbox. Uses the existing
      `scribetriangleright`/`scribetriangledown` icons (no new asset) as the sole expand/collapse
      trigger; no other row control toggles it.
- [x] 7.3 Build the compact state chip and the expanded assigner/date/action-button block. Added
      `ScribeAssignmentChip` (shared lang-key + color mapping consumed by both the row's chip and
      the filter-chip row, so they can never disagree) and `ScribeInboxRowState.BuildExpandedDetail`
      (assigner name via a new `ResolvePlayerName` seam, in-game date, and Accept/Decline/Discard/
      Cancel `Button`s gated on `ScribeAssignmentActor` + current state, matching
      `ScribeAssignmentTransitions`' legal-action set exactly). `ScribeTrackerCounterText`/
      `ScribeLinkIcon` reuse doesn't apply yet — no tracker/link-kind assignment content exists
      until Quest Link (§10) or a Tracker-kind assignment is created.
- [x] 7.4 Build the state filter-chip row for the Inbox tab (one toggleable pill per state, always
      visible). `ScribeInboxContentState` owns a `HashSet<ScribeAssignmentState>` defaulting to
      every state shown (nothing hidden until the player narrows it) — the spec's "always visible,
      active state visible at a glance" scenario is player-driven, not a default-narrowed view.
- [x] 7.5 Wire the Inbox tab into the Assignment Desk (Inbox sub-tab), the standalone Inbox block
      (sole view), and the nav-button entry points on Lectern/Scriptorium/Chalkboard — one shared
      implementation, not per-surface copies. `ScribeDialogBase.BuildInboxContent()` (base, so every
      surface shares it unmodified) now builds real `ScribeInboxContent` from
      `modSystem.MyReceivedAssignments`, viewed as `ScribeAssignmentActor.Assignee`; action taps send
      `ScribeAssignmentActionMessage` via the new `SendAssignmentAction`. Added
      `ScribeDialogBase.OnMyAssignmentsChanged` (subscribed in the ctor, unsubscribed in
      `OnGuiClosed`, mirroring `OnMyPinsChanged`) so an open Inbox/Assignment view live-reconciles on
      every `ScribeModSystem.MyAssignmentsChanged` push. **Follow-up gap, not blocking**: the
      Seen-flag flip-on-open (design.md Decision 4) has no wire message yet — task 4.1's message
      list never included one, and its only current consumer is the shimmer/particle indicators
      (§8.4/8.5, explicitly out of scope for the core loop) — revisit alongside those.

## 8. Nav buttons and particle indicator on existing blocks

- [x] 8.1 Add the Inbox nav button to `GuiDialogScribeLecternLibGui.GetExtraNavButtons()`.
- [x] 8.2 Add the Inbox nav button to `GuiDialogScribeScriptorium.GetExtraNavButtons()`; remove/
      update the stale "Scriptorium-only Assign & History" comment in
      `BlockEntityScriptorium.cs:17`.
- [x] 8.3 Add the Inbox nav button to `GuiDialogScribeChalkboard.GetExtraNavButtons()`.
- [x] 8.4 Implement the ambient particle emitter (tick-interval-gated, client-side, player-local),
      scoped to Assignment Desk, Inbox, Lectern, Scriptorium, and Chalkboard block entities.
      All five share `BlockEntityScribeWritingStation`, so the emitter lives ONCE there: a client-side
      `RegisterGameTickListener(OnAssignmentParticleTick, 1500ms)` registered in `Initialize` using the
      **BlockEntity's own** tick-listener method (not `capi.Event`'s), so the inherited
      `OnBlockRemoved`/`OnBlockUnloaded` → `UnregisterAllTickListeners()` cleans it up automatically with
      no manual bookkeeping. Each tick checks `ModSystem.HasUnseenAssignment` (new — the shared trigger
      condition 8.5 also uses) and proximity (`BlockPos.DistanceTo`, `ScribeAssignmentParticleEmitter.
      DetectionRadius` = 6 blocks), then calls the new `ScribeAssignmentParticleEmitter.SpawnAt`. Uses
      `AdvancedParticleProperties` (not `SimpleParticleProperties`) specifically for its `HsvaColor`
      `NatFloat[4]` field — the "mean ± variance" color spread Decision 9 calls for. The 1-in-5
      rainbow-hue accent can't be expressed as one continuous distribution, so each tick's total mote
      count (1-3, sparse) is split into two separate `SpawnParticles` calls sharing every other property:
      one narrow-amber-band batch, one full-0-255-hue batch. `capi.World.SpawnParticles` is called with
      no `dualCallByPlayer`, so nothing reaches the server or any other client. Starting HSV/motion
      values per Decision 9; tune-by-eye, not final.

      **Prerequisite fix, not originally scoped here**: the Seen-flag flip-on-open (design.md Decision 4)
      had no wire message yet — a gap §7.5 explicitly disclosed and deferred to "revisit alongside
      §8.4/8.5" — so both this task and 8.5 would have had no way to ever clear their trigger condition.
      Added `ScribeAssignmentStore.MarkAllSeen` (Core), a new empty-payload `ScribeMarkAssignmentsSeenMessage`
      (mirroring the `ScribeClearTimerMessage` precedent), its server handler
      `OnServerReceivedMarkAssignmentsSeen`, and a client-side send from `OnClickSwitchToInbox` — so
      opening the Inbox tab now actually marks unseen assignments seen server-side, matching Decision 4.
- [x] 8.5 Implement the Inbox nav-button shimmer (design.md Decision 9b). New `ScribeShimmerWrap`
      (`ScribeInboxShimmer.cs`) wraps a nav button in a `ShaderMask` painted with a `LinearGradient`-built
      `SKShader` (built ONCE, not per-frame) using `SKBlendMode.SrcATop` so only the icon's already-painted
      pixels recolor, not the surrounding transparent box — matching the Flutter shimmer-loading cookbook
      pattern almost exactly, as the design doc anticipated. The sweep itself is driven by a plain
      `AnimationController` (the same primitive `ScribeRowSizeAnimation`/`ScribeAnimatedList` use, per the
      "reuse whichever looping/continuous-tick pattern... rather than inventing a new ticker" guidance) —
      restarted from 0 on `AnimationStatus.Completed` to loop, sliding the shader via `ShaderMask.OffsetX`
      translation rather than a `GradientTween`/rebuilt gradient each frame (cheaper, and RenderShaderMask's
      paint path is explicitly built for this: it draws an oversized rect clipped to the button's bounds,
      so translating the offset sweeps the same shader pattern fully across and back off both edges).
      **Deliberate deviation from the task's literal wording**: the controller is owned locally by this
      widget's own `State`, NOT the shared `ScribeAnimationRegistry` `ScribeRowSizeAnimation` uses to
      survive a host `ForceRebuild` remount — a row collapse/reveal's direction and end-state are
      meaningful and must survive that remount intact, but a shimmer is a purely decorative, perpetually-
      looping sweep, so a remount simply restarting its phase from 0 is imperceptible; the extra registry
      plumbing (threading an id + the registry instance through every `TitleButton` call site across four
      dialog subclasses) wasn't judged worth it for a cosmetic effect explicitly marked tune-by-eye. New
      `ScribeDialogBase.ShowInboxShimmer` (`HasUnseenAssignment && !IsInboxView`) is the shared trigger,
      passed as `TitleButton`'s new `shimmer` parameter from the Assignment Desk/Lectern/Scriptorium/
      Chalkboard Inbox nav-button call sites; the standalone Inbox block has no other tab and needs none.

## 9. Accepted-task placement and rendering

- [x] 9.1 Implement Accept-time placement resolution: currently-held Scribe document → inventory
      scan (picker if multiple) → disabled Accept control if none, per
      `assignment-state-machine`'s placement requirement. Client (`ScribeDialogBase.ComputeAcceptCandidates`)
      resolves candidates from `capi.World.Player.InventoryManager` — the held item alone (via
      `ActiveHotbarSlot`) if it's an eligible (`IScribeDocumentItem` + `IsSlotWriteable`) surface (held
      always wins outright — design.md Decision 7), else every eligible inventory item. The Inbox row's
      Accept control (`ScribeInboxContent.cs`'s new `BuildAcceptControl`) renders disabled with a
      `Tooltip` explanation when empty, a plain button when there's exactly one candidate, or a
      `Dropdown<int>` picker + button when there's more than one. `ScribeAssignmentActionMessage` gained
      `TargetInventoryId`/`TargetSlotId` (Accept-only) so the server resolves the EXACT slot the client
      chose via the existing `ResolveItemPacketSlot` convention (fallback to active hand). On a successful
      Accept, `ScribeModSystem.Assignment.cs`'s new `TryPlaceAcceptedAssignment` builds the placed
      `ScribeBlock` — keeping the SAME `TaskId` as the assignment record (new Core
      `ScribeDocument.AppendAssignedBlock`, unlike `AppendClonedBlocksFrom`'s fresh-id convention) so later
      Done→Completed derivation and Delete→Discard can find the canonical store record by that shared id —
      writes it into the resolved slot's document, and best-effort refreshes an already-open dialog on
      that item via `ScribeNotebookSaveMessage`. Also wired the two Decision-adjacent requirements this
      unblocks: derived Completed (`NotifyAssignmentDoneChanged`, hooked into `CompleteTaskForPlayer` and
      `CompleteUnpinnedTaskAtSource` — the two server choke points every completion trigger, Read/Editor/
      Pinned/HUD alike, funnels through) and Delete→Discard (`NotifyAssignmentDiscardOnDelete`, hooked into
      `DeleteTaskForPlayer`) — see 9.4's note, done together since both share the placement plumbing.
- [x] 9.2 Add a `ReadOnly`/`CompletionAndPinLive`-style pair to `ScribeEditRowData`
      (`ScribeEditorContent.cs`), mirroring `ScribeReadRowData`'s existing fields
      (`ScribeReadContent.cs:79,134,137-139`). **Correction (9.3 session):** only the field
      shape was added here — nothing in `ScribeEditRow`/`ScribeFrozenEditorRow.Build` reads
      `ReadOnly` or `CompletionAndPinLive` yet, so an accepted assigned task's text does NOT
      actually render frozen in the Editor view. Confirmed still unwired (grep) while
      implementing 9.3; see that task's note for why it stays that way for now.
- [x] 9.3 Add the leading-icon visual marker for accepted assigned tasks, applied consistently
      across Tablet, Read, Edit, and Pinned rendering. New shared `ScribeAssignedTaskIcon`
      (`ScribeRowWidgets.cs`) builds a fixed-width leading column, placed after the grip/before
      the checkbox in `ScribeReadRow`, `ScribeEditRow`/`ScribeFrozenEditorRow`, and
      `ScribePinRow` — but UNLIKE the grip spacer, each call site only adds it to that row's
      children when `IsAcceptedAssignment` is true, so an ordinary (non-assigned) row's layout
      is untouched rather than reserving a permanently-empty column (2026-08-30 correction: the
      first pass always reserved the column at zero opacity, mirroring the grip-spacer
      convention — reverted after playtest feedback that the gap read as an unwanted permanent
      indent on every row). Uses the existing `"scribeguest"` icon as a placeholder pending a
      dedicated asset (§13.4, out of scope here). Threaded `IsAcceptedAssignment` through every
      row-data record on the accepted-assignment path: `ScribeReadRowData`, `ScribeEditRowData`,
      and the new `ScribePinRowData` field, plus the Core `ScribePinnedRef.IsAcceptedAssignment`
      flag (`ScribePinStore.SetPin`/`ReconcileSnapshotsForActor` compute it from the block's
      `Assignment.State`) so a HUD-pinned accepted task carries the marker across a document
      reload, requiring a `ScribePinCodec` version bump (v5→v6, append-only, see
      `docs/CODEC-MIGRATION.md`). Explicitly does NOT wire the Editor's frozen-text half of
      Decision 5 (see 9.2's corrected note) — swapping `ScribeMultilineField` for a static
      renderer without auditing its focus-index/tablet-cuneiform/jump-navigation wiring risked a
      subtly broken row-navigation experience; disclosed as a deferred follow-up, not silently
      dropped.
- [x] 9.4 Wire the Delete affordance on an accepted assigned task to perform the Discard
      transition (network message + local removal) instead of a bare local delete.
      `ScribeModSystem.PinOperations.cs`'s `DeleteTaskForPlayer` (the single choke point for the Delete
      affordance across Read/Editor/Pinned/HUD, addressed by docId+taskId) now captures the block's
      `Assignment` before `ScribeCompletion.ApplyDelete` removes it, then calls the new
      `NotifyAssignmentDiscardOnDelete` (see 9.1's note) when it was Accepted — which applies Discard to
      the canonical `ScribeAssignmentStore` record through the SAME actor-validated `TryApplyAction` path
      a wire Discard action would use (not a bare store write), then pushes a fresh sync to both parties.
      No separate network message needed — the existing delete-task message already round-trips through
      this one server method.
- [x] 9.5 Confirm Complete/Counter/Pin/Reorder affordances remain fully live and unchanged on
      accepted assigned tasks. Verified by reading every server choke point touched this tier
      (`ScribeModSystem.PinOperations.cs`): `CompleteTaskForPlayer`/`CompleteUnpinnedTaskAtSource`,
      `DeleteTaskForPlayer`, `SetPinForPlayer`, `ReorderPinsForPlayer`, and
      `SetTrackerQuantityForPlayer` (the Counter path) all still run their unconditional mutation
      logic — 9.1/9.4's additions are strictly a pre-mutation `Assignment` snapshot read plus a
      post-mutation `NotifyAssignment*` call, neither of which gates or short-circuits the
      existing behavior. Client-side, the new `ScribeAssignedTaskIcon` (9.3) is inserted as an
      additional row child alongside the grip/checkbox, never wrapping or replacing them, so no
      existing affordance's hit-test or event wiring changed.

## 10. Quest Link (Layer 1)

- [x] 10.1 Add the Quest Link creation path: reading the installed quest mod's static
      `config/quests/*.json` catalog, capturing name/description into the new Link block at
      creation time. New `ScribeQuestCatalog` (Mod) reads `vsquest`'s own catalog via
      `capi.Assets.GetMany<T>` with a tiny local DTO (no reference to the vsquest assembly —
      decompiling confirmed the JSON carries only `id`; title/description resolve via
      `Lang.Get(id + "-title"/"-desc")`, matching how `VsQuest.QuestSystem` itself loads them).
      `ScribeBlock` gained `LinkDescription` (Core; codec v11 — see docs/CODEC-MIGRATION.md and
      `ScribeAssignmentStore`'s own record format, also bumped) alongside the existing `LinkLabel`,
      since a quest Link's description has no Handbook page to reopen later the way a guide-page
      Link's does. `ScribeDocument.AddQuestLink`/`InsertQuestLink` mirror the existing guide-link
      pair exactly. **UI decision (asked the user, not guessed)**: the footer's "New Task" add-kind
      drop-up gained a "Quest Link" tile (hidden when the catalog is empty) that swaps the SAME
      open floating menu to a flat list of quest titles in place — reusing every bit of the
      existing overlay/animation/barrier plumbing rather than opening a second dropdown or building
      a search-first list dialog. No search/scroll: vsquest catalogs are typically a handful to a
      few dozen entries; a very large third-party catalog would overflow ungracefully — a disclosed
      limit, not something this pass builds search for. `LinkDescription` is captured and persisted
      but has NO consuming display surface yet (no Scribe UI renders a Link's description anywhere,
      for any Link flavor) — a disclosed gap, not a silent drop; a future pass could surface it as
      a tooltip/expandable line. Fixed two related pre-existing gaps found while wiring this: the
      TSV codec's `TextFor`/`BuildBlock` only special-cased `IsGuidePage` for the title-in-Text
      convention, so a quest Link would have round-tripped with a blank Text/title before this fix.
- [x] 10.2 Gate the Quest Link option in every Link-creation picker (item handbook's Add Link,
      New Task dropdown, etc.) behind `IsModEnabled("vsquest")`. **Scope call**: the item/guide-page/
      meal-page Handbook patches (`ScribeHandbookPatch`/`ScribeGuidePageHandbookPatch`/
      `ScribeMealPageHandbookPatch`) inject their "Add Link" onto a SPECIFIC page a specific
      item/guide/meal already opened — vsquest has no Handbook integration at all (confirmed via
      decompiling its shipped assembly: no handbook page registration anywhere), so there is no
      per-quest Handbook page for a Quest Link option to attach to. The only "every Link" picker a
      Quest Link option can actually plug into is the generic New Task dropdown, which is where it
      was added; `ScribeQuestCatalog.IsAvailable`/`ReadCatalog`'s emptiness gates it (an empty
      catalog — vsquest absent or installed with zero quests — omits the tile entirely, satisfying
      the `IsModEnabled` gate transitively via "nothing to show").
- [x] 10.3 Confirm an orphaned Quest Link (vsquest since uninstalled) renders correctly from its
      captured text with no error state. `ScribeItemRef.ResolveDisplay`/`OpenHandbookPage` and
      `ScribeRowWidgets.ScribeLinkIcon` gained an `IsQuest` branch alongside the existing
      `IsGuidePage` one (book glyph, label from the stored `LinkLabel`, click is an intentional
      no-op — Layer 1 has no navigable target for a quest the way a guide page always has a
      Handbook page to reopen). Also fixed a real bug this surfaced: `ScribeImportValidator.
      ShouldDegrade` checked `IsGuidePage` but not `IsQuest`, so importing a quest Link would have
      tried to resolve `"quest:vsquest:..."` as an item code and degraded it to a plain Task —
      added the same exemption guide-page Links already had. Every render/click surface (read,
      editor, Pin Tab, HUD) funnels through these same shared helpers, so the fix and the no-op
      apply uniformly with no per-surface duplication to verify separately. Verified by the Core
      round-trip tests (10.1) plus manual reasoning over every call site (no game-side smoke test
      run this pass, since vsquest isn't installed in this dev environment — orphan rendering was
      verified by code inspection of the shared helpers, not an in-game screenshot).

## 11. Quest soft auto-detect (Layer 2)

- [ ] 11.1 Add the Harmony patch/reflection layer scoped to `VsQuest.QuestSelectGui` (found by
      type name), reading `questGiverId`/`activeQuests` and the three tracker-count properties,
      wrapped in try/catch with self-disable on failure.
- [ ] 11.2 Wire detected accept-state into the Quest Accept policy's Always/Never/Prompt behavior.
- [ ] 11.3 Wire detected kill/place/break progress into the linked task's display; leave gather
      objectives at accept-state-only.
- [ ] 11.4 Wire detected quest completion into the Quest Completion policy's Always/Never/Prompt
      behavior.

## 12. Settings and handbook

- [ ] 12.1 Add the Quest Accept Policy and Quest Completion Policy dropdowns to the Settings
      dialog's Behavior section, gated on `IsModEnabled("vsquest")`.
- [ ] 12.2 Add handbook documentation for the Assignment Desk, Inbox block, and assignment
      workflow (ungated — always visible).
- [ ] 12.3 Add handbook documentation for Quest Links and auto-detect, gated on
      `IsModEnabled("vsquest")`.

## 13. Assets

- [ ] 13.1 Model/texture/crafting-recipe assets for the Assignment Desk block and item.
- [ ] 13.2 Model/texture/crafting-recipe assets for the Inbox block and item.
- [ ] 13.3 Particle texture/visual tuning for the ambient unseen-assignment indicator.
- [ ] 13.4 Leading-icon glyph asset for the accepted-assigned-task visual marker.

## 14. Documentation and release hygiene

- [x] 14.1 Add a `VSAPI-NOTES.md` entry documenting the vsquest dialog-reflection technique (field
      names, fragility, the try/catch self-disable pattern) so it isn't re-derived later.
- [x] 14.2 Update `ROADMAP.md` to move Assignment out of "later" and record Quest support as a new
      v1.4.0 tier.
- [x] 14.3 Add a `CHANGELOG.md` `[Unreleased]` entry once implementation lands.
