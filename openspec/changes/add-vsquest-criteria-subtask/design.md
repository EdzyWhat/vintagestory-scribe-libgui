## Context

`ScribeQuestCatalog.ReadCatalog` (`src/Mod/ScribeQuestCatalog.cs`) already parses vsquest's static
`config/quests/*.json` catalog once per dialog session, purely for the Quest Link picker and for
`ScribeQuestObjectiveDef` (kill/block-place/block-break, positionally zipped against a live
tracker read while `VsQuest.QuestSelectGui` happens to be open — see `quest-auto-detect`).
`RawQuest` today omits `gatherObjectives` entirely, since gather has no live counter anywhere in
vsquest (a documented permanent gap for *progress*, not for *knowing the target*).

`quest-objective-task` already defines the `QuestObjective` block kind and its
generate-once/reconcile-in-place lifecycle, built for Progression Framework: one child per
objective, real item icon when a single item resolves, generic icon + captured label otherwise,
excluded from carried-inventory tracking. `ScribeDialogBase.Editor.cs`'s `OnClickAddQuestLink`
already branches on `entry.Source == ScribeQuestSource.ProgressionFramework` to generate and then
progress-update these children; it has no VS Quest branch today.

Decompiling `VsQuest.QuestSelectGui` (confirmed against the local MIT source clone) shows vsquest
itself renders a quest's objective summary via a single per-quest lang key,
`Lang.Get(questId + "-obj", ...progressValues)`, where the positional args are the flat
concatenation `gatherProgress + trackerProgress (kill, place, break) + actionProgress` — i.e. the
arg count depends on the quest's `actionObjectives`, which Scribe does not model at all (a
separate, open-ended reflection-registry mechanism). Calling that lang key ourselves with a wrong
arg count risks a malformed/garbled string, not just a missing icon.

## Goals / Non-Goals

**Goals:**
- Show a one-time, non-live acceptance-criteria hint for VS Quest Quest Links, covering all four
  static objective types (kill, gather, block-place, block-break).
- Reuse `quest-objective-task`'s existing model/rendering unchanged for Progression Framework.

**Non-Goals:**
- Modeling vsquest's `actionObjectives` (arbitrary registered action checks with no fixed shape) —
  out of scope; a quest with only action objectives simply gets no generated children, same as a
  quest with none of the four modeled types today.
- Any live update of these children after creation (see proposal.md's Why — no signal exists).
- Making a VS Quest Quest Link's follow/activate action open anything (see proposal.md's Why —
  confirmed impossible; no code change follows from this, so no task tracks it either).

## Decisions

**D1 — One `QuestObjective` child per objective, built from catalog `validCodes`/`demand`, not
from vsquest's own `"{questId}-obj"` display lang key.**
Alternative considered: call `Lang.Get(questId + "-obj", ...)` with the target counts (or zeros)
as args, since it's the exact human-authored text vsquest's own GUI shows. Rejected: the arg count
must exactly match `gatherObjectives.Count + killObjectives.Count + blockPlaceObjectives.Count +
blockBreakObjectives.Count + actionObjectives.Count`, and Scribe cannot compute the last term (no
model of `actionObjectives`) — a mismatch either throws or silently leaves a literal `{n}` in the
rendered string for any quest that mixes action objectives with the four modeled types. Building
one child per *modeled* objective, independently, degrades safely: an unmodeled action objective
is simply absent from the subtask list rather than corrupting a shared string.

**D2 — Label resolution mirrors `quest-objective-task`'s existing single-item-vs-generic split.**
When an objective's `validCodes` resolves to exactly one concrete, non-wildcard item/block/entity
code, the child carries that code (real icon + name, identical to a Tracker row). Otherwise it
falls back to a generic icon and a `"{KindLabel} {demand}"` label reusing the SAME
`scribe:scribe-questobjective-kill`/`-blockplace`/`-blockbreak` lang keys `FormatProgress` already
uses for live PF/vsquest progress text, plus one new equivalent `scribe:scribe-questobjective-gather`
key. No new vsquest-specific lang content is required. Kill objectives resolve against the entity
registry, gather against the item registry (mirroring Tracker), and block-place/block-break against
the block registry — three different single-code lookups, each already a solved problem elsewhere
in the codebase; a lookup miss only ever degrades to the generic label, never a failure.

**D3 — Generation is one-shot, gated by source, and never reconciled again.**
`OnClickAddQuestLink` adds a VS Quest branch calling `ReconcileQuestObjectives(...,
createMissing: true)` once at creation, exactly like the Progression Framework branch's first call
— but with no follow-up `SetQuestObjectiveProgress` calls, since there is nothing to report.
`CurrentQuantity` is left at its default (0) on every generated child. This is intentionally
inert afterward: nothing else in the codebase ever calls `ReconcileQuestObjectives` for a
VS-Quest-sourced parent, so "no live updates" falls out of simply never wiring a call site, not a
new guard.

## Risks / Trade-offs

- **[Risk]** A `CurrentQuantity` of permanent 0 could visually read as "0 progress," which is
  literally true at creation time but may look broken once the player has actually progressed. →
  **Mitigation:** none attempted — this is the disclosed static-snapshot trade-off proposal.md
  accepts; a future change could hide the current-count portion specifically for VS-Quest-sourced
  children if this proves confusing in practice (not in scope here).
- **[Risk]** Entity/block single-code resolution (D2) may not have existing helper functions the
  way item resolution does (Tracker only ever resolves items). → **Mitigation:** low severity by
  construction — a resolution miss falls back to the already-required generic label, never blocks
  child creation or throws.

## Migration Plan

No data or format changes — reuses the existing `QuestObjective` block kind and codec support
added for Progression Framework. Purely additive behavior gated on `entry.Source ==
ScribeQuestSource.VsQuest`; Progression Framework's path is untouched. Rollback is a normal code
revert.
