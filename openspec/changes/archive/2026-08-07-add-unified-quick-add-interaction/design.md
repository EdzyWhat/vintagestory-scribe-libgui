## Context

Scribe has three writing surfaces: the placed **Lectern** block, and two carriable items,
the **Notebook** and the **Tablet**. Each opens a Scribe document dialog, but their
right-click gesture maps grew independently:

- **Lectern** — plain right-click opens Read; `Shift`+right-click opens the plain Editor
  view (see `BlockScribeLectern.OnBlockInteractStart` → `wantEditor = Controls.ShiftKey`).
- **Notebook** — plain right-click opens Read; `Shift`+right-click falls through to the base
  `CollectibleBehaviorGroundStorable` for ground placement.
- **Tablet** — plain right-click opens the always-edit tablet dialog; `Shift`+right-click
  quenches a hard tablet **if** aimed at water, otherwise falls through to ground placement
  (`ItemScribeTablet.OnHeldInteractStart`, lines 69–88).

The roadmap's portability tier called for a standalone "quick-add hotkey." The player has
since redirected that: quick capture should be **item-contextual** — always acting on the
Scribe surface already in hand or under the cursor — not a global keybind that prompts with
no surface selected. This change unifies capture behind one gesture across all three
surfaces and retires the standalone-hotkey idea.

The constraint that shaped the modifier choice: `Shift`+right-click is the vanilla
ground-placement gesture, so taking it for quick-add on held items requires relocating
ground placement. We verified in `VSSurvivalMod.dll` that the vanilla **spear** advertises
`Ctrl`+`Shift`+right-click for its own ground placement — an established precedent to follow.

## Goals / Non-Goals

**Goals:**
- One gesture, `Shift`+right-click, performs quick-add on the Lectern, Notebook, and Tablet:
  open the editor, insert a new empty task at the top, focus the caret.
- Held-item ground placement (Notebook, Tablet) moves to `Ctrl`+`Shift`+right-click.
- The Tablet's water-quench keeps `Shift`+right-click, disambiguated purely by whether the
  player is aiming at a water block (water → quench, else → quick-add).
- Quick-add reuses the existing add-task document operation — no new Core capability, and
  it respects the document's task cap with the editor's existing "document full" feedback.
- Interaction help (`GetHeldInteractionHelp`, block hints) advertises the new gestures.

**Non-Goals:**
- No standalone/global quick-add hotkey. Quick-add is only ever engaged on a Scribe surface
  in hand or under the cursor.
- No change to `src/Core/` — this is a Mod-side interaction wiring change.
- No new dependency; no ConfigLib.
- The backpack container item stays out of scope (deferred / likely cut).

## Decisions

**1. Modifier scheme: `Shift` = quick-add, `Ctrl`+`Shift` = ground place.**
The player weighed `Sprint`(`Ctrl`)+right-click for quick-add and rejected it: a player
sprinting (holding sprint) who panic-right-clicks would open the editor and be halted
mid-flight — the "sprinting from a bear" failure. `Shift`+right-click is the safe,
discoverable capture gesture; ground placement is the rarer action, so it takes the
two-key `Ctrl`+`Shift` combo. *Alternative considered:* keep `Shift` = ground place and put
quick-add on `Ctrl`+`Shift` — rejected because capture is the high-frequency action and
should get the cheaper gesture.

**2. Scribe owns its own modifier enforcement — the base ground-storable gate is only a
hint below it.** Verified in `VSSurvivalMod.dll`: `CollectibleBehaviorGroundStorable.Interact()`
gates placement on `byEntity.Controls.ShiftKey` **only** (line 63:
`if (blockSel == null || val == null || !byEntity.Controls.ShiftKey) return;`). Its
`GetHeldInteractionHelp` sets `HotKeyCodes = StorageProps.CtrlKey ? {"ctrl","shift"} : {"shift"}`
— i.e. `CtrlKey` changes only the **displayed** hint, not the engine gate. The spear's
"Ctrl+Shift" is therefore a *displayed convention*, not an enforced one. Because
`ItemScribeTablet`/`ItemScribeNotebook` already override `OnHeldInteractStart`, they fully
control which modifier combos reach `base.OnHeldInteractStart` (and thus the ground-storable
behavior). Implementation: our override calls `base` (letting ground storage run) **only**
when `Controls.CtrlKey && Controls.ShiftKey`; a `Shift`-only press is intercepted for
quick-add and never reaches the base behavior, so no double-action can occur.

**3. Tablet quench stays on `Shift`+right-click, disambiguated by aim.** The existing code
already branches on `TryQuench(...)` succeeding (which requires aiming at a water container).
We keep that: on `Shift`+right-click, attempt quench; if it fires, done; if it does not
(not aimed at water), perform quick-add instead of falling through to ground placement.
Ground placement only happens on `Ctrl`+`Shift`. *Alternative considered:* a distinct
modifier for quench — rejected as unnecessary; aim already disambiguates and the player
confirmed "by aim."

**4. Quick-add reuses the existing add-task path; a thin "open + add-top + focus" seam.**
Quick-add is not a new document mutation — it is the editor's existing add-task operation
invoked at open time, with the new task inserted at the **top** and the caret focused. The
shared entry point (`ScribeDialogBase` editor-open) gains a parameter/seam so a caller can
request "open into the editor, add an empty task at index 0, focus its input." The Lectern,
Notebook, and Tablet interaction handlers all route through this one seam so the effect is
identical. If the document is at its task cap, the add is refused and the same "document
full" feedback the editor's add control raises is surfaced; the editor still opens.

**5. Lectern loses its `Shift` = plain-editor gesture; the Editor nav tab replaces it.**
Opening the editor *without* adding a task is still reachable — plain right-click → Read →
Editor nav tab. This is the only breaking change to a formerly-shipped gesture and must be
called out in the CHANGELOG/handbook.

## Risks / Trade-offs

- **[Breaking muscle memory for existing players]** — Lectern `Shift`+right-click and
  held-item `Shift`+right-click both change meaning. → Call out prominently in the 1.0
  CHANGELOG, the in-game handbook, and the wiki; the new gestures are surfaced in
  interaction help so they are discoverable in-client.
- **[Ground placement becomes harder to discover]** — moving it to `Ctrl`+`Shift` means a
  player who habitually crouch-places will now quick-add instead. → `GetHeldInteractionHelp`
  advertises `Ctrl`+`Shift`+right-click = place-on-ground for both items; this matches the
  vanilla spear so it is not a novel convention.
- **[Accidental quick-adds spawn empty tasks]** — a stray `Shift`+right-click on a surface
  inserts an empty top task. → Empty tasks are cheap and the caret is focused so the player
  is already positioned to type or immediately delete; this matches the "capture first,
  organize later" intent. No mitigation beyond the existing delete affordance.
- **[Tablet aim-ambiguity edge]** — a `Shift`+right-click aimed at a water block always
  quenches (never quick-adds). → Acceptable and intended; quenching a hard tablet is the
  rarer, deliberate action and the player confirmed aim-based disambiguation.

## Migration Plan

No data migration — this is interaction wiring only; documents, codec version, and
persistence are untouched. Rollout is the normal 1.0 build. Rollback is a code revert; no
save format changes to reverse.
