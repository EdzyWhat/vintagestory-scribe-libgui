## MODIFIED Requirements

### Requirement: Read-view completion applies the completion policy locally and immediately

Completing a task from the read view SHALL apply the player's completion policy to the read view's own
document view and refresh immediately — the same optimistic-then-confirm model the editor uses — rather
than sending the completion to the server and waiting for a resync to make the result visible. The
visible result SHALL NOT depend on whether the completed task is pinned: a completion under a
document-mutating policy (`Delete`, `Sink`, `UnpinSink`) SHALL be reflected in the read view for an
unpinned task exactly as for a pinned one. The completion policy's document semantics SHALL be defined
by a single shared Core function used by both the server and every client view, so no surface derives
its own policy behavior; that function treats the `Delete` policy as `Sink` for a task that repeats yearly
(and for a parent group containing one), so such a task is never removed. The authoritative server resync
SHALL still arrive and supersede the optimistic result.

#### Scenario: Completing an unpinned task under Delete removes its row immediately

- **WHEN** the player completes an unpinned document task from the read view while their completion
  policy is `Delete`
- **THEN** the task's row is removed from the read view immediately (not only after a later, unrelated
  refresh), the scroll offset holds, and the authoritative resync later confirms the same result

#### Scenario: Pinned and unpinned completions behave identically in the read view

- **WHEN** the player completes a task from the read view under a document-mutating policy
- **THEN** the read view reflects the policy's effect regardless of whether that task was pinned — the
  pinned case does not rely on the pin push while the unpinned case is left stale

#### Scenario: A read-only source does not optimistically predict a refused mutation

- **WHEN** the player completes a task on a permanently read-only source (a hard/fired tablet), where
  the server collapses every document-mutating policy to a plain unpin
- **THEN** the read view does not optimistically remove or reorder the task (which the server would
  refuse); the visible change is driven by the authoritative resync instead

#### Scenario: A repeating task under Delete moves instead of disappearing

- **WHEN** the player completes a task that repeats yearly from the read view while their completion
  policy is `Delete`
- **THEN** the task's row moves to the bottom, checked, immediately, and the authoritative resync later
  confirms the same result
