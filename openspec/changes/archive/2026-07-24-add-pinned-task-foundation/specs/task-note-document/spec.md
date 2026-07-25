> The document-model identity and codec changes this feature relies on (stable `DocId`/`TaskId`,
> codec v4 write / v3+v4 read + migration seam, `FindByTaskId`, delete-reports-id) live in the
> `add-document-task-identity` change, which this change depends on. This delta only removes the
> old shared per-document pin toggle, whose one consumer this change re-points to the per-player
> pin store (see the `player-pins` capability).

## REMOVED Requirements

### Requirement: Pin a task
**Reason**: Pinning is no longer a shared, per-document boolean toggled by position; it is now a
per-player reference to a task by stable identity (see the new `player-pins` capability). The
document no longer stores any pinned state.
**Migration**: Callers that toggled a task's pinned flag now record a per-player pin via the
`player-pins` capability, keyed by `(DocId, TaskId)`. Existing prior-version documents that
carried pinned flags have those flags surfaced at deserialization (see `add-document-task-identity`
→ "Prior-version pin flags are surfaced for migration") so they can be drained into the current
player's pin store.
