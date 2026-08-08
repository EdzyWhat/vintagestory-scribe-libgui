# Roadmap

Scribe grows one tier at a time. Each tier becomes one or more **OpenSpec changes**
(`openspec/changes/`) when we reach it; this file is the high-level map.

Detailed architect-level implementation specs for the tiers and feature clusters below now
live in **`docs/specs/`** (written 2026-07-21). Each links its VS API hooks, C# data
structures, and sequencing. When a tier is picked up, its spec is the input to an
`openspec-propose`. This file stays the map; the specs hold the "how."

The progression axis is **less access friction**: early tools are clunky handheld
objects; late tools make your tasks ambient. It's grounded in the archaeology of
writing *and* vanilla mechanics (you write *into* clay with a stylus — stone age;
you write *on* paper with ink + pen — metal age). Fully unlocked by the early metal
age (the saw); anything past that is cosmetic.

| Tier | Artifact | Capability | Status |
|------|----------|-----------|--------|
| Scratch | Clay & wax tablet (handheld) | up to 10 tasks, 1 pin; clay dries hard → re-wet in water or fire to make permanent; wax always rewritable | **shipped v1.0** |
| ~~Scratch+~~ | ~~Reed/cattail paper~~ | ~~a few more lines~~ | **cut** — the clay→wax step already covers the low tier; a paper micro-tier added no capability |
| Collection | Leather-bound notebook | infinite pages | **shipped v0.2** |
| Organization | Writing desk (private block) | consolidates notes; copy/paste export between Lectern/Notebook/Desk | planned **v1.2+** (after the v1.1 polish release) |
| Portability | Pinned-task HUD | pins from any source shown on an always-on HUD; quick-add gesture | **HUD shipped** (v0.1); backpack container **cut** |
| Social | Bulletin board (public) + chalkboard | shared board; chalkboard is drawable | planned (Board) |

## Staged plan

- **v1 — Lectern slice** *(shipped, v0.1.x)*: one lectern block (reuses the vanilla
  "lecturn-book-open" shape — plain wood) with a task checklist + short note,
  server-authoritative and multiplayer-safe. Built modularly so later tiers slot in. The
  **row-list rework** (S1 + S2 shipped: `lectern-edit-in-place-rows` archived 2026-07-21)
  delivered a single custom-drawn row element with native clipping, a continuous scroll path,
  and edit-in-place keyboard conventions (Enter/Shift+Tab/Esc, Mac caret routing).
- **v2 — Notebook (collection)** *(shipped, v0.2.0)* → `docs/specs/v2-notebook.md`. Leather-bound
  held item, infinite pages. Introduced the **`docId`-on-item + server-side document store** that
  v3 reuses. The single-editor lock does **not** carry over (a held stack has one holder — matches
  vanilla `ItemBook`, which uses no lock).
  - **Timers & alarms (Clockmaker perk)** *(shipped, v0.2.0)* → `docs/specs/timers-and-alarms.md`.
    Real-time + in-game-time reminders that fire a toast + sound, optionally on the HUD next to pins.
    Client-local state (a `ScribePlayerSettings`-style JSON + one client poller — no native scheduler
    exists; poll `Calendar.TotalHours`/`DateTime.UtcNow`), gated to the clockmaker's `tinkerer` trait.
    The timer PAGE lives on the notebook. Does NOT add due-dates to tasks (respects the discipline
    reminder below) — it's a separate opt-in feature that may reference a task by id.
- **v3 — Scratch tier: clay & wax tablets** *(shipped, v1.0.0)* → `docs/specs/v3-clay-tablet.md`
  (retitled in place; the filename is unchanged so existing pointers still resolve). Two sibling
  artifacts. **What actually shipped differs from the original spec in three ways** — reconciled here:
  - **Clay tablet:** grid-crafted from knife + stick + clay (not clayforming), up to 10 tasks / 1
    pin, in red / blue / fire-clay colours. **The "wets out / dissolves in water" mechanic was
    replaced by a dry→hard→re-wet life-cycle:** a fresh tablet is wet and editable; left alone it
    dries **hard** over ~2 in-game days (locking the writing); a hardened tablet is **softened back
    to wet by dunking it in water** (`Harden`/rehydrate, not `dissolveInWater` — the tablet is never
    destroyed), or **fired in a fire pit** to make its writing permanent. The fire-to-permanent
    archive trade-off shipped as designed. Not rack-storable; no offhand stylus.
  - **Wax tablet:** the reusable surface — a wooden frame of beeswax, grid-crafted (saw + planks +
    stick + beeswax). **Never dries or fires**, so always rewritable; same 10-task / 1-pin limits as
    clay. Balanced against clay by material cost (beeswax) and no permanent-archive path, not by a
    water punishment. (The considered heat/melt-fragility inverse stayed cut.)
  - **Added beyond the spec:** tablet text renders in a bespoke **cuneiform script** by default
    (toggleable in Settings), and the unified **Shift+right-click quick-add** gesture (see v5) — a
    BREAKING change to the lectern/held-item interaction map — landed alongside this tier.
- **v4 — Writing desk (organization)** *(planned, v1.2+ — after the v1.1 polish release)* → `docs/specs/v4-writing-desk.md`. Private owner-gated
  block; consolidates notes + categories; **kanban tabs** (Active / Backlog / Completed) as
  the fuller home for the completed-task funnel. Also the home for **within-document search**
  (a text box filtering the desk's rows by typed query — reuses the kanban filtered-row-list
  mechanism, no Core/server change; global cross-document search is a later, separate change)
  and the **faction/shared task-assignment** idea — VS ships a first-party player-group system,
  so this may need no external dependency (see open decision below).
- **v5 — Portability** → `docs/specs/v5-backpack-hud.md`. **The HUD half shipped early (v0.1.0):**
  the always-on **pinned-task HUD** (native `HudElement`, not ImGui) now aggregates pins from every
  source — lectern, notebook, and tablet — not just a held document. **Quick-add shipped in v1.0** but
  as a unified **Shift+right-click** gesture on the writing item rather than a standalone hotkey (see
  the v3 note; it's a BREAKING change to the interaction map). **The backpack container itself was
  cut** — the HUD delivered the portability goal (your tasks are ambient) without a new container item.
- **v6 — Bulletin board (social)** → `docs/specs/v6-bulletin-board.md`. Public shared block +
  signatures + a guestbook (append-only) variant. The **drawable chalkboard** is recommended
  as a v6.1 sub-change (no vanilla drawable precedent → from-scratch stroke GUI).

## Near-term, actionable (not tied to a future tier)

- **Codec version-aware read migration (standalone Core change).** *(Decided 2026-07-21.)*
  Today `ScribeDocumentCodec` hard-rejects any `Version` ≠ current; before any feature bumps the
  version (v6 signatures/policy, chronicle stamps/flags), extract the version-branched read + the
  bump into **its own small, dependency-free Core change** — pure `src/Core/`, unit-tested with a
  "reads an older blob" fixture, no game install needed. Doing it in isolation de-risks the one
  place a mistake corrupts existing saves, and both v6 and chronicle then build on a codec that
  already tolerates old layouts. See `docs/specs/README.md` → Shared Core-model conventions #1 for
  the single-version-line rule (append fields in version order; never two "v4"s).
- **In-game user feedback / error surface** *(requested 2026-07-24, playtest submission
  2026-07-24T22-41-15).* There is no user-facing way to surface an edit error today: the server
  silently rejects an oversized edit (over `ScribeDocumentCodec.MaxBlocks`/`MaxTextLength`) and the
  client shows nothing (confirmed in TESTING.md `fe168d81` / add-lectern-block 8.7 — rejection works,
  feedback is absent). Build a lightweight in-Vintage-Story message/notice surface so the player is
  told *what* went wrong and *what the limit is* when an edit is refused, with room to reuse it for
  other edit issues (lost lock, save failure, etc.). User also wants a per-task soft length limit
  (~1000 chars) surfaced through the same channel. NOTE: the earlier ToastLib approach was rejected
  (see Done/superseded); pick a mechanism that fits the LibGUI rebuild (an in-dialog inline notice or
  a native `HudElement`, not ToastLib). Promote to an OpenSpec change when picked up.
  - **UPDATE 2026-07-26 — the length case is solved by CLIPPING instead of reporting.** v1 caps task
    text at 1000 chars: the editor field enforces a maxlength and the codec clips-instead-of-rejects on
    read (which also fixes the `fe168d81` silent-rejection bug). No user-facing error notice ships for it.
  - **UPDATE 2026-08-06 — the error surface is now PARTIALLY RESOLVED (shipped in v1.0.0).**
    `zero-point-three-fixes` §7 shipped a real in-Vintage-Story notice via `TriggerIngameError` —
    the vanilla client error channel — so refused edits now tell the player *what* went wrong (e.g.
    the tablet 10-task cap surfaces "A tablet holds at most 10 tasks", and the lost-lock / blocked-edit
    cases route through the same channel). This is the LibGUI-appropriate mechanism the earlier note
    called for (not ToastLib). Still open: a fuller inline notice for save-failure and other edge
    cases — the channel exists, coverage is incremental.
- **Lectern GUI polish** → `docs/specs/lectern-gui-polish.md`. Most items are now delivered
  or retired. **Shipped:** placement facing (v0.1.0), custom SVG icon set
  (`add-custom-svg-row-icons`), side-rail nav column (the LibGUI right-col nav IS the side
  rail + skeuomorphic ribbon), pixel-art GUI backdrop (lectern aesthetic direction delivered),
  "Edit" → "Task Editor" relabel (2026-07-30). **Obsolete:** fold-switch-into-toggle
  (shelved — the nav column already provides clean view switching). **Still deferred:** damped
  left-gutter scaling — `ControlSize` in `ScribeRowStyle.cs` scales linearly today; a
  sub-linear factor would reduce the grip/checkbox column's proportional share at large text
  sizes (minor, no OpenSpec change yet); block shape refinement + notebook item PNG update
  (art-gated).

## Presentation & polish (deferred, mostly asset-gated)

→ `docs/specs/presentation-and-fonts.md`. Merges the checkbox stamp/erase animation
(this is **S4** of the row-list rework — the seam already exists in
`ScribeRowElement.DrawCheckboxGlyph`), the smooth drag-reorder preview animation (**S3**;
supersedes the on-hold `lectern-drag-reorder-feedback` change's "rows don't shift" non-goal),
custom per-tier fonts (cuneiform for the tablet, rustic script for books — loadable via
FreeType, gated on a license check), and lightly-scoped handwriting-skill / item-aging visuals.
All render-only; several need art/audio assets before they can start.

**Sequencing vs. lectern-gui-polish** *(decided 2026-07-21, gate passed 2026-07-30)*: the
chrome layout is now settled (side rail delivered, "Task Editor" relabel landed, fold-toggle
shelved). S3 (drag preview) and S4 (checkbox animation) can proceed without rebasing risk.

## Chronicle & integrations (later)

→ `docs/specs/chronicle-and-integrations.md`. Merges: death-leaves-a-last-entry,
calendar-stamped entries + passive chronicle-building, milestone-suggested tasks (self-detected
via inventory polling — zero dependency; three third-party trigger mods were researched and
rejected), one-way Slack push via Incoming Webhooks (config-gated, undiscoverable, secret never
logged/synced), and long-term Handbook bookmarking. Plus program-level meta workstreams:
cross-world JSON export/import, localization (`lang/` beyond `en.json` — start the key structure
early), a handbook/wiki authoring pass near shipping, and crediting JeanPierre (Wanderer's
Sketchbook) in CREDITS.

## Immersion ideas (curated — see project plan Reference for the full brainstorm)

Grounded, distinctive ideas worth tracking. The clay-tier and social-tier ones are folded into
their specs above (`v3-clay-tablet.md`, `v6-bulletin-board.md`); the chronicle-style ones into
`chronicle-and-integrations.md`. Still-open highlights:

- **Firing a tablet is a real crafting decision** *(shipped v1.0)* — a fired tablet becomes a
  permanent, read-only archive vs. a hardened one that can be re-wet and revised. The single most
  historically-grounded mechanic on the list; shipped as the dry→hard→re-wet/fire life-cycle (see
  the v3 note above — the final mechanic is water-**reversible** hardening, not water-fragility).
- **Fire vs. water asymmetric fragility across tiers** — as shipped, clay's water axis is
  *reversible* (dunking re-wets a hardened tablet) rather than *destructive*. If a later paper/leather
  tier wants an inverse, it would invert this reversibility, not a lost-to-water punishment.
- **Death leaves a last entry**, **calendar-stamped passive chronicle**, **signed vs. unsigned
  notes**, **guestbook board variant**, **milestone-suggested tasks** — all specced (see
  chronicle + v6 specs).
- **Wax-seal "soft security"** — **decided: not via Envelopes** (that mod is items-only with no
  API to seal a Scribe block/docId). Build native later or skip. Not urgent.
- **Writing desk as faction task-assignment** — folded into `v4-writing-desk.md`; the
  faction-backing choice is an open decision below.
- **Handbook bookmarking** and **Slack push** — both decided-pursue, both in the chronicle spec;
  late-stage.
- Lower-priority / needs investigation: handwriting neatening with practice (skill curve),
  item aging/wear visuals.

### UX lessons from PM/notetaking apps (Notion, Todoist, Bullet Journal, GTD, etc.)

The core insight: **capture speed matters more than organization** — the game's "long branching
tech tree → distraction" problem is GTD's "open-loop anxiety," relieved by getting a thought out
fast. Concrete, cheap directions this suggests (several now folded into specs):

- **A dedicated quick-add hotkey** — highest-leverage UX investment for a held writing item.
  Specced in `v5-backpack-hud.md`.
- **Sort completed tasks toward the bottom** (or a collapsed "Done (N)" group) via the existing
  `MoveBlock` primitive — the lighter cousin of the v4 kanban funnel.
- **Reorder via mouse drag** (already shipped) over select+step buttons — VS is heavily
  mouse-driven, so drag is the consistent choice.
- **Tab / Shift+Tab / Enter to save-and-move-focus between rows** — shipped in S2
  (`lectern-edit-in-place-rows`, archived 2026-07-21); survey adjacent hotkey affordances
  (Ctrl+Enter to commit-and-add-below, etc.) as a small batch for a future change.
- **A "carry forward" migration** for the clay tablet's 3-line cap (Bullet-Journal-style) — a
  Core op, specced in `v3-clay-tablet.md`.
- **Discipline reminder:** resist due dates / priority / tags as structured `ScribeBlock`
  fields; let players encode them as plain-text conventions (e.g. a `!` prefix) — zero schema
  cost, opt-in. A full multi-column Kanban is a mismatch for VS's single-column GUI; the desk's
  categories/tabs cover grouping better.

For the full design record and rationale, see the project plan.

## Open decisions (surfaced by the 2026-07-21 exploration; carried into each spec)

These are the cross-cutting forks the specs couldn't settle without you. They don't block the
specs (each documents its assumed default) but they shape sequencing and scope.

> **Coherence review (2026-07-21):** the eight specs were written in parallel and blind to
> each other, so several extend the same `src/Core/` surface incompatibly — two specs both
> claim codec "v4", two invent different entry-timestamp representations, two model
> immutability on different axes, and `v4`/`v6` both need one shared access-policy enum.
> These are reconciled in **`docs/specs/README.md` → "Shared Core-model conventions"**, which
> is binding on any spec becoming a proposal. Decisions #4 and #5 below are the two forks from
> that review that still need *you*, not just a convention.

1. **v4 faction-backing** — **DECIDED 2026-07-21: defer.** Ship the personal writing desk first;
   leave faction backing (built-in player groups vs. shared owner-UID list vs. third-party mod)
   as an open question until v4 is actually scoped. The player-group finding (VSAPI-NOTES) means
   the no-dependency path exists whenever we return to it.
2. **Lectern-polish icon direction** — **DONE.** Custom SVGs authored and registered
   (`add-custom-svg-row-icons`, archived 2026-07-21): `scribepin`/`scribegrip`/`scribeclose`/
   `scribeedit`. All four row/control icons are hand-drawn, tooltip-backed, and wired.
   "Edit" → "Task Editor" relabel landed 2026-07-30.
3. **v5 HUD pin scope** — when the source document is on an item you're NOT holding, do pinned
   tasks still show (needs a server-pushed "my pins" summary) or only the currently-held
   document's pins?
4. **Notebook document store** — **DECIDED 2026-07-21: one shared store, generalize the packets.**
   A single artifact-agnostic `"scribe:doc:<docId>"` store in SaveGame holds notebook + desk +
   tablet + backpack docs, distinguished only by docId (proves the store is artifact-agnostic — a
   v3 goal, and makes the drop-on-death "current document" lookup trivial). The v1 lectern's
   BlockPos-keyed edit/toggle/move packets are **generalized** to carry an abstract document
   handle (BlockPos OR docId) so lectern and held items share one wire path — accepting the
   rewrite of v1's confirmed wire format now, while it's early dev with no shipped saves. Gates
   v2/v3/v5 and the buildable chronicle features (see `docs/specs/README.md` convention #6).
5. **Public board concurrency & signatures** (v6) — **DECIDED 2026-07-21: lock-free last-write-wins
   + always-attributed.** The editable public bulletin board takes NO editor lock (anyone edits;
   whole-document last-write-wins on save), and every public-board entry is **always signed** with
   the writer's name (no anonymous posting on public boards). These cohere: always-on attribution
   makes lock-free clobbers traceable to a named author, and it also settles the "who may delete an
   entry" question (author-match is always available). The append-only guestbook never contends
   regardless. Supersedes v6's own recommended defaults (single-lock + board-level policy).

### Reserved & don't-re-propose (forward-relevant guardrails)

The full record of shipped, removed, and rejected work lives in `CHANGELOG.md`, the archived
OpenSpec changes (`openspec/changes/archive/`), and git history. Two items are kept here because
they still shape future work:

- **Reserved: freeform text-section blocks** (`ScribeBlockKind.Text`,
  `ScribeDocument.AddTextSection`) are reserved for a future item/recipe, not the lectern (the
  "Add Note" button was removed in add-lectern-block task 8.18). The Core capability +
  `ScribeBlockRowCell` text-row rendering are kept working so a future recipe can reuse them with
  no Core changes.
- **Don't re-propose ConfigLib / the ImGui debug overlay / ToastLib.** All three were tried or
  investigated and deliberately dropped (ConfigLib + the `#if DEBUG` ImGui overlay adopted then
  removed once the LibGUI rebuild made them inert/never-rendered on Apple Silicon; ToastLib
  rejected as stale for 1.22.x with no persist-and-update primitive). The HUD uses a native
  `HudElement`, not ImGui. See the archived `add-imgui-configlib-tuning` change for the details.
