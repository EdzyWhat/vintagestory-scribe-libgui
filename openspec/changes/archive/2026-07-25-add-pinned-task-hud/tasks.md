## 1. Core: settings + ordering semantics — DONE (partly reworked in §1b)

- [x] 1.1 Add `int HudMaxRows { get; set; } = 3` to `ScribePlayerSettings` with bounds constants.
- [x] 1.2 Replace the `CompleteUnpins` bool with a `ScribeCompletionPolicy` enum (`Sink`/`Unpin`/`Delete`, default `Sink`).
- [x] 1.3 ~~Round-trip `HudMaxRows` + `CompletionPolicy` in `ScribePinCodec` (settings blob v2)~~ — **SUPERSEDED by §1b:** the settings binary codec is removed; settings are now client-local JSON. The `ClampHudMaxRows`/`NormalizePolicy` guards survive (moved to the config-load path).
- [x] 1.4 Add `ScribePinOrdering.ForDisplay` (game-agnostic): pin order with done pins after not-done, stable within each group; does not mutate input.
- [x] 1.5 xUnit coverage: ordering (done sinks, stable among each group, empty, no-mutate). **The settings-codec tests are removed in §1b** (round-trip/clamp/unknown-policy/legacy-bool no longer apply to a binary blob; keep clamp/normalize coverage on the surviving Core guards).

## 1b. Core + Mod: settings-storage pivot (client-local JSON, policy-in-message)

- [x] 1b.1 **Remove the settings binary codec** from `ScribePinCodec`: `SerializeSettings`/`TryDeserializeSettings`/`SerializeSettingsStore`/`TryDeserializeSettingsStore`/`WriteSettings`/`ReadSettings`, the `SPSE`/`SPSS` magic + `SettingsVersion`, and the bool→enum migration. Keep `ClampHudMaxRows`/`NormalizePolicy` (either on `ScribePlayerSettings` or a small Core helper) for the JSON-load path. Leave the pin codec untouched.
- [x] 1b.2 **Client-local preference config:** add a new `scribe-hud-config.json` (separate from `ScribeClientConfig`) persisting `CompletionPolicy`/`HudMaxRows`/`HudCollapsed` via `StoreModConfig`/`LoadModConfig`. Load it in `ScribeModSystem.StartClientSide`, hold a mutable instance, normalize/clamp on load, and `StoreModConfig` it whenever a preference changes.
- [x] 1b.3 **Carry the policy in the completion request:** add a completion-policy field to `ScribeCompleteTaskMessage`; the client sends its current policy, the server validates/normalizes it (unknown → `Sink`) and applies it in `CompleteTaskForPlayer` instead of reading server settings.
- [x] 1b.4 **Remove the server settings layer:** delete `ScribePlayerSettingsMessage` (+ its `RegisterMessageType`/`SetMessageHandler`), `PushSettingsTo`, `OnClientReceivedPlayerSettings`, `mySettings`/`MySettings`, the `SettingsStoreSaveKey` persistence in `OnSaveGameLoaded`/`OnGameWorldSave`, the `PushSettingsTo` call in `OnPlayerNowPlaying`, and `ScribePinStore`'s `_settings`/`GetSettings`/`SetSettings`/`SerializeSettings` + the settings half of `LoadFrom`. Pins stay fully intact.
- [x] 1b.5 **Update settings tests:** drop the codec settings block in `ScribePinCodecTests.cs`; drop `PersistenceScenarios`' settings-survives-restart assertion and `FixtureBuilders`' `SetSettings` seed; retarget `PinScenarios` policy tests to set the client policy via the completion message (or a test seam) rather than `SetSettings`.

## 2. Mod: player-owned pin store + policy completion + client pin list

- [x] 2.1 **Grief-proof snapshot refresh:** narrow `ScribePinStore.RefreshSnapshots` to reconcile ONLY the acting player. Change its signature to take the acting `playerUid` (threaded from `BlockEntityScribeLectern.ApplyEdit`'s `fromPlayer`); update text/done for that player's surviving pinned tasks and REMOVE that player's pins whose `TaskId` the edit deleted. Never touch other players' pins.
- [x] 2.2 **Store owns done + completion policy (policy from the request):** `ScribeModSystem.CompleteTaskForPlayer` records done in the acting player's pin store first (authoritative), then honors the **policy carried in `ScribeCompleteTaskMessage`** (validated/normalized server-side — see 1b.3): *Sink* = mark pin done, keep it; *Unpin* = remove the pin; *Delete* = remove the task from its source doc when resolvable (Sign pattern: `MarkDirty` + packet) and remove the pin. Write through to the source doc's done when resolvable, reconciling only the acting player. Re-push the acting player.
- [x] 2.3 **Destruction is non-destructive:** confirm the block-removal path (`OnBlockRemoved`) only `UnregisterDoc`s and does NOT clear/orphan pins (already changed). Remove the now-dead `OrphanAll`/`Orphaned`-setting code (done); keep `ScribePinnedRef.Orphaned` for codec format-compat only.
- [x] 2.4 Extend `ScribeModSystem.OnClientReceivedPinnedSet` to keep the full `List<ScribePinnedRef>` (not only the `(DocId,TaskId)` keys); add `IReadOnlyList<ScribePinnedRef> MyPins`; leave `myPins`/`IsPinnedForMe`/`MyPinsChanged` intact.
- [x] 2.5 Confirm the client-local preference config (§1b.2) exposes `HudMaxRows`/`CompletionPolicy`/`HudCollapsed` to the HUD, and `MyPinsChanged` fires on pin-set pushes. (Preference changes are local — the HUD reads them directly and rebuilds; no server push involved.)
- [x] 2.6 **Rewrite `tests/Integration.Tests/PinScenarios.cs`** for player-owned behavior: breaking a block keeps the pin intact + completable (not orphaned); another player's edit/completion doesn't change my pin; my own edit deleting a task removes only my pin; completion writes through to a resolvable source and reconciles only me; store-owned done survives an unresolvable source.

## 3. Mod: the LibGUI HUD element

- [x] 3.1 Add `HudScribePins : GuiBase` (`src/Mod/HudScribePins.cs`, client-side) following the `GuiGlobalOverlay` precedent: override `DialogType => EnumDialogType.HUD`, `OnEscapePressed => false`, `ShouldReceiveRenderEvents => opened`, `ShouldReceiveKeyboardEvents() => false` (keep mouse events ON for the checkbox); a right-top corner-anchored, non-draggable `WindowConfig` sized to content, re-anchored in `OnRenderGUI` like `GuiGlobalOverlay`.
- [x] 3.2 `Build()`: a `Column` of up to `MySettings.HudMaxRows` rows in the Core-computed order, each the read row minus chrome — `Row[ Checkbox(value: done, onChanged: complete), Expanded(Text(LastKnownText, glowStyle)) ]`. No grip spacer, no pinned tint, no unpin control. `glowStyle` uses `TextStyle.GlowWidth`/`GlowColor` (dark halo); a completed row mutes (`OnSurfaceVariant` + reduced opacity). Reuse `Checkbox`, `Text`, `ScribeVsIconGlyph`/`VsIcon`, and `Theme.Of(context).ColorScheme`.
- [x] 3.3 Show a "+N more" muted `Text` row (indicative only) when the player's pins exceed `HudMaxRows`.
- [x] 3.4 Sink-after-complete: on completing a row keep it in place for a ~2s undo window (re-toggling reverts), then rebuild with it sunk; prefer a LibGUI implicit animation (`AnimatedOpacity`/`AnimatedSlide`). Drive the timer off the D2 tick.
- [x] 3.5 Refresh on `ScribeModSystem.MyPinsChanged` via `ForceRebuild()` (primary); register a low-frequency `RegisterGameTickListener` safety re-read / undo-timer; unregister the tick + unsubscribe the event in `Dispose()`.
- [x] 3.6 Construct + wire the HUD into `ScribeModSystem.StartClientSide` so it self-opens when player data is ready.

## 4. Mod: visibility (auto-show, collapse, persisted client-local)

- [x] 4.1 On each `MyPinsChanged`: open the HUD when the player has ≥1 pin; close it when zero pins. Render collapsed vs. expanded off the client-local `HudCollapsed` preference.
- [x] 4.2 Register a rebindable `scribepinhud` hotkey (`RegisterHotKey` + `SetHotKeyHandler`, `HotkeyType.GUIOrOtherControls`) in `StartClientSide`; add an on-HUD collapse control (arrow / ±). Both flip the `HudCollapsed` preference.
- [x] 4.3 On a collapse toggle, update the held client-local config and `StoreModConfig` it (no network). Collapsing **minimizes** the HUD to a compact, still-clickable header (re-expandable) rather than hiding it; animate the transition via LibGUI (`AnimatedSize`/`AnimatedOpacity`). Distinguish collapsed (has pins, minimized) from hidden (zero pins).
- [x] 4.4 **Core: HUD-position preferences.** Add a `ScribeHudAnchor` enum {`TopLeft`, `TopMiddle`, `TopRight`, `MiddleLeft`, `MiddleRight`, `BottomLeft`, `BottomRight`} and add `HudAnchor` (default `TopRight`), `HudOffsetX`, `HudOffsetY`, and `HudRowWidth` (default 250) to `ScribePlayerSettings`, normalized/clamped in `Normalized()` on load (unknown anchor → `TopRight`; clamp row width to a sane range). Unit coverage alongside the existing clamp/normalize tests.
- [x] 4.5 **Mod: anchor + offset + fixed width in the HUD.** Replace `HudScribePins.AnchorTopRight` with an anchor+offset resolver that positions the window per `HudAnchor`/`HudOffsetX`/`HudOffsetY` (topRight's default offset shifts left by ~map width so it clears the default minimap; middleRight/topMiddle also offsettable to clear the coordinate/block-info overlays), re-applied in `OnRenderGUI`. Constrain the task-row area to `HudRowWidth` via a fixed-width `Column` (LibGUI RenderFlex) so long tasks wrap within that width. Add the in-game verification to §7 (default clears the minimap; changing anchor/offset in `scribe-hud-config.json` + reload is honored).

## 5. Mod: completion wiring (checkbox → policy op)

- [x] 5.1 The row checkbox `onChanged` sends `ScribeCompleteTaskMessage(DocId, TaskId, Policy)` carrying the client's current completion policy (§1b.3); the server validates/normalizes it, applies it, and re-pushes the pin set, so the HUD updates via 3.5.
- [x] 5.2 Ensure the HUD consumes ONLY the checkbox click (not keyboard, not empty-area clicks) so gameplay input still flows; verify the HUD renders behind the lectern dialog.

## 6. Localization + assets

- [x] 6.1 Add new lang strings under `assets/scribe/lang/` with the `scribe:` domain prefix: HUD toggle hotkey name/description, "+N more", and completion-policy labels (Sink/Unpin/Delete).

## 7. Verify

- [x] 7.1 `dotnet build src/Mod/Mod.csproj` clean; `dotnet test tests/Core.Tests` green (ordering tests + surviving clamp/normalize coverage; the settings-codec tests are removed per §1b.5).
- [x] 7.2 Restage (`build/restage.sh`) and FULLY relaunch the client (mod loads at boot; no hot reload). — Done (restaged + relaunched across the 2026-07-25 playtest sessions)
- [x] 7.3 In-game: pin a task at a lectern → it appears on the HUD with correct text + done state, legible over a busy background via glow. — Confirmed (playtest 2026-07-25T09-52-31, TESTING.md `f3df9416`)
- [x] 7.4 In-game: complete a task with **Sink** → it mutes and, after ~2s, sinks to the bottom (re-toggle within 2s undoes); with **Unpin** → completion removes it; with **Delete** → completion deletes the task. — Confirmed (playtest 2026-07-25T22-36-25, TESTING.md `8439e474`; policy switching via the Settings UI)
- [x] 7.5 In-game: break the lectern hosting a pinned task → the pin stays on the HUD (player-owned, non-destructive) and is still completable from its snapshot; a re-place restores its source. — Confirmed (playtest 2026-07-25T09-52-31, TESTING.md `9805a162`)
- [x] 7.6 In-game: with more pins than the max, confirm exactly max rows + a "+N more" indicator; change the `HudMaxRows` value in `scribe-hud-config.json` and confirm the HUD honors it (no restart needed beyond a reload). — Confirmed (playtest 2026-07-25T09-52-31, TESTING.md `12dcaaa7`)
- [x] 7.7 In-game: HUD auto-shows on the first pin and hides at zero; the toggle hotkey AND the on-HUD control collapse/expand it (animated); the collapsed state persists across a relog; verify the preference carries to a DIFFERENT world (client-local, cross-world). — Confirmed (playtest 2026-07-25T09-52-31, TESTING.md `16464b57`)
- [x] 7.8 Update `TESTING.md` (via the what-to-test skill) with the in-game items 7.3–7.7; sync deltas to main specs and archive per the openspec flow when confirmed. — TESTING.md carries the confirmed items; specs synced + archived via this archive flow. (Two follow-ups logged, not blockers: `80777b7b` stale-editor-under-Delete, `dfad74a8` hanging HUD checkbox → scribe-list-collapse.)
