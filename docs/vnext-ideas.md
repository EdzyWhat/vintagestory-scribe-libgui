# Scribe — vNext idea backlog

A durable parking lot for post-1.0 feature ideas, captured before they're sketched in detail.
**Nothing here is a commitment** — this is a personal project, so the delivery order is whatever
sparks joy (see Prioritization below). When an item is ready to build, it graduates to an
`openspec-propose` (or folds into an existing `docs/specs/*` tier). This doc is the raw
brainstorm + triage layer *above* [`ROADMAP.md`](../ROADMAP.md); the roadmap holds the tier map and
the specs hold the "how." The companion **"Scribe — The Big Board"** Slack canvas is the
pretty, at-a-glance front-end for this doc + the roadmap.

**How to read the status tags:**
- 🆕 **new** — not yet represented anywhere in the roadmap/specs.
- 🗺️ **on roadmap** — already a planned tier or spec; this entry adds detail/nuance to fold in.
- ✅ **partly shipped** — related capability already ships; this extends it.
- ⚠️ **concern flagged** — the author has a stated reservation (scope, immersion, architecture);
  keep the concern attached to the idea so it isn't lost when we scope it.

Captured 2026-08-07. Author's own reasoning/concerns are preserved deliberately — they're the
hardest part to reconstruct later.

---

## 1. Item & block visual polish

### 1.1 Revamped Timer Page appearance — 🆕 new
A ticking **Temporal Gear** seen as if through glass embedded in the notebook page. Skeuomorphic
depth for the Clockmaker timer view.
- *Open Qs:* is the gear a live-animated render (spinning), a static art layer, or a shader-ish
  "glass" overlay? How does it read against the existing paper backdrop and the timer rows? Does
  it reuse the vanilla temporal-gear item texture (we already ship `reference-temporalgear.png`)?

### 1.2 Per-tab subtitle + distinct theming — 🆕 new
Each tab in the Notebook & Lectern gets a **subtitle** and a **more distinct appearance / colour
theme** — not just a different paper texture. Make tabs feel like separate places.
- *Open Qs:* which tabs (Tasks / Pins / Timer / Settings)? Is theming per-tab colour accents on
  chrome, or fuller backdrop swaps? Interaction with the existing per-material tablet backdrops.

#### Row-CREATION animation — verdict DECIDED 2026-08-08 (drastically reshapes 1.2's "feel")
Broader goal behind 1.2: the GUI's instantaneous nature feels "too computery"; make the Notebook and
other advanced items feel visceral/concrete. A new task/note row should **arrive** with motion, not pop
in. Two independent LibGUI-source explorations converged on the same verdict (findings in `VSAPI-NOTES.md`
→ "Row-CREATION animation" under the LibGUI section):
- **Use a SLIDE-in (paint-only `Transform` translate), NOT a height-expand.** The load-bearing reason:
  `Transform`/`Opacity` animate **paint only** — layout passes through untouched — so the new row is laid
  out at **full natural size every frame**. That *structurally* eliminates the mid-animation
  height-change jitter that ANY measured-height approach (height-expand, `AnimatedSize`) suffers when the
  auto-focused editable row grows/wraps/swaps its body mid-animation. Height-expand (extending
  `ScribeHeightFactorRender` 0→1) re-measures the live child every frame, so the content races the expand.
- **Auto-focus-on-create is only safe with slide/fade.** Focus + keystrokes route through `FocusManager`,
  independent of geometry — but the *visible caret* and *mouse* hit-testing both depend on layout `Size`.
  Under height-expand the caret is clipped invisible near t=0 and a click misses (and would blur the row);
  slide/fade keep the row full-size, so caret shows and clicks land.
- **Fade is optional, layered on the SAME controller via `Curves.Interval`** (e.g. slide over `Interval(0,
  0.7)`, fade over `Interval(0, 0.5)`) — no second controller. Fade alone reads too subtle ("computery");
  its job is to soften the slide's arrival. Gotcha: `RenderOpacity` skips paint below α≈0.001, so start a
  fade at a small non-zero α (or let the slide carry t=0) to avoid a one-frame invisible-but-focused row.
- **Same registry pattern as collapse.** All stock `Animated*` widgets SNAP under `ForceRebuild` (Begin==
  End==target on fresh `InitState`), so this needs a host-owned persisted `AnimationController` keyed by
  the new row's stable id — a `ScribeCreateRegistry` sibling to `ScribeCollapseRegistry`, driving stock
  `Transform`(+`Opacity`) from the controller value. `AnimationController` already has `Forward`/`Resume`/
  settable `Value`; no new controller machinery. Reuses [[scribe-collapsible-direction-agnostic-expand]]'s
  infrastructure. The §4.1 continuous re-hover (fix-list-collapse-stale-hover) already covers the identical
  stale-hover-on-reflow bug this animation would otherwise reintroduce, since its trigger is
  animation-direction-agnostic.
- **Trade-off + 4 in-game unknowns (feel judgments, not source-answerable):** paint-only means the row's
  full-height slot opens **instantly** on frame 1 (rows below snap to final positions), then the row slides
  into that open slot — it CANNOT open the gap progressively without reintroducing the jitter/clip. Whether
  "instant gap, then glide in" reads as concrete or as "rows jump, one glides" needs playtest. Also unknown
  until tested: slide direction vs. the list's clip rect, exact duration (~180–220ms to match collapse) +
  curve (`EaseOutCubic` vs. a slight `EaseOutBack` overshoot), and confirming no fade lower-bound flash.
- *Scope note:* this is a **1.2** input, explored during 1.1 planning — NOT part of 4.1. It also depends on
  the task-type content decisions (what mounts inside a freshly-created row) tracked in
  [[picker-keystone-resolved]] §2.x.

### 1.3 Clay Tablet **block** variant — 🆕 new ⚠️ concern flagged
A wall-mountable / leaning block version of the Clay Tablet that's **still usable** (openable to
edit) while placed. Think: propped against a wall or hung like a small plaque.
- *Concern:* the tablet is currently a held item with a display transform on shelves/cabinets
  (see the 1.0.1 tuning). A true block variant is a new block entity + placement + interaction —
  meaningful scope. How does its data relate to the held item (same docId store? a block that
  *is* a placed tablet)?
- *Open Qs:* lean vs. wall-mount vs. both? Does placing consume the held tablet and vice-versa?

### 1.4 New Lectern model — more "Scribe" — 🆕 new (art-gated)
The lectern currently reuses the vanilla `lecturn-book-open` shape (plain wood). Make it visibly
*ours*: add an **ink & quill** model element on the lectern.
- *Note:* roadmap already lists "block shape refinement" as art-gated under lectern-gui-polish —
  this is the concrete direction for it.

### 1.5 Stylus widget on the Tablet — 🆕 new (art-gated)
A long pen-like **stylus** modelled attached to the side of each Tablet, reinforcing the
"write into clay" fantasy.
- *Note:* the v3 spec explicitly shipped "no offhand stylus" as a mechanic — this is a purely
  cosmetic model element, not a held offhand item, so it doesn't reopen that decision.

### 1.6 Add `<` and `>` glyphs to the cuneiform font — ✅ DELIVERED (v1.1, `add-arrow-substitution-and-cuneiform-glyphs`)
Author two new cuneiform glyphs for the **less-than / greater-than** characters so tablet text can
render them. Author wants this in **v1.1**.
- **This is NEW ART, not a Core alias.** The shipped bundle
  (`src/Mod/assets/scribe/textures/fonts/cuneiform-glyphs-1.json`) carries **54** authored
  characters today — `A–Z 0–9 . , ' - : ; ! ? " ( ) + / = % # * @` — with **no `<` or `>`** (verified
  2026-08-08). Unlike the `& → +` and `[ ] { } → ( )` aliases in `CuneiformLineLayout.Aliases` (which
  only work because an existing glyph resembles the target), `<`/`>` have no lookalike to alias to —
  the nearest, `(`/`)`, are semantically wrong. So they must be **drawn** in the sibling **glyph-forge**
  tool (`~/claude/glyph-forge/`), which is where the individual `glyph-*.json` source files live (the
  bundle is `generatedFrom: glyphs/glyph-*.json` — the per-glyph sources are NOT in this repo).
- **Work involved (small, well-trodden path — same as the prior `+ / = % # * @` symbol sync):**
  (1) author `<` and `>` glyph strokes in glyph-forge; (2) regenerate/re-sync the combined bundle
  JSON; (3) bump `characterCount` 54 → 56; (4) update the `CuneiformTests` authored-count assertion
  (`Parse_ShippedBundle_ContainsAllAuthoredCharacters`, currently expecting 54); (5) smoke-test the
  render via the `.cuneiform <text>` client harness (dot-prefix client command). No Core alias entry
  needed (they're real authored glyphs, not stand-ins).
- *Why now:* rounds out the ASCII-symbol coverage and pairs naturally with any future comparison/
  arrow-ish plaintext conventions; also just completes the "type any symbol on a tablet" expectation.
- *Value/effort feel:* :star: value (completeness/polish), **S effort but art-gated** — the code path
  is trivial and proven; the gate is drawing two legible glyphs that read as `<`/`>` in the cuneiform
  style. Cross-reference [[cuneiform-character-coverage-plan]] for the prior coverage pass.

### 1.7 Add arrow glyphs (`←`/`→`) to the cuneiform font — ✅ DELIVERED (v1.1, `add-arrow-substitution-and-cuneiform-glyphs`)
Author cuneiform glyphs for the **left/right arrow** characters so tablet text can render the arrows
that the plaintext auto-substitution (see **4.5**) produces. Sibling to **1.6** — same NEW-ART path,
not a Core alias (no existing glyph resembles an arrow to alias to, the way `& → +` aliases work).
- **Pairs directly with 4.5.** 4.5 turns typed `->`/`<-` into `→`/`←` in the text buffer for *every*
  surface; on a tablet those characters then hit the cuneiform layout, which today has no glyph for
  them (the shipped bundle carries the 54 ASCII chars only — verified in 1.6). So without an authored
  arrow glyph, a tablet would drop/box the arrow. Building 4.5 first makes this glyph the thing that
  lets the substitution look right on a tablet specifically.
- **Open Q — how many arrows?** 4.5 as scoped only produces `←`/`→` (horizontal). If we later want
  `↑`/`↓`/`↔` (needs a taller multi-line convention to type them), that's more glyphs — decide the
  arrow set *before* drawing, so the bundle count bumps once. Lean: just `←`/`→` for v1.1 to match 4.5.
- **Work involved:** identical to 1.6 — (1) draw the arrow glyph strokes in **glyph-forge**
  (`~/claude/glyph-forge/`); (2) regenerate/re-sync the bundle
  (`src/Mod/assets/scribe/textures/fonts/cuneiform-glyphs-1.json`); (3) bump `characterCount`
  (56 → 58 if it lands after 1.6's `<`/`>`, or 54 → 56 if standalone); (4) update the `CuneiformTests`
  authored-count assertion; (5) smoke-test via `.cuneiform <text>`. No Core alias entry.
- *Value/effort feel:* :star: value (completeness — makes 4.5 look right on the tablet tier),
  **S effort but art-gated** on drawing legible arrow glyphs in the cuneiform style. Cross-reference
  [[cuneiform-character-coverage-plan]] and **1.6** (author together — one glyph-forge session, one
  bundle regen, one count bump).

---

## 2. New task types — "New Task" dropdown

The unifying idea: the **New Task** action becomes a small dropdown offering several task
*kinds*. Standard task stays the default; the others are opt-in.

### 2.1 Dropdown itself — 🆕 new
Options: **Standard** · **Linked (Handbook)** · **Tracked (numeric goal)** · **Mapped**.
- *Chrome — DECIDED 2026-08-08:* this dropdown is the entry point for choosing a task *kind*, and it
  adds **no new button** — it upgrades the existing **New Task** action. Picking Tracked creates a
  row already-expanded with its inline "pick item" control (which opens the search picker; see
  §Picker — RESOLVED). This satisfies the < 500 px real-estate constraint.
- *Open Qs:* does this live on the Lectern/Notebook only, or Tablets too (Tablets are the "quick
  scratchpad" tier — maybe standard-only there to keep them simple)? How does it interact with the
  Shift+RC quick-add gesture (which always makes a *standard* task at top — likely stays
  standard-only, as quick-add is by definition the zero-friction path)?

### 2.2 Tracked (numeric progression) task — 🆕 new
Track collecting items against a goal (e.g. "collect 32 reeds"). When editing a row, an extra
button (alongside pin & delete) spins out a **wizard/modal**: pick an item + a count, and the task
then tracks against the player's inventory, showing progress.
- *Item picking — RESOLVED 2026-08-08:* two entry methods, **support both but sequence them** — a
  handbook "Add to task" link **first** (smaller build; captures the exact variant), then a decoupled
  in-Scribe creative-style **search-first picker** as the follow-up (see §Picker — RESOLVED in the
  Prioritization section). Open Q narrows to the *entry point*, not the picker itself (see the
  real-estate note in that subsection).
- *Count scope — OPEN FORK, decide in the Tracked design phase (2026-08-08):* what inventory counts
  toward progress? **Tallybook (decompiled 2026-08-08) counts carried-only** — it walks
  `player.InventoryManager.Inventories.Values` filtered to `ClassName == "hotbar" || "backpack"`,
  event-driven off inventory change, **no polling, no chest scan** ("what are you carrying, updated
  the instant it changes"). That's the snappy/simple option but ignores stockpiles. The competing
  option — **carried + nearby containers** — better matches the "do I have 100 flax" stockpile
  fantasy but reopens polling/scanning and "which containers count." Not locked now; settle inside
  this change's design phase (consistent with how Tracked sizing is already deferred there).
- *Open Qs:* Does progress auto-complete the task at goal? Polling vs. event-driven (Tallybook is
  event-driven; the chronicle spec's milestone-suggested tasks poll — pick deliberately, don't
  assume). Client-local vs. synced (note: Scribe lectern data is *server-authoritative* per the Sign
  pattern, unlike Tallybook which is fully client-side + read-only — a Tracked count that must sync
  to a placed lectern for other players is a harder problem than a personal client HUD).
- *Design overlap:* the chronicle spec's "milestone-suggested tasks (self-detected via inventory
  polling)" is a related primitive — but see the polling-vs-event-driven fork above before assuming
  it's build-once-use-both.

### 2.3 Linked (Handbook) task — 🆕 new
A task that links to a handbook entry: in read view the player clicks the link and it **opens the
handbook** for that item. Possibly: type an item name and it auto-links, or a dedicated
"link to handbook entry" task kind.
- *Note:* roadmap's chronicle spec already reserves **"long-term Handbook bookmarking"** — this is
  a lighter, per-task cousin. Consider unifying the "open handbook at entry X" primitive.
- *Open Qs:* free-text auto-linking (fuzzy match a word → handbook page) is much harder than an
  explicit "pick a handbook entry" task kind. Prefer the explicit kind first.

### 2.4 Mapped task — 🆕 new ⚠️ concern flagged
Attach a task to a specific **map location** — it shows up on the world map, maybe as a custom
marker. Possibly accessed as a **side widget on the map view** that propagates back to the held
Notebook.
- *Concern:* map integration is a distinct UI surface (VS world map / waypoint system). Splits
  development away from the writing dialogs. Might be cleaner as "a waypoint that references a
  task" than "a task that owns a waypoint."
- *Open Qs:* does it create a real vanilla waypoint/marker? Two-way sync (edit on map ↔ edit in
  notebook)? Does this need the Desk, or work on a held Notebook?

### 2.5 Crafting task — 🆕 new
Pick a target **item to craft**, and Scribe captures **all its ingredients** into a numeric task
(or a checklist of numeric sub-goals) so the player gets help gathering everything the recipe
needs. Essentially "Tracked task, auto-populated from a recipe."
- *Relationship to 2.2:* builds directly on the Tracked (numeric goal) primitive — the difference
  is the *source* of the goals: 2.2 is a single hand-entered item+count; 2.5 derives the whole set
  from a recipe. Almost certainly should be built after (or with) 2.2, sharing the inventory-poll +
  progress-render machinery.
- *Entry point — shares handbook-pin with 2.2 (DECIDED 2026-08-08):* the same injected handbook
  content offers a **"Craft this"** link alongside "Track this" — but it appears **only when the item
  is craftable** (recipe lookup over `capi.World.GridRecipes`; see §Handbook-pin in the Picker
  section for detection + the grid-only scope caveat). So Crafting's item-entry rides on the same
  Harmony postfix as Tracked — no separate picker surface needed to *start* a Craft task.
- *Open Qs:* how deep does ingredient capture go — just the **direct** grid/recipe inputs, or
  **recursively** down to raw materials (e.g. planks → logs)? Recursive is far more useful but much
  harder (multi-level recipe graph + intermediate crafting steps). Start with direct inputs. Which
  recipe does it read (grid recipe by item code — VS exposes these)? How are multi-output or
  variant recipes handled? Does completing the gather auto-suggest the craft?
- *Value/effort feel:* :star::star::star: value (this is a genuinely useful survival aid), but
  **L effort** — gated on 2.2 existing first and on the direct-vs-recursive scope decision.

---

## 3. Ambient / world-visible tasks

### 3.1 Hover-over-block task preview — 🆕 new ⚠️ concern flagged
Hovering a Scribe block (lectern? tablet block?) shows a **task preview** — maybe just a little
text, not the full UI.
- *Author's own tension (preserved):* "is this too disruptive? Maybe we don't need the full UI,
  just a little text. But that might then be disruptive to fully switch in, and it splits the
  development, and also distracts — hurting immersion. Lots to discuss."
- *Open Qs:* interaction-help text (the vanilla `GetPlacedBlockInfo` line) vs. a floating custom
  tooltip. Where's the line between "helpful glance" and "immersion-breaking HUD clutter"?

### 3.2 Bulletin Board / Chalkboard — world-visible text — 🗺️ on roadmap (v6) ⚠️ concern flagged
A new Scribe block whose text is **visible in the world without opening an interface** — direct
inspiration from the **Billposting** mod. Ideally you could check off a checkbox just by looking
at it. Envisioned as a **2×2 wall-mounted block** (like a big painting; Billposting's fist is
2×1) giving more visible space without opening the full dialog.
- *Roadmap status:* v6 "Bulletin board (social) + chalkboard" already exists as a planned tier
  (`docs/specs/v6-bulletin-board.md`), with decided lock-free last-write-wins + always-signed
  concurrency. **This entry adds the key new requirement:** in-world rendered text + in-world
  interaction (check a box without opening a dialog).
- *Author's framing (preserved):* "This would be an architectural shift, I have no idea how to do
  this. But it has been done!" Billposting is about a **town center**; "our version is more about
  community work to be done, but there's definitely overlap I want to be conscious of."
- *Open Qs:* how is text rendered onto a block face in-world (Billposting is the precedent to
  study)? How does a "look at the checkbox and toggle it" raycast interaction work? Fold these
  into the v6 spec rather than a fresh tier.

---

## 4. Editor UX fixes & affordances

### 4.1 Faster delete — fix the mouse-move-to-reveal bug — 🆕 new (bug-ish, high value)
When a row is deleted, the delete button doesn't reappear until the mouse **moves**, even though
the delete animation has already slid a *new* row under the cursor. Result: mass-deleting forces
a tiny mouse wiggle between every delete — a frustrating loop.
- *Note:* this is close to a bug/polish item, not a feature. Likely a hover-state / hit-test
  refresh issue after the row-list re-layout animation settles. Cross-reference the
  `[[forcerebuild-vs-reconciling-libgui]]` and `[[libgui-settling-loops-and-race-diagnosis]]`
  notes — the post-layout settling machinery is where the stale hover state lives.
- *Likely small + high-satisfaction — a good early standalone change.*
- **STATUS 2026-08-08 — the hover half is DONE, split into two parts:**
  - ✅ The stale-**hover** bug is fixed by the `fix-list-collapse-stale-hover` change (a per-frame
    synthetic pointer-move re-dispatch, armed by both an in-flight collapse and any `ForceRebuild` via
    `RootElement`-identity detection). Playtest-confirmed: delete-without-wiggle, empty-row cleanup, and
    no-flicker all pass; HUD-unpin fixed by the general rebuild detector (re-test pending).
  - ⏳ The stale-**click-target** bug remains — see 4.1b below. This is the other half of "faster delete"
    and is a committed v1.1 item.
  - 🐛 **Known minor artifact (accepted 2026-08-08):** on a `ForceRebuild` that isn't collapse-animated
    (new-row creation, unpin), there's a **single-frame flicker** — the fresh tree paints once with
    `hovered=false` (controls off) before the next-frame synthetic hover-refresh lands. Root cause: the
    refresh needs a laid-out tree to hit-test, and LibGUI runs build→layout→paint as one sealed sequence,
    so our dispatch can only land *after* that first paint. Eliminating it fully needs to dispatch hover
    in the layout↔paint gap, which we can't reach from a `GuiBase` subclass without a `gui`-dep hook
    (forbidden) or manually re-laying-out the tree ourselves (duplicates LibGUI constraint logic —
    fragile). **Judged not worth chasing and left as-is (author, 2026-08-08):** the same one-frame
    rebuild flash is visible in OTHER mods built on LibGUI, i.e. it's inherent library behavior, not a
    Scribe defect. Playtest verdict: "single frame flicker… we keep proper track of the mouse and the
    hover stuff stays on screen properly." Revisit only if it becomes objectionable, or for free if
    LibGUI ever exposes a post-layout/pre-paint hook.

### 4.1b Fluid mass-delete — fix the stale CLICK target mid-collapse — 🆕 new (proposed for v1.1, 2026-08-08)
The sibling of 4.1, surfaced by its playtest. After the hover fix you can **see** the delete button on
the row that slides under a stationary cursor mid-collapse — but **clicking** it does nothing until the
~200ms collapse finishes; the click only lands on the second press. So true fluid mass-delete (delete,
delete, delete without moving the mouse) still stutters.
- *Root cause (diagnosed, not yet fixed):* the departing row's **ghost-snapshot** — the visual copy that
  animates the collapse in place — still occupies the shrinking layout box under the cursor, so it
  **intercepts the hit-test** and swallows the click that was meant for the live row sliding up behind
  it. This is a hit-test/click-target problem, distinct from the hover problem 4.1 solved (hover is a
  read; the click is a write that lands on the wrong element).
- *Likely fix direction:* make the departing ghost-snapshot **transparent to hit-testing** while it
  collapses (it's a non-interactive visual, so it should never claim pointer events) — or route the click
  through it to the live row beneath. Check whether LibGUI's `RenderObject` hit-test has an
  ignore/hit-test-behavior flag we can set on the snapshot, mirroring Flutter's `IgnorePointer` /
  `HitTestBehavior.translucent`. Cross-ref the `ScribeCollapsible` / ghost-snapshot logic and the
  `[[libgui-settling-loops-and-race-diagnosis]]` note.
- *Scope:* its own small `openspec-propose` change (NOT folded into `fix-list-collapse-stale-hover`, whose
  Non-Goals explicitly exclude it). Bundle it into the **v1.1** release alongside 4.1 so "faster delete"
  ships whole. Author confirmed 2026-08-08: "include the mass-delete click-target in 1.1… make sure we
  tackle it soon."

### 4.2 Undo — 🆕 new ⚠️ concern flagged (author leans "maybe not")
An undo button, maybe at the bottom of the Edit page. Would need to store the last X changes
temporarily and group them (e.g. capture a text segment when typing slows). Or scope it down to
just **undo of task deletion**.
- *Author's own reservation (preserved):* "This is a very out-of-Vintage-Story-feeling capability.
  Very age of computers. Maybe not appropriate, and a massive architectural hurdle."
- *Triage lean:* if pursued, undo-delete-only is dramatically cheaper than full text undo and
  covers the most painful loss. Full undo is likely out of scope / off-theme.

### 4.3 Open a held Scribe item without closing other windows — 🆕 new
Today the only way to open a Scribe item is to close everything else first. As features start to
bleed across windows (handbook links, crafting tasks referencing recipes, etc.), it becomes
valuable to open your Tablet/Notebook/carried Scribe item **while the inventory or Handbook is
already open**. Idea: a **hotkey** that opens the held Scribe item over the current screen — or,
if you're **hovering a Scribe item** (in inventory), a hotkey to open *that* item.
- *Why it matters now:* the crafting task (2.5) and linked/handbook task (2.3) both create moments
  where the player is in the Handbook and wants to jot into Scribe, or is in inventory and wants to
  check a list — the current close-everything flow fights that.
- **Reframe (2026-08-07):** this is NOT a coexistence problem. VS already runs many dialogs at once
  (Scribe+inventory, Handbook+inventory, dozens of block inventories), with **Alt** toggling
  cursor-vs-camera mode. The *only* missing piece is a **trigger** to open a held Scribe item while
  a GUI already has focus — today the sole open-path is a world-space right-click, which requires no
  GUI be open, so you're forced to close everything.
- **Engine facts confirmed** (decompiled — see `VSAPI-NOTES.md` "can a client hotkey OPEN a dialog
  while another GUI is focused"): a client hotkey CAN open a Scribe dialog while inventory/Handbook
  is open, and `TryOpen()` **coexists** (never closes the others — it only un-focuses them). One
  hazard: a focused text field (Handbook search / Scribe editor row) eats plain keys → bind a
  **modifier combo** or mark the hotkey global. **No coexistence spike needed.**
- **Trigger design (decided 2026-08-07 after a two-front UX-research pass — VS-native precedents +
  cross-game/note-app patterns):** ship a **single hover-aware toggle hotkey**, mirroring vanilla's
  Survival Handbook (`H`/`Shift+H`) and Scribe's own pin-HUD (`P`) rather than inventing a pattern.
  - *Hotkey type:* register in `HotkeyType.GUIOrOtherControls` (a **modifier combo or a free
    non-letter key**, never a bare letter under the default `CharacterControls` type — that type is
    suppressed while a dialog is focused and a focused text field swallows plain letters). Scribe's
    `scribepinhud` on `P` already does exactly this (`ScribeModSystem.cs:245`, with a comment
    "so it fires even while a dialog is open") — mirror it. Default key + a Scribe Settings rebind;
    avoid the taken defaults (E, C, H, M, T, Q, P, F-keys).
  - *Dialog exposes `ToggleKeyCombinationCode`* so re-pressing the key closes just that window (the
    vanilla World Map / Macro Editor toggle idiom).
  - *Which item opens — resolution ladder (revised from the old "error on multiple"):* **(1) hovered
    inventory slot** (`InventoryManager.CurrentHoveredSlot` — the exact field the Handbook's H/Shift+H
    and the Q-drop key use; if you're hovering a Scribe item, *that* one opens, zero ambiguity) → **(2)
    active hotbar slot** if it's a Scribe item → **(3) last-used Scribe item** (cheap to store, correct
    ~90% of the time) → **(4) single-candidate auto-open** if the player owns exactly one anywhere. Only
    if none of those resolve does anything user-facing happen. This replaces the earlier plan to pop a
    "you have multiple Scribe items" error — research flagged that error as the one dead-end in the
    flow (it punishes a legitimate state with no path forward); last-used-wins removes it at near-zero
    cost.
- **Quick-capture — reconsidered, likely redundant (2026-08-07):** an earlier note here proposed a
  separate one-key "quick-jot" mini-dialog (the v5 spec's `scribequickadd`). On review that is
  **largely already shipped**: the v1.0 **Shift+right-click quick-add** opens the editor with a fresh
  task at the top, caret focused — one gesture, no scrolling. And the *plain-Enter* editor gesture
  already commits-and-inserts-a-new-task-below (`ScribeMultilineField.cs`), so in-editor capture is
  also a single key. Once the hover-aware **open trigger** above exists, a distinct one-line dialog
  adds little over "open the item + Enter." **Not carrying quick-capture as its own feature** unless a
  concrete gap surfaces that the trigger + Shift+RC + Enter don't already cover. (Guards against the
  recurring trap of re-listing shipped capability as backlog — checked against CHANGELOG 1.0.)
- **Multi-window ergonomics to bank (DLL-confirmed, flag to the eventual spec):** (a) **Escape closes
  *every* open dialog at once** — vanilla has no per-window Escape (`GuiManager.OnEscapePressed` loops
  the whole open set), so single-window dismissal must be the window's own X button or a re-press of
  its toggle key. (b) **Vanilla draws no focus indicator** — with Scribe + Handbook both open, nothing
  shows which one keyboard input goes to; if that matters (it does, since both have text fields),
  Scribe must render the active-window cue itself. (c) LibGUI's `WindowConfig.Position` lets us anchor
  Scribe **off-center** so it doesn't open exactly atop a center-anchored inventory/handbook (the
  "did the first window vanish?" failure mode); persist a user-dragged position rather than
  re-centering on reopen.
- *Value/effort feel:* :star::star: value (quality-of-life, grows with cross-window features),
  **S–M effort now** that the mechanics are known and the trigger is decided — the remaining work is
  implementation, not an engine or design unknown. Rides along with the v1.2 New Task dropdown (that's
  what makes cross-window use matter).

### 4.4 Draggable pinned-task HUD — 🆕 new (proposed for v1.1)
Let the player **drag the pinned-task HUD to reposition it**, the way VS windows offer a
fixed↔movable toggle that frees a grab handle. Author wants this in **v1.1** and wants it
**nondestructive** to a player's existing HUD placement across the update.
- **Why NOT the native mechanism (the load-bearing constraint):** VS's built-in movable-window
  affordance is `WindowConfig.Draggable` — it frees the OS/native window grab handle, which is
  exactly the "goofy Mac chrome" to avoid. **On macOS the native button/chrome hit-test is broken:**
  every default VS button's clickable area is only **50% width × 50% height pinned to the top-left**
  (a Retina/scaling bug), so the top-right, bottom-left, and bottom-right quadrants are *dead pixels*.
  A native grab handle inherits that same broken hit-test. So the drag handle must be a **custom
  LibGUI affordance**, since LibGUI does its own hit-testing and isn't subject to the native chrome
  bug. The HUD already deliberately sets `Draggable = false` (`HudScribePins.CreateWindowConfig`) —
  keep it false; do not flip it.
- **Nondestructive is essentially free — the position is ALREADY persisted, and not as raw pixels.**
  The HUD position is stored in `ScribePlayerSettings` as `HudAnchor` (1 of 7 corners/edges) +
  `HudOffsetX`/`HudOffsetY` nudge (clamped ±300), re-applied every frame in `HudScribePins.ApplyAnchor()`.
  There is *already* a Settings UI writing those exact fields (anchor picker + offset sliders). So a
  drag gesture is just a **second, direct way to write the same two offset fields** the sliders
  already write — no new persisted state, no schema/codec bump, and a pre-1.1 player's stored
  anchor+offset is read unchanged after the update (fully nondestructive by construction).
- **No new engine primitive needed.** LibGUI's `GestureDetector` already exposes
  `onPress`/`onMove`/`onRelease` (confirmed in `reference/vslibgui/.../GestureDetector.cs`). A drag =
  onPress captures the grab anchor + starting offset → onMove converts the cursor delta into
  `HudOffsetX`/`HudOffsetY` (respecting each anchor's sign convention, since +offset always means
  "toward center" — see the `ApplyAnchor` switch) → onRelease commits via
  `modSystem.UpdateMySettings(...)` (the same persist path `ToggleCollapsed` uses). Clamp to the
  existing ±300 `MinHudOffset`/`MaxHudOffset` and the on-screen clamp `ApplyAnchor` already applies.
- *Open Qs:* (a) **Grab affordance** — a dedicated drag handle glyph in the header row (next to the
  chevron/gear), or make the whole header draggable (it already has a `GestureDetector` for collapse —
  would need press-drag vs. tap-collapse disambiguation)? A dedicated handle is cleaner and dodges the
  tap/drag conflict. (b) **Does dragging across screen quadrants re-pick the `HudAnchor`** (so dragging
  to the bottom-left snaps the anchor to `BottomLeft` and zeroes the offset), or does it only ever move
  the offset within the current anchor? Anchor-re-pick is more intuitive for a big move but is more
  work; offset-only is trivial but caps how far you can drag (±300 from the current anchor). Lean:
  offset-only for v1.1 (trivial, ships fast), anchor-re-pick as a later polish. (c) **Live preview vs.
  commit-on-release** — almost certainly live (move the window as you drag, persist on release).
- *Value/effort feel:* :star::star: value (direct-manipulation QoL that the Settings sliders already
  approximate), **S effort** — no new primitive, no new persisted state, reuses the existing
  offset fields + persist path. A clean, self-contained v1.1 inclusion.

### 4.5 Auto-substitute typed arrows (`->`/`<-` → `→`/`←`) — ✅ DELIVERED (v1.1, `add-arrow-substitution-and-cuneiform-glyphs`)
As the player types, turn the ASCII digraphs `->` and `<-` into the Unicode arrows `→`/`←`
automatically in their task/note text — the small "smart replacement" nicety every editor has, so a
player can jot "mine -> smith" and get "mine → smith".
- **Open Q — where the substitution lives:** it should be a **text-layer transform, not a Core-model
  transform**, so it works uniformly across every writing surface (lectern, notebook, tablet) and both
  task and note kinds, and doesn't bake presentation into the stored document. Two candidate seams:
  (a) at the input widget, rewriting the buffer on keystroke as the digraph completes (immediate, visible
  as you type, but has to be careful about caret position after the 2-char → 1-char shrink and about not
  eating a legitimately-typed `->`); (b) at render/display time only, leaving the stored bytes as `->`
  (safer, fully reversible, but the stored text and the shown text diverge, which complicates search and
  copy/paste). Lean (a) for the "feels like a real editor" payoff, but scope the caret handling honestly.
- **Interaction with the cuneiform tablet (see 1.7).** On a tablet the substituted `→`/`←` then hits the
  cuneiform layout, which has **no arrow glyph today** — so this feature and **1.7** (draw the arrow
  glyphs) are a matched pair: 4.5 produces the character, 1.7 makes it render on a tablet. On the lectern
  and notebook (normal font) the Unicode arrow renders with no new art. Decide whether to gate 4.5 behind
  1.7 or ship 4.5 first and let tablets show a fallback until the glyph lands.
- **Scope guard:** keep it to the two horizontal arrows for v1.1 (matches 1.7's lean). Resist growing it
  into a general autocorrect/emoji-substitution engine — that's off-theme (the "age-of-computers" concern
  the author flags on 4.2 undo) and a maintenance sink. A tiny fixed digraph table is the whole feature.
- *Value/effort feel:* :star: value (small delight, "real editor" feel), **S effort** on the lectern/
  notebook (a bounded input-buffer transform, no Core/codec change); the tablet half is gated on 1.7's art.
  Cross-reference **1.7** (the tablet glyph) and [[cuneiform-character-coverage-plan]].

---

## 5. The Writing Desk (later big tier) — 🗺️ on roadmap (v4)

The Desk is the next *big* tier (`docs/specs/v4-writing-desk.md`): private
owner-gated block, consolidates notes, kanban tabs, within-document search, faction/shared
task-assignment. The ideas below expand or stress-test that scope.

### 5.1 Copy/paste between items — 🗺️ on roadmap (core Desk feature)
Take the full data of one Notebook (or other item) and copy it onto another. **Author calls this
the one the Desk "definitely should include."** Aligns with the roadmap's existing "copy/paste
export between Lectern/Notebook/Desk."
- *Decided 2026-08-07:* this is a **Desk-only feature** — explicitly distinct from the basic
  in-text copy/paste already shipped in the editor. Copying a *whole document's data* between items
  is part of the Desk's serious-organization fantasy, so it does **not** get pulled into an interim
  release. Stays parked with the Desk tier.

### 5.2 CSV / spreadsheet import-export — 🗺️ on roadmap ⚠️ concern flagged
Take tasks **out of and into** the game as CSV, pasteable directly into Excel / Google Docs; build
a list outside the game and bring it back.
- *Roadmap status:* the chronicle spec already lists "cross-world JSON export/import" as a meta
  workstream; CSV-for-spreadsheets is the user-facing framing of the same capability.
- *Concern (author):* part of the broader Desk immersion worry below.

### 5.3 Assignment / notifications — 🗺️ on roadmap (v4 faction feature)
If a player has visited the Desk, they can be **notified of tasks someone else assigned** them and
pick those into their pins/notebooks. Matches v4's "faction/shared task-assignment"; the
faction-backing choice is a deferred open decision (roadmap Open Decisions #1 — VS has a
first-party player-group system, so possibly no dependency).

### 5.4 Search — 🗺️ on roadmap (v4 within-doc search) ⚠️ concern flagged
Highlight/filter tasks. Could be Desk-exclusive or more general.
- *Author's own reservation (preserved):* "I haven't heard of anyone actually use the mod yet to
  know if they're going to have that many tasks that a mod would be useful." → **validate demand
  before building.** Roadmap already scopes within-doc search to the Desk and global
  cross-document search as a later separate change.

### 5.5 Custom renamable tabs / sections — 🆕 new ⚠️ concern flagged (author leans "bad")
Multiple renamable sections you can move tasks between.
- *Author's own verdict (preserved):* "this is something you'd want everywhere if you're a project
  manager, but seems pointless to everyone else. Might turn Vintage Story into a JOB. Bad."
- *Triage lean:* keep parked; the v4 kanban tabs (Active/Backlog/Completed) already cover grouping
  without turning the game into project-management software.

### 5.6 Desk crafting cost — 🆕 new
Maybe the Desk takes **iron nails** specifically — signals it's a "more serious use" block, gates
it behind the metal age.

### 5.7 Desk model — 🆕 new ⚠️ concern flagged (scope + wood-variant question)
Two directions: (a) a **small block that sits on top of** a player-built (chiselable) desk — lets
players customize the surrounding furniture; or (b) a **fully-modelled desk block** (wood, legs,
drawers).
- *Open Q (author):* how do we account for **different woods**? "We can look at mods that have
  multiple wood-grain models for examples." (This is a known VS pattern — texture-swap by wood
  type.)

### 5.8 Overarching Desk concern — ⚠️ concern flagged (the big one)
Author's own framing, preserved verbatim in spirit: *"The Desk's fantasy is really expansive, and
very developed — but it might not feel as grounded and real as the rest of Vintage Story, which is
the main thing that concerns me. That and the massive scope. Maybe it's an iterative thing..."*
- **Triage implication:** the Desk should ship as a **minimal core release** (place block + copy/paste
  + maybe kanban tabs) with the expansive features (CSV, assignment, search) as later increments —
  each earning its place against real player demand and the immersion bar.

---

## 6. Integrations & tech-risk items

### 6.1 Modular Backpacks integration — 🆕 new ⚠️ concern flagged
Let players attach their Notebook to a backpack. Modular Backpacks already supports attaching tools
that carry collected data, so there may be a hook.
- *Concern (author):* "Might be a mess."
- *Guardrail check:* this would be a **new mod dependency** (soft, presumably) — the CLAUDE.md
  guardrail requires asking before adding any dep. Investigation-only until then.

### 6.2 VTML support in tasks — 🆕 new ⚠️ concern flagged
Support [VTML](https://wiki.vintagestory.at/VTML) (Vintage Story's rich-text markup) in task text.
- *Author's own read (preserved):* "it might not be that crazy. Or maybe it's extremely crazy.
  Hopefully no one asks for this."
- *Triage lean:* investigation spike to gauge effort before committing; low priority unless
  requested.

---

## 7. Presentation / marketing

### 7.1 Remake the ModDB page with Photoshop explanatory art — 🆕 new
Richer mod-DB page: **callout graphics** (Photoshop) for the HUD and for crafting, not just prose +
screenshots.
- *Note:* the three parallel mod-page files (`docs/media/mod-page*.{txt,html}`) stay text; this is
  new **image assets** (annotated screenshots) to embed. Pairs naturally with a future release's
  screenshot pass.

---

## Cross-reference summary — idea → roadmap

| Idea | Roadmap home |
|------|--------------|
| Desk copy/paste, CSV, assignment, search (5.1–5.4) | v4 (`v4-writing-desk.md`) + chronicle export |
| Custom tabs (5.5) | v4 kanban tabs (author leans cut) |
| Bulletin board world-text + check-in-world (3.2) | v6 (`v6-bulletin-board.md`) — adds in-world render req |
| Linked/handbook task (2.3) | chronicle "handbook bookmarking" (lighter cousin) |
| Tracked numeric task (2.2) | chronicle "milestone-suggested via inventory polling" (shared primitive) |
| Lectern model / block refinement (1.4) | lectern-gui-polish "block shape refinement" (art-gated) |
| Everything else (1.1–1.3, 1.5, 1.7, 2.1, 2.4, 3.1, 4.1, 4.2, 4.5, 5.5–5.8, 6.x, 7.1) | 🆕 net-new — no roadmap home yet |

## Prioritization — our chosen order (decided 2026-08-07)

**Framing (decided):** there are no "commitments" here — this is a personal project, so the
delivery order is whatever sparks joy. A bias toward **frequent, small releases** is deliberate:
each release bumps Scribe up the ModDB "recently updated" board, which plausibly drives downloads
— a source of genuine motivation. (Perspective check: v1.0 shipped today; the mod didn't exist 10
days ago. We're fine. Ship often, enjoy it.)

### :link: Dependency map (pressure-test 2026-08-07 — what actually gates what)

A single-dev backlog single-threads, so the useful question isn't "what's the version list" but
"what unlocks what." The post-v1.1 items untangle like this:

- **Held-item open trigger (4.3)** — *zero dependencies*, fully designed, S–M. Can ship as its own
  small release **anytime**; it does not need the New Task dropdown (we'd coupled them on a soft
  rationale — mechanically they're independent).
- **New Task dropdown (2.1)** — a cheap shell (M), but *pointless without at least one non-Standard
  kind to host*. So it never ships alone; it ships *with* Linked or Tracked.
- **Linked (2.3)** and **Tracked (2.2)** — **both sit on ONE shared dependency: an easy in-game
  *picker*.** Linked needs "pick a Handbook entry"; Tracked needs "pick an item type." This is the
  keystone — and it was the author's stated fear ("it has to be EASY, and there's no type-ahead
  search in the game"). *Crack the picker and both kinds open up.* **RESOLVED 2026-08-08** — see
  the "Picker — RESOLVED" subsection below. Bottom line: it's a *Small* embedded search-first
  sub-view, not a scary new screen and not an XL rebuild.
- **Crafting (2.5)** — sits *on top of* Tracked (same inventory-poll + progress machinery; a recipe
  is just the *source* of the goals). Gated on Tracked existing. A later follow-up, not a peer.
- **Mapped (2.4)** — orthogonal; its own map-UI surface, doesn't touch the picker. Independent, heavier.

**Keystone takeaway:** the picker is the lock on the entire "task kinds" family. It's a *bounded
research question*, not an open-ended build — resolve it first, and the sequencing of 2.1/2.2/2.3
falls out of the answer. Until then, the trigger (4.3) is the one post-v1.1 feature that can move
with no blockers.

### :key: Picker — RESOLVED (2026-08-08, two research passes: discoverability UX + VS-native feasibility)

**Decision — first cut is search-only, embedded, Small.** An **auto-focused search field sitting
on top of an always-populated, virtualized result list**, opened by a labeled **"+ Add item"**
button (never a bare hotkey/gesture as the sole opener — a curious, non-super-user player only
reliably finds a labeled button). This is the creative-inventory / JEI model the player is already
trained on: a novice sees a default set of clickable items and clicks; a power-user types "flax"
and hits Enter. It **never shows an empty resultless box** (the exact failure mode that makes raw
type-ahead undiscoverable), because a default result set is visible from the moment it opens.
Directly serves the real use case — *"I need 100 flax and have zero"* — which hover-to-pick
**cannot** (you can't hover an item you don't have). Category browsing is deferred (see below).

**The author's two fears, both refuted with source evidence:**

- **"A scary new screen suddenly pops up" — NO.** Scribe already switches among 6 views inside its
  one window (Read / Editor / Pinned / Visitors / History / Timer) via a `viewMode` flag +
  `ForceRebuild()` (`src/Mod/ScribeDialogBase.ViewSwitching.cs`, `ScribeDialogBase.cs:87`). The
  picker is just a **7th view in that same machinery** — same "the dialog changed pages" feel the
  player already knows, no new `GuiDialog`, no context switch. (LibGUI also ships `Overlay` +
  `Stack`/`Positioned` if a float-over-current-view drawer is ever preferred, but the existing
  view-switch is lowest-risk.)
- **"An insane amount to rebuild from scratch" — NO, it's S.** The heavy parts already ship in the
  `gui` 3.1.0 dep. Reuse: `TextField` + `TextEditingController` (Scribe already uses these),
  `ListView.Builder` (virtualizes — keeps ~15 live rows out of thousands, so a full catalog is
  performance-safe), and `ItemStackDisplay(new ItemStack(collectible))` to draw each item's icon
  from just its collectible. Filter is a **public API**: `ItemStack.MatchesSearchText(world, text)`
  / `StringUtil.ToSearchFriendly`. Catalog source is `capi.World.Collectibles`. Net-new is just a
  row widget (icon + name), the controller→refilter wiring, and the on-pick callback. **The XL fear
  was imagining a reimplementation of the creative-inventory `GuiElementItemSlotGrid` — which is
  welded to `IInventory` and which you never touch.** Precedent: the survival Handbook builds its
  whole browsable catalog off exactly this seam (`ModSystemSurvivalHandbook.SetupBehaviorAndGetItemStacks`
  → `GetHandBookStacks` over `capi.World.Collectibles`).

**Categories exist and cost zero taxonomy — but are deferred to a later cut.** Every item carries
`CollectibleObject.CreativeInventoryTabs` (14 fixed tab codes: `general, flora, terrain, decorative,
clutter, construction, mechanics, aquatic, items, liquids, tools, clothing, creatures, meta`), plus
`EnumTool Tool` (Pickaxe/Axe/Sword/…) and 1.22's `TagSet Tags`. Other mods self-register into these
same tabs, so the taxonomy auto-extends across mods/updates — **Scribe authors and maintains
nothing.** The one honest asterisk: tabs are *coarse* — most items dump into a giant `"general"`
bucket — so category **drill-down works but isn't granular**. Therefore, when categories are added
they should be an **optional filter chip on top of search**, never a mandatory drill-down over VS's
coarse taxonomy. Not in the first (search-only) cut.

**Icon rendering confirmed:** `new ItemStack(CollectibleObject, stacksize)` is a valid display stack
for any collectible (no inventory slot needed); LibGUI's `ItemStackDisplay` renders it via a cached
offscreen `ItemStackRenderer`, so a list of many icons doesn't re-render each frame.

**Needs-confirmation before build (small):** exact vanilla collectible count (functionally moot —
both `ListView` and `GridView.Builder` virtualize); how thoroughly vanilla items populate 1.22
`Tags` (only matters if we ever prefer tag facets over creative-tab facets); and a sanity-check that
`GridView.Builder` virtualization in shipped `Gui.dll` 3.1.0 matches the 2.0.0 source clone (only
relevant if a *grid* cut is chosen later — the search-only list cut doesn't need it).

**Real-estate constraint (author, 2026-08-08) — the picker must not permanently cost chrome.** The
task manager is deliberately kept *small* (< 500 px wide in-game); buttons must earn their place and
the row already carries pin + delete. Two facts keep the picker cheap here:
- **The picker sub-view itself is free** — it's a temporary full-window takeover (the 7th
  `viewMode`) that borrows the whole dialog while open and hands it back. Nothing new sits in the
  steady-state layout.
- **Two entry points, both DECIDED 2026-08-08:**
  - **(a) Task *kind* — via the New Task dropdown (2.1).** Choosing Standard / Tracked / Linked
    rides on upgrading the existing **New Task** action to a small dropdown — it adds *no* new
    button, just upgrades chrome we already have. (This is 2.1 exactly as specced.)
  - **(b) *Item* pick — via a kind-gated expanding row.** A row reveals its inline "pick item"
    control **only when its kind is Tracked**; Standard rows stay pristine. Chrome appears exactly
    when it's relevant and costs nothing on the common case, honoring the "earn its place" rule
    without a permanent per-row button. (Chosen over right-click-a-row — discoverability risk for a
    curious player — and over a hover-revealed icon — visual noise on every Tracked row.)
  - *Note the clean composition:* the New Task dropdown picks the kind → a Tracked row is created
    already-expanded with its "pick item" control visible → clicking that control opens the
    search-first picker sub-view. Kind-choice and item-pick never both crowd the row at once, and
    the < 500 px steady-state layout is untouched.
  - **Handbook-pin serves BOTH Tracked AND Crafting (author, 2026-08-08).** The injected handbook
    content should offer **two** actions on the item's page, each its own link/button with a short
    description and a **custom Scribe SVG icon inline** to set it apart:
    - **"Track this"** → creates a Tracked task (collect *N* of this item).
    - **"Craft this"** → creates a Crafting task (§2.5): capture the item's ingredients × the desired
      count. **This link appears ONLY if the item is actually craftable** — gate it on a recipe
      lookup (see the craftability-detection note under §Handbook-pin below).
  - *Inline SVG icon — CONFIRMED FEASIBLE:* vanilla `IconComponent` (a `RichTextComponentBase`,
    `VintagestoryAPI.dll`) takes an `iconPath`, loads it as an SVG asset
    (`capi.Assets.TryGet(...WithPathPrefixOnce("textures/"))`), and draws it via `capi.Gui.DrawSvg`
    tinted to the font color — the *same* DrawSvg path Scribe already uses for its icon-buttons. So a
    Scribe glyph can be embedded inline in the handbook link text. (Details in `VSAPI-NOTES.md`.)
    Since custom SVG works, no Unicode-symbol fallback is needed.
  - *Link vs. button — DECIDED 2026-08-08: inline clickable text (`LinkTextComponent`) + inline SVG.*
    Author's first preference was a real button, even a vanilla one. But a handbook page body is
    **rich-text** (`RichTextComponentBase[]`) — you **cannot** embed an interactive button widget in
    the text flow; the only inline-clickable component is `LinkTextComponent`. A real button is only
    possible as a **separate floating `GuiDialog` overlay** anchored to the handbook window via a tick
    listener (exactly Tallybook's "← Back to Tallybook" pattern, `HandbookReturnButton`) — which
    **floats detached from the specific item** (it reads the currently-open page, can't sit on the
    item's own line or scroll with it) and, if built with vanilla `AddSmallButton`, hits the
    **macOS top-left-quadrant hit-test bug** (a LibGUI overlay would dodge that but is still detached).
    Chosen the inline link because it visibly belongs to the item and scrolls with it; the custom SVG
    gives it enough visual weight to read as an affordance, not plain prose.
  - **(c) *Item* pick — via a handbook "Add to task" link (a SECOND, decoupled method).** In
    addition to the in-Scribe search picker, an "→ Add to task" link is injected into the vanilla
    handbook page for any item, so the player can pin the *exact item they're reading about* straight
    into a Tracked task. Modelled on **Tallybook** (`tallybook` mod, decompiled 2026-08-08). Decided
    2026-08-08 to **support both** entry methods — but explicitly **NOT committed to shipping them in
    the same release** (author, 2026-08-08). **Handbook-pin is the first cut** (author, 2026-08-08:
    *"the handbook pin is easier, frankly — let's prioritize that first"*) — it's genuinely the
    smaller build: a Harmony postfix + a "create Tracked task from this ItemStack" callback, with no
    new viewMode. The in-Scribe search picker (a/b) is the decoupled follow-up (it needs the whole
    7th viewMode: search field + virtualized list + row widget + filter wiring). Two reasons it's
    worth the extra surface, not just
    redundancy: (i) it captures the **exact variant** the handbook is showing (a plain search hit
    resolves to the *base* `ItemStack`, which is ambiguous for multi-variant items like soil), and
    (ii) it's in-context discovery — you're already looking at the thing. See §Handbook-pin
    (Harmony) below for the mechanism + the technique decision it requires.

**Handbook-pin mechanism + technique decision (from decompiling Tallybook 0.3.6, 2026-08-08).**
Tallybook adds its handbook link via a **Harmony `Postfix` on
`CollectibleBehaviorHandbookTextAndExtraInfo.GetHandbookInfo`** — the per-*item* method that builds a
page's `RichTextComponentBase[]` body. The postfix just *appends* a `LinkTextComponent` to the
returned array. Key facts this settles:
- **It's robust to handbook-overwrite mods** (the author's stated worry) *because* it patches the
  collectible's content-builder, **not** `GuiDialogHandbook`. Any handbook dialog — vanilla,
  survival, or a third-party replacement — that renders item pages calls `GetHandbookInfo` for the
  body, so the link appears regardless of which dialog mod "won." And a `Postfix` appends, so multiple
  mods can patch the same method without excluding each other (the opposite of dialog-replacement
  mods that stomp one another). Only breaks if a handbook mod rewrites/bypasses `GetHandbookInfo`
  itself — rare.
- **Harmony is NOT a new dependency.** It ships *inside* Vintage Story at
  `/Applications/Vintage Story.app/Lib/0Harmony.dll` — any code mod can `using HarmonyLib` with zero
  download and nothing declared in `modinfo.json` (Tallybook declares only `game >= 1.22.0`). So the
  CLAUDE.md "no new mod dependencies" guardrail **does not apply** here — there is nothing to bundle,
  install, or version-track.
- **What it IS is a technique decision: runtime IL-patching in Scribe's codebase.** Scribe has been
  deliberately Harmony-free (it rebuilt the GUI on LibGUI rather than patch anything). A
  `GetHandbookInfo` postfix is low-risk as patches go (append to a return array, wrapped in
  try/catch, degrade gracefully if the patch ever fails to apply — exactly what Tallybook does), but
  any IL patch is inherently more update-fragile than pure-API use. **Decided 2026-08-08: accept
  Harmony as an acceptable technique for this feature.** This is a first for the project — keep the
  patch minimal, defensive, and confined to this one seam.

**Two links, both custom-SVG-labelled; Craft is craftability-gated (author, 2026-08-08).** The
postfix appends **two** actions to the page (each a link with a short description + an inline Scribe
SVG via `IconComponent`, confirmed feasible above): **"Track this"** (always) and **"Craft this"**
(only when the item is craftable).
- *Craftability detection — cheap, from Tallybook's `RecipeProbe`:* build a
  `Dictionary<outputShortCode, List<GridRecipe>>` by walking `capi.World.GridRecipes` **once** (cache
  it; invalidate on recipe reload), then "is this craftable" is a key lookup on the page's item code.
  Tallybook also excludes recipes that *consume their own output* (avoids degenerate self-cycles) and
  groups genuinely-distinct recipes (vanilla vs. modded) for an alt-recipe chooser.
- *⚠️ Scope caveat to decide in design:* `capi.World.GridRecipes` is **grid crafting only** — it does
  NOT include smelting / cooking / knapping / clayforming / barrel / firepit recipes. A naive gate
  therefore hides "Craft this" for e.g. a smelted ingot or a knapped tool head even though the player
  *can* make it. Options: accept grid-only for the first cut (simplest, matches Tallybook), or fold
  in the other recipe registries later (each is its own `capi.World.*Recipes`-style list). Note this
  same grid-only limit bounds the §2.5 recursive-ingredient ambition too.

*Not specced yet — sequenced into the task-kinds cluster (2.2/2.3/2.5), not pulled forward.
Handbook-pin is the first cut and serves both Tracked (2.2) and Crafting (2.5); the in-Scribe search
picker is a decoupled follow-up.*

### :rocket: v1.1 — interim polish release (NEXT)
Cheap, visible wins bundled into a fast follow-up:
- **4.1 Faster delete (hover half)** — kill the mouse-wiggle-to-reveal loop after a row deletes.
  Small, high-satisfaction, closest to a bug. ✅ implemented (`fix-list-collapse-stale-hover`),
  playtest-confirmed 2026-08-08 (HUD-unpin re-test pending).
- **4.1b Fluid mass-delete (click half)** — the sibling bug the 4.1 playtest surfaced: mid-collapse the
  delete button is now *visible* but the *click* misses until the collapse ends, because the departing
  ghost-snapshot intercepts the hit-test. Own small change (make the snapshot hit-test-transparent).
  Author-committed to v1.1 2026-08-08 so "faster delete" ships whole.
- **1.2 Per-tab subtitles + colour theming** — tabs feel like distinct places, not just paper
  swaps.
- **4.4 Draggable HUD** (author-requested for v1.1, 2026-08-08) — custom LibGUI drag handle on the
  pinned-task HUD, writing the existing `HudOffsetX/Y` fields (nondestructive; no new state). Small,
  self-contained, direct-manipulation QoL. Avoids the native movable-window chrome (broken on macOS —
  50%×50% top-left hit-test).
- **1.6 Cuneiform `<`/`>` glyphs** (author-requested for v1.1, 2026-08-08) — draw two new glyphs in
  glyph-forge, regenerate the bundle (count 54 → 56), bump the `CuneiformTests` assertion. Trivial,
  proven code path; art-gated on drawing the two glyphs.
- **4.5 Auto-substitute typed arrows** (author-requested for v1.1) — turn typed `->`/`<-` into `→`/`←`
  in the text buffer. Bounded input-buffer transform, no Core/codec change; ships on lectern/notebook
  immediately.
- **1.7 Cuneiform arrow glyphs (`←`/`→`)** (author-requested for v1.1) — the tablet-tier companion to
  4.5: draw the arrow glyphs in glyph-forge so the substituted arrows render on a tablet. Same
  art-gated path as 1.6; author 1.6 + 1.7 together (one glyph-forge session, one bundle regen).

*Why these:* all small, all immediately visible to a returning player, and together they justify a
release note without a big build. Keeps us high on the "recently updated" board. (4.4, 1.6, 4.5, and
1.7 are the newest adds; each is self-contained, so if any slips the rest still stand as the release.
1.6/1.7 are art-gated — they ship only once the glyphs are drawn; 4.5 ships on the lectern/notebook
without waiting on 1.7, tablets picking up the arrow glyph when it lands.)

### :mag: Exploration candidate (scope-check before building)
- **2.2 Tracked (numeric progression) tasks** — high value, but *feels* like a big chunk. Author
  is open to being proven wrong: **run an exploration/spike first** to size it honestly before
  deciding whether it's one release or a mini-tier. Don't commit to a version until scoped.

### :desktop_computer: Desk-bound (do NOT pull forward)
- **5.1 Copy/paste a whole Scribe item** — **reclassified as a Desk feature**, distinct from the
  basic in-text copy/paste already shipped. This is "clone the full document data from one item to
  another," which belongs to the Desk's serious-organization fantasy, not an interim release. Stays
  parked with the Desk (v4), not a near-term item.

### :thinking_face: Investigation spikes (effort unknown — scope before committing)
6.1 Modular Backpacks (dependency ask), 6.2 VTML, 3.2 in-world board render, 3.1 hover preview.

### :snowflake: Parked / leaning-no
4.2 undo (off-theme, "age-of-computers"), 5.5 custom renamable tabs ("turns VS into a JOB").

### Later clusters (when we get to them)
- **New Task dropdown** = 2.1 dropdown + 2.2 tracked + 2.3 linked (2.4 mapped is heavier — its own
  change). *Note: 2.2 may graduate out of this cluster if the exploration says it's release-sized
  on its own.*
- **Desk (minimal first)** = place block + 5.1 copy/paste; then 5.2 CSV / 5.3 assignment / 5.4
  search as later increments gated on real demand + the immersion bar.

## Meta / off-project ideas

- **Start a dedicated Vintage Story Slack workspace** — a separate workspace to organize Scribe
  (and future VS-modding) planning, instead of living in the Salesforce enterprise workspace where
  the Big Board canvas currently sits. Low effort, purely organizational. (Added to the board
  half-jokingly, but genuinely useful for keeping game-dev context separate.)
  - **Verdict (2026-08-07): parked — leaning strongly no.** The decisive issue is **MCP
    portability**: the Claude↔Slack connection is authorized against the Salesforce *Enterprise
    Grid* org (`enterprise.slack.com`, team `T01G0063H29`), which is employer-provisioned. A
    personal free workspace is a separate tenant, and there's no reason to expect the existing MCP
    grant to reach it — so we'd likely **lose CLI/MCP Slack access entirely** on the new workspace.
    That trades the *big* convenience (agent-driven canvas/message editing) for the *small* one
    (tidiness), whose upside was never clear. Free-tier limits pile on: 90-day history (>1yr
    permanently deleted), max 10 app integrations, one-to-one external only — more ways for the MCP
    to fail to attach. Only way to fully confirm portability is to actually try adding the connector
    to a new workspace; not worth the spend unless that itch returns.
