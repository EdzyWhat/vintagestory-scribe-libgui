## 1. Core: assignment data model

- [ ] 1.1 Replace `ScribeBlock.AssignedToUid` (`src/Core/ScribeBlock.cs`) with a `ScribeAssignment?`
      value type carrying assigner UID, current state, in-game assigned-date, and the Seen flag.
- [ ] 1.2 Add a `ScribeAssignmentState` enum (`Unaccepted`, `Accepted`, `Declined`, `Cancelled`,
      `Discarded`, `Completed`) to `src/Core/`.
- [ ] 1.3 Add pure transition-validation logic (which actor may move which state to which state)
      in `src/Core/`, matching the matrix in `design.md` exactly.
- [ ] 1.4 Add `ScribeQuestAcceptPolicy` and `ScribeQuestCompletionPolicy` enums (`Always`/`Never`/
      `Prompt`) to `src/Core/`, mirroring `ScribeCompletionPolicy`'s shape.
- [ ] 1.5 Add a Quest-namespaced `LinkTarget` convention (e.g. a `quest:` prefix) alongside the
      existing `page:` guide-page convention, staying within the existing `Link` block kind (no
      new `ScribeBlockKind`).

## 2. Core: unit tests

- [ ] 2.1 Unit-test every legal and illegal transition in the assignment state matrix (both
      actors, every state).
- [ ] 2.2 Unit-test that Completed is only reachable via the underlying task's done-flag, never
      directly.
- [ ] 2.3 Unit-test Delete-on-Accepted performing the Discard transition.
- [ ] 2.4 Unit-test the Seen flag's default-unseen and mark-seen-on-view behavior independent of
      state transitions.
- [ ] 2.5 Unit-test codec round-trip of the new `ScribeAssignment` field (present and absent
      cases) and of the Quest-namespaced `LinkTarget`.

## 3. Codec/serialization

- [ ] 3.1 Update `ScribeDocumentJsonCodec`/binary codec to serialize/deserialize the new
      `ScribeAssignment` type in place of the old bare `AssignedToUid` string.
- [ ] 3.2 Confirm import (`ScribeDocumentJsonCodec.cs`'s existing "assignment is place-bound, not
      shareable" rule) still strips assignment state on import — update the comment to reference
      the new type instead of the old bare UID.
- [ ] 3.3 Update `docs/CODEC-MIGRATION.md` and any codec version-window tests/comments affected by
      the field shape change.

## 4. Networking

- [ ] 4.1 Add client→server messages for: send assignment, Accept, Decline, Cancel, Discard.
- [ ] 4.2 Add server→client sync for assignment state changes (both Assigner's and Assignee's open
      dialogs, if any, update live).
- [ ] 4.3 Add the Quest Accept/Completion policy preferences to the existing player-settings sync
      message, following `ScribeCompletionPolicy`'s existing wire pattern.

## 5. Assignment Desk block

- [ ] 5.1 Add the Assignment Desk block/blocktype, item, crafting recipe, and block entity
      (reusing the writing-station block-entity base).
- [ ] 5.2 Implement `IScribeDocumentHost.GetLayout` for the Assignment Desk: `W = PixelArtSize`,
      `AspectH = 1.2`, with the active tab's content region laid out as a 1:1 square within that
      box (design.md Decision 8) — a real layout, not a placeholder pending art.
- [ ] 5.3 Add `Assignment` and `Inbox` members to `ScribeLecternView`
      (`ScribeDialogBase.cs:88`) and the corresponding `IsAssignmentView`/`IsInboxView` exposers.
- [ ] 5.4 Build the Assignment Desk's GUI dialog class, defaulting to the Assignment tab, with a
      nav button pair switching between Assignment and Inbox — reusing Lectern/Scriptorium widget
      parts per `design.md` Decision 1.
- [ ] 5.5 Build the Assignment tab's create-and-send form (task text entry, target player
      picker, send action) — the only surface in the mod with this capability.

## 6. Inbox block

- [ ] 6.1 Add the Inbox block/blocktype, item, crafting recipe, and block entity (reusing the
      writing-station block-entity base).
- [ ] 6.2 Implement `IScribeDocumentHost.GetLayout` for the Inbox block: same `W = PixelArtSize`,
      `AspectH = 1.2`, 1:1 square content region as the Assignment Desk's Inbox tab
      (design.md Decision 8).
- [ ] 6.3 Build the Inbox block's GUI dialog class, opening directly to the shared Inbox tab with
      no Assignment tab present.

## 7. Shared Inbox tab UI

- [ ] 7.1 Build the Inbox row `StatefulWidget` with a per-row `expanded` bool, collapsed/expanded
      renderings per `design.md` Decision 6.
- [ ] 7.2 Build the leading chevron disclosure control (reusing `ScribeRowButton` chrome),
      chevron-only trigger, leading-edge placement before the checkbox.
- [ ] 7.3 Build the compact state chip and the expanded assigner/date/action-button block, reusing
      `ScribeTrackerCounterText`/`ScribeLinkIcon` where applicable for tracker/link-kind assigned
      tasks.
- [ ] 7.4 Build the state filter-chip row for the Inbox tab (one toggleable pill per state,
      always visible, `ScribeRowButton`-style chrome).
- [ ] 7.5 Wire the Inbox tab into the Assignment Desk (Inbox sub-tab), the standalone Inbox block
      (sole view), and the nav-button entry points on Lectern/Scriptorium/Chalkboard — one shared
      implementation, not per-surface copies.

## 8. Nav buttons and particle indicator on existing blocks

- [ ] 8.1 Add the Inbox nav button to `GuiDialogScribeLecternLibGui.GetExtraNavButtons()`.
- [ ] 8.2 Add the Inbox nav button to `GuiDialogScribeScriptorium.GetExtraNavButtons()`; remove/
      update the stale "Scriptorium-only Assign & History" comment in
      `BlockEntityScriptorium.cs:17`.
- [ ] 8.3 Add the Inbox nav button to `GuiDialogScribeChalkboard.GetExtraNavButtons()`.
- [ ] 8.4 Implement the ambient particle emitter (tick-interval-gated, client-side, player-local),
      scoped to Assignment Desk, Inbox, Lectern, Scriptorium, and Chalkboard block entities.
      Manual `SimpleParticleProperties`/`AdvancedParticleProperties` spawn on the sampler tick —
      NOT `Block.ParticleProperties` (that engine mechanism is always-on/unconditional per block,
      not per-viewing-player, per `design.md` Decision 9). Start from the HSV/motion values in
      Decision 9 (base amber tone, ~1-in-5 particles re-rolled to a full-range hue for the
      rainbow-sparkle accent) and tune by eye.
- [ ] 8.5 Implement the Inbox nav-button shimmer (design.md Decision 9b): a `ShaderMask` +
      animated `LinearGradient`/`GradientTween` sweep across the button icon, looping while the
      viewing player has an unseen assignment AND that surface's Inbox tab is not the active
      view; stops once either condition clears. Reuse the existing looping-animation ticker
      pattern from `ScribeRowSizeAnimation.cs`/`ScribeAnimatedList.cs` rather than a new one.
      Applies to Assignment Desk, Lectern, Scriptorium, and Chalkboard's Inbox nav buttons; the
      standalone Inbox block has no other tab and needs no shimmer.

## 9. Accepted-task placement and rendering

- [ ] 9.1 Implement Accept-time placement resolution: currently-held Scribe document → inventory
      scan (picker if multiple) → disabled Accept control if none, per
      `assignment-state-machine`'s placement requirement.
- [ ] 9.2 Add a `ReadOnly`/`CompletionAndPinLive`-style pair to `ScribeEditRowData`
      (`ScribeEditorContent.cs`), mirroring `ScribeReadRowData`'s existing fields
      (`ScribeReadContent.cs:79,134,137-139`), so an accepted assigned task's text renders frozen
      in the Editor view too, not just Read.
- [ ] 9.3 Add the leading-icon visual marker for accepted assigned tasks, applied consistently
      across Tablet, Read, Edit, and Pinned rendering.
- [ ] 9.4 Wire the Delete affordance on an accepted assigned task to perform the Discard
      transition (network message + local removal) instead of a bare local delete.
- [ ] 9.5 Confirm Complete/Counter/Pin/Reorder affordances remain fully live and unchanged on
      accepted assigned tasks.

## 10. Quest Link (Layer 1)

- [ ] 10.1 Add the Quest Link creation path: reading the installed quest mod's static
      `config/quests/*.json` catalog, capturing name/description into the new Link block at
      creation time.
- [ ] 10.2 Gate the Quest Link option in every Link-creation picker (item handbook's Add Link,
      New Task dropdown, etc.) behind `IsModEnabled("vsquest")`.
- [ ] 10.3 Confirm an orphaned Quest Link (vsquest since uninstalled) renders correctly from its
      captured text with no error state.

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

- [ ] 14.1 Add a `VSAPI-NOTES.md` entry documenting the vsquest dialog-reflection technique (field
      names, fragility, the try/catch self-disable pattern) so it isn't re-derived later.
- [ ] 14.2 Update `ROADMAP.md` to move Assignment out of "later" and record Quest support as a new
      v1.4.0 tier.
- [ ] 14.3 Add a `CHANGELOG.md` `[Unreleased]` entry once implementation lands.
