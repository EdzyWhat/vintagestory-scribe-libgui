## Context

`add-tablet-clay-type-backdrops` (archived pending) made clay type a **variant** — three discrete clay
tablet items (`scribetablet-clay-red`/`-blue`/`-fire`) plus wax — and added a `fired` stack attribute with
per-type fired backdrops, but recorded the firing mechanic (and any drying) as Non-Goals, so `fired` is
never set true in play and a soft clay tablet stays editable forever.

This change gives the clay tablet a full **wet → hard → fired** life-cycle. Clay type stays the item's own
variant; the state is expressed with two boolean stack attributes on top of it:

| State | `hard` | `fired` | Editable? | Reversible? | How reached |
|-------|--------|---------|-----------|-------------|-------------|
| Wet   | false  | false   | yes       | —           | freshly crafted |
| Hard  | true   | false   | no        | yes (water) | dries ~2 in-game days |
| Fired | (n/a)  | true    | no        | no          | fired in a firepit |

"Editable" is `!hard && !fired`. `fired` wins if both are somehow set (firing a hard tablet). Only two new
bits of state — no new variant, no new item — so the backdrops change's `fired` attribute is untouched and
`hard` slots in beside it.

Findings from reading the shipped API/lib (ground truth, decompiled — recorded in VSAPI-NOTES.md):

- **Native item state-transition system.** `EnumTransitionType` includes `Harden` (also `Dry`, `Perish`,
  `Ripen`, …). `TransitionableProperties` (JSON `transitionableProps` / `transitionablePropsByType`) carries
  `FreshHours` (stays in the current state), `TransitionHours` (how long the change takes once it starts),
  and `TransitionedStack` (the resulting item). The engine ticks it server-side in
  `UpdateAndGetTransitionStatesNative` against `world.Calendar.TotalHours`, storing progress under the
  stack's `transitionstate` tree attribute. **This is the engine-native "dries out over ~2 in-game days"**:
  a clay tablet declares a `Harden` transition with `FreshHours ≈ 48` game-hours → the same clay variant
  marked hard. Skipped for `ItemSlotCreative` and when `timeFrozen` is set.
- **Both transforms DROP custom attributes.** `CollectibleObject.OnTransitionNow(slot, props)` (virtual)
  clones the FIXED `props.TransitionedStack.ResolvedItemstack` and the caller `SetFrom`s it — it does NOT
  copy the input stack's attributes. Firepit `DoSmelt` (virtual) clones `combustibleProps.SmeltedStack` the
  same way. So a plain-JSON `Harden` or `smeltedStack` would produce a *blank* hard/fired tablet, losing the
  document. The fix for both is the same: override the hook, let base build the output, then copy
  `scribeDocument` from the input onto the output and set the state flag.
- **Transition is NOT reversible by the engine.** There is no built-in "un-harden." Rehydration (hard → wet)
  is our own action: swap the stack to the wet variant-state (clear `hard`, reset the `transitionstate` so
  the dry-out clock restarts) and re-carry the document.
- **The document already persists on the stack** via `ScribeDocumentAttributes` (`scribeDocument` bytes) and
  `fired` via `ItemScribeTablet.ReadFired`. Clay type is the item variant (no `ReadClayType`). No new packet.
- **The dialog** (`GuiDialogScribeTablet`) is currently always-edit: its ctor calls `EnterEditorMode(...)`
  before `Build()`. Read-only mode means NOT entering editor mode and rendering the existing read view (the
  base `ScribeDialogBase` has a non-editor render path the Lectern uses).

## Goals / Non-Goals

**Goals:**
- Dry a wet clay tablet into a hard clay tablet over ~2 in-game days (native `Harden` transition), carrying
  its document, of the same clay variant.
- Rehydrate a hard clay tablet back to wet — resetting the dry-out timer, keeping the document — when it is
  exposed to water (dropped into water, or held while the player enters water), like a torch extinguishing.
- Fire an unfired (wet or hard) clay tablet in a firepit into a fired clay tablet (`fired = true`), like
  vanilla clay pottery — no crucible, at the clay melting point — carrying its document.
- Make a hard OR fired tablet read-only: view-only dialog, no add/check/pin/edit, no title edit. Hard is a
  temporary read-only (rehydrate to edit again); fired is permanent.
- A hard-or-fired tablet with no document shows a small centered "set/fired without writing" message.
- Give each state a distinct backdrop (wet glossy / hard dried / fired ceramic) per clay type, making the
  fired backdrops (already built) and the new hard tint reachable so they can be tuned.

**Non-Goals:**
- Any `Dry`-vs-`Harden` realism beyond a single ~2-day timer; humidity, container-based drying rates, or
  weather effects on the timer.
- Wax tablet firing or hardening (wax melts / does not dry — out of scope).
- New fired/dried art beyond interim tints (deferred like the fired art was).
- Un-firing a fired tablet, wax-wipe, stylus edit gate — still deferred.
- Changing the wet tablet's editable behaviour or its 10-task / 1-pin cap.
- Any `src/Core/` change or new network packet.

## Decisions

### 1. State = two bools (`hard`, `fired`) over the existing clay variant
Keep clay type as the item variant. Add a `hard` boolean stack attribute beside the existing `fired`.
Editable ⇔ `!hard && !fired`. Add `ItemScribeTablet.ReadHard(stack)` mirroring `ReadFired`. This is the
least-churn representation: no new variant/item, the backdrops change's `fired` handling is untouched, and
every consumer (dialog, policy, backdrop) resolves the same two reads.

*Alternative — make hard/fired additional variant states like clay type.* Rejected: firing/drying would
have to swap the item code (not just an attribute), multiplying variants (red-wet/red-hard/red-fired × 3)
and complicating the smelt/transition output resolution for no gameplay gain — the state is per-stack, not
per-item-type.

### 2. Dry via native `Harden` transition + an overridden `OnTransitionNow`
Declare `transitionablePropsByType` on `scribetablet.json` for the clay variants only (`*-clay-*`, excluding
wax): a `Harden` transition with `freshHours ≈ 48` (≈2 in-game days; finalize in play), a short
`transitionHours`, and `transitionedStack` → the SAME clay variant. Override
`ItemScribeTablet.OnTransitionNow` to call `base.OnTransitionNow(...)`, then copy `scribeDocument` from the
input onto the hardened output and set `hard = true`. Wax has no transition props; an already-fired tablet
must not harden (guard on `fired`, or omit — see Risks). This reuses the entire vanilla transition tick
(server-authoritative, calendar-driven, persisted) and only adds the attribute carry-through.

### 3. Rehydrate (hard → wet) on water exposure, torch-style
A hard clay tablet softens back to wet when exposed to water, mirroring how a lit torch is extinguished:
- **Dropped into water:** hook the item-in-water path (the same signal vanilla uses to extinguish a dropped
  torch / the entity-item water check) to swap a hard tablet stack back to wet.
- **Held while the player enters water:** on the active-hotbar tablet, detect the holder entering water
  (swim/wade) and soften it.
Softening = clear `hard`, reset the `transitionstate` so the ~2-day dry-out clock restarts from full, and
keep the document. Fired tablets never rehydrate (guard on `fired`). Finalize the exact vanilla water-detect
hook during implementation (decompile the torch extinguish path); treat as a finalize-in-code detail.

### 4. Fire via `combustiblePropsByType.smeltedStack` + an overridden `DoSmelt`
Declare `combustiblePropsByType` for the clay variants only: each clay variant's `smeltedStack` → the SAME
clay variant, `meltingPoint` ≈ vanilla clay pottery (~650 °C, finalize against the shipped value),
`smeltedRatio: 1`, `requiresContainer: false`. Override `ItemScribeTablet.DoSmelt` to call `base.DoSmelt(...)`
then copy `scribeDocument` from the input and set `fired = true` on the output (clear/ignore `hard` — fired
wins). An unfired tablet fires whether wet or hard; a fired tablet is not itself combustible (guard so it
can't be re-fired into a blank). Reuses the whole vanilla firepit flow (temperature, duration, progress bar).

*Alternative — a bespoke firing interaction (right-click block, custom recipe).* Rejected: firepit smelting
is the idiom players expect for clay, is server-authoritative already, and needs no new UI.

### 5. Editability is the read-only switch, resolved once
The dialog and policy both compute `editable = !ReadHard(stack) && !ReadFired(stack)` at one place. When not
editable the dialog does NOT enter editor mode — it renders the inherited read view — and selects an
uneditable `ScribeDocumentPolicy`. One resolve point, mirroring how the backdrop selection is centralized in
`ForTablet`. The empty-state message (Decision 6) and the state-keyed backdrop (Decision 7) both branch off
the same two reads.

### 6. Empty-state message for a blank hard/fired tablet
When a hard-or-fired tablet's document has no tasks/notes (e.g. from Creative), the read view shows a small
centered lang-key line instead of an empty list, so it reads as intentional rather than broken. Wet blanks
open editable as before. Use state-appropriate wording (a hard tablet is "dried — dunk in water to edit"; a
fired tablet is "fired without any writing"); at minimum a fired key `scribe:tablet-fired-empty` and a hard
key `scribe:tablet-hard-empty`.

### 7. Backdrop keyed by clay type AND state
Extend `ScribeBackdrop.ForTablet` to take the state (or resolve `hard`/`fired` inside it) and return a
distinct spec per (clay type × {wet, hard, fired}). Wet = the existing soft backdrop; fired = the existing
fired tint; hard = a NEW tint (lighter, drier than wet, distinct from fired). Interim = tinted soft art,
same approach the fired tint already uses.

### 8. Uneditable document policy
Add a read-only `ScribeDocumentPolicy` preset (e.g. `UneditableTablet`) whose `CanAdd`/`CanPin` always deny;
the dialog consults it at the same add/pin mutation boundary the `Tablet` cap uses, and the editor entry
points are simply not offered. Applies to both hard and fired (editability is the discriminator, not which
flag is set). Keeps the wet-tablet `Tablet` policy untouched. Core, VS-API-free.

## Risks / Trade-offs

- **Two transform overrides drift:** we depend on the base building the output then us copying attributes in
  both `OnTransitionNow` (dry) and `DoSmelt` (fire); if a future engine version changes either shape the
  carry-through could silently break. Mitigate: copy defensively (guard nulls) and cover both transforms
  with an integration/Atlas check that the output retains the document bytes.
- **Rehydration hook uncertainty:** the exact vanilla "item/holder exposed to water" signal (torch
  extinguish path) must be confirmed by decompile; there may not be one clean shared hook for both the
  dropped-item and held-while-swimming cases. If held-while-swimming is costly, ship drop-in-water first and
  flag the held case — but the proposal promises both, so budget for a per-tick holder-in-water check on the
  active tablet. Flagged in tasks.
- **Timer tuning:** `freshHours ≈ 48` and firepit `meltingPoint`/`meltingDuration` are eyeballed and
  finalized in play (like the ingredient codes were), not blocking.
- **Read-only completeness:** EVERY edit affordance must be gone for a hard OR fired tablet (add row, check,
  pin, reorder, title edit, the always-edit ctor path). A missed path lets a set tablet be edited. Mitigate
  with an explicit read-only audit task covering both states.
- **Re-firing / re-hardening guards:** MaxStackSize is 1; a fired tablet must not be combustible or
  hardenable (no re-fire into a blank, no dry loop), and a hard tablet fires fine (unfired). Resolve the
  guards explicitly in tasks.
- **State precedence:** if both `hard` and `fired` are ever set (fire a hard tablet), `fired` must win
  (permanent, ceramic backdrop). Assert this in the editability/backdrop resolve.
- **Sequencing / archive-order drift:** modifies `clay-wax-tablet-item` / `tablet-dialog` /
  `scribe-document-policy`, last touched by `add-tablet-clay-type-backdrops`; author deltas against current
  spec text and apply after that change archives, matching exact requirement headers (MEMORY.md trap).
