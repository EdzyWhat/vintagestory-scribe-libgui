// Live cuneiform single-line title input: a drop-in for the base dialog's stock LibGUI TextField (same
// TextEditingController, FocusNode, and OnKeyDown hook), so the base's title-commit machinery
// (_isTitleEditing, CommitTitleIfEditing, the blur listener, the deferred _pendingTitleEditRebuild/_pendingTitleFocus)
// keeps working unchanged; only the rendered widget differs. Renders through ScribeCuneiformFieldRender,
// single-line and hard-clipped by default (no '…' glyph), or wrapped to the band width with SingleLine=false
// (the title slot caps it at two lines). Keyboard handling mirrors TextField (typing, Backspace/Delete,
// Left/Right/Home/End, clipboard) minus selection: caret-only.

using System;
using Gui.Core.Framework;         // RenderObject
using Gui.Widgets.Animations;     // AnimationController, AnimationStatus (stroke-progression reveal)
using Gui.Widgets.Events;         // KeyboardEvent, PointerEvent, IKeyDownHandler, IKeyCharHandler
using Gui.Widgets.Framework;      // Widget, StatefulWidget, State, Theme, IFocusable
using Gui.Widgets.Input;          // FocusNode, GestureDetector, TextEditingController, TextSelection, TextEditingValue
using OpenTK.Mathematics;         // Vector2, Vector4
using Scribe.Core.Cuneiform;      // GlyphBundle
using Vintagestory.API.Client;    // GlKeys

namespace Scribe;

/// <summary>
/// A single-line cuneiform text input bound to an external <see cref="TextEditingController"/> and
/// <see cref="FocusNode"/>, a drop-in for the base dialog's stock <see cref="Gui.Widgets.Input.TextField"/> title
/// editor so <see cref="ScribeDialogBase"/>'s title commit/blur machinery is untouched. Caret-only (no selection).
/// </summary>
public sealed class ScribeCuneiformTitleField : StatefulWidget, IFocusable
{
    public ScribeCuneiformTitleField(
        TextEditingController controller,
        FocusNode focusNode,
        float fontSizeEm,
        GlyphBundle? bundle,
        Action<KeyboardEvent>? onKeyDown = null,
        float jitterStrength = 0f,
        int jitterSeed = 0,
        float rotationDegrees = 0f,
        bool progression = false,
        CuneiformGlow glow = default,
        float strokeWeightScale = 1f,
        bool singleLine = true,
        Gui.Widgets.Framework.Key? key = null)
        : base(key)
    {
        Controller = controller;
        FocusNode = focusNode;
        FontSizeEm = fontSizeEm;
        Bundle = bundle;
        OnKeyDown = onKeyDown;
        JitterStrength = jitterStrength;
        JitterSeed = jitterSeed;
        RotationDegrees = rotationDegrees;
        Progression = progression;
        Glow = glow;
        StrokeWeightScale = strokeWeightScale;
        SingleLine = singleLine;
    }

    public TextEditingController Controller { get; }
    public FocusNode FocusNode { get; }
    public float FontSizeEm { get; }
    public GlyphBundle? Bundle { get; }
    /// <summary>Jitter strength (0..1) for the title strokes; 0 = crisp.</summary>
    public float JitterStrength { get; }
    /// <summary>Fixed jitter seed, so typing does not reseed letters already on screen.</summary>
    public int JitterSeed { get; }
    /// <summary>Whole-character rotation in degrees; 0 = upright. Paint-time only.</summary>
    public float RotationDegrees { get; }
    /// <summary>Whether newly-typed title text presses in stroke-by-stroke (gated by the player's client setting,
    /// like the rows); false reveals instantly.</summary>
    public bool Progression { get; }
    /// <summary>Per-material outer glow behind the title strokes; default disabled.</summary>
    public CuneiformGlow Glow { get; }
    /// <summary>Stroke-weight scale passed to the render object; 1 = base weight.</summary>
    public float StrokeWeightScale { get; }
    /// <summary>When true (default) the title is one hard-clipped line; when false it WRAPS to the band width
    /// (the title slot caps visible height at two lines). Caret nav is by character index either way.</summary>
    public bool SingleLine { get; }
    /// <summary>Parent key hook, invoked BEFORE this field acts on a key (mirrors TextField). The tablet points it
    /// at the base's title key handler for the maxlength gate and Enter/Escape commit.</summary>
    public Action<KeyboardEvent>? OnKeyDown { get; }

    public override State CreateState() => new ScribeCuneiformTitleFieldState();
}

internal sealed class ScribeCuneiformTitleFieldState : State<ScribeCuneiformTitleField>, IKeyDownHandler, IKeyCharHandler
{
    private TextEditingController Controller => Widget.Controller;
    private FocusNode FocusNode => Widget.FocusNode;

    // Stroke-progression reveal, mirroring the row field: only a pure APPEND animates its new suffix; any
    // other change snaps to fully revealed. revealController exists only when Widget.Progression is set.
    /// <summary>The shared stroke-count reveal schedule (same Core speed knob as the rows).</summary>
    private static readonly Scribe.Core.Cuneiform.RevealSchedule RevealScheduleShared =
        new(Scribe.Core.Cuneiform.CuneiformReveal.PerStrokeMs);
    private AnimationController? revealController;
    private bool revealActive;
    private int revealBaselineChars;
    private string revealTrackedText = "";

    public override void InitState()
    {
        base.InitState();
        // The controller and focus node are owned by the base dialog; we only listen so external changes repaint.
        FocusNode.Owner = Element;
        Controller.AddListener(OnControllerChanged);
        FocusNode.AddListener(OnFocusChanged);

        // Existing title text starts fully revealed; only later appends press in. Without Progression there is no
        // controller and the title reveals instantly.
        revealTrackedText = Controller.Text;
        if (Widget.Progression)
        {
            revealController = new AnimationController(
                TimeSpan.FromMilliseconds(1), Element.Owner!.GetTickerProvider());
            revealController.OnValueChanged += OnRevealTick;
            revealController.OnStatusChanged += OnRevealStatus;
        }
    }

    public override void Dispose()
    {
        Controller.RemoveListener(OnControllerChanged);
        FocusNode.RemoveListener(OnFocusChanged);
        if (revealController is not null)
        {
            revealController.OnValueChanged -= OnRevealTick;
            revealController.OnStatusChanged -= OnRevealStatus;
            revealController.Dispose();
            revealController = null;
        }
        base.Dispose();
    }

    private void OnControllerChanged()
    {
        UpdateReveal();
        SetState(() => { });
    }

    private void OnFocusChanged() => SetState(() => { });

    /// <summary>A pure append animates only the new suffix (prior letters become the always-revealed baseline);
    /// anything else snaps to fully revealed.</summary>
    private void UpdateReveal()
    {
        if (revealController is null)
        {
            return;
        }

        string text = Controller.Text;
        bool isAppend = text.Length > revealTrackedText.Length
            && text.StartsWith(revealTrackedText, StringComparison.Ordinal);

        if (isAppend)
        {
            double elapsedMs = revealActive
                ? revealController.Value * revealController.Duration.TotalMilliseconds
                : 0.0;
            if (!revealActive)
            {
                revealBaselineChars = revealTrackedText.Length;
            }

            double durationMs = Scribe.Core.Cuneiform.CuneiformReveal.TotalDurationMs(
                revealBaselineChars, CumulativeRevealUnits(text), RevealScheduleShared);
            if (durationMs > 0)
            {
                revealActive = true;
                revealController.Duration = TimeSpan.FromMilliseconds(durationMs);
                revealController.Forward(from: Math.Clamp(elapsedMs / durationMs, 0.0, 1.0));
            }
        }
        else
        {
            revealActive = false;
            revealController.Stop();
        }

        revealTrackedText = text;
    }

    /// <summary>Prefix-sum of per-character stroke-units for <paramref name="forText"/>, matching the render
    /// object's per-stroke timing; one unit per character when the bundle is not loaded yet.</summary>
    private int[] CumulativeRevealUnits(string forText)
    {
        if (Widget.Bundle is not { } bundle)
        {
            var flat = new int[forText.Length];
            Array.Fill(flat, 1);
            return Scribe.Core.Cuneiform.CuneiformReveal.CumulativeStrokeUnits(flat);
        }

        var layout = new Scribe.Core.Cuneiform.CuneiformLineLayout(bundle);
        return Scribe.Core.Cuneiform.CuneiformReveal.CumulativeStrokeUnits(layout.StrokeUnitsFor(forText));
    }

    private void OnRevealTick(double _) => SetState(() => { });

    private void OnRevealStatus(AnimationStatus status)
    {
        if (status == AnimationStatus.Completed)
        {
            revealActive = false;
            SetState(() => { });
        }
    }

    public void OnKeyChar(KeyboardEvent e)
    {
        if (!FocusNode.HasFocus || e.KeyChar == '\0' || char.IsControl(e.KeyChar))
        {
            return;
        }
        e.Handled = true;
        InsertAtCaret(e.KeyChar.ToString());
    }

    public void OnKeyDown(KeyboardEvent e)
    {
        if (!FocusNode.HasFocus)
        {
            return;
        }

        // Let the parent handler gate the key first (maxlength, Enter/Escape commit); stop if it handled the event.
        Widget.OnKeyDown?.Invoke(e);
        if (e.Handled)
        {
            return;
        }

        // Swallow other keys so they don't leak to game movement (Alt excepted, per LibGUI's TextField).
        if (!e.Alt)
        {
            e.Handled = true;
        }

        string text = Controller.Text;
        int len = text.Length;
        int caret = Math.Clamp(Controller.Selection.ExtentOffset, 0, len);

        switch (e.KeyCode)
        {
            case (int)GlKeys.BackSpace:
                if (caret > 0)
                {
                    Controller.Text = text.Remove(caret - 1, 1);
                    Controller.Selection = TextSelection.Collapsed(caret - 1);
                }
                break;

            case (int)GlKeys.Delete:
                if (caret < len)
                {
                    Controller.Text = text.Remove(caret, 1);
                    Controller.Selection = TextSelection.Collapsed(caret);
                }
                break;

            case (int)GlKeys.Left:
                Controller.Selection = TextSelection.Collapsed(Math.Max(0, caret - 1));
                break;

            case (int)GlKeys.Right:
                Controller.Selection = TextSelection.Collapsed(Math.Min(len, caret + 1));
                break;

            case (int)GlKeys.Home:
                Controller.Selection = TextSelection.Collapsed(0);
                break;

            case (int)GlKeys.End:
                Controller.Selection = TextSelection.Collapsed(len);
                break;

            case (int)GlKeys.V when e.Ctrl:
                string? clip = Element?.Owner?.GetClipboard()?.GetText();
                if (!string.IsNullOrEmpty(clip))
                {
                    InsertAtCaret(clip);
                }
                break;
        }
    }

    private void InsertAtCaret(string s)
    {
        string text = Controller.Text;
        int caret = Math.Clamp(Controller.Selection.ExtentOffset, 0, text.Length);
        Controller.Text = text.Insert(caret, s);
        Controller.Selection = TextSelection.Collapsed(caret + s.Length);
    }

    /// <summary>Places the caret from a click via the render object's hit-test; the GestureDetector proxy wraps
    /// it as its single child, so a proxy-local point maps straight through.</summary>
    private void OnPress(PointerEvent e)
    {
        FocusNode.RequestFocus();
        e.Handled = true;

        if (Element?.RenderObject is not { } proxy) return;
        var render = proxy.Children.Count > 0 ? proxy.Children[0] as IScribeEditableTextRender : null;
        if (render is null) return;

        Vector2 local = proxy.GlobalToLocal(new Vector2(e.X, e.Y));
        int offset = Math.Clamp(render.OffsetAtPosition(local), 0, Controller.Text.Length);
        Controller.Selection = TextSelection.Collapsed(offset);
    }

    public override Widget Build(BuildContext context)
    {
        var colors = Theme.Of(context).ColorScheme;
        int caret = Math.Clamp(Controller.Selection.ExtentOffset, 0, Controller.Text.Length);
        int selectionAnchor = Math.Clamp(Controller.Selection.BaseOffset, 0, Controller.Text.Length);

        return new GestureDetector(
            onPress: OnPress,
            child: new ScribeCuneiformFieldRenderWidget(
                text: Controller.Text,
                caret: caret,
                selectionAnchor: selectionAnchor,
                hasFocus: FocusNode.HasFocus,
                fontSizeEm: Widget.FontSizeEm,
                inkColor: colors.OnSurface,
                caretColor: colors.Primary,
                selectionColor: colors.Primary with { W = 0.35f },
                bundle: Widget.Bundle,
                padX: 0f,   // the title band supplies its own inset; keep the glyphs flush like the RichText did
                padY: 0f,
                boxColor: Vector4.Zero,
                borderColor: Vector4.Zero,
                borderThickness: 0f,
                cornerRadii: Vector4.Zero,
                singleLine: Widget.SingleLine,
                jitterStrength: Widget.JitterStrength,
                jitterSeed: Widget.JitterSeed,
                rotationDegrees: Widget.RotationDegrees,
                glow: Widget.Glow,
                strokeWeightScale: Widget.StrokeWeightScale,
                glyphDrawScale: CuneiformMetrics.GlyphDrawScale,
                caretHeightScale: CuneiformMetrics.CaretHeightScale,
                revealActive: revealActive,
                revealBaselineChars: revealBaselineChars,
                revealElapsedMs: revealController is not null
                    ? revealController.Value * revealController.Duration.TotalMilliseconds
                    : 0.0));
    }
}
