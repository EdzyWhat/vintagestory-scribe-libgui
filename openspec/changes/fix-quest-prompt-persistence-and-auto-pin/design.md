## Context

`ScribeQuestWatcher` (`src/Mod/ScribeQuestWatcher.cs`) is a purely client-side detector: every tick
it reads quest-mod state that is already synced to the client (vsquest's per-entity
`WatchedAttributes`, Progression Framework's per-player `WatchedAttributes` tree, or PF's reflected
shared server-quest field) and fires `onAccepted`/`onCompleted` callbacks. Its `_acceptedSeen`/
`_completedSeen`/`pfAcceptedSeen`/`pfCompletedSeen` sets exist only to stop it firing the same
callback on every tick *within one session* — the underlying attribute (e.g. `lastaccepted-X`)
never goes away, so without the set the callback would fire every second forever. On a relog those
sets reset to empty, the attribute is still there, and the callback fires again — that is the bug.
The class's own doc-comment already discloses this as a session-only limitation.

The callbacks land in `ScribeModSystem.Quest.cs`'s `OnQuestAccepted`/`OnQuestCompleted`, which
branch on the per-player `QuestAcceptPolicy`/`QuestCompletionPolicy` (`Always`/`Never`/`PromptHud`/
`PromptPopup`/`Prompt`) and either send `ScribeAutoLinkQuestMessage` immediately or `QueuePrompt` a
`ScribeQuestPrompt` for the HUD/modal to render. `QueuePrompt` already dedups by the exact tuple
`(Source, QuestCode, IsCompletion)` against the in-memory `pendingQuestPrompts` list — the same
tuple `HudScribePins` uses as its animation entry key. That tuple is the natural ledger key: it is
already how this system identifies "which prompt."

Existing server-side per-player stores (the pin store, the assignment store, the player-location
store) all follow the same pattern: a Mod-side store class persisted via
`sapi.WorldManager.SaveGame.GetData/StoreData` under its own key, loaded in `OnSaveGameLoaded`,
saved on the save-game-saving event, keyed by `player.PlayerUID`. `ScribePlayerSettings` is a
different, unrelated tier — client-local JSON, identical across every world — which is why it is
the wrong place for anything keyed by "this quest, in this world."

See `proposal.md` for the "why."

## Goals / Non-Goals

**Goals:**
- Make a player's accept/dismiss decision on a given quest prompt durable across a relog and a
  server restart, scoped to the world it was decided in.
- Let accepting a quest optionally auto-pin the resulting task, controlled by a new client-local
  preference.
- Reuse existing seams (the `(Source, QuestCode, IsCompletion)` tuple, the save-game store pattern,
  the `ScribeAutoLinkQuestMessage` round trip, `SetPinForPlayer`) rather than inventing new
  infrastructure.

**Non-Goals:**
- Detecting every possible "the underlying quest genuinely reset" case with certainty. Repeatable
  quests are rare in both vsquest and Progression Framework content today; this design gives a
  best-effort answer (see Decision 4) and leaves deeper detection as a documented limitation, the
  same way the current session-only gap already is.
- Changing anything about `ScribeQuestWatcher`'s own detection mechanism (attribute keys, reflection
  paths, fail-closed behavior) — it stays exactly as much a pure client-side detector as it is
  today.
- Retrofitting a "was this quest ever decided" answer for quests decided before this change ships
  (see Migration Plan).

## Decisions

### Decision 1: The ledger lives in a new server-authoritative, per-player, per-world store — not `ScribePlayerSettings`
A new `ScribeQuestDecisionStore` (Mod-side, mirroring `ScribePinStore`) holds, per player UID, a set
of `(Source, QuestCode, IsCompletion) → Decision` entries (`Decision` = `Accepted` or `Dismissed`).
Persisted via `sapi.WorldManager.SaveGame.GetData/StoreData` under its own key, loaded in
`OnSaveGameLoaded` and saved alongside the pin/assignment/player-location stores in
`ScribeModSystem.ServerLifecycle.cs`. Synced to its owning player on join (mirroring
`PushPinsTo`) and whenever it changes for that player, so the client-side watcher path can consult
it without a round trip on every tick.

**Alternative considered**: store it in `ScribePlayerSettings`. Rejected — that class is explicitly
client-local and identical across every world a player plays; a quest code decided in World A
would wrongly suppress the same code's prompt in World B.

### Decision 2: The ledger is consulted one layer above `ScribeQuestWatcher`, not inside it
`ScribeQuestWatcher` stays a narrow, VS-API-facing detector with no persistence or server
awareness — consistent with its own doc-comment ("this class... is NOT the mechanism that prevents
a duplicate"). The consultation point is `ScribeModSystem.Quest.cs`'s `OnQuestAccepted`/
`OnQuestCompleted`/`QueuePrompt`, which already hold the policy-branching logic and already have
access to `capi` and the client's synced copy of the ledger. Before queuing a prompt (or sending an
immediate auto-link under the `Always` policy), these methods check the synced ledger for an
existing decision on that exact `(Source, QuestCode, IsCompletion)` and skip if one exists.

### Decision 3: Recording a decision reuses the accept round trip; dismiss gets one new message
Accepting already round-trips through `ScribeAutoLinkQuestMessage` →
`OnServerReceivedAutoLinkQuest`, which is authoritative for whether the Link was actually added.
That handler now also records an `Accepted` ledger entry for `(Source, QuestCode, IsCompletion:
false)` — no new message needed for accept. A completion-prompt's "accept" (mark the linked task
done) similarly piggybacks on the existing `ScribeCompleteTaskMessage` path, recording `Accepted`
for `(Source, QuestCode, IsCompletion: true)` server-side once the completion is applied.

Dismissing has no existing round trip (today it is a pure client-side `pendingQuestPrompts.Remove`
with no network effect), so this adds one new small message, `ScribeDismissQuestPromptMessage`
(`Source`, `QuestCode`, `IsCompletion`), sent from `DismissQuestPrompt`. The server records a
`Dismissed` entry and the store's normal sync-on-change delivers it back, which is fine — the
client already dropped the prompt from `pendingQuestPrompts` optimistically, same as every other
optimistic-client / authoritative-server pattern this codebase already uses (e.g. pin add/remove).

### Decision 4: "Fresh state" detection is best-effort, not a state fingerprint
The ledger key intentionally does NOT include a quest-instance fingerprint (attempt count, reset
timestamp, etc.) — vsquest and Progression Framework expose no reliable "this is a new cycle of a
repeatable quest" signal to a passive client-side reader today. Instead: a `Dismissed` or `Accepted`
entry for `(Source, QuestCode, IsCompletion: false)` is cleared automatically the next time
`ScribeQuestWatcher` observes that quest's accept-state attribute transition from absent/inactive to
active for a player who has no live Quest Link for that `(Source, QuestCode)` — i.e., the same
"already linked" absence check `OnServerReceivedAutoLinkQuest` already performs, reused as the
"is this decision still live" check. This is deliberately approximate; it is documented in the spec
as a known scenario (a repeatable quest resetting), not engineered further.

### Decision 5: Auto-pin executes server-side, in the same handler that creates the Link
`OnServerReceivedAutoLinkQuest` already reads `doc.Blocks[^1].TaskId` right after `doc.AddQuestLink`
(to seed Progression Framework objective children). Auto-pin reuses that same just-created
`TaskId`: a new bool field on `ScribeAutoLinkQuestMessage`, `AutoPin`, is set by the client from
`MySettings.AutoPinOnQuestAccept` at send time (mirroring how `ScribeCompleteTaskMessage` already
carries the client's `Policy`/`SubtaskBehavior`). When `message.AutoPin` is true, the handler calls
the existing `SetPinForPlayer(fromPlayer, host.DocId, newTaskId, pinned: true, ...)` — the same
server-side pin-add path the manual Pin control's network handler uses — right after
`host.Flush()`. No new pin-store code path; this is a second caller of an existing one.

**Alternative considered**: have the client send a follow-up pin-add message once it learns the new
TaskId from the resulting document sync. Rejected — adds a race (the doc sync and the pin-add would
need to be sequenced) and a round trip for no benefit, when the server already has the TaskId in
hand at the moment it is created.

### Decision 6: Completion-prompt "decisions" reuse `Accepted`/`Dismissed` semantics
A completion-prompt's dismiss and its "mark done" action are conceptually the same shape as an
accept-prompt's — this design does not introduce a third decision value. `IsCompletion` in the key
already distinguishes the two prompt kinds, so `Accepted`/`Dismissed` mean "linked" / "dismissed
this accept-prompt" for one kind and "marked done" / "dismissed this completion-prompt" for the
other, matching each kind's own existing action semantics.

## Risks / Trade-offs

- **[Risk]** A player who dismisses a quest, then much later actually wants to track it, has no UI
  path back to the prompt (it is gone) short of manually creating a Quest Link. → **Mitigation**:
  unchanged from today — manual Quest Link creation already exists as a fallback for any quest
  outside the auto-detect flow (`quest-auto-detect`'s "auto-detection... never blocks manual Quest
  Links" requirement already covers this).
- **[Risk]** The new `ScribeQuestDecisionStore` is one more per-player collection that could grow
  unbounded over a very long-lived world with a huge quest catalog. → **Mitigation**: bound it the
  same way the pin store already bounds its own per-player set (an upper-bound check on
  deserialize), sized generously against realistic vsquest/PF catalog sizes.
- **[Trade-off]** Decision 4's approximate reset-detection means a genuinely repeatable quest that
  resets while the player still holds a decided-and-dismissed entry, but the reset does NOT clear
  their existing Link (edge case: they were auto-linked, then deleted the task, quest resets) will
  not re-prompt. This mirrors today's already-disclosed limitation class rather than introducing a
  new one.

## Migration Plan

- New save-game key defaults to an empty store on first load (no existing data) — no migration
  needed for existing worlds; every player's ledger simply starts empty, meaning any quest already
  mid-flow before this ships will prompt (at most) once more on the next relog before settling into
  the new persisted behavior.
- `AutoPinOnQuestAccept` follows the same additive-property pattern every other
  `ScribePlayerSettings` bool already uses (defaults `true` for a config file that predates the
  field; no format bump).
- No rollback concern beyond the normal "revert the mod version" — the new save-game key is
  additively read; an older mod version simply ignores it.
