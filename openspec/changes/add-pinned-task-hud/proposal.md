## Why

The per-player pin foundation (`add-pinned-task-foundation`) persists and syncs each player's pinned
tasks but never *renders* them — a pin only tints its row inside the one lectern that owns it. The
foundation's own proposal names the goal it deferred: "see their own pinned tasks aggregated on a HUD."
This change delivers that HUD: an always-available on-screen list of the player's pins, so their top
goals stay ambient without opening any block, and so a pin whose lectern is far away, broken, or in an
unloaded chunk is still visible and actionable.

## What Changes

- **Make pins player-owned and grief-proof (a foundation upgrade).** A pin becomes the pinning
  player's own copy of a task: the per-player pin store is **authoritative for the task's completed
  state**, and a pin's text/done change **only from the owning player's own actions** — never from
  another player editing the shared source. A pin **survives destruction of its source block** and
  stays completable even when unresolvable. Any source-document action (complete/delete/unpin)
  reconciles **only the acting player's** matching pin, keyed by stable `TaskId`. This closes a
  griefing vector (a shared task rewritten to something inappropriate can't alter my pinned copy) and
  was explicitly a non-goal in the original proposal — now adopted as the change's foundation.
- Add an on-screen **pinned-task HUD**, built on **LibGUI** (the mod's GUI framework; a HUD-type
  `GuiBase` following LibGUI's own `GuiGlobalOverlay` precedent), that lists the current player's own
  pinned tasks. Each row is modeled on the lectern **read view** stripped of chrome — just a checkbox
  and the task's last-known text — with a **dark text glow** for readability over the world (no
  background underlay) and a **muted** treatment for a completed task's text. It reads the
  already-synced per-player pin set; it introduces no new persisted document state.
- The HUD's list is an **ordered, automatic** view: pin order, with a **completed** task waiting ~2s
  (an undo window) then **sinking to the bottom**. This is the only reordering the HUD performs;
  manual reordering is deferred to the future in-document "Pinned tab" (roadmap).
- Completion from the HUD is **policy-driven**, not a manual unpin. Clicking a row's checkbox completes
  the task by identity (reusing `ScribeCompleteTaskMessage`); what happens next is the player's
  **completion policy**: *Sink* (default — stays pinned, mutes, sinks), *Unpin* (removes the pin), or
  *Delete* (deletes the underlying task). Completion records done in the player's store first and
  writes through to the source document when resolvable. There is **no manual per-row unpin control**.
- The HUD **auto-shows** whenever the player has ≥1 pin and hides at zero, and also has a **rebindable
  show/hide hotkey** plus an on-HUD collapse control; collapsing **minimizes** the HUD (leaving a small
  re-expand affordance) rather than hiding it, animated via LibGUI. Its collapsed state persists via
  `ScribePlayerSettings.HudCollapsed`.
- Add a **configurable maximum number of HUD rows** (default 3) as a per-player preference; pins beyond
  the max are not shown (with a small "+N more" affordance).
- The HUD's **screen position is configurable**: a 7-position anchor enum (topLeft / topMiddle /
  topRight / middleLeft / middleRight / bottomLeft / bottomRight, default **topRight**) with per-anchor
  X/Y **offsets** to clear vanilla overlays (minimap / coordinate overlay / block-info overlay), and a
  **fixed row width** (default 250px). The default top-right is pre-offset left of the minimap so it
  isn't drawn under it. Code-defaulted and JSON-editable now; a settings-UI picker is deferred (same
  bucket as the max-rows / completion-policy pickers).
- **Player preferences are client-local, not per-world.** The three player preferences
  (`CompletionPolicy`, `HudMaxRows`, `HudCollapsed`) are stored in a **client-local JSON config**
  (`StoreModConfig`/`LoadModConfig`, the `ScribeClientConfig` pattern) — per-player, identical across
  all the player's worlds, hand-editable on disk, and **not** server-synced. They are personal display/
  behavior preferences with no grief surface, so they need no server authority. This **replaces** the
  foundation's server-side, per-world settings blob and its client↔server settings sync (dead
  scaffolding: nothing consumed the synced value and the server never mutated it). Pins themselves stay
  server-authoritative and per-world.
- **Completion policy travels with the completion request.** Because policy now lives client-side, the
  client carries its `CompletionPolicy` in `ScribeCompleteTaskMessage`; the server validates/normalizes
  it and applies it (instead of reading a server-side per-player setting). *Delete* still deletes the
  shared task authoritatively on the block but removes **only the acting player's** pin.
- Retain the full pushed pin list on the client. Today `OnClientReceivedPinnedSet` discards everything
  but the `(DocId, TaskId)` keys (enough for the lectern's `IsPinnedForMe` tint); the HUD needs each
  pin's text/done snapshot, so the client will keep the full `ScribePinnedRef` list.
- Register a rebindable **`scribepinhud`** hotkey through the native input system (no new dependency).

Explicitly NOT in scope: the backpack item, quick-capture hotkey, and a full "Pinned tab" inside a
document GUI — the **only** place pins can be manually reordered (separate later changes); ConfigLib
settings UI (the max-rows and completion-policy settings are code-defaulted here, exposed in a settings
UI later). This change also does NOT revisit the v5 spec's obsolete document-boolean pin model — pins
are per-player and identity-addressed as shipped.

## Capabilities

### New Capabilities
- `pinned-task-hud`: an always-available on-screen overlay (LibGUI HUD) listing the current player's
  own pinned tasks (text/done snapshot), sourced from the synced per-player pin set. Covers the HUD's
  visibility rules (auto-show on ≥1 pin, rebindable toggle, persisted collapse), its bounded row count
  (configurable max), its automatic ordering (completed tasks sink to the bottom after a brief undo
  window), complete-by-identity from a row under the player's completion policy, its live refresh
  when the pin set changes, and its configurable screen position (7-position anchor + per-anchor
  offsets + fixed row width, default top-right offset clear of the minimap).

### Modified Capabilities
- `player-pins`: (1) player preferences (a **maximum-HUD-rows** display preference, default 3, and a
  **completion-policy** setting — *Sink* default / *Unpin* / *Delete* — replacing the boolean
  complete-to-unpin flag) become **client-local, per-player, cross-world** preferences persisted in a
  client JSON config, no longer a server-side per-world settings blob; the prior server settings
  persistence + client↔server sync is removed; (2) a pin becomes a **player-owned, store-authoritative
  copy** — the store owns done-state, only the owner's own actions update a pin's text/done, and a pin
  survives source destruction; (3) any source-document action reconciles **only the acting player's**
  pins by `TaskId`; (4) completion is requested with the acting player's completion policy carried in
  the completion message and validated server-side. The prior soft-orphan lifecycle is retired (the
  `Orphaned` flag stays only for codec format-compat). Pin sync shape is unchanged.

## Impact

- **Core (`src/Core/`)**: keep the `ScribePlayerSettings` POCO — `HudMaxRows` (default 3, clamp
  bounds), `CompletionPolicy` enum (`Sink`/`Unpin`/`Delete`, default `Sink`), `HudCollapsed` — as the
  in-memory shape the client config serializes; retain the `NormalizePolicy`/`ClampHudMaxRows` guards
  (moved to the config-load path). Add **HUD-position preferences** to the POCO: a `ScribeHudAnchor`
  enum (`TopLeft`/`TopMiddle`/`TopRight`/`MiddleLeft`/`MiddleRight`/`BottomLeft`/`BottomRight`, default
  `TopRight`), per-anchor `HudOffsetX`/`HudOffsetY`, and a fixed `HudRowWidth` (default 250), each
  normalized/clamped on load like the existing fields. Add a game-agnostic **ordering helper** (given a
  pin list + done states, return the display order with done tasks sunk to the bottom). Unit-tested; no
  VS API reference. **Remove the settings binary codec** (`SPSE`/`SPSS`, `SerializeSettings`/`ReadSettings`/
  `WriteSettings` + the old-bool migration) from `ScribePinCodec` — settings are now Newtonsoft JSON,
  not a codec blob; the pin codec is untouched (separate magic/version).
- **Mod (`src/Mod/`)**: narrow `ScribePinStore.RefreshSnapshots` to reconcile **only the acting
  player** (passed from `BlockEntityScribeLectern.ApplyEdit`'s `fromPlayer`) — update text/done for
  surviving pinned tasks and remove that player's pins whose `TaskId` the edit deleted; stop setting
  `Orphaned` and drop the block-removal orphaning (the store owns done-state and pins survive
  destruction). Have the completion op record done in the store and honor the **policy carried in
  `ScribeCompleteTaskMessage`** (validated/normalized server-side; Delete is a new server op following
  the Sign persistence pattern), writing through to the source when resolvable. **Persist player
  preferences client-locally**: a new client JSON config (`scribe-hud-config.json`, separate from
  `ScribeClientConfig` so a collapse write can't clobber its hand-edited layout knobs), loaded in
  `StartClientSide` and held as a mutable instance saved on change via `StoreModConfig`. **Remove the
  server settings layer**: `ScribePlayerSettingsMessage` (+ its registration/handler),
  `PushSettingsTo`, `OnClientReceivedPlayerSettings`, `mySettings`, the `SettingsStoreSaveKey`
  save-game persistence, and `ScribePinStore`'s `_settings`/`GetSettings`/`SetSettings`/
  `SerializeSettings` + the settings half of `LoadFrom`. Extend `OnClientReceivedPinnedSet` to retain
  the full `List<ScribePinnedRef>` and expose a `MyPins` accessor (keeping `IsPinnedForMe`/
  `MyPinsChanged`). Add `HudScribePins : GuiBase` (LibGUI, `EnumDialogType.HUD`); register the
  `scribepinhud` hotkey in `StartClientSide`; wire the HUD checkbox to `ScribeCompleteTaskMessage`.
- **No new dependencies**: LibGUI is the existing hard `gui` dep; native rebindable hotkeys only. (The
  native `HudElement`, ImGui, and ToastLib paths are all rejected — LibGUI already provides working
  HUD-type dialog semantics via `GuiGlobalOverlay`; see design.md and VSAPI-NOTES.)
- **Existing behavior preserved**: the lectern's per-player pin tint (`IsPinnedForMe`) and the
  read-view identity-addressed completion are unchanged. **Behavior changed**: the pin store now owns
  done-state, reconciles per acting player (grief-proof), no longer soft-orphans, and no longer holds/
  persists/syncs per-player settings (those move client-local) — this updates the
  `add-pinned-task-foundation` behavior and its integration scenarios (settings-persistence and
  settings-seeding tests are dropped/retargeted).
