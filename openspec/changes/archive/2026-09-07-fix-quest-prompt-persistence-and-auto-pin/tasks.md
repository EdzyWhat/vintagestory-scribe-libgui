## 1. Core: decision model and settings field

- [x] 1.1 Add a `ScribeQuestDecision` enum (`Accepted`, `Dismissed`) and a small
      `ScribeQuestDecisionEntry`/set type to `src/Core/` (source, quest code, `IsCompletion`,
      decision) with no `VintagestoryAPI` reference; verify with a new `Core.Tests` unit covering
      add/lookup/serialize round-trip.
- [x] 1.2 Add `AutoPinOnQuestAccept` (bool, default `true`) to `ScribePlayerSettings.cs`; verify a
      `Core.Tests` case confirms an old config JSON missing the key loads with the field defaulted
      to `true`.

## 2. Mod: server-authoritative decision store

- [x] 2.1 Create `ScribeQuestDecisionStore` (Mod-side, mirroring `ScribePinStore`'s shape) with
      per-player add/lookup/remove and a serialize/deserialize pair bounded the same way the pin
      store bounds its own per-player set; verify with a unit test round-tripping a serialized
      store through deserialize.
- [x] 2.2 Wire load/save into `ScribeModSystem.ServerLifecycle.cs` alongside `pinStore`/
      `assignmentStore` (`OnSaveGameLoaded` + the save-game-saving handler, its own
      `QuestDecisionStoreSaveKey`); verify by saving and reloading a local test world and
      confirming (via a temporary log line, removed before commit) the store round-trips non-empty.
- [x] 2.3 Add sync-to-owning-player delivery (mirroring `PushPinsTo`) sent on player join and
      whenever that player's decision set changes; verify by observing the client receives the
      packet on join in a local playtest.

## 3. Network: dismiss message + accept/complete recording

- [x] 3.1 Add `ScribeDismissQuestPromptMessage` (Source, QuestCode, IsCompletion) and register its
      channel handler; verify the message round-trips in a local client/server smoke test.
- [x] 3.2 In `OnServerReceivedAutoLinkQuest` (`ScribeModSystem.Quest.cs`), after a successful
      `doc.AddQuestLink`, record an `Accepted` decision for `(Source, QuestCode, IsCompletion:
      false)` in the decision store; verify via a local playtest that a second relog does not
      re-raise the same accept-prompt.
- [x] 3.3 In the completion-request handler path (`ScribeCompleteTaskMessage`'s server handling),
      when the completed task is a pinned Quest Link, record an `Accepted` decision for
      `(Source, QuestCode, IsCompletion: true)`; verify via local playtest that completing a linked
      quest task does not leave a stray completion-prompt re-appearing on relog.
- [x] 3.4 Handle `ScribeDismissQuestPromptMessage` server-side by recording a `Dismissed` decision
      for the given key; verify via local playtest that dismissing an accept-prompt, then relogging,
      does not re-raise it.
- [x] 3.5 Send `ScribeDismissQuestPromptMessage` from `DismissQuestPrompt`
      (`ScribeModSystem.Quest.cs`); verify the existing optimistic client-side removal from
      `pendingQuestPrompts` is unchanged (no regression to the HUD's immediate dismiss feedback).

## 4. Watcher/prompt-flow: consult the synced ledger

- [x] 4.1 Cache the client's synced decision set (from task 2.3's delivery) on `ScribeModSystem`;
      verify it updates in place on each re-delivery without requiring a relog.
- [x] 4.2 In `OnQuestAccepted`/`OnQuestCompleted` and `QueuePrompt`, skip queuing (and skip the
      `Always` policy's immediate auto-link send) when a decision already exists for that exact
      `(Source, QuestCode, IsCompletion)`; verify with a `Core.Tests`-adjacent Mod-side unit test if
      feasible, else a scripted local playtest: accept a quest, relog, confirm no re-prompt; dismiss
      a different quest, relog, confirm no re-prompt.
- [x] 4.3 Implement Decision 4's approximate reset-clear: when `ScribeQuestWatcher` observes a
      quest's accept-state attribute go from absent/inactive to active for a player with no live
      Quest Link for that `(Source, QuestCode)`, clear any existing decision entry for
      `(Source, QuestCode, IsCompletion: false)` before evaluating the policy; verify with a unit
      test simulating the transition against a stubbed decision set.

## 5. Auto-pin on accept

- [x] 5.1 Add `AutoPin` (bool) to `ScribeAutoLinkQuestMessage`, set by the client from
      `MySettings.AutoPinOnQuestAccept` at send time in `SendAutoLinkQuest`; verify the field
      round-trips in the existing message-serialization test coverage (or add one).
- [x] 5.2 In `OnServerReceivedAutoLinkQuest`, after `doc.AddQuestLink`/objective reconciliation and
      before `host.Flush()`, call `SetPinForPlayer(fromPlayer, host.DocId, doc.Blocks[^1].TaskId,
      pinned: true, ...)` when `message.AutoPin` is true; verify via local playtest that accepting a
      quest with the setting on adds the task to the HUD/Pin Tab immediately.
- [x] 5.3 Verify via local playtest that accepting with the setting off links the task but does not
      pin it (today's behavior preserved).

## 6. Settings UI

- [x] 6.1 Add the `AutoPinOnQuestAccept` row to `ScribeSettingsContent.cs` next to the existing
      Quest Accept/Completion policy rows, hidden under the same "only visible if a quest mod is
      installed" gate; verify by opening Settings with and without a quest mod installed in a local
      playtest.

## 7. Cross-cutting verification

- [x] 7.1 Run the `Core` test suite (`dotnet test`) and confirm all new and existing tests pass.
- [x] 7.2 Run a full local Atlas/manual pass covering: accept-then-relog (no re-prompt), dismiss-
      then-relog (no re-prompt), a completion-prompt after an accept-prompt was dismissed (still
      prompts — independent decision), auto-pin on/off, and the new Settings row's visibility gate.
