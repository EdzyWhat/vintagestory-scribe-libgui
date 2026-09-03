## Context

The Assignment system (`src/Core/ScribeAssignment.cs`, `ScribeAssignmentTransitions`) is fully
shipped: a six-state, player-trusting state machine with no partial-completion telemetry, sent
from the Assignment Desk's Create Assignments tab, accepted/declined via the shared Inbox tab on
the Desk, Lectern, Scriptorium, Chalkboard, or standalone Inbox block. All sync is instant and
server-authoritative — there is no concept of distance anywhere in the flow today.

This is fine for players sharing a base, but on RP/faction servers spanning long distances it
reads as unlimited-range instant communication, which several popular RP mods (Envelopes,
Noticeboard) deliberately avoid by making mail a real, carried, lossable item. This change adds
that option as a second delivery path, gated by server policy, without disturbing the shipped
in-range flow. It followed an extended design-exploration (see project memory
`assignment-postal-delivery-mode-exploration`) that researched those mods directly and converged
on the shape below through direct back-and-forth with the author.

## Goals / Non-Goals

**Goals:**
- Give server admins a policy lever (`DeliveryMode`) between always-instant, always-physical, and
  an automatic hybrid.
- In hybrid mode, make the physical/instant choice a player-visible, player-overridable toggle —
  not a silent, unexplained restriction.
- Reuse every existing mechanism that already does the job: `IScribeDocumentItem` for the notice's
  document payload, the Notebook/Tablet held-item-right-click convention for opening it, the
  existing `AcceptedIntoLabel` placement mechanism for what happens after Accept, the existing
  ambient particle/badge for discovery, the existing `OnStormTick` heartbeat idiom for the
  proximity scan.
- Keep the in-range path (today's shipped behavior) completely unchanged and unaffected in
  `Hybrid` mode.

**Non-Goals:**
- A dedicated Scribe Mailbox block (drop-off/pickup inventory tab). Parked as a follow-on change;
  hand-carry (or a third-party mod's own mailbox, e.g. Messenger Pigeons) is the only transport for
  this change.
- Group/faction assignment targets, including an "Anyone" target. Unrelated fork, parked
  separately.
- A crafting-grid merge of two same-recipient notices into one. Mechanism verified feasible
  (`CollectibleObject.OnCreatedByCrafting`, matching the `BlockPie.OnCreatedByCrafting` precedent)
  but not committed to this change; listed as an optional stretch task.
- Any return-leg physical item. Complete/Discard already sync instantly today regardless of
  distance via `OnPlayerNowPlaying` → `PushAssignmentsTo`, and that continues unchanged once an
  assignment is Accepted — this change only ever adds friction to the outbound
  Assign→Accept leg.
- Envelopes-mod wrap compatibility. Unverified; the Task Notice is a plain `ItemStack` and doesn't
  depend on it either way.

## Decisions

**`DeliveryMode` is a three-value server setting, not a boolean.** `AlwaysInstant` / `AlwaysPhysical`
/ `Hybrid`. Alternative considered: a single "enable physical mode" boolean plus always showing the
toggle. Rejected because it forces every server to deal with the toggle's added UI even if they
never want physical delivery at all (most servers, especially small/co-op ones) — the three-value
setting lets `AlwaysInstant` servers see zero change to the Create Assignments tab.

**The range check runs once, at Assign time, and is never re-evaluated.** Alternative considered: a
live/continuous check (e.g. re-verify distance right before Accept). Rejected — it would require
new tick/sweep infrastructure and would make an assignment's delivery path unpredictable after the
fact. The accepted asymmetry (an in-range pair who later drift apart keeps syncing like today) is
intentional: this solves "can't ping a stranger across the kingdom," not "two people who already
had a link might later separate."

**The Hybrid toggle is symmetric — a computed default, never a hard gate.** Alternative considered
(and originally recommended, then reversed by the author): disable/gray out "Local Inboxes" when
the target is out-of-range, to prevent the toggle from defeating the range check's purpose.
Reversed because real cases exist where the assigner already knows, via out-of-band coordination
(Discord/RP chat), that forcing instant delivery is fine — e.g. the target is temporarily away but
returning soon, or the task doesn't require the assigner to have reached them at all. Making the
override symmetric (either direction, no confirmation) also simplifies the UI-state matrix versus a
one-directional escape hatch, and is consistent with the state machine's existing player-trusting
design stance (no telemetry, no enforcement beyond what the mechanics naturally provide).

**No inline override warning; an info (ⓘ) button is the only explanation surface.** Alternative
considered: a contextual note when the player picks the toggle position against the computed
default (e.g. "this target is currently far away"). Dropped deliberately to keep the number of UI
states small and testable — assigning players in practice already know their target's real
situation through out-of-band chat, so the added state didn't pay for itself.

**Toggle labels name the mechanism, not a distance claim: "Local Inboxes" / "Send a Notice."**
"Local Inboxes" describes what's actually true regardless of the toggle position chosen — the
resulting record is a normal `ScribeAssignmentStore` entry, Accept/Decline-able from *any*
Scriptorium/Lectern/Chalkboard/standalone-Inbox anywhere on the server, with zero physical
footprint. Calling it "Instant" was rejected because the symmetric override can put it in play for
an out-of-range target too, where "instant" as a speed claim would read as contradictory.

**Task Notice acceptance reuses the Notebook/Tablet held-item-right-click convention verbatim — no
new interaction to design.** The document dialog opens exactly as it does for those items, rendered
locked/read-only (edit controls suppressed, per the existing `IScribeDocumentItem` read-only
pattern), with two explicit buttons added: Accept and Decline.

**An unaccepted Task Notice has no `ScribeAssignmentStore` record at all — true embodiment, not
notification-gating.** The item itself is the sole record until Accept. Consequences, all treated
as intentional rather than gaps: there is no digital Cancel for an unaccepted notice (physically
retrieving or destroying it is the equivalent action, matching Scribe's existing "lose the item,
lose the content" document philosophy); Decline consumes the item with no record created and no
notification sent back to the Assigner; the Assigner's Sent Assignment History shows nothing for an
unaccepted notice (their confirmation that the send worked is the sealed notice itself, sitting in
the Create Assignments tab's output slot until they take it — mirroring a quern's flour-output
slot, never auto-inserted into inventory).

**Accepting converts the notice into a normal tracked assignment, entering the state machine
already at Accepted.** This reuses the existing `AcceptedIntoLabel` bind-to-first-legal-item
placement mechanism unchanged. From that point forward a notice-originated assignment is
indistinguishable from an in-range one — same Complete/Discard sync, same read-only history view.

**Task Notice recipe: knife + parchment + reed → 8.** Reed (not dry grass) reads as "wrapping the
cutting into a scroll," matching the reused scroll placeholder model. Chosen deliberately cheap —
the transit friction (paper-tier, one-notice-per-send, no batching beyond what a single send's
`BatchId` already covers) is the intended soft cap on frequent remote delegation, not scarcity of
the item itself.

**Proximity discovery is a generic at-rest scan, not a mailbox-specific feature.** Reuses the
`OnStormTick` heartbeat idiom: for the small side-list of online players with an outstanding
physical notice, a ~10-15 block scan via `IWorldChunk.BlockEntities` (sparse dict) and
`GetEntitiesAround` (dropped items), gated by a chunk-boundary movement check so it's free for
stationary players. Works identically whether the notice is dropped, sitting in a plain chest, or
(later) a purpose-built Mailbox block — no code coupling to any specific container type.

## Risks / Trade-offs

- **[Risk]** Server admins may not understand what `Hybrid` mode actually restricts, given the
  symmetric override makes the range check advisory rather than enforced. → **Mitigation**: the
  info (ⓘ) button's longer explanation is the primary teaching surface; `AlwaysPhysical` remains
  available for admins who want a hard requirement instead.
- **[Risk]** An Assigner who sends a notice that gets Declined has no way to find out short of
  asking out-of-band. → **Mitigation**: accepted as consistent with the "true embodiment" fiction
  (an unanswered physical letter is exactly this ambiguous in real life); flagged explicitly here
  as an intentional design choice rather than an oversight, so it isn't "fixed" later without
  revisiting this reasoning.
- **[Risk]** Last-known-position, captured only on logout, will be stale/wrong if a player's client
  crashes or the server crashes without a clean disconnect. → **Mitigation**: low-stakes failure
  mode — a stale position only affects which toggle position is pre-selected, which the player can
  always override anyway.
- **[Trade-off]** Choosing not to gate the Task Notice recipe by `DeliveryMode` means the item stays
  craftable even on an `AlwaysInstant` server where it can never be used for its purpose. Judged
  acceptable — gating crafting by a server setting would be a new mechanism for no real benefit.

## Migration Plan

No data migration: this only adds new optional fields (last-known-position, `DeliveryMode` setting,
the Task Notice item type) alongside the existing, unchanged `ScribeAssignmentStore` schema.
Existing in-flight assignments are entirely unaffected — they were all created via the in-range
path and continue exactly as today. `DeliveryMode` defaults to `Hybrid` on upgrade; a server admin
who wants zero behavior change should set it to `AlwaysInstant` explicitly.

## Open Questions

- Whether the crafting-grid two-notice merge ships in this change or a later one — listed as an
  optional/stretch task, not required for apply-readiness.
- The exact longer-form copy for the info (ⓘ) button's explanation dialog — left to implementation,
  not a design-level decision.
