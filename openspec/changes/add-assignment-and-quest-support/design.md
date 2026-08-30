## Context

Two reservations already exist in the codebase for this work: `ScribeBlock.AssignedToUid`
(`src/Core/ScribeBlock.cs:85-87`, "Reserved for a future assignment capability... Unset by
default") and a comment on `BlockEntityScriptorium.cs:17` earmarking the Scriptorium for a
"v1.3 assignment system... Scriptorium-only Assign & History / Inbox nav buttons." This design
supersedes the latter: the dedicated Assignment Desk block, not the Scriptorium, becomes the
create/send surface (see proposal's Modified Capabilities). `ScribeDocumentJsonCodec.cs:27-28`
already documents "assignment is place-bound, not shareable" and never imports it — that
invariant carries forward unchanged.

Three existing mechanisms turn out to be exactly what this change needs, discovered while
grounding the design against the current code rather than assumed from scratch:

- **`ScribeLecternView`** (`ScribeDialogBase.cs:88`) is a single private enum — `{ Read, Editor,
  Pinned, Visitors, History, Timer, Inventory }` — shared by every Scribe dialog (Lectern,
  Scriptorium, Chalkboard, Notebook, Clockmaker's Notebook). Each surface has grown its own
  view(s) onto this same enum over time (Visitors for the Guestbook, Timer for gearworks tuning,
  Inventory for Scriptorium's item slots). There is no separate "tab framework" to build —
  Assignment and Inbox are two more members on this enum.
- **`GetExtraNavButtons()`** (`ScribeDialogBase.cs:451`, `protected virtual`) is the exact
  extension point each surface already uses to add its own nav buttons
  (`GuiDialogScribeLecternLibGui.cs:26`, `GuiDialogScribeScriptorium.cs:118`,
  `GuiDialogScribeChalkboard.cs:116`, `GuiDialogScribeNotebook.cs:97`). The Inbox nav button on
  Lectern/Scriptorium/Chalkboard is an addition to this existing override, not new plumbing.
- **`ScribeReadContent.cs`** already has a per-row `ReadOnly` flag on `ScribeReadRowData`
  (`ScribeReadContent.cs:79,134`) *decoupled* from a `CompletionAndPinLive` flag
  (`ScribeReadContent.cs:137-139`) — "keep the checkbox and hover pin INTERACTIVE even though
  ReadOnly is true." This is precisely the shape an accepted assigned task needs (frozen text,
  live everything else) and already exists for the Read view. The Editor view's
  `ScribeEditRowData` has no equivalent field yet — `ScribeFrozenEditorRow` currently exists only
  as a transitional animation ghost, not a permanent per-row state — so this is the one place a
  genuinely new (small) mechanism is needed, mirroring a proven pattern rather than inventing one.
- **`ActiveHandHoldsAnyScribeDocumentItem()`** (`ScribeDialogBase.cs:71`) already answers "is the
  player currently holding some Scribe document item, regardless of which" — exactly the check
  Accept-time placement resolution needs for its "currently held" step.
- **`IScribeDocumentHost.GetLayout(pixelArtSize)`** (`IScribeDocumentHost.cs:87`) returns a
  `ScribeLayout(W, AspectH, Props)` per host type (`BlockEntityScribeWritingStation.cs:169`,
  `NotebookHost.cs:93`) — each surface already supplies its own aspect ratio/dimensions this way.
  Assignment Desk and Inbox are two more hosts implementing this interface, not a new layout
  mechanism.

On the quest side: VS Quest (MIT) has no completion/accept event, so any live signal requires
either polling `QuestSystem.getPlayerQuests()` or reflecting into its own client-side dialog.
Tallybook (a separate, unrelated mod, decompiled for reference) proves the reflection route
works today: `VsQuests.ReadQuestDialog()` finds the open `VsQuest.QuestSelectGui` by type name,
reads its private `questGiverId`/`activeQuests` fields via `AccessTools.Field`, and further reads
each active quest's `killTrackers`/`blockPlaceTrackers`/`blockBreakTrackers[].count` properties —
confirming progress-counter mirroring is reachable via the same technique. Gather objectives have
no such counter (vsquest scans inventory on demand instead), so that is a permanent, documented
gap rather than a bug.

## Goals / Non-Goals

**Goals:**
- One Inbox pipeline serving both player-to-player Assignment and Quest-linking, built on the
  existing `ScribeLecternView` / `GetExtraNavButtons` / per-row `ReadOnly` mechanisms rather than
  parallel new ones.
- A truthful, append-only assignment state machine with distinct Assigner/Assignee permissions.
- Quest support that never requires a hard dependency, never writes into vsquest, and is
  invisible to players who don't have vsquest installed.

**Non-Goals:**
- Alegacy Quest Framework integration (design-inspiration only; its license gates code
  adaptation behind Discord contact — out of scope for this change).
- Mirroring gather-objective quest progress (vsquest has no incremental counter for it).
- Any migration of live `AssignedToUid` data (the field is unset in every shipped save today).
- A generic, reusable N-tab framework — two more `ScribeLecternView` members is the right scope,
  not a refactor of Read/Editor/Pinned/etc. onto a new abstraction.

## Decisions

### 1. View integration: extend `ScribeLecternView`, don't build a new tab system
Add `Assignment` and `Inbox` to the existing enum. Only the Assignment Desk dialog ever sets
`viewMode` to `Assignment`; Assignment Desk, the Inbox block, and Lectern/Scriptorium/Chalkboard
(via their `GetExtraNavButtons()` override) can all set it to `Inbox`. Each of those three
existing surfaces' nav-button override gains one more `ScribeRowButton` (matching the existing
`IsVisitorsView`/`IsHistoryView`-style active-color pattern) wired to switch into `Inbox`.
**Alternative considered**: a new standalone tab-container widget generalizing Read/Editor/Pinned
too. Rejected — no other surface needs that generality right now, and retrofitting four
established views onto a new abstraction is a much larger, riskier change than adding two enum
members to a mechanism already designed for exactly this kind of growth.

### 2. Data model: `ScribeAssignment` replaces bare `AssignedToUid`
`src/Core/ScribeBlock.cs` gains a `ScribeAssignment?` (or equivalent value type) carrying:
assigner UID, current `ScribeAssignmentState`, the in-game assigned-date, and the seen/unseen
flag (see Decision 4). `ScribeAssignmentState` and the transition-validation logic are new Core
types — game-agnostic, unit-testable without a game install, matching the project's
`Core`-must-not-reference-the-VS-API discipline. The bare string `AssignedToUid` is removed
entirely rather than kept alongside the richer type, since it was never populated in a shipped
save (no dual-write/migration path needed).

### 3. State machine

| From state | Assignee can → | Assigner can → |
|---|---|---|
| **Unaccepted** | Accept → *Accepted* · Decline → *Declined* | Cancel → *Cancelled* |
| **Accepted** | Discard → *Discarded* · (checking off the task → auto *Completed*) | — (cancel window closed) |
| **Declined** | terminal | terminal |
| **Cancelled** | terminal | terminal |
| **Discarded** | terminal | terminal |
| **Completed** | terminal | terminal |

- **Completed is derived, never a manual transition**: it mirrors the underlying task's own
  done-flag. One source of truth; no button ever offers "Complete" as a state change.
- **Cancel is pre-acceptance only.** Once Accepted, only the Assignee's Discard can end it.
- **Every terminal state is hard-terminal** — a retry is a brand-new assignment record, never a
  revived old one (append-only history, "locked-on-send" ethos carried over from the original
  design memory).
- **Delete on an accepted task performs Discard**, not a bare local removal — the normal Delete
  affordance, applied to an assigned+accepted task, transitions it to *Discarded* so the
  Assigner's read-only record never silently desyncs from what the Assignee actually did.
- **The Assigner keeps read-only visibility past Unaccepted** — their Assignment tab keeps
  showing the task's current state (Accepted, later auto-Completed) as a record of what they
  sent; only the action buttons disappear once it's out of their hands.
- Validation of legal transitions (which actor, from which state) lives in `src/Core/` as pure
  logic, so the matrix above is unit-testable independent of any GUI wiring.

### 4. New (unseen) is a flag, not a state
`Unaccepted` carries a `Seen` bool rather than New being a separate `ScribeAssignmentState` value.
Opening the Inbox flips it server-side (unless the Assignee immediately acts, in which case the
state moves on anyway). This flag drives the particle indicator and row highlight; it does not
change which transitions are legal — Unaccepted's rules apply whether `Seen` is true or false.

### 5. Row rendering: extend the editor path to match the read path
`ScribeEditRowData` gains the same `ReadOnly` + `CompletionAndPinLive` pair `ScribeReadRowData`
already has (`ScribeReadContent.cs:79,134,137-139`). An accepted assigned task's row renders its
text via the same static/frozen widget `ScribeFrozenEditorRow` already uses for its ghost state
(now also used as a *persistent* render, not just an animation transient) while its
checkbox/pin/tracker-counter/delete/reorder controls stay wired normally — Delete performing
Discard per Decision 3. Inbox-specific fields (assigner name, in-game date, state chip) are new
small static-helper widgets following the existing `ScribeTrackerCounterText`-style pattern
(shared by whichever views need them), not a divergent one-off.

### 5b. Filter/picker widget: filter-chip row
The Inbox tab's multi-select state filter (Decision/requirement already locked: "one or more of
the six states") is a row of toggleable filter chips — one pill per state, active ones
highlighted — always visible above the row list, reusing `ScribeRowButton`-style chrome.
**Alternatives considered**: a dropdown-with-checkboxes is more compact but hides the active
filter until opened; tabs-per-state (Tallybook's approach) suits browsing distinct content
categories, not multi-selecting values of one field, and would stack a third tab layer on top of
the Assignment/Inbox tabs already above it. A chip row keeps the active filter visible at a
glance with no extra click.

### 6. Inline-expand, chevron-only, leading edge
Each Inbox row is a `StatefulWidget` with a per-row `expanded` bool (matching Tallybook's proven
pattern, confirmed against Scribe's own Flutter-style `StatefulWidget`/`State` architecture).
Collapsed: checkbox/tracker + text + depth-indent + a compact state chip. Expanded: adds assigner
name, in-game assigned-date, and the legal state-change button(s) for the viewing player. The
toggle is a chevron disclosure triangle (▸/▾) built from `ScribeRowButton` chrome, placed at the
row's leading edge (before the checkbox, matching the depth-indent tree convention) and is the
**only** expand/collapse trigger — clicking text or other row controls never toggles it, keeping
every existing hit-target (checkbox, tracker stepper, pin, delete) unambiguous.

### 7. Accepted-task placement resolution
On Accept: (1) check `ActiveHandHoldsAnyScribeDocumentItem()`-style presence on the currently
active hand slot — opening a block GUI doesn't clear the hotbar selection, so "most recently
held" is simply "currently held," no new persisted tracking needed; (2) if absent, scan inventory
for eligible Scribe document items — if more than one is found, show a small picker so the player
chooses; (3) if none exist anywhere, the Accept button is **disabled** (not clickable-then-error)
with an explanatory tooltip, computed from the same inventory state the server already has.

### 8. Dimensions via `IScribeDocumentHost`
Assignment Desk's and Inbox's block entities each implement `IScribeDocumentHost.GetLayout` and
supply their own `ScribeLayout(W, AspectH, Props)`, following the exact pattern
`BlockEntityScribeWritingStation`/`NotebookHost` already use — no new dimension mechanism.
**Decided**: both hosts use `W = PixelArtSize`, `AspectH = 1.2` (a bounding box 1.2× taller than
wide) as a placeholder ahead of final art. Within that box, each tab's own content region (the
Assignment tab's create/send form, the Inbox tab's row list) renders as a 1:1 square — the
remaining ~0.2×W of vertical space is the title bar plus the Assignment/Inbox tab-switcher nav
row above the square content area. This is a real, implementable layout now (not blocked on art)
— final art can restyle the frame around this box later without changing the ratio logic.

### 9. Particle indicator
A new ambient particle emitter, scoped to Inbox-capable block entities (Assignment Desk, Inbox,
and Lectern/Scriptorium/Chalkboard), checked on a tick interval mirroring the existing
`ScribeAmbientLightSampler`'s periodic-sample precedent rather than a per-frame check. Client-side
only: each client evaluates "does the local player have an unseen assignment associated with a
nearby Inbox-capable block" and spawns particles locally — no server broadcast to other players'
clients, and no visibility into other players' unseen assignments.

**Mechanism, confirmed against two reference mods**: Tallybook (decompiled) has no particle
code at all — its only "ready" indicator is an entity outline/glow tint on NPCs
(`QuestReadyGlow`), which doesn't apply to blocks and isn't a candidate here. Particles Plus
(`reference/QuestsInvestigations/particlesplus-2.5.8.zip`, decompiled) achieves its per-block
ambient particles by assigning `CollectibleObject.ParticleProperties` and letting the engine's
own always-on ambient system handle spawning — elegant, but unconditional: every player near the
block sees it, with no way to make it player-specific. Since this indicator must be visible only
to the one player with an unseen assignment, that mechanism is ruled out; a manually-triggered
client-side spawn (`IWorldAccessor.SpawnParticles`/`SimpleParticleProperties`, gated by the
per-player unseen check on the sampler tick) is required instead. Particles Plus's particle
*definitions* are still a useful source of realistic starting field values, since they are JSON
wrappers around the same native `AdvancedParticleProperties` fields Scribe would set directly:
HSV color as mean±variance, velocity, gravity, life length, quantity, and size.

**Starting values** (playtest-tunable, not final): HSV (0–255 scale, matching VS's own range)
around H≈32/S≈200/V≈250 for the base warm gold/amber tone consistent with Scribe's ink-and-
parchment palette, alpha ≈180±40; slight negative gravity so particles float rather than fall or
drip; low spawn quantity (sparse motes, not a fountain); ~1.5–2.5s life length. Reads as a soft
"sparkle/attention" cue rather than an environmental ambient effect (contrast with Particles
Plus's own drip/smoke-style presets, which lean environmental).

**Multicolor accent (decided)**: most spawned particles keep the amber/gold hue band above; a
smaller subset — starting ratio ~1-in-5 particles, tunable — spawn with a randomized full-range
hue (H rolled across the entire 0–255 range instead of the narrow amber band) so an occasional
rainbow-colored mote sparkles among the mostly-gold field, rather than either a uniform single
color or a fully randomized rainbow effect. Same motion/life/size/gravity values as the base
particle; only hue is re-rolled per-instance.

### 9b. Inbox nav-button shimmer when the tab isn't already showing
The world-space particle (Decision 9) only helps a player who is already near a block; it does
nothing for a player already standing at a Lectern/Scriptorium/Chalkboard/Assignment Desk whose
Inbox tab isn't the currently active view — the block-level particle is easy to miss while
looking straight at the open dialog. Add a periodic shimmer sweep across the Inbox nav button's
icon itself, playing whenever the viewing player has an unseen assignment AND the Inbox tab is
not the dialog's current view (so it never plays redundantly while Inbox is already open, and it
naturally covers all four surfaces that have a *non-default* Inbox button — the standalone Inbox
block needs no shimmer since it has no other tab to sit on).

**Mechanism**: matches the Flutter shimmer-loading cookbook pattern
(docs.flutter.dev/cookbook/effects/shimmer-loading) almost exactly, and every primitive it needs
is already in the shipped `gui` 3.1.0 `Gui.dll` (confirmed by decompile, not just the version-
skewed 2.0.0 clone): `Gui.Widgets.Painting.ShaderMask` (wraps a child widget, applies an
`SKShader` with a blend mode — `ShaderMask.cs`), `Gui.Widgets.Painting.LinearGradient`, and
`Gui.Widgets.Animations.GradientTween`/`AnimatedContainer` for driving the sweep on a loop. Wrap
the Inbox `ScribeRowButton`'s icon in a `ShaderMask` whose linear-gradient highlight band offset
animates across the button's bounds on a repeating tween while the trigger condition holds, and
stops (renders the button plain) once the condition clears — reuse whichever
looping/continuous-tick animation-driving pattern the existing row-size/list animations already
use (`ScribeRowSizeAnimation.cs`, `ScribeAnimatedList.cs`) rather than inventing a new ticker.

**Alternatives considered**: a pulsing opacity/glow (simpler, but less distinctive and easy to
confuse with hover/press feedback the button already has) or a color-shift (conflicts with the
existing `ActiveColor` mechanism `ScribeRowButton` already uses to mark the *currently selected*
tab — a shimmer sweep doesn't compete with that visual language the way a recolor would).

### 10. Quest integration
- **Quest Link** is a new `LinkTarget` namespace (e.g. a `"quest:"` prefix) parallel to the
  existing `"page:"` guide-page prefix in `link-task`, resolved by reading the static
  `config/quests/*.json` asset catalog only. The Link picker's "Quest Link" option is gated on
  `IsModEnabled("vsquest")` at render time (no new gating mechanism — same check used everywhere
  else vsquest visibility matters in this change).
- **Soft auto-detect** is a Harmony patch/reflection layer scoped narrowly to
  `VsQuest.QuestSelectGui` (found by type name, exactly as Tallybook's `FindQuestDialog()` does),
  reading `questGiverId`/`activeQuests` and the three tracker-count properties via
  `AccessTools.Field`/`AccessTools.Property`. Wrapped in try/catch that silently disables itself
  on any reflection failure (mirrors Tallybook's own `TrackVsQuests = false` self-disable on
  error) — Layer 1 (manual Quest Link) is entirely unaffected if Layer 2 breaks.
- `ScribeQuestAcceptPolicy` / `ScribeQuestCompletionPolicy` are two new Core enums
  (`Always`/`Never`/`Prompt`, default `Prompt`) following the existing `ScribeCompletionPolicy`
  pattern exactly: per-player, client-local, carried in a network message, normalized/applied
  server-side.
- Orphan handling: a Quest Link's name/description text is captured at creation time and never
  re-derived from the live catalog on render — so if vsquest is later uninstalled, the Link still
  renders correctly from its own stored text, just without further auto-detect enrichment.

## Risks / Trade-offs

- **[Risk]** Harmony-reflecting into vsquest's private fields is fragile to any future vsquest
  version renaming them. → **[Mitigation]** Same defensive shape Tallybook already ships:
  try/catch around every reflective read, self-disable on failure, Layer 1 (manual Quest Link,
  zero reflection) keeps working regardless.
- **[Risk]** Adding two members to the shared `ScribeLecternView` enum touches every existing
  surface's view-mode switch statements (Lectern, Scriptorium, Chalkboard, Notebook, Clockmaker's
  Notebook). → **[Mitigation]** C#'s exhaustive-switch warnings surface any unhandled case at
  compile time; only Assignment Desk ever assigns the new `Assignment` value, and only
  Assignment-Desk/Inbox/the three nav-button surfaces ever assign `Inbox` — every other surface's
  switch just needs a no-op default, the same shape existing surfaces already use for view values
  that don't apply to them (e.g. Notebook already ignores `Inventory`).
- **[Risk]** Replacing `AssignedToUid` with a richer type is a wire/save-shape break. →
  **[Mitigation]** The field is unset in every shipped save (reserved, never populated) — this is
  a same-cycle type replacement, not a live-data migration; no codec compat window needed.
- **[Risk]** Particle checks across every Inbox-capable block instance placed in a loaded area
  could add up if a player builds many. → **[Mitigation]** Tick-interval-gated (matching
  `ScribeAmbientLightSampler`'s periodic-sample cadence, not per-frame) and range-gated; purely
  client-side and player-local, never a server-side per-block-per-player computation.
- **[Risk]** Gather-objective quest progress cannot be mirrored (no incremental counter exists in
  vsquest). → **[Mitigation]** Documented as a permanent, known gap rather than something to
  work around — accept-state still mirrors correctly for gather quests, just not a live count.

## Open Questions

- ~~Exact `ScribeLayout` aspect ratio / dimensions~~ — resolved: `W = PixelArtSize`,
  `AspectH = 1.2`, 1:1 square tab content within it (Decision 8). Final art can restyle the frame
  without changing this ratio logic.
- ~~Exact filter/picker widget~~ — resolved: filter-chip row (Decision 5b).
- Exact particle spawn-rate/size/max-concurrent tuning, the rainbow-accent ratio (~1-in-5
  starting point), and the shimmer sweep's period/width/opacity (Decisions 9/9b) — mechanisms and
  starting values are decided; only the numeric constants remain playtest-tunable, same as the
  `.geartune` knobs elsewhere in the mod.
