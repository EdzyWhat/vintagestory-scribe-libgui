## Why

Scribe ships against **LibGUI `gui` 3.1.0** (`src/Mod/modinfo.json`) but its GUI code was written
against the **2.0.0** mental model and never reworked after the DLL bump. LibGUI 3.1.0 grew from ~300
to ~486 public types; the single highest-value addition for a text-heavy mod like Scribe is
**`DefaultTextStyle`** (an inherited text-style widget, Flutter-style) plus **`TextStyle.Merge`**.

Confirmed from the shipped DLL, `Text.Build` now does:

```csharp
TextStyle textStyle = DefaultTextStyle.Of(context);
TextStyle style = StyleOverride?.Merge(textStyle) ?? textStyle;
```

So an ancestor `DefaultTextStyle` supplies defaults and each `Text` overrides only the delta. Scribe
currently fights this by hand: it constructs **34 `new TextStyle`** across 9 files and threads
**`FontFamily = taskFont` 15 times** so the player's chosen Task Text Font reaches every label. That
threading is repetitive, easy to forget (this session we found the Pinned-tab policy picker and the
timer radios each missing it), and the ongoing cost is paid every time a new widget is added.

Adopting `DefaultTextStyle` lets each tab set the player's font + window scale **once** at its
subtree root; children inherit it and specify only what differs (a bold weight, a muted color). This
removes a whole class of "forgot to thread the font" bugs and shrinks the styling surface.

Secondary motivation: the LibGUI dev docs had drifted (said "NOT adopted / v2.0.0" while we ship
3.1.0 as a production hard dep, and told future sessions to re-clone source that is 2.0.0-only). That
doc-freshness fix was applied alongside authoring this proposal; it is recorded here as done, not as
scoped work.

## What Changes

- **Adopt `DefaultTextStyle` for the tab subtrees.** Each dialog tab (Read, Edit, Pinned, History,
  Timer, Settings) wraps its content in a `DefaultTextStyle` carrying the player's resolved Task Text
  Font family and the window-scaled base size. Descendant `Text`/label widgets drop their explicit
  `FontFamily = taskFont` and any redundant `FontSize`, keeping only genuine per-widget overrides
  (color, weight, alignment).
- **Preserve rendering exactly.** This is behavior-preserving: every string that renders in the Task
  Text Font today MUST still render in it, at the same effective size, after the sweep. No visual
  regression; no change to what the player sees.
- **Keep the `ButtonState` workaround.** 3.1.0's `ButtonState` still reads
  `Element.Owner.GetSoundPlayer()` / `GetTickerProvider()` at build/tap time, so the
  "never `ForceRebuild` a mounted Button" rule stays. This proposal does NOT attempt to remove it and
  documents that the upgrade did not fix it.
- **Out of scope (flagged for later, not done here):** `VtmlConverter` rich text; `MarqueeText` /
  `AnimatedText` / `AnimatedSize`; `LayoutBuilder`; `FocusScope`; `ErrorBoundary`; `StepperButton`
  (vs. the just-built `ScribeNumericField`); `SettingsDialog` / theme presets; and updating the
  user-facing mod-page copy (`docs/media/mod-page*`) that still says "gui 2.0.0". Each is a separate,
  independently-shippable evaluation.

## Capabilities

### New Capabilities
- `gui-text-style-inheritance`: GUI text widgets inherit their font family and base size from a
  per-tab `DefaultTextStyle` ancestor rather than each widget re-specifying them; the player's chosen
  Task Text Font and the window text-size scale propagate through inheritance, and adopting this
  mechanism preserves current rendering.

### Modified Capabilities
<!-- none: this is behavior-preserving; no existing requirement's behavior changes -->

## Impact

- **Code (Mod only; `src/Core/` untouched):** the 9 files that build `TextStyle`s /
  thread `taskFont` — `ScribeDialogBase.cs`, `GuiDialogScribeNotebook.cs`,
  `GuiDialogClockmakerNotebook.cs`, `ScribeReadContent.cs`, `ScribeEditorContent.cs`,
  `ScribePinnedContent.cs`, `ScribeSettingsContent.cs`, `HudScribePins.cs`, `ScribeNumericField.cs`
  (plus `ScribeRowConstants.cs` / `ScribeMultilineField.cs` / `ScribeModSystem.cs` which reference
  the font). The HUD is not a "tab" — evaluate whether it gets its own `DefaultTextStyle` root or is
  left as-is.
- **Dependencies:** none added — `DefaultTextStyle` / `TextStyle.Merge` are already in the shipped
  `gui` 3.1.0 DLL. Honors the "no new mod dependencies" guardrail.
- **Docs:** `docs/libgui-reference.md` and `docs/libgui-migration-guide.md` were refreshed alongside
  this proposal (adoption status, 3.1.0-vs-2.0.0 version-skew warning, decompile-the-DLL guidance).
- **Verification:** `Core` suite unaffected (no `Core` change); the risk is visual regression, so
  verification is in-game — confirm every tab's text still uses the chosen Task Text Font at the
  chosen window size across all font/size settings, and that switching the setting still live-updates
  every label.
- **Reference caveat:** the local `reference/vslibgui/` clone is 2.0.0 and does NOT contain these
  APIs; decompile `gui_3.1.0.zip`'s `Gui.dll` (ships with `Gui.pdb`) for ground truth.
