# LibGUI Reference

A working reference for **LibGUI** (`ripls56/vslibgui`) — a from-scratch, Flutter-style reactive
UI framework for Vintage Story that bypasses the native `GuiComposer`/`GuiElement` system and
renders via SkiaSharp. Written for the `explore-libgui-adoption` change so we don't re-derive this
each session.

> **Status of adoption:** ADOPTED — LibGUI is a production hard dependency (modid `gui`). The
> go/no-go spike (archived `explore-libgui-adoption`) came back GO; the lectern read view migrated
> in `adopt-libgui-foundation`, the editor in `migrate-editor-view-libgui`, and the native
> `GuiComposer` lectern dialog has been deleted. See the Scribe→LibGUI mapping in
> [`libgui-migration-guide.md`](libgui-migration-guide.md) and the adopted-facts log in
> `VSAPI-NOTES.md` (§ "LibGUI").

> ⚠️ **Version skew — read before trusting the local source clone.** We ship against **gui 3.1.0**
> (see `src/Mod/modinfo.json`), but the local `./reference/vslibgui/` clone and the GitHub upstream
> are both stuck at **2.0.0** (upstream `main` = commit `42503d9`, and there is **no 3.1.0 tag or
> branch** — the author ships compiled DLLs to the mod portal ahead of pushing source). So the clone
> below is a 2.0.0 reference, useful for the shared core but **wrong on anything new in 3.1.0**
> (`DefaultTextStyle`, `TextStyle.Merge`, `VtmlConverter`, `MarqueeText`, `AnimatedText`,
> `ErrorBoundary`, `LayoutBuilder`, `FocusScope`, `SettingsDialog`, theme presets). **For 3.1.0
> ground truth, decompile the shipped DLL** — this is the authoritative source, not GitHub:
> ```
> ~/.dotnet/tools/ilspycmd -l c "$HOME/Library/Application Support/VintagestoryData/Mods/gui_3.1.0.zip → Gui.dll"
> # extract the zip first; Gui.pdb ships alongside, so line numbers are accurate.
> ```

## Where to look things up (do this before guessing)

Local, gitignored clones exist — **search them with ripgrep before assuming a top-level summary is
complete** (the wiki and the source disagree in at least one important place; see the variable-height
note below), but heed the version-skew warning above:

- **Wiki** → `./.wiki/*.md` — e.g. `rg -i "variableHeight" ./.wiki/`
- **Full source (2.0.0)** → `./reference/vslibgui/` — ground truth vs. the wiki *for the 2.0.0 core*.
  E.g. `rg -n "public ListView" ./reference/vslibgui/`. **Do not trust it for 3.1.0-only APIs** —
  decompile `Gui.dll` from `gui_3.1.0.zip` for those.

Re-clone if missing (note: these fetch **2.0.0**, not the 3.1.0 we ship):
```
git clone --depth 1 https://github.com/ripls56/vslibgui.wiki.git .wiki
git clone --depth 1 https://github.com/ripls56/vslibgui.git reference/vslibgui
```

**Wiki pages this doc draws from** (all under `./.wiki/`): Home, Getting-Started, Architecture,
Widgets-Reference, Windowing, Dialogs, Layout, State-Management, Animations, Event-Handling,
Custom-Widgets, Scrolling, Rendering, Extensibility. (The Home table also links a "Sound" page,
but it 404s — no such page exists yet.) Plus the mod portal (https://mods.vintagestory.at/libgui)
and the GitHub source.

## Identity & facts

| | |
|---|---|
| Repo | `github.com/ripls56/vslibgui` (portal name **libGUI**) |
| **Dependency modid** | **`gui`** (generic — collision risk); assembly **`Gui.dll`** |
| License / version | MIT / **v3.1.0 shipped** (we depend on it); local source clone + GitHub upstream are **2.0.0 only** — see the version-skew warning above |
| Target | VS **1.22.0–1.22.3**, **net10.0** |
| Side | `Universal`, `requiredOnClient: true`, `requiredOnServer: false` (rendering is client-only) |
| Maturity | Young: ~1861 downloads, 2 consumers (HudUI, ChatUI — both by the author), 2 retracted early releases |
| Renders via | **SkiaSharp** on the Ortho render stage; **HarfBuzz** text shaping (native `.dylib`/`.so`/`.dll`) |
| Deps pulled in | SkiaSharp, HarfBuzzSharp, Svg.Skia, OpenTK.\*, 0Harmony (all resolve from the game's `Lib/`) |

**Apple-Silicon caveat (the make-or-break unknown):** LibGUI maps macOS to a **single `osx` RID**
(`NativeLibraryLoader.cs` — `if (OperatingSystem.IsMacOS()) return "osx"`), i.e. no
`osx-arm64`/`osx-x64` split. If the shipped `libHarfBuzzSharp.dylib` isn't arm64/universal, text
shaping fails. This is the same class of native-render risk that already makes VSImGui dead on
this Mac — the spike must confirm LibGUI actually renders here.

## The model — three trees (Flutter, ported to C#)

If you know Flutter, you know LibGUI.

```
   Widget tree            Element tree              RenderObject tree
  (immutable config)  →  (mounted, reconciled)  →  (layout + paint)
  recreated each          long-lived; holds          Size / X / Y /
  Build(); cheap          State<T>; diffed via       LayoutConstraints;
  to allocate             CanUpdate(old,new)         PaintInternal()
```

- **`Widget`** — immutable description. `StatelessWidget` (override `Build`), `StatefulWidget` +
  `State<T>` (`CreateState`), `InheritedWidget` (context propagation, e.g. `Theme`),
  `RenderObjectWidget` (wraps a custom `RenderBox`).
- **`Element`** — the reconciled instance. **`BuildOwner`** holds a dirty set, sorts by depth
  (parents first), and rebuilds once per frame. `UpdateChild` reuses an element when
  `CanUpdate` (same runtime type **and** same `Key`), else unmounts + recreates.
- **`RenderObject`/`RenderBox`** — does layout against `LayoutConstraints` and paints at local
  origin `(0,0)`; the parent positions it.
- **Frame loop** (`GuiBase.OnRenderGUI`): advance tickers → `BuildOwner.BuildDirtyElements()` →
  layout (only if `NeedsLayout`) → translate/clip → paint into an `SKPictureRecorder`, caching the
  `SKPicture` so **clean frames replay the cached picture** instead of repainting.

### State & rebuilds

- **`SetState(Action fn)`** is the only correct way to trigger a rebuild — runs `fn`, marks the
  element dirty; **only that element's subtree rebuilds**, ancestors are untouched.
- Don't read UI state outside `Build()`; don't call `SetState` from inside `Build()`.
- **Controlled-component pattern** (parent owns value, child is stateless):
  `new Checkbox(value: _done, onChanged: v => SetState(() => _done = v))`.
- **`TextEditingController`** exposes a `TextField`'s text (add listener in `InitState`, remove in
  `Dispose`). `ValueNotifier<T>`/`ChangeNotifier` for shared observable values;
  `ListenableBuilder` rebuilds on change (this is how the theme hot-reloads).
- **Keys:** use `ValueKey<T>` to give list items stable identity across reorders — without a key,
  identity is positional (directly relevant to Scribe's drag-reorder). `GlobalKey` lets you reach
  an element imperatively (used by `Scrollable.EnsureVisible`).

## Layout

- **`LayoutConstraints { MinWidth, MaxWidth, MinHeight, MaxHeight }`** — factories `Tight(w,h)`
  (exact), `Loose(maxW,maxH)` (min 0), `TightFor(width?, height?)`; helpers `Loosen()`,
  `Enforce(other)`, `Constrain(size)`. Root gets `Tight(windowSize)` for a fixed window, or
  `Loose(screen)` for a shrink-wrap window.
- **Flex (`Row`/`Column`, both wrap `RenderFlex`)**: `MainAxisAlignment`
  (Start/End/Center/SpaceBetween/SpaceAround/SpaceEvenly), `CrossAxisAlignment`
  (Start/Center/End/**Stretch**), `MainAxisSize` (Max = fill / Min = shrink-wrap), `Spacing`.
  Three-pass algorithm: (1) lay out fixed children, (2) divide remaining main-axis space among
  `Expanded`/`Flexible` by flex factor, (3) position via alignment.
- **`Expanded(flex:, child:)` / `Flexible`** — claim remaining main-axis space proportionally;
  only valid as a **direct** child of `Row`/`Column`.
- **`Wrap`** — flows children into runs, wrapping on overflow (`spacing`, `runSpacing`).
- **`Stack` + `Positioned`** — absolute overlay layout when you need it.
- **`Align`/`Center`** (normalized `-1..1`), **`Padding`** (`EdgeInsets`), **`SizedBox`**,
  **`ConstrainedBox`**, **`AspectRatio`**, **`FittedBox`**, **`IntrinsicHeight`/`IntrinsicWidth`**
  (size a child to its natural content — useful for variable-height rows).
- **No percentage/fractional sizing widget** — relative sizing is via `Expanded`, `Stretch`, and
  `Align`, not "50%".

> **Headline gotcha:** inside any scroll view `Expanded` is **inert** — the viewport gives children
> unbounded height (`Loose(width, 100000)`), so there's nothing to expand into. Rows in a scroll
> view need an explicit height (`SizedBox`, `itemHeight`, or a variable-height measurement path).

## Data-driven rows / "box generation"

There is **no dedicated layout-generator API**. Data → rows happens one of three ways:

1. **`ListView` / `GridView.Builder`** with `itemBuilder: (ctx, index) => widget` — the
   virtualized path for large/uniform lists.
2. **LINQ into `children`**: `items.Select(x => new MyRow(x)).ToList()` fed to
   `Row`/`Column`/`Wrap`/`Stack`/`ListView`/`GridView`.
3. **`Wrap`** — auto-flowing multi-row layout.

Row *sizing* is then the `RenderFlex` job above (`Expanded`, `Stretch`, `MainAxisSize`).

## Scrolling & virtualization

`ListView` **virtualizes**: only viewport items (+1 buffer each side) are live — "a list of 10,000
items maintains ~15 live elements." Off-range elements are **unmounted**; entering ones are rebuilt
and cached by index. Visible range: `firstVisible = floor(offset/itemHeight) - 1`,
`lastVisible = ceil((offset + viewportHeight)/itemHeight) + 1`.

> **Variable-height rows — wiki/source disagreement (verified in source):** the wiki's *Scrolling*
> page shows only uniform `itemHeight` and says "all items must have the same height." **The source
> is richer** — `ListView` has constructors taking `estimatedItemHeight` + `variableHeight: true`
> (`reference/vslibgui/Gui/Gui/Widgets/Scroll/ListView.cs:44` and `:88`), backed by an
> `ItemHeightCache` that measures each row on first layout and corrects offsets. **This is the
> feature Scribe's editable, growing rows depend on — trust the source, and prove it in the spike.**

- **`ScrollController`** (`Attach(tickerProvider)` first): `Offset`, `ViewportSize`, `ContentSize`,
  `MaxScrollExtent = Max(0, ContentSize - ViewportSize)`, `OnChanged`; methods `JumpTo`,
  `AnimateTo(offset, duration, curve)`, `StartSimulation(velocity, min, max)` (kinetic fling),
  `Dispose`. Default physics `ClampingScrollPhysics` (drag 25.0, min velocity 100 px/s).
- **`Scrollbar` is standalone** — not built into `ListView`; wrap the scrollable and share one
  `ScrollController`. Track/thumb default to `OnSurface` at alpha 0.1/0.4.
- **`Scrollable.EnsureVisible(element, duration?, curve?)`** — scrolls the nearest scrollable
  ancestor to reveal a `GlobalKey`ed target (replaces manual scroll-into-view math).
- **`SingleChildScrollView`** — NOT virtualized (builds the whole child); small content only.
- **`RepaintBoundary`** caches a subtree as an `SKPicture` — wrap static neighbors of animated
  content; avoid on per-frame-animated or tiny widgets.

## Theming & styling — the structured seam Scribe never had

- **`ColorScheme`** (readonly struct) — ~18 semantic `Vector4` colors: `Primary`/`OnPrimary`,
  `Secondary`/`OnSecondary`, `Surface`/`OnSurface`/`OnSurfaceVariant`/`SurfaceLow`/`SurfaceHigh`,
  `Background`/`OnBackground`, `Border`/`OutlineVariant`, `Error`/`OnError`, `StateHover`,
  `StateSelected`. The default palette is a warm gold/brown "parchment" set — already close to
  Scribe's aesthetic. (`reference/vslibgui/Gui/Gui/Widgets/Framework/Theme.cs:15`.)
- **`ThemeData`** = `ColorScheme` + `TextTheme` + per-widget style structs (`ButtonStyle`,
  `CheckboxStyle`, `SliderStyle`, `DropdownStyle`, `ProgressBarStyle`, `ItemSlotStyle`, …), each
  with a `Default(colors)` factory so widget styles derive **relatively** from the scheme. Read it
  via **`Theme.Of(context).ColorScheme`**.
- **User-configurable + hot-reload:** `ThemeConfig`/`GuiConfig` load `#RRGGBB[AA]` overrides from
  `ModConfig/libgui.json`, merged over defaults; a `FileSystemWatcher` **live-reloads** the theme
  (screens rebuild via a `ListenableBuilder` on the theme notifier).
- **`BoxStyle`** (on `Container`) — the per-widget styling struct: `Width`/`Height`, `Padding`
  (`EdgeInsets`), `Color` (`Vector4`), gradient, `CornerRadius` (`Vector4`: X=TR,Y=BR,Z=TL,W=BL;
  `new Vector4(r)` = uniform), `BorderThickness`/`BorderColor`, `BoxShadow` list, `ClipBehavior`
  (None/HardEdge/AntiAlias), `HitTestBehavior` (Defer/Opaque/Translucent).
- **`TextStyle`** — `FontSize`, weight, align, overflow, outline, glow, `Color` (draw order:
  glow → outline → fill). **`VtmlText`** renders VS VTML markup (`<br>`, `<strong>`, `<i>`,
  `<font>`, `<a>`, `<icon>`, `<itemstack>`, `<hk>`). **`EdgeInsets`**: `Zero`, `All`, `Only`,
  `Symmetric(vertical:, horizontal:)`, `Ltrb`.
- Bundled fonts (Cormorant Unicase, JetBrains Mono, Playfair Display) via `FontRegistry`.

## Dialogs & block-entity integration (Scribe's exact need)

- **`GuiBase : GuiDialog`** — a real VS dialog: `TryOpen`/`TryClose`, focus, lifecycle all
  integrate normally. Override `Build()` (immutable tree, runs once on open) and
  `CreateWindowConfig()`.
- **`GuiDialogBlockEntityBase : GuiBase`** — for block-bound dialogs. Verified in
  `reference/vslibgui/Gui/Gui/GuiDialogBlockEntityBase.cs`:
  - Constructors: `(BlockPos pos, ICoreClientAPI capi)` **(no inventory — matches Scribe's lectern,
    which isn't an inventory block)** and `(BlockPos, InventoryBase, ICoreClientAPI)`.
  - **`SendBlockEntityPacket(object payload)`** and `SendBlockEntityPacket(int packetId)` — the
    server-authoritative wire Scribe already uses; `OnGuiClosed()` sends sentinel **`1001`** on
    close and syncs inventory if present.
  - **Auto-close on walk-away:** `OnFinalizeFrame` checks `IsOutOfRange(playerPos, pos,
    InteractionRange)` — overridable `InteractionRange`. (Scribe currently overrides
    `IsInRangeOfBlock` on the native base to fix a Creative-reach bug — verify the equivalent here
    in the spike.)
  - **Floaty positioning:** override `Anchor` returning a **cached** `WorldAnchor` (do not allocate
    per call); dialog floats above the block in immersive mouse mode. `WorldAnchor` takes
    primitives (matrices/frame/scale) so it's unit-testable; `TryProject` returns false when behind
    camera (keep last position).

## Events, animations, custom widgets, extensibility (condensed)

- **Events:** `GestureDetector` (`onTap`/`onEnter`/`onExit`/`onPress`/`onRelease`/`onMove`/
  `onWheel`; handling sets `PointerEvent.Handled`), `MouseRegion` (hover/cursor), depth-first
  hit test (top-most first). Keyboard/focus: `FocusNode`, `FocusManager.RequestFocus`,
  `IKeyDownHandler`/`IKeyCharHandler`. `HitTestBehavior` Opaque/Translucent/Defer.
- **Animations:** `AnimationController(duration, tickerProvider)` (create in `InitState`, dispose in
  `Dispose`), `CurvedAnimation` + `Curves.*`, tweens (`FloatTween`, `ColorTween`, `Vector4Tween`,
  `OffsetTween`), `AnimatedBuilder`; implicit widgets `AnimatedOpacity`/`AnimatedContainer`/
  `AnimatedScale`/`AnimatedSlide`/etc. (Relevant to the roadmap's checkbox-stamp / drag-preview
  animations.)
- **Custom widgets:** prefer composition (`StatelessWidget`/`StatefulWidget`); drop to
  `RenderObjectWidget` + a `RenderBox` (`PerformLayout` + `PaintInternal` using
  `context.SharedPaint`, no allocations in paint) only for genuinely novel layout/painting. Use
  `SetProperty(ref field, value, relayout:/repaint:)` in setters; always remove listeners in
  `Dispose` (the #1 leak source).
- **Extensibility:** `WidgetTransformerRegistry.Register(key, IWidgetTransformer)` lets *other*
  mods alter keyed widgets in your screens without Harmony — only widgets with an explicit `Key`
  are eligible.
- **Debug commands:** `/ui tree | bounds | paint | heatmap | redraw | recreate | showcase | flash`
  — a built-in live inspector/perf overlay (a possible replacement for Scribe's
  `ScribeInspectOverlay`).

## Integration mechanics (how a consumer wires it up)

- Reference `Gui.dll` with `<Private>false</Private>`; `OpenTK.Mathematics.dll` and
  `SkiaSharp.dll` resolve from `$(VINTAGE_STORY)/Lib` (also `<Private>false</Private>`). Not on
  NuGet.
- Declare a dependency on modid **`gui`** in `modinfo.json`.
- `GuiModSystem` (LibGUI's `ModSystem`) sets up the Skia renderer + theme watcher on
  `StartClientSide`; your dialog just subclasses `GuiBase`/`GuiDialogBlockEntityBase`.

## 3.1.0 improvements not yet adopted

The spike questions are all answered (adoption is done — renders on Apple Silicon, block-entity
lifecycle + packets intact, variable-height rows work). The open thread now is **which 3.1.0
additions to adopt**, since we migrated onto a 2.0.0 mental model and upgraded the DLL without
reworking the code. Tracked in `openspec/changes/adopt-libgui-31-improvements/` (proposal). Headline
candidates:

- **`DefaultTextStyle` + `TextStyle.Merge`** (biggest win) — an inherited text style Scribe could set
  once per tab, collapsing the ~34 hand-built `TextStyle`s and ~15 manual `FontFamily = taskFont`
  threadings into "override only the delta."
- **`VtmlConverter.Convert(vtml, TextStyle, ILogger?)`** — rich text for notes/guestbook.
- **`MarqueeText`, `AnimatedText`, `AnimatedSize`, `LayoutBuilder`, `FocusScope`, `ErrorBoundary`,
  `StepperButton`, `SettingsDialog`, theme presets** — situational; evaluate per feature.

**Still true after the upgrade (do not expect 3.1.0 to have fixed it):** `ButtonState` still reads
`Element.Owner.GetSoundPlayer()` / `GetTickerProvider()` at build/tap time, so the NPE-on-remount
means the "never `ForceRebuild` a mounted Button" workaround remains necessary.
