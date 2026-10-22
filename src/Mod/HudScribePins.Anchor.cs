using System.Diagnostics;
using Gui;
using Gui.Widgets.Events;
using OpenTK.Mathematics;
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace Scribe;

public sealed partial class HudScribePins
{
    /// <inheritdoc />
    public override EnumDialogType DialogType => EnumDialogType.HUD;

    /// <summary>Escape falls through to the game; a HUD never closes on it.</summary>
    public override bool OnEscapePressed() => false;

    public override bool ShouldReceiveRenderEvents() => IsOpened();

    /// <summary>Keyboard stays with the game; mouse stays on so checkboxes are clickable and clicks outside the
    /// content fall through.</summary>
    public override bool ShouldReceiveKeyboardEvents() => false;

    /// <inheritdoc />
    protected override WindowConfig CreateWindowConfig() => new()
    {
        // Size null shrink-wraps to content (the anchor is applied post-layout). A concrete Position suppresses
        // the base's first-frame auto-centering; OpaqueHitTest off lets clicks outside the content reach the world.
        Size = null,
        Position = Vector2.Zero,
        Draggable = false,
        Resizable = false,
        OpaqueHitTest = false,
        Clip = false,
    };

    /// <summary>Re-anchors right after shrink-wrap layout settles so a row-count change doesn't mis-position
    /// the window for a frame.</summary>
    protected override void OnShrinkWrapLayoutCompleted() => ApplyAnchor();

    /// <inheritdoc />
    public override void OnRenderGUI(float deltaTime)
    {
        // LibGUI recomputes hover only on real mouse motion, so arm the latch to re-dispatch a synthetic
        // pointer-move for a few frames when: (1) a collapse is animating; (2) a reconcile reorders rows (armed in
        // RebuildHudBody, since RootElement is unchanged); (3) a remount swaps RootElement (self-open).
        if (collapseRegistry.AnyAnimating) hoverRefreshLatch.Arm();
        hoverRefreshLatch.ArmIfRebuilt(RootElement);
        if (hoverRefreshLatch.Tick()) RefreshHoverAtCursor();

        // Re-anchor every frame (a window resize needn't re-run layout). WindowSize is part of AnchorInputs so the
        // position self-corrects once real content lays out.
        ApplyAnchor();
        base.OnRenderGUI(deltaTime);
    }

    /// <summary>Re-dispatches a synthetic pointer-move at the cursor so LibGUI re-runs hit-testing; see
    /// <see cref="ScribeHoverRefresh"/> for the coordinate conversion.</summary>
    private void RefreshHoverAtCursor()
    {
        if (RootElement?.RenderObject == null) return;
        var local = ScribeHoverRefresh.ToWindowLocal(
            capi.Input.MouseX, capi.Input.MouseY, GetUiScale(), WindowPos);
        EventDispatcher.DispatchPointerMove(RootElement, new PointerEvent(local.X, local.Y));
    }

    /// <summary>Minimap clearance width for the <see cref="ScribeHudAnchor.TopRight"/> anchor, read from the
    /// open vanilla minimap HUD dialog's bounds, converted to this HUD's GUIScale-normalized units, plus the
    /// vanilla dialog-to-screen padding. OuterWidth is raw pixels, divided by scale to match how screenW is
    /// derived from FrameWidth. Falls back to <see cref="DefaultTopRightMinimapClearanceX"/> when the
    /// dialog isn't resolvable (not composed yet, or the full map is open).</summary>
    private float ResolveTopRightMinimapClearanceX(float scale)
    {
        var minimapBounds = capi.Gui.OpenedGuis
            .OfType<GuiDialogWorldMap>()
            .FirstOrDefault(dlg => dlg.DialogType == EnumDialogType.HUD)
            ?.SingleComposer?.Bounds;
        if (minimapBounds == null)
        {
            TraceMinimapClearance(scale, null, DefaultTopRightMinimapClearanceX);
            return DefaultTopRightMinimapClearanceX;
        }

        float result = (float)(minimapBounds.OuterWidth / scale) + (float)GuiStyle.DialogToScreenPadding;
        TraceMinimapClearance(scale, minimapBounds, result);
        return result;
    }

    private float? _lastTracedMinimapClearance;

    /// <summary>Diagnostic-only: logs the inputs behind <see cref="ResolveTopRightMinimapClearanceX"/> when the
    /// result changes. Filter <c>[scribe-hud-anchor]</c> in <c>build/scribe-log.sh --client</c>.</summary>
    [Conditional("DEBUG")]
    private void TraceMinimapClearance(float scale, ElementBounds? minimapBounds, float result)
    {
        if (_lastTracedMinimapClearance == result) return;
        _lastTracedMinimapClearance = result;

        if (minimapBounds == null)
        {
            capi.Logger.Notification(
                "[scribe-hud-anchor] scale={0} minimap=<unresolved> -> fallback clearanceX={1}", scale, result);
            return;
        }

        capi.Logger.Notification(
            "[scribe-hud-anchor] scale={0} minimap.OuterWidth={1} minimap.absX={2} minimap.absY={3} " +
            "FrameWidth={4} -> clearanceX={5}",
            scale, minimapBounds.OuterWidth, minimapBounds.absX, minimapBounds.absY, capi.Render.FrameWidth, result);
    }

    /// <summary>
    /// Positions the shrink-wrapped window per <see cref="ScribePlayerSettings.HudAnchor"/> plus the
    /// <see cref="ScribePlayerSettings.HudOffsetX"/>/<see cref="ScribePlayerSettings.HudOffsetY"/> nudge.
    /// A positive offset always moves the window toward screen center from the anchored edge. The
    /// <see cref="ScribeHudAnchor.TopRight"/> anchor adds <see cref="ResolveTopRightMinimapClearanceX"/> so the
    /// HUD clears the minimap by default.
    /// </summary>
    private void ApplyAnchor()
    {
        float scale = RuntimeEnv.GUIScale;
        float screenW = capi.Render.FrameWidth / scale;
        float screenH = capi.Render.FrameHeight / scale;

        var settings = modSystem.MySettings;
        var anchor = ScribePlayerSettings.NormalizeAnchor(settings.HudAnchor);

        bool minimapOn = !capi.Settings.Bool.Exists("showMinimapHud") || capi.Settings.Bool["showMinimapHud"];
        float minimapClearanceX = anchor == ScribeHudAnchor.TopRight && minimapOn
            ? ResolveTopRightMinimapClearanceX(scale)
            : 0f;
        var key = new AnchorInputs(
            screenW, screenH, anchor, minimapClearanceX + settings.HudOffsetX, settings.HudOffsetY, minimapOn,
            minimapClearanceX, WindowSize);
        if (_lastAnchorInputs == key) return;
        _lastAnchorInputs = key;

        // Offsets are relative to the anchor's pre-baked offset (only TopRight has one: the minimap clearance on
        // X). The clearance applies only when the minimap is on; "showMinimapHud" is written by GuiDialogWorldMap,
        // so absent means never toggled, i.e. on by default.
        float offX = key.OffX;
        float offY = key.OffY;

        // Left/top edges add the offset and right/bottom edges subtract it (both toward center); center anchors shift from the midpoint.
        float x = anchor switch
        {
            ScribeHudAnchor.TopLeft or ScribeHudAnchor.MiddleLeft or ScribeHudAnchor.BottomLeft
                => CornerMargin + offX,
            ScribeHudAnchor.TopMiddle
                => (screenW - WindowSize.X) / 2f + offX,
            _ // TopRight, MiddleRight, BottomRight
                => screenW - WindowSize.X - CornerMargin - offX,
        };

        float y = anchor switch
        {
            ScribeHudAnchor.TopLeft or ScribeHudAnchor.TopMiddle or ScribeHudAnchor.TopRight
                => CornerMargin + offY,
            ScribeHudAnchor.MiddleLeft or ScribeHudAnchor.MiddleRight
                => (screenH - WindowSize.Y) / 2f + offY,
            _ // BottomLeft, BottomRight
                => screenH - WindowSize.Y - CornerMargin - offY,
        };

        // Never let the window escape the screen (a large offset or a huge row count could push it off).
        WindowPos = new Vector2(
            Math.Clamp(x, 0f, Math.Max(0f, screenW - WindowSize.X)),
            Math.Clamp(y, 0f, Math.Max(0f, screenH - WindowSize.Y)));
    }
}
