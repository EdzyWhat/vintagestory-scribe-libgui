## Context

Scribe depends on LibGUI `gui` 3.1.0 but was written against the 2.0.0 model. In 2.0.0 there was no
inherited text style, so Scribe reaches the player's chosen Task Text Font by constructing a
`TextStyle` at every text widget and setting `FontFamily = taskFont` by hand — 34 `new TextStyle`
sites across 9 files, with `FontFamily = ScribeTaskFont.Resolve(...)` threaded through 15 of them.
Missing one of those threadings is a live bug class: this session alone, the Pinned-tab Completion
policy picker and the Timer-tab mode radios both shipped without the font and had to be patched.

3.1.0 adds `Gui.Widgets.Basic.Theming.DefaultTextStyle : InheritedWidget` and `TextStyle.Merge`.
Verified directly from the shipped `Gui.dll` (with `Gui.pdb`):

```csharp
// DefaultTextStyle
public TextStyle Style { get; }
public DefaultTextStyle(TextStyle style, Widget child, Key? key = null)
public static TextStyle Of(BuildContext context)

// Text.Build
TextStyle textStyle = DefaultTextStyle.Of(context);
TextStyle style = StyleOverride?.Merge(textStyle) ?? textStyle;
```

So an ancestor `DefaultTextStyle` sets defaults, and each `Text` merges its partial override on top.
This is exactly the Flutter mechanism the LibGUI author ported.

## Goals / Non-Goals

**Goals:**
- Set the player's Task Text Font + window-scaled base size once per tab via `DefaultTextStyle`.
- Remove the per-widget `FontFamily = taskFont` threading, eliminating the "forgot to thread it" bug
  class.
- Preserve rendering exactly — same font, same effective size, same live-update behavior.

**Non-Goals:**
- Rich text (`VtmlConverter`), `MarqueeText`/`AnimatedText`/`AnimatedSize`, `LayoutBuilder`,
  `FocusScope`, `ErrorBoundary`, `StepperButton`, `SettingsDialog`/theme presets — each is a separate
  evaluation, out of scope here.
- Removing the `ButtonState` `ForceRebuild` workaround (3.1.0 did not fix the underlying NPE).
- Updating user-facing mod-page copy (`docs/media/mod-page*`) that still says "gui 2.0.0" — flagged,
  not done here.
- Any `src/Core/` change (Core never references the VS API or LibGUI; nothing to do there).

## Decisions

- **Scope the `DefaultTextStyle` per tab, not once globally.** Different tabs already resolve the
  same player settings but build independent subtrees; a per-tab ancestor keeps each tab's Build
  self-contained and avoids threading a style down through the dialog shell. The shared resolution
  logic (font family + scaled size) can live in one helper the tabs call.
- **Inherit family and base size; keep explicit overrides for real deltas.** Color, weight,
  alignment, `SoftWrap`, and intentionally-divergent sizes stay on the per-widget `TextStyle`. Only
  the redundant `FontFamily` (and redundant base `FontSize` where it merely re-derives the tab scale)
  come off.
- **Behavior-preservation is the acceptance bar, verified in-game.** There is no `Core` test surface
  here; the whole risk is visual. Each tab is checked against the current build across the font/size
  setting matrix before the site is considered converted.
- **Convert tab-by-tab, not in one sweep.** Each tab is an independent, individually-verifiable unit,
  which keeps any regression localized and reviewable.

## Risks / Trade-offs

- **Silent font regression.** If a widget's `FontFamily` is removed but no `DefaultTextStyle`
  ancestor covers it (e.g. a widget hoisted into a global overlay / tooltip that renders outside the
  tab subtree), it falls back to the default font. Mitigation: audit overlay/tooltip content
  (`useGlobalOverlay: true`) explicitly — those render through a different subtree and may need their
  own `DefaultTextStyle` or a retained explicit font.
- **The HUD is not a tab.** `HudScribePins` builds outside the dialog. Decide per-widget whether it
  gets its own `DefaultTextStyle` root or keeps explicit styles; do not assume the tab pattern
  applies unchanged.
- **Merge semantics: default-valued fields inherit (verified from the DLL).** `override.Merge(base)`
  compares each field of `override` against a fresh `default(TextStyle)`; if it differs from default
  it wins, otherwise the field is taken from `base`. Confirmed field-by-field in the decompiled body
  (`FontFamily`, `FontSize`, `Color`, `Weight`, `Align`, `Overflow`, `Outline*`, `Glow*`, `Boldness`,
  `SoftWrap`, `Decoration`, `MaxLines`). Two consequences that shape the implementation:
  - A widget setting only `Color` correctly inherits the ancestor's family and size — the partial
    override works as hoped.
  - **A widget that explicitly sets a field to its *default* value silently inherits instead.** The
    landmines are `SoftWrap = false`, `Align = <the enum's zero value>`, `FontSize = 0`, and a
    zero/transparent `Color`. **Rule: the per-tab `DefaultTextStyle` ancestor carries ONLY
    `FontFamily` (+ base `FontSize` if we scale it there) and leaves every other field at default**,
    so per-widget non-default overrides always win and no widget is surprised by inheriting a
    non-default `Align`/`SoftWrap`. In particular, do NOT set `SoftWrap = true` on the ancestor — a
    child wanting `false` (the default) could not override it.
- **Low upside ceiling.** This is cleanup, not a feature; the payoff is fewer future bugs and a
  smaller styling surface, not player-visible value. Worth doing because the bug class is real and
  recurring, but it should not crowd out feature work.
