## 1. HUD anchor cache

- [x] 1.1 Add `private record struct AnchorInputs(float ScreenW, float ScreenH, ScribeHudAnchor Anchor, float OffX, float OffY, bool MinimapOn)` inside `HudScribePins` and a `private AnchorInputs? _lastAnchorInputs` field.
- [x] 1.2 At the top of `ApplyAnchor()`, compute the current key from the same values the method already reads, compare to `_lastAnchorInputs`, and return early if equal. If different, store the new key and proceed.
- [x] 1.3 Reset `_lastAnchorInputs = null` in `OnMyPinsChanged()` so a settings change forces one recompute on the next frame (belt-and-suspenders; the key comparison already handles it because the anchor/offset fields will differ).

## 2. Tick guard

- [x] 2.1 Add `if (pendingCompletions.Count == 0) return;` as the first statement in `OnTick()`.

## 3. Verify

- [x] 3.1 Run `dotnet build` — confirm zero errors and zero warnings.
- [x] 3.2 In-game: open the HUD with pins, resize the game window — confirm the HUD repositions correctly.
- [x] 3.3 In-game: check off a pin and confirm the undo window and completion still fire correctly.
