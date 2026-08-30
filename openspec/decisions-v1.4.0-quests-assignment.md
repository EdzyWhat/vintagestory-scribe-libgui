# v1.4.0 decisions log — Quest support + Assignment system

Staging doc, not an OpenSpec artifact. Captures every decision made in the design
conversation preceding `/openspec-propose` so nothing is lost to context compaction mid-draft.
Source memory: [[v13-assignment-system-design]] (superseded/expanded by this doc).
Investigation source: `reference/QuestsInvestigations/quests.md` + the mod zips/clones
extracted alongside it.

Delete or fold into `design.md` once the actual OpenSpec change exists and this is redundant.

## Scope

One combined v1.4.0 OpenSpec change, not two. Rationale: the whole point is that Assignment's
Inbox pipeline and Quest-linking share a data model — designing the Inbox in isolation would
have to guess at needs the Quest side can't see yet.

---

## Capability 1: Quest framework support

### Research findings (grounding facts, not decisions)

- **VS Quest** (`github.com/G3rste/vsquest`, MIT license, most downloads): JSON-defined quests
  (`config/quests/*.json`), server-authoritative. `QuestSystem.getPlayerQuests()` is public but
  **no completion/accept event exists** — any live sync needs polling or Harmony-patching.
  Gather objectives are inventory-scanned on demand (auto-consume), the opposite of Scribe's
  no-telemetry stance. A soft dependency is possible via `IsModEnabled` but still needs a
  compile-time reference to `vsquest.dll` — same tier as the ConfigLib exception, not a free read.
- **Alegacy Quest Framework** (GitLab `DreadMob/Alegacy-Quest-Framework`): turns out to be a
  bespoke mega-framework for one specific server (hard-deps on their own `arcanumlib`, custom
  GUI, no cross-mod integration API). License permits reading source for design ideas freely;
  requires Discord contact before adapting/porting any actual code (Discord:
  `discord.gg/uvybdcAdV8`). Good for design inspiration (stage/branch quest model, pluggable
  objective-tracker registry) only — not an integration target.
- **Tallybook** (client-side quest/errand tracker, found via decompile, not originally in
  quests.md): achieves "zero compat patches" by (1) reading vsquest's quest JSON straight off
  the shared asset domain (no dependency needed — assets are globally readable) for the
  catalog, and (2) Harmony-reflecting into the open quest-dialog's private fields
  (`questGiverId`, `activeQuests` on `VsQuest.QuestSelectGui`) to scrape live accept/progress
  state while the dialog is on screen. `ReadActiveQuest` also reads `killTrackers` /
  `blockPlaceTrackers` / `blockBreakTrackers[].count` — confirms progress counters ARE reachable
  via the same reflection technique. **Gather objectives have no live counter** (inventory-scanned
  on demand, not tracked incrementally) — a real, permanent gap, not a bug to fix.
- Its `QuestReadyGlow` class is an **entity outline/glow tint** on quest-giver NPCs, not spawned
  particles — does not transfer to a block-based indicator.
- Murgle Quests / ForgottenQuests / VintageQuesting: minor. Murgle is just a vsquest content
  pack (hard-deps on vsquest, so `IsModEnabled("vsquest")` covers it transitively).
  ForgottenQuests is a bespoke Russian-server mod. VintageQuesting has a nice declarative
  reputation/requirements DSL worth citing as inspiration but no repo/API to integrate with.

### Decisions

1. **Two-layer model.**
   - **Layer 1 — Quest Link**: a Link-style task (same pattern as existing craftinginfo Links)
     referencing a quest by id. Reads only the static public `config/quests/*.json` catalog for
     name/description. Zero dependency, zero reflection — works even if Layer 2 fails entirely.
     Usable **anywhere** existing Link tasks work (Notebook/Tablet/Lectern/Scriptorium) — NOT
     Scriptorium-bound, since this is a personal reference, not a social/place-bound action.
   - **Layer 2 — soft auto-detect**: while vsquest's dialog is open, Harmony-reflects into its
     private fields to learn accept-state and mirror kill/place/break objective progress (not
     gather — see gap above). Silently no-ops if vsquest is absent or a future version renames
     those fields; Layer 1 is unaffected.
2. **Auto-detect targets VS Quest only** (MIT, known fields). Explicitly does NOT reflect into
   Alegacy Quest Framework, given its license's adaptation-contact clause — Alegacy quests still
   get manual Layer-1 Links, just never introspected.
3. **Mirrors progress counts**, not just accept-state (user's explicit choice, against the
   initial accept-state-only recommendation) — kill/place/break trackers via the same reflection
   as accept-state. Gather-objective progress is a documented, permanent gap.
4. **Read-only, always.** Nothing Scribe does ever writes back into vsquest.
5. **Two independent settings**, following the existing `ScribeCompletionPolicy` pattern
   (`src/Core/ScribeCompletionPolicy.cs` — per-player, client-local enum, carried in a network
   message, normalized/applied server-side):
   - `ScribeQuestAcceptPolicy` (Always / Never / Prompt, default **Prompt**): what happens when
     auto-detect sees a newly-active quest.
   - `ScribeQuestCompletionPolicy` (Always / Never / Prompt, default **Prompt**): what happens
     when auto-detect sees a linked quest complete in vsquest.
   - These are independent — a player can auto-accept but always confirm completion, etc.
6. **Fully hidden unless `IsModEnabled("vsquest")`** — same gating precedent as the ConfigLib
   soft dependency. Applies to: Settings dialog rows (Accept/Completion policy), handbook
   documentation, and the Link-picker's "Quest Link" option (wherever a player picks a Link
   subtype, e.g. the item handbook's "Add Link" button, New Task dropdown).
7. **Orphan-safe**: if vsquest is later uninstalled, existing Quest Links degrade to inert plain
   Links using their captured-at-creation-time name/description text. No error state, no
   breakage, just stops auto-refreshing/auto-detecting.

---

## Capability 2: Assignment system

### Research findings (grounding facts, not decisions)

- `src/Core/ScribeBlock.cs:85-87` already has a **reserved** `AssignedToUid` field ("Reserved
  for a future assignment capability... Unset by default"), and
  `src/Core/ScribeDocumentJsonCodec.cs:27-28` already documents "assignment is place-bound, not
  shareable" — never imported. This needs to grow into a richer assignment-state object, not
  just a bare UID.
- `src/Mod/BlockEntityScriptorium.cs:17` has a reserved comment: "v1.3 assignment system can
  attach the Scriptorium-only Assign & History / Inbox nav buttons" — superseded by this design
  (Assignment tab is Desk-only now, not Scriptorium).
- **No generic tab abstraction exists.** Read/Editor/PinTab are each a bespoke bool-flag mode on
  `ScribeDialogBase` (`ScribeDialogBase.ViewSwitching.cs`, `ScribeDialogBase.PinTab.cs`), switched
  via a "sidebar nav button" widget (`ScribeRowButton` with `ActiveColor` —
  `src/Mod/ScribeRowWidgets.cs:452-458`) that already exists and is the natural visual switcher
  for an Assignment/Inbox tab pair. A new bespoke mode flag on a new dialog, not a new generic
  tab framework, is the way to build this.
- Row-level primitives are already shared/reusable: `ScribeRowButton` (floating pin/delete),
  `ScribeTrackerCounterText` (N/N counter with satisfied/in-progress styling), `ScribeLinkIcon`
  (leading item/book icon). Inbox rows should reuse these directly rather than rebuilding them.
- Scribe's widget architecture is Flutter-style declarative (`StatefulWidget`/`State`), so a
  per-row `expanded` bool is a natural fit — unlike bolting the same idea onto native VS
  GuiComposer.
- **Tallybook UX mining** (`Tallybook.GuiDialogTallybook`, decompiled): uses native
  `AddHorizontalTabs` for its top-level sections (Items/Side quests/History/etc, each label
  showing a live count). **No detail modal anywhere** — every row (`PinRow`, `NodeRow`,
  `SiteRow`, all subclassing an abstract `Row`) carries a per-item `Expanded` bool; collapsed
  rows are a compact one-liner, expanding unfolds indented child rows in place. Filtering is
  tab-based (each tab IS a filtered view), not a separate search/dropdown control. Tooltips
  (`AddHoverText`) absorb explanatory text that would otherwise clutter the row.
  **Verdict: inline-expand-in-place, not modal, is the strongest pattern** — confirmed as the
  chosen direction below.

### Decisions

#### Blocks & surfaces
1. **Assignment Desk** (new block): 2 tabs, Assignment + Inbox. GUI dimensions/composition
   reform and reuse parts of the existing Lectern/Scriptorium LibGUI widget tree rather than
   building from scratch.
2. **Inbox** (new block): single-purpose, right-click opens the Inbox tab GUI only (no
   Assignment tab — can't create/send from here).
3. **Assignment tab (create + send) is Assignment-Desk-only.** Lectern/Scriptorium/Chalkboard
   never get a create affordance — this supersedes the old `BlockEntityScriptorium.cs:17`
   reservation.
4. **Inbox-viewing nav button** added to Lectern, Scriptorium, and Chalkboard: launches the same
   Inbox tab GUI that lives natively on Assignment Desk / Inbox block.
5. **Particle indicator scope: every Inbox-capable block** — Assignment Desk, standalone Inbox,
   AND Lectern/Scriptorium/Chalkboard all show an ambient particle effect when the viewing
   player has an unseen ("New") assignment and is in range. Real spawned particles (vanilla
   translocator-style), not an entity-glow trick (Tallybook's `QuestReadyGlow` technique doesn't
   apply to blocks).

#### State machine
6. **Full matrix** (Unaccepted carries the New/unseen distinction as a flag, not a separate
   persisted state — see #7):

   | From state | Assignee can → | Assigner can → |
   |---|---|---|
   | **Unaccepted** | Accept → *Accepted* · Decline → *Declined* | Cancel → *Cancelled* |
   | **Accepted** | Discard → *Discarded* · (checking off the task → auto *Completed*) | — (cancel window closed) |
   | **Declined** | terminal | terminal |
   | **Cancelled** | terminal | terminal |
   | **Discarded** | terminal | terminal |
   | **Completed** | terminal | terminal |

   - **Completed is automatic**, derived from the underlying task's own done-flag — never a
     manual state-button transition. One source of truth (the task's done state), not two things
     that could disagree.
   - **Cancel is pre-acceptance only.** Once Accepted, the Assigner has committed; only the
     Assignee's Discard can end it from there.
   - **All terminal states are hard-terminal** (locked-on-send ethos, append-only history). A
     retry means the Assigner sends a brand-new assignment record, never reviving an old one.
   - **Assigner retains read-only visibility** after Accepted: their Assignment tab keeps
     showing the task and its current state (Accepted, later auto-Completed) as a record of what
     they sent — just no more action buttons past Unaccepted.
7. **New (unseen) is a cosmetic flag on Unaccepted**, not a separate persisted state. Opening the
   Inbox transitions it to seen/"Unaccepted" (plain) if no action is taken. Drives the particle
   effect and row highlight; does not change the transition rules (Unaccepted's rules apply
   whether New or seen).
8. **Delete-on-accepted-task performs the Discard transition.** The normal Delete affordance,
   when used on an assigned+accepted task, removes it locally AND updates the Assigner's record
   to Discarded — one action, no silent desync between the assignee's copy and the assigner's
   record.

#### Row UX
9. **Inline-expand, not modal** (confirmed against Tallybook's pattern). Collapsed row: checkbox/
   tracker + task text + depth-indent + a compact state chip. Expanded (via chevron): assigner
   name, in-game assignment date, and the state-change action button(s).
10. **Chevron disclosure triangle** (▸/▾), reusing `ScribeRowButton` chrome for hover/press.
    - **Chevron-only trigger** — clicking row text/other controls never accidentally
      expands/collapses; keeps existing checkbox/tracker/pin/delete hit-targets unambiguous.
    - **Leading edge, before the checkbox** — matches the depth-indent tree convention (disclosure
      triangles lead the content they reveal) and stays clear of the trailing pin/delete floating
      buttons.
11. **Filter/picker** in the Inbox tab to selectively show/filter by state (New/Unaccepted/
    Accepted/Declined/Cancelled/Discarded/Completed) — mirrors Tallybook's tab-as-filter idea,
    exact widget TBD in design.md (dropdown vs. filter-chip row).

#### Accepted-task placement & rendering
12. **Placement target resolution**, in order: (1) whatever's in the player's currently-active
    hand slot at Accept-time (opening a block GUI doesn't clear the hotbar selection, so "most
    recently held" simplifies to "currently held" — no new persisted tracking needed), (2) if not
    eligible, scan inventory for eligible Scribe document-bearing items — if more than one
    exists, **let the player choose** via a small picker, (3) if none exist anywhere, **the
    Accept button is proactively disabled/greyed out** with an explanatory tooltip — never a
    dead-end click-then-error.
13. **Visual marker for accepted assigned tasks**: a small leading icon/glyph in the row's
    leading area (same visual slot Tracker/Link rows already use for their item icon, different
    glyph) — consistent across Tablet, Read, Edit, and Pinned views. Not a row-wide background
    tint (rejected — bigger per-view paint-treatment lift for less-established precedent).
14. **Text is not editable** on accepted assigned tasks — likely implemented by rendering the
    frozen/read-style text widget for that row even while the surrounding document is in Editor
    mode (Read mode's non-editable rendering already exists as a precedent to reuse; exact
    wiring is a design.md detail).
15. **Other affordances stay normal**: Complete (if applicable — but see #6, drives auto-Completed
    assignment state), Counter (if applicable), Pin, Reorder all behave exactly as they do for any
    other task.

---

## Open items for design.md (not blocking, but not yet nailed down)

- Exact filter/picker widget for the Inbox tab (dropdown vs. filter-chip row vs. tab-per-state).
- Exact wiring for "frozen text within an otherwise-editable document" (#14) — needs a look at
  `ScribeDialogBase.Editor.cs` row-building to see the cleanest seam.
- Assignment Desk's exact GUI dimensions and which specific Lectern/Scriptorium widget subtrees
  get reused vs. rebuilt — needs `GuiDialogScribeScriptorium.cs` / `GuiDialogScribeLecternLibGui.cs`
  layout study during design.md drafting.
- Particle effect visuals (color, motion, spawn rate) — likely a quick playtest-tunable constant,
  not a design-time decision.
