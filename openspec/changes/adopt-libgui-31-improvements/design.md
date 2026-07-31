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
- **Merge semantics: initializer-valued fields inherit (verified from the DLL — CORRECTED during
  implementation).** `override.Merge(base)` compares each field of `override` against `new TextStyle()`
  — the **parameterless ctor, which runs the property initializers**, NOT `default(TextStyle)` (the
  all-zero struct). If a field differs from that initializer value it wins, otherwise it's taken from
  `base`. Confirmed field-by-field in the decompiled `Merge` body. **The initializer sentinels are:
  `FontFamily = "sans-serif"`, `FontSize = 14f`, `Color = Vector4.One` (white), `Weight = Normal`,
  `Align = Left`, `SoftWrap = true`, `Overflow = Clip`, `MaxLines = 0`, all `Outline*/Glow*/Boldness =
  0`.** (An earlier draft of this doc had this inverted — it named `default(TextStyle)` and listed
  `SoftWrap = false` / `Align = zero` / `FontSize = 0` as the inherit-cases. That was backwards.)
  Consequences that shape the implementation:
  - A widget setting a field to a NON-initializer value overrides fine — `Color = OnSurface`,
    `FontSize = 13*scale`, `Weight = Bold`, `Align = Center`, `SoftWrap = false` all win as intended.
    The common partial overrides (e.g. only `Color`) work as hoped.
  - **A widget that sets a field back to its initializer value CANNOT override the ancestor** — Merge
    reads it as "unset" and inherits. The real landmines: you cannot force `FontFamily = "sans-serif"`
    under a non-sans ancestor (sans-serif IS the sentinel), cannot re-assert `FontSize = 14`, white,
    `SoftWrap = true`, or `Align = Left`. So a per-tab ancestor is family-wrap-all-or-nothing:
    descendants that were deliberately neutral sans-serif flip to the ancestor's family and can't opt
    back out. This drove real decisions: HUD + Settings stay UNWRAPPED (all-neutral), and the
    metadata on the wrapped History/Timer/Guestbook tabs deliberately flips to the task font (user
    approved). **Rule: the per-tab `DefaultTextStyle` ancestor carries ONLY `FontFamily` (+ base
    `FontSize`) and leaves every other field at its initializer default**, so per-widget non-default
    overrides always win and nothing is surprised by inheriting a non-default `Align`/`SoftWrap`. In
    particular, do NOT set `SoftWrap`/`Align`/`Color` on the ancestor.
- **Low upside ceiling.** This is cleanup, not a feature; the payoff is fewer future bugs and a
  smaller styling surface, not player-visible value. Worth doing because the bug class is real and
  recurring, but it should not crowd out feature work.
