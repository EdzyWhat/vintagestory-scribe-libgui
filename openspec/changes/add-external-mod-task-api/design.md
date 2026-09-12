## Context

See `proposal.md` for motivation (Notice Board's integration request). This covers how the four
pieces fit together: the public method itself, the new `ExtraInfo` hover-detail field, the
last-opened-item tracker, and the failure-notice path.

Relevant existing precedent this design deliberately reuses rather than reinvents:
- `ScribeAssignmentStore.TryCreate` / `TryCreateAccepted` / `TryCreateSent` → one private
  `TryCreateCore`: the "several public front doors, one private engine" shape.
- `ScribeModSystem.Handbook.cs`'s `ResolveWriteableCarriedSlot`: silent last-opened-or-first-writeable
  resolution, no picker, skips locked (hardened/fired) items.
- `ScribeAssignedTaskIcon` / `IsAcceptedAssignment`: a block-level marker snapshotted into
  `ScribePinnedRef` (pin codec), rendered by the Pin Tab's row widget, never referenced by
  `HudScribePins.cs`.
- `NotifyServerNotebookOpened` → `OnServerReceivedNotebookOpened`: the existing client→server message
  sent by all three Scribe item types on dialog-open, today used only to record pickup history.

## Goals / Non-Goals

**Goals:**
- One small, stable public method any external mod can call, requiring no Scribe-specific knowledge
  of the document model beyond title/body/extra-info.
- Reuse existing mechanisms (icon-visibility split, target resolution, front-door/core-engine
  shape) rather than inventing parallel ones.
- Leave `ScribeAssignment` and its state machine completely untouched.

**Non-Goals:**
- Bulk/multi-task import for an external caller (deferred; see proposal.md).
- Any link-handling beyond plain text inside `bodyText`.
- A way for Scribe to reference the calling mod's own UI.

## Decisions

**One method, optional trailing parameters, no `TryImportTasks` for now.**
Earlier drafts considered two or three methods (a bare-bones creator, a "with provenance" variant,
and a bulk-JSON importer). Once the hover-detail field became a single opaque string gated purely on
"is it non-empty," the bare-bones/provenance split stopped doing any useful work — `extraInfo` is just
another optional parameter. The bulk importer is cut entirely for now: it would need a server-side
counterpart to `ScribeImportValidator` (today client-only) that doesn't exist, and no caller has asked
for more than one task yet. Result: `TryCreateExternalTask(player, title, bodyText, extraInfo)` is the
whole public surface.

**`ExtraInfo` is a new, independent field — not a reuse of `ScribeAssignment`.**
The original candidate design considered piggybacking on the existing assignment stamp icon
(`ScribeAssignedTaskIcon`/`ScribeAssignment`), since its rendering, pin-snapshot, and Pin-Tab-only
visibility were already built. Rejected: `ScribeAssignment.AssignerUid`/`TargetPlayerUid` are real
player UIDs (`ScribeAssignmentStore.TryCreateCore` rejects blank ones), so an external mod's identity
(a mod name, not a player) has nowhere to go without misusing that field — and a self-assignment
(the only legal way to construct one without a second real player) loses the caller's `sourceLabel`
entirely, defeating the point. Reusing it would also risk the assignment lifecycle machinery
(decline/cancel/redirect/batch grouping) ever touching a record that was never a real assignment.
Instead, `ExtraInfo` is a plain nullable string on `ScribeBlock`, following the exact shape already
used for `LinkDescription` (nullable, opaque to Core, one codec version bump, never re-derived).

**The new hover icon is Pin-Tab/Read/Editor-visible, HUD-suppressed — because that's already how the assignment icon works, not a new mechanism.**
Verified directly: `HudScribePins.cs` builds its own widget tree and never references
`IsAcceptedAssignment`/`AssignerUid`/etc.; only `ScribePinnedContent`/`ScribePinRow` (feeding the Pin
Tab) and the Read/Editor row builders do. Both surfaces already read from the same
`ScribePinnedRef` snapshot — the HUD's own code simply chooses not to render those fields. `ExtraInfo`
follows the identical shape: one field, one pin-codec bump, rendered only by the Pin-Tab/Read/Editor
row builders, never referenced by `HudScribePins.cs`.

**`ExtraInfo` attaches to whichever block is at Depth 0, regardless of kind.**
Verified the Read-row mapping (`ScribeDialogBase.Layout.cs`) already computes assignment-icon fields
for every block kind, including `Text` — it is not gated to `Task`. So a promoted Depth-0 `Text`
block (the title-absent case) can carry `ExtraInfo` and render the icon with no additional gating
logic needed.

**Target resolution is silent (last-opened, else first-writeable, else fail) — not a picker.**
Two existing precedents disagree: Assignment Accept always shows a picker when 2+ candidates exist
(deliberately, after removing silent selection — 2026-08-31 triage found it a bad surprise); the
Handbook "Add to Scribe" flow resolves silently with no picker at all. This design follows the
Handbook precedent: an external-mod add is a lightweight, easily-undone action like a Handbook add,
not a consequential accept like a player-to-player assignment. It is also the only option available —
this call has no client leg at all (the caller is server-side), so there is no UI to show a picker in
even if we wanted one.

**Build the server-side last-opened tracker now, piggybacked on an existing message.**
`lastOpenedScribeItemDocId` exists today only on the client. Rather than add a new network message,
the server-side equivalent (`Dictionary<string playerUid, Guid> lastOpenedDocIdByPlayer` on the
server's `ScribeModSystem`) is populated inside the *existing* `OnServerReceivedNotebookOpened`
handler — already invoked by all three Scribe item types on dialog-open, already carrying
`(IServerPlayer fromPlayer, docId)`, today used only to record pickup history. In-memory, per-session,
not save-persisted, mirroring the client-side field's own lifetime. The existing handler's own comment
notes the DocId is "only a loose hint" for a freshly-picked-up item with no synced document yet — that
is acceptable here, since a stale/wrong entry only affects a convenience default and falls back to
first-writeable exactly like the Handbook flow already tolerates for the same reason.

**Failure notification is a new, minimal server→client path — the method's `bool` is caller-only.**
Every existing Scribe error surface (`TriggerIngameError`) requires `ICoreClientAPI` and is only
reachable because those failures are detected client-side before any server round trip. This call has
no client leg, so Scribe needs its own way to tell the target player why their task wasn't created.
Kept minimal: a direct server→client notice to that one player (not a new dialog, not a bespoke UI
element), distinguishing the three known cases (no item / all locked / target full). The calling mod
does not need to build its own failure UI as a result.

## Risks / Trade-offs

- **[Risk]** A caller puts markup (HTML/VTML/etc.) into `extraInfo` expecting it to render.
  → **Mitigation**: document the contract plainly (plain text only, shown verbatim) as part of the
  public method's doc comment — this is the one place external developers will actually read.
- **[Risk]** The pin-codec version bump for `ExtraInfo` is one more forward/backward-compatibility
  seam to keep correct forever. → **Mitigation**: follow the exact existing additive-bump convention
  (new field, new version number, never reshuffle) already used for `LinkDescription` (v11) and
  `IsAcceptedAssignment` (v6) — a well-worn path in this codebase, not a new risk class.
- **[Risk]** Once external mods depend on `TryCreateExternalTask`'s signature, changing it becomes a
  breaking change for someone else's mod. → **Mitigation**: keep the signature intentionally small
  now; grow it later only via new trailing optional parameters, mirroring
  `ScribeAssignmentStore.TryCreate`'s own established growth pattern rather than reshuffling existing
  ones.
- **[Risk]** The last-opened tracker's DocId hint can be stale (item traded away, dropped, etc.).
  → **Mitigation**: already handled by design — resolution re-validates the item is currently carried
  and writeable before using it, falling back otherwise; same defensive check the Handbook flow
  already performs.

## Migration Plan

Purely additive; no destructive migration:
- New `ScribeBlock.ExtraInfo` field defaults to `null` — existing documents and saves are unaffected.
- Pin codec version bump is additive-only (older pin blobs parse unchanged; the new field defaults
  to absent).
- The last-opened-per-player tracker is new in-memory server state with no persisted shape — nothing
  to migrate, and it is safely absent/empty on first boot after this ships.
- Rollback is a plain revert: no on-disk data becomes invalid if this change is reverted after
  shipping, since nothing it introduces is required by any other feature.

## Open Questions

- Exact visual asset for the new hover-info icon (a new glyph/texture, distinct from the assignment
  stamp) — a content/art choice, not a behavior question.
- Exact wording of the three failure-notice strings — copy, not design.
