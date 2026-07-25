## Context

The per-player pin foundation (`add-pinned-task-foundation`, `add-document-task-identity`) landed a
complete server-authoritative, identity-addressed pin layer:
- `ScribePinnedRef` (`src/Core/ScribePinnedRef.cs`) — a pin: `OwnerDocId`, `TaskId`,
  `PinnedAtTotalHours`, `Orphaned`, `LastKnownText`, `LastKnownDone`. The snapshot fields exist
  precisely so a UI can render a pin whose block is unloaded/broken.
- `ScribePinStore` (`src/Mod/ScribePinStore.cs`) — server-side per-player `List<ScribePinnedRef>`,
  persisted with the save, snapshot-refreshed on edit, soft-orphaned on delete.
- Sync — the server pushes a player's full list via `ScribePinnedSetMessage.PinnedRefBytes`; the client
  handler `ScribeModSystem.OnClientReceivedPinnedSet` fires `MyPinsChanged`.
- Server ops already reusable from a HUD: `ScribeCompleteTaskMessage` (complete by `(DocId,TaskId)`,
  lock-free, complete-to-unpin applied server-side) and `ScribeSetPinMessage` (set/clear a pin by
  identity). `ScribePlayerSettings` carries `CompleteUnpins` and a reserved unread `HudCollapsed`.

**This change reworks that foundation's ownership model (D-Owned/D-Reconcile below).** The foundation
treated a pin as a durable *reference* whose snapshot mirrored shared document state and soft-orphaned
on delete. This change makes a pin the pinning player's *own copy*: the store owns done-state, only
the owner's own edits update it, and destruction is non-destructive. `CompleteUnpins` becomes the
`CompletionPolicy` enum and the `Orphaned` flag is retired as a signal.

The foundation deferred the HUD to "a later change." An earlier exploration spec
(`docs/specs/v5-backpack-hud.md`) sketched a HUD but predates this refactor: it assumes pins are
document state (`ScribeBlock.Pinned`, a ≤3 cap in Core, `CollectPins`). That data model is obsolete.
Its hotkey-API research is still accurate and is reused. Its `HudElement`/`HudElementCoordinates`
native-Cairo recipe is **superseded** (see D1): the mod's GUI is LibGUI, and LibGUI ships a working
HUD-type dialog, so the HUD is built on LibGUI rather than the native path.

## Goals / Non-Goals

**Goals:**
- Make a pin the pinning player's **own, store-authoritative copy** of a task: grief-proof (only the
  owner's actions change it), survivable across source destruction, completable with no resolvable
  source.
- Render the current player's own pins on screen from the synced set, including source-unresolvable
  pins.
- Present them as an automatically-ordered list: pin order, with completed tasks sinking to the bottom
  after a brief undo window.
- Let the player complete from a HUD row by identity, reusing the existing completion op; the outcome
  follows the player's completion policy (Sink/Unpin/Delete), store-first with write-through.
- Auto-show on ≥1 pin, hide at zero, plus a rebindable toggle whose collapse state persists.
- Bound the visible rows by a per-player configurable maximum (default 3).
- Keep Core game-agnostic and unit-tested; no document-codec version bump.

**Non-Goals:**
- The backpack item, quick-capture hotkey, and a full in-document "Pinned tab" — the only place pins
  can be manually reordered (separate later changes).
- **Manual reorder or a manual per-row unpin control on the HUD** — ordering is automatic and removal
  is policy-driven.
- A settings UI for the new max-rows / completion-policy values (code defaults now; ConfigLib/settings
  surface later).
- Any change to the lectern's per-player tint or the pin sync shape.
- Surfacing that a pin's snapshot has diverged from a griefed source (the divergence is intended).
- Reviving the v5 document-boolean pin model.

## Decisions

### D1: LibGUI HUD (`HudScribePins : GuiBase`, `EnumDialogType.HUD`)
Implement the HUD on **LibGUI**, not the native Cairo `HudElement`. LibGUI's root is a real VS
`GuiDialog` (`GuiBase : GuiDialog`), and LibGUI already ships a working HUD-type dialog —
`GuiGlobalOverlay : GuiBase` (`reference/vslibgui/Gui/Gui/GuiGlobalOverlay.cs`) — that overrides
`DialogType => EnumDialogType.HUD`, `DrawOrder`, and `ShouldReceive{Keyboard,Mouse}Events`. So HUD
semantics (no focus steal, Escape never closes, renders behind dialogs) are fully reachable through
LibGUI; `GuiBase` already defaults `ToggleKeyCombinationCode => null` and `PrefersUngrabbedMouse =>
false`. `HudScribePins` subclasses `GuiBase`, overrides `DialogType => EnumDialogType.HUD`,
`OnEscapePressed => false`, `ShouldReceiveRenderEvents => opened`, and a corner-anchored `WindowConfig`,
mirroring `GuiGlobalOverlay`. It keeps mouse events ON (the checkbox must be clickable) but
`ShouldReceiveKeyboardEvents => false` so gameplay keys still flow. Deriving from `GuiBase` gives
automatic `Theme` injection, so rows reuse `Theme.Of(context).ColorScheme` and the lectern read row's
widgets (`Checkbox`, `Text`+`TextStyle`, `ScribeVsIconGlyph`) directly.

Rows are the lectern **read row minus chrome**: `Row[ Checkbox, Expanded(Text) ]` — no grip spacer, no
pinned tint (every HUD row is pinned, so the tint carries no signal), no pin/delete buttons. Text uses
`TextStyle.GlowWidth`/`GlowColor` (a soft dark halo) for readability over the world rather than a
background underlay; a completed task's text **mutes** (`OnSurfaceVariant` + reduced opacity — LibGUI
`TextStyle` has no line-through, only `None`/`Underline`).

**Why not the previously-chosen native path**: the original D1 rejected a LibGUI dialog on the false
premise that a screen HUD *requires* the native `HudElement`'s HUD semantics. `GuiGlobalOverlay`
disproves that. LibGUI is the guardrail-safe path (the `gui` hard dep already ships) and reuses the
mod's theme, widgets, and text-glow. Alternatives still rejected: native `HudElement` (would duplicate
Cairo styling and can't reuse LibGUI widgets/theme), ImGui (Release-excluded, Apple-Silicon-blocked),
ToastLib (stale, transient-only).

### D2: Refresh on `MyPinsChanged`, not primarily on the tick
Pins already push event-driven; subscribe `HudScribePins` to `ScribeModSystem.MyPinsChanged` and
rebuild the widget tree there via LibGUI's `ForceRebuild()` (the authoritative moment the set changed).
Keep a low-frequency tick only as a cheap safety re-read / to drive the sink-after-complete timer
(D-Order). This avoids per-frame work and matches how the lectern dialog already repaints.

### D3: Retain the full pin list on the client (the one foundation gap)
`OnClientReceivedPinnedSet` currently collapses the pushed `List<ScribePinnedRef>` into
`HashSet<(Guid,Guid)> myPins` (all `IsPinnedForMe` needs). Extend it to also keep the full list and add
a read accessor `IReadOnlyList<ScribePinnedRef> MyPins` (carrying each ref's `LastKnownText` /
`LastKnownDone` snapshot). `IsPinnedForMe` and `MyPinsChanged` stay as-is (the lectern is untouched).
This is Mod-only, no wire-format change (the bytes already carry the full list). Alternative rejected:
a second server push shaped for the HUD — wasteful, the data is already on the wire.

### D4: Player preferences are client-local JSON, not a server per-world settings blob
The three player preferences — `CompletionPolicy` (`Sink`/`Unpin`/`Delete`, default `Sink`, replacing
the boolean `CompleteUnpins`), `HudMaxRows` (default 3), and `HudCollapsed` — are **per-player,
client-local, and identical across all of that player's worlds**. They are personal display/behavior
preferences with **no grief surface**, so they need no server authority or per-world scope. Keep the
`ScribePlayerSettings` POCO in Core (game-agnostic, unit-testable) as the in-memory shape, but persist
it as a **client-local Newtonsoft JSON config** via `ICoreAPICommon.StoreModConfig`/`LoadModConfig` —
the same mechanism `ScribeClientConfig` uses. Use a **separate config file** (`scribe-hud-config.json`),
NOT `ScribeClientConfig`: that class is deliberately re-read fresh on every dialog open and has no
write path, so persisting a runtime collapse toggle onto a shared instance would clobber a player's
hand-edited layout-tuning knobs. `ScribeModSystem` loads this config in `StartClientSide`, holds it as
a mutable instance, and `StoreModConfig`s it whenever a preference changes. Clamp `HudMaxRows` and
normalize an unknown `CompletionPolicy` on load (reuse the Core `ClampHudMaxRows`/`NormalizePolicy`
guards) so a hand-edited or garbled JSON can't request thousands of rows or an invalid policy.

**This replaces the foundation's server-side settings blob and client↔server settings sync.** That
sync was dead scaffolding — the client `MySettings` accessor had no consumer and the server never
called `SetSettings` — so removing it strands no working behavior. The settings **binary codec**
(`SPSE`/`SPSS`, `SerializeSettings`/`ReadSettings`/`WriteSettings`, the old-bool migration) is dropped
from `ScribePinCodec`; JSON replaces it. The pin codec/store are untouched (pins stay server-
authoritative and per-world; distinct save key + blob magic). Alternatives rejected: keeping the
server per-world settings blob (contradicts "same across worlds," and re-introduces the client→server
write path this pivot dissolves); folding the fields into `ScribeClientConfig` (write path would
clobber its per-open tuning knobs); ConfigLib panel (client-global JSON only, ImGui panel is broken on
Apple Silicon — the user's dev machine — and is why the future in-mod Settings Tab exists at all).

### D5: HUD completion is policy-driven; store-first, write-through; no manual unpin
A row's checkbox sends `ScribeCompleteTaskMessage(DocId, TaskId, Policy)` (lock-free, no block
resolution). Because the completion policy is now a **client-local** preference (D4), the client
**carries its policy in the message**; the server **validates/normalizes** the incoming enum (unknown
→ `Sink`) rather than reading a server-side per-player setting. The server records the completed state
in the player's **own** pin store first (authoritative, so it works with no resolvable source —
D-Owned), writes through to the source document's done when it resolves, and then applies that policy:
- **Sink** (default): mark the pin done, **keep** it. The client mutes the row and sinks it to the
  bottom after a brief undo window (D-Order).
- **Unpin**: mark done, then drop the player's pin (the old `CompleteUnpins == true` behavior).
- **Delete**: delete the underlying task from its source document when resolvable (a new/extended
  server op following the vanilla Sign persistence pattern, `MarkDirty` + packet) and drop the pin.
  Destructive; see Open Questions on undo.

The source write-through reconciles ONLY the acting player's pin (trivially — it is their action).
There is **no manual per-row unpin control** on the HUD — removal is always a consequence of the
configured policy. Because the HUD must receive clicks, it keeps mouse events on but sets
`ShouldReceiveKeyboardEvents => false` so gameplay keys still flow; only checkbox clicks are consumed.

### D-Order: automatic ordering, completed sinks after a ~2s undo window
The HUD list is ordered = **pin order with done tasks sunk to the bottom**. Core owns the pure ordering
(a game-agnostic helper over the pin list + done states). When a row is completed on the HUD, it stays
in place for ~2s — an **undo window** in which re-toggling the checkbox reverts completion — then the
HUD rebuilds with it sunk (prefer a LibGUI implicit animation, `AnimatedOpacity`/`AnimatedSlide`). The
2s timer is a client UI concern (driven off the D2 tick), not Core or server state. This is the only
reordering the HUD performs; **manual reorder is deferred to the in-document "Pinned tab"** (roadmap).

### D-Owned: pins are player-owned, grief-proof, and store-authoritative
A pin is the pinning player's **own copy** of a task, not a live view of shared document state:
- **The store owns done-state.** A pinned task's completed state lives in the per-player
  `ScribePinStore`; completion writes it there first. So a pin is completable even when its source is
  unloaded or its block is destroyed.
- **Grief-proof snapshot.** A pin's text/done are updated ONLY by the pinning player's own actions.
  `ScribePinStore.RefreshSnapshots` is narrowed to reconcile just the acting player (passed from
  `BlockEntityScribeLectern.ApplyEdit`'s `fromPlayer`); another player editing or completing the
  source never touches my pin. This closes the griefing vector (someone rewriting a shared task to
  something inappropriate can't change what my HUD shows).
- **Destruction is non-destructive.** Breaking the source block does NOT remove or orphan a pin — the
  block-removal path only `UnregisterDoc`s (a re-place restores it; the snapshot renders it meanwhile).
  A pin is removed only by the owner's own action: their completion policy (Unpin/Delete) or deleting
  the task in their own edit view (reconciled by `TaskId`).
- **Reconcile by stable `TaskId`, never text.** Text matching would break on duplicate-text tasks and
  on the owner's own re-wording, and would let a griefer sever reconciliation by changing text.

This **supersedes** the foundation's soft-orphan model and the earlier-this-change "orphans auto-clear"
decision: there is no orphan concept anymore. `ScribePinnedRef.Orphaned` remains only for codec
format-compatibility and is no longer set or read as a signal. (Also superseded: the "distinct
orphaned treatment" on the HUD.) This was a stated non-goal in the original proposal; it is now the
change's foundation, adopted at the user's direction.

### D-Reconcile: a player's own edit reconciles only their own pins
When a player applies a document edit (`ApplyEdit`, which knows `fromPlayer` and holds the lock), the
server diffs the edit against that player's pins by `TaskId`: surviving pinned tasks get their
text/done snapshot refreshed; pins whose task the edit deleted are removed from that player's set. No
other player's pins are consulted or changed. The same rule covers completing/unpinning a task from an
edit view. Alternative rejected: refreshing all pinners on any edit (the pre-change behavior) — it is
exactly the griefing vector D-Owned closes.

### D6: Visibility = auto-show on ≥1 pin; hotkey **and** on-HUD control toggle a persisted collapse
Distinguish **hidden** (zero pins → HUD not shown at all) from **collapsed** (has pins, but minimized).
On each `MyPinsChanged`: if the player has ≥1 pin, the HUD opens; at zero pins it closes. Collapse is a
persisted client preference (`HudCollapsed`) toggled from **two** entry points for redundancy: a
rebindable `scribepinhud` hotkey (`RegisterHotKey` + `SetHotKeyHandler` in `StartClientSide`) and an
on-HUD collapse control (a small arrow / ±). Collapsing **minimizes** the HUD to a compact, still-
clickable header (so the on-HUD control can re-expand it) rather than hiding it entirely, animated via
LibGUI (`AnimatedSize`/`AnimatedOpacity`). Because collapse is now a client-local preference (D4),
flipping it mutates the held config instance and `StoreModConfig`s it — no network round-trip. The
future in-mod "Settings Tab" will drive the same client config (and `HudMaxRows`/`CompletionPolicy`)
through the same path. Alternative rejected: a server-synced collapse flag — needless network + per-
world scope for a personal, cross-world UI preference.

## Risks / Trade-offs

- **Rebuild cost on refresh** → LibGUI rebuilds are cheap (only the dirty subtree; clean frames replay
  a cached `SKPicture`), so a `ForceRebuild()` per `MyPinsChanged` is fine — no fixed-plate/blank-rows
  dance is needed the way the native path required. Do not rebuild per frame; the tick is low-frequency.
- **Interactive HUD grabbing input** → Keep `ShouldReceiveKeyboardEvents => false` so gameplay keys
  flow; mouse events stay on but the only consuming control is the checkbox. Verify a click
  near-but-not-on the checkbox doesn't get eaten and that the HUD renders behind the lectern dialog.
- **Snapshot staleness for a completed-elsewhere task** → The HUD reads last-known snapshots; a task
  completed on a loaded document re-pushes the set (D2 refresh), but a change while the source is
  unloaded won't refresh until it reloads. Acceptable — the snapshot is explicitly "last known."
- **Settings value abuse** (`HudMaxRows` huge/negative, or an out-of-range `CompletionPolicy` from a bad
  blob) → Clamp `HudMaxRows` on codec read; treat an unknown policy value as the default (`Sink`); the
  HUD additionally hard-caps how many rows it will render.
- **Destructive `Delete` policy** → completing a task can permanently delete it. Mitigate with the same
  ~2s undo window the sink uses, and consider a confirm (Open Questions). Off by default (`Sink`).
- **Untested VS-API + LibGUI-HUD surface** → LibGUI-as-HUD is proven by `GuiGlobalOverlay` but not yet
  exercised in this mod; the sink animation and glow are new. Validate in-game early (verify steps).

## Migration Plan

**Settings storage moves from the save game to a client JSON file.** The prior server-side settings
blob (`SPSE`/`SPSS`) is removed rather than migrated: it was dead scaffolding (no consumer of the
synced value; the server never wrote it), so any settings blob already in a save is simply ignored on
load and drops out on the next save — no user-visible loss, because it never drove behavior. Players
start on defaults (`Sink`, `HudMaxRows` 3, not collapsed) and the client writes `scribe-hud-config.json`
on first change. There is no bool→enum wire migration to preserve (the old `CompleteUnpins` was never
actually mutated in a shipped build); the enum default is `Sink`, matching the old bool's `false`
default. **Pins are unaffected** — distinct save key + blob magic, still server-authoritative and
per-world. **Ownership behavior changes** (D-Owned): a build with this change no longer sets/reads the
`Orphaned` flag as a signal (the field stays in the pin codec for format-compat); any pin previously
soft-orphaned in a save loads as an ordinary, completable pin (an improvement — it becomes actionable
again). No pin data is dropped on upgrade. Rollback removes the HUD element + hotkey + the client
config; a reverted build would fall back to its own server settings defaults (it never read the JSON).

## Open Questions

- **Anchor/position**: default **right-top** (clear of hotbar/coordinates), with an offset resolved
  in-game. Whether to expose anchor as a user setting is deferred to a later change (leaning "later").
- **Completed-text mute level & sink feel**: exact opacity/color for a muted done row and the sink
  animation timing/curve — settle in-game, not a spec decision.
- **`Delete` policy safety**: does completing under the Delete policy need a confirm, or is the ~2s
  undo window enough given deletion is destructive?
- **Griefed-source visibility**: a pin's snapshot will legitimately diverge from a source another
  player changed (that is the grief-proofing working). Whether to ever surface a "source changed" hint
  is out of scope now.
- **"+N more" affordance**: indicative text only for now (the Pinned tab that it might open is a later
  change).
