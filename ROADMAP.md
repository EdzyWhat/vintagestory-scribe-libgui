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

| Tier | Artifact | Capability |
|------|----------|-----------|
| Scratch | Clay tablet (soft/unfired) | 3 lines; wets out if you fall in water holding it |
| Scratch+ | Reed/cattail paper | a few more lines |
| Collection | Leather-bound notebook | infinite pages; built-in implement holder |
| Organization | Writing desk (private block) | consolidates all your notes + categories |
| Portability | Note-taker's backpack + HUD | whole collection on the go; pin ≤3 tasks to HUD |
| Social | Bulletin board (public) + chalkboard | shared board; chalkboard is drawable |

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
- **v3 — Scratch tier: clay & wax tablets** → `docs/specs/v3-clay-tablet.md` (retitled in place;
  the filename is unchanged so existing pointers still resolve). Two sibling artifacts:
  - **Clay tablet:** soft/unfired item, 3-line UI, clayform-a-flat-slab, stylus in offhand,
    **wets out in water**, rack-storable. Mostly JSON (clayforming pattern, `dissolveInWater`,
    `scrollrackable`, stylus = `writingTool` item). The fire-to-permanent-archive trade-off has a
    real gotcha (kiln firing drops stack attributes — see the spec and VSAPI-NOTES).
  - **Wax tablet (addendum 2026-07-21):** the reusable Roman-era surface. **Does NOT wet out in
    water** (water-immune by simply omitting clay's water code). Balanced against clay not by an
    invented punishment but by material cost (beekeeping-gated beeswax) and **no path to a
    permanent fired archive** — wax is erasable/reusable (smooth-flat = reset) but never
    permanent. The two tablets are mutually non-dominating. (A heat/melt-fragility inverse was
    considered and cut: nothing in vanilla damages a held item when the player is on fire, and
    open-air proximity heating of a hotbar item is unconfirmed — see VSAPI-NOTES.)
- **v4 — Writing desk (organization)** → `docs/specs/v4-writing-desk.md`. Private owner-gated
  block; consolidates notes + categories; **kanban tabs** (Active / Backlog / Completed) as
  the fuller home for the completed-task funnel. Also the home for **within-document search**
  (a text box filtering the desk's rows by typed query — reuses the kanban filtered-row-list
  mechanism, no Core/server change; global cross-document search is a later, separate change)
  and the **faction/shared task-assignment** idea — VS ships a first-party player-group system,
  so this may need no external dependency (see open decision below).
- **v5 — Backpack (portability)** → `docs/specs/v5-backpack-hud.md`. Hotkey-accessed;
  always-on **pinned-task HUD** (≤3 pins, native `HudElement` — not ImGui); plus a
  **quick-add hotkey** for one-line capture without opening the full document.
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
  - **UPDATE 2026-07-26 — the error SURFACE is DEFERRED past v1; the length case is solved by CLIPPING
    instead of reporting.** v1 (RELEASE.md A1) caps task text at 1000 chars: the editor field enforces a
    maxlength and the codec clips-instead-of-rejects on read (which also fixes the `fe168d81` silent-
    rejection bug). No user-facing error notice ships for it. The general feedback surface (lost lock,
    save failure) stays a post-v1 item — the specific v1 errors are being solved individually (clipping
    here) rather than by building the shared notice channel now.
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

- **Firing a tablet is a real crafting decision** — a fired tablet becomes a permanent,
  read-only archive vs. staying soft/editable but water-fragile. The single most
  historically-grounded mechanic on the list (v3 spec).
- **Fire vs. water asymmetric fragility across tiers** — clay is fireproof but water-fragile;
  paper/leather should invert this. Keeps later tiers from being strictly "better."
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
