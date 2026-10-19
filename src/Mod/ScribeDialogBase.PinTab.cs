using System;
using System.Collections.Generic;
using System.Diagnostics;        // Conditional (DEBUG-only scroll trace)
using System.Linq;
using Gui;                       // GuiDialogBlockEntityBase, WindowConfig
using Gui.Rendering;             // EdgeInsets
using Gui.Rendering.Text;        // TextStyle, FontWeight
using Gui.Widgets.Basic;         // Text, WindowFrame, VsIcon, Container, Button
using Gui.Widgets.Events;        // PointerEvent, KeyboardEvent
using Gui.Widgets.Framework;     // Widget, StatefulWidget, State, Theme, ValueKey, Key
using Gui.Widgets.Input;         // Checkbox, FocusNode, GestureDetector, MouseRegion, Dropdown, DropdownItem, TextField, TextFieldStyle, TextEditingController, TextSelection, TextEditingValue
using Gui.Widgets.Gestures;      // ScrollController
using Gui.Widgets.Layout;        // Column, Row, Expanded, Padding, SizedBox, Center, Align, Alignment, CrossAxisAlignment, MainAxisAlignment
using Gui.Widgets.Overlay;       // Tooltip
using Gui.Widgets.Painting;      // BoxStyle
using Gui.Widgets.Scroll;        // ListView, SingleChildScrollView, Scrollable, Scrollbar
using Gui.Widgets.Spans;         // TextSpan
using Gui.Core.Layout;           // MainAxisSize
using OpenTK.Mathematics;        // Vector2
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;   // ItemStack (Tracker/Link display item)
using Vintagestory.API.Config;   // Lang, GlobalConstants
using Vintagestory.API.MathTools;  // BlockPos

namespace Scribe;

public abstract partial class ScribeDialogBase
{
    // ---------------- Pin Tab ----------------

    /// <summary>The Pin Tab body: the player's pins across every document (pin-list order, no row cap), each editable via <see cref="ScribeEditRow"/> but sourced from <see cref="ScribeModSystem.MyPins"/>, plus the completion-policy picker. Focus uses dialog-owned <see cref="pinFocusNodes"/> keyed by TaskId.</summary>
    private Widget BuildPinnedContent()
    {
        SyncPinFocusNodes();

        float layoutW = host.GetLayout(modSystem.MySettings.PixelArtSize).W;

        Guid? autoFocus = autoFocusPinTaskId;
        autoFocusPinTaskId = null; // one-shot

        var orderedPins = OrderedPinsForDisplay();

        // Row text seeds from the live edit buffer if a keystroke is mid-resync, else the server snapshot.
        var rows = orderedPins
            .Select(p =>
            {
                // Resolve a pinned Tracker/Link's icon + name here (where capi lives); a plain Task yields (null, null) and keeps its text field.
                var (stack, name) = ResolvePinItem(p);
                // The pin snapshot carries its own assignment provenance (no source document), so only the assigner uid needs resolving.
                string? assignerName = p.IsAcceptedAssignment ? ResolvePlayerNameForInbox(p.AssignerUid) : null;
                // Same synthetic page-scheme substitution as the read/editor rows. Display-only.
                bool isStaticVsQuestObjective = p.Kind == ScribeBlockKind.QuestObjective
                    && ScribeQuestCatalog.IsStaticObjectiveCode(p.LinkTarget);
                string? iconLinkTarget = p.Kind == ScribeBlockKind.QuestObjective && stack is null
                    && !isStaticVsQuestObjective
                    ? ScribeLinkTarget.ForPage(p.LinkTarget ?? "")
                    : p.LinkTarget;
                return new ScribePinRowData(
                    p.OwnerDocId, p.TaskId, p.LastKnownDone,
                    pinEditBuffer.TryGetValue(p.TaskId, out var buffered) ? buffered : p.LastKnownText,
                    Kind: p.Kind, DisplayStack: stack, DisplayName: name,
                    TargetQuantity: p.TargetQuantity, CurrentQuantity: p.CurrentQuantity, LinkTarget: iconLinkTarget,
                    Depth: p.Depth, IsAcceptedAssignment: p.IsAcceptedAssignment, ExtraInfo: p.ExtraInfo,
                    AssignerName: assignerName,
                    AssignedDate: p.IsAcceptedAssignment
                        ? AssignmentDisplay.Date(p.AssignedDate, p.AssignedTimestamp, capi.World)
                        : null,
                    AcceptedDate: p.IsAcceptedAssignment
                        ? OptionalAssignmentDate(p.AcceptedDate, p.AcceptedTimestamp)
                        : null,
                    IsStaticVsQuestObjective: isStaticVsQuestObjective);
            })
            .ToList();

        return new ScribePinnedContent(
            rows: rows,
            focusNodes: pinFocusNodes,
            autoFocusTaskId: autoFocus,
            onTextChanged: OnPinTextChanged,
            onCommitText: CommitPinTextEdit,
            onToggleComplete: OnPinCompleteTask,
            onDelete: OnPinDeleteTask,
            onUnpin: OnPinUnpinTask,
            // A pinned item name is a hyperlink to its target's Handbook page, from the snapshot code, so it works with the source document unloaded.
            onOpenLink: OnPinOpenLink,
            onReorder: OnPinReorder,
            completionPolicy: modSystem.MySettings.CompletionPolicy,
            onCompletionPolicyChanged: p => modSystem.UpdateMySettings(s => s.CompletionPolicy = p),
            // Matches the title row's horizontal inset so the picker lines up with the title band.
            policyPickerPadding: EdgeInsets.Only(left: 10 + 0.04f * layoutW, right: 0.04f * layoutW),
            style: RowStyle,
            scrollController: sharedScrollController,
            showSubtitleRow: modSystem.VisualTuning.ShowSubtitleRow,
            // Host-owned so a removed pin's collapse survives the resync reconcile and OnRenderGUI can read AnyAnimating.
            collapseRegistry: pinCollapseRegistry,
            // A departing row finished collapsing: re-clamp the shorter scroll extent.
            onDepartureSettled: RequestClampToExtent,
            // Live shade so the policy-caption hover tooltip dims with the body in low light.
            currentShade: currentShade,
            // Per-surface restyle of the policy picker's open menu (the chalkboard fixes unreadable selected-row colors).
            decoratePolicyDropdownStyle: DecoratePolicyDropdownStyle,
            assignedStampBitmap: modSystem.GetGuiTextureBitmap(ScribeAssignedTaskIcon.Asset),
            externalStampBitmap: modSystem.GetGuiTextureBitmap(ScribeExternalInfoIcon.Asset));
    }

    /// <summary>Resolves a pinned item's icon + name from its snapshot code (like <see cref="ResolveRowItem"/> but without a live document block, so it works when the source is unloaded); <c>(null, null)</c> for a plain Task pin.</summary>
    private (ItemStack? Stack, string? Name) ResolvePinItem(ScribePinnedRef p)
    {
        if (p.Kind == ScribeBlockKind.QuestObjective)
        {
            // Mirrors ResolveRowItem's QuestObjective branch: the match code lives in LinkTarget, bypassing scheme dispatch.
            return p.TargetItemCode is { } objItemCode
                ? ScribeItemRef.ResolveDisplay(capi.World, objItemCode, null)
                : (null, p.LinkLabel ?? p.LinkTarget);
        }
        string? code = p.Kind switch
        {
            // A Craft parent's recipe OUTPUT item is in TargetItemCode, like a Tracker.
            ScribeBlockKind.Tracker or ScribeBlockKind.Craft => p.TargetItemCode,
            ScribeBlockKind.Link => p.LinkTarget,
            _ => null,
        };
        if (code is null) return (null, null);
        return ScribeItemRef.ResolveDisplay(capi.World, code, p.LinkLabel);
    }

    /// <summary>Opens a pinned item's Handbook page from the snapshot code (the source document may be unloaded). Never touches completion.</summary>
    private void OnPinOpenLink(Guid taskId)
    {
        var pin = modSystem.MyPins.FirstOrDefault(p => p.TaskId == taskId);
        if (pin is null) return;
        string? code = pin.Kind switch
        {
            ScribeBlockKind.Tracker or ScribeBlockKind.Craft or ScribeBlockKind.QuestObjective => pin.TargetItemCode,
            ScribeBlockKind.Link => pin.LinkTarget,
            _ => null,
        };
        if (code is not null) ScribeItemRef.OpenHandbookPage(capi, code);
    }

    /// <summary>Syncs <see cref="pinFocusNodes"/> with the pin set (add/dispose nodes, prune stale edit buffers). Each node's listener tracks the focused row and commits the row left on a row-to-row move, like <see cref="OnRowFocusChanged"/>.</summary>
    private void SyncPinFocusNodes()
    {
        var live = modSystem.MyPins.Select(p => p.TaskId).ToHashSet();

        foreach (var taskId in pinFocusNodes.Keys.ToList())
        {
            if (!live.Contains(taskId))
            {
                pinFocusNodes[taskId].Dispose();
                pinFocusNodes.Remove(taskId);
            }
        }
        foreach (var taskId in live)
        {
            if (!pinFocusNodes.ContainsKey(taskId))
            {
                var node = new FocusNode();
                var id = taskId; // capture per-iteration
                node.AddListener(() => OnPinRowFocusChanged(id));
                pinFocusNodes[taskId] = node;
            }
        }

        foreach (var taskId in pinEditBuffer.Keys.ToList())
        {
            if (!live.Contains(taskId)) pinEditBuffer.Remove(taskId);
        }
    }

    private void OnPinRowFocusChanged(Guid taskId)
    {
        if (!pinFocusNodes.TryGetValue(taskId, out var node) || !node.HasFocus) return;
        // A different row just gained focus (click-to-edit another row): commit the row we left.
        if (focusedPinTaskId is { } prev && prev != taskId) CommitPinTextEdit(prev);
        focusedPinTaskId = taskId;
    }

    /// <summary>The pins in on-screen order, shared by <see cref="BuildPinnedContent"/> and <see cref="EditorFieldTraversalNodes"/> so Tab order can't drift. Sink ordering applies only when the policy sinks done pins; the HUD's BuildOrderedRows applies the identical rule.</summary>
    private IReadOnlyList<ScribePinnedRef> OrderedPinsForDisplay()
    {
        var policy = modSystem.MySettings.CompletionPolicy;
        bool sinkOrder = policy is ScribeCompletionPolicy.Sink or ScribeCompletionPolicy.UnpinSink;
        return sinkOrder
            ? ScribePinOrdering.ForDisplay(modSystem.MyPins)
            : modSystem.MyPins;
    }

    /// <summary>Buffers a live text change (write-through, so a resync rebuild re-seeds from it); the edit commits on blur/Enter via <see cref="CommitPinTextEdit"/>.</summary>
    private void OnPinTextChanged(Guid taskId, string text)
    {
        pinEditBuffer[taskId] = text;
    }

    /// <summary>Commits a row's buffered edit: sends it if non-blank and changed from the snapshot, then drops the buffer. Blank edits are dropped unsent (the server would reject them) and the field re-seeds on the next rebuild. Called on blur, Enter, and row-to-row focus move.</summary>
    private void CommitPinTextEdit(Guid taskId)
    {
        if (!pinEditBuffer.TryGetValue(taskId, out var text)) return;
        pinEditBuffer.Remove(taskId);

        var pin = modSystem.MyPins.FirstOrDefault(p => p.TaskId == taskId);
        if (pin is null) return; // pin left the set meanwhile

        string trimmed = text.TrimEnd(); // commit-time normalization, matching the editor (no leading trim)
        if (string.IsNullOrWhiteSpace(trimmed)) return; // reject blank — leave the task unchanged
        if (trimmed == pin.LastKnownText) return;        // no change

        SendEditPinnedTask(pin.OwnerDocId, taskId, trimmed);
    }

    /// <summary>Pin Tab checkbox: completes by identity immediately (no undo delay, unlike the HUD) under the current policy; the server re-pushes into <see cref="OnMyPinsChanged"/>.</summary>
    private void OnPinCompleteTask(Guid docId, Guid taskId)
    {
        var pin = modSystem.MyPins.FirstOrDefault(p => p.TaskId == taskId);
        if (pin?.Kind == ScribeBlockKind.Text)
        {
            OnPinUnpinTask(docId, taskId);
            return;
        }
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeCompleteTaskMessage
        {
            DocId = docId.ToByteArray(),
            TaskId = taskId.ToByteArray(),
            Policy = (byte)modSystem.MySettings.CompletionPolicy,
            SubtaskBehavior = (byte)modSystem.MySettings.SubtaskBehavior,
        });
    }

    /// <summary>Pin Tab delete: deletes the underlying task by identity and drops any in-flight edit buffer so a stale commit can't fire.</summary>
    private void OnPinDeleteTask(Guid docId, Guid taskId)
    {
        pinEditBuffer.Remove(taskId);
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeDeleteTaskMessage
        {
            DocId = docId.ToByteArray(),
            TaskId = taskId.ToByteArray(),
            SubtaskBehavior = (byte)modSystem.MySettings.SubtaskBehavior,
        });
    }

    /// <summary>Pin Tab unpin: removes only the pin (the task survives) and records an optimistic removal so the row vanishes everywhere without waiting for the server.</summary>
    private void OnPinUnpinTask(Guid docId, Guid taskId)
    {
        pinEditBuffer.Remove(taskId);
        modSystem.SetOptimisticPin(docId, taskId, snapshotIfPinned: null, source: null, insertEdge: default);
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeSetPinMessage
        {
            DocId = docId.ToByteArray(),
            TaskId = taskId.ToByteArray(),
            Pinned = false,
        });
    }

    /// <summary>Pin Tab drag-reorder: sends the whole new pin order. <paramref name="from"/>/<paramref name="to"/> index the ON-SCREEN order (done-sunk under a sink policy), not MyPins' raw storage order, so the working copy must start from the same list the rows were rendered from. <see cref="ScribePinOrdering.Reorder"/> moves a depth-0 pin with its pinned children and no-ops on cross-depth or same-index drops (no packet).</summary>
    private void OnPinReorder(int from, int to)
    {
        var pins = OrderedPinsForDisplay().ToList();
        if (!ScribePinOrdering.Reorder(pins, from, to)) return;

        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeReorderPinsMessage
        {
            DocIds = pins.Select(p => p.OwnerDocId.ToByteArray()).ToList(),
            TaskIds = pins.Select(p => p.TaskId.ToByteArray()).ToList(),
        });
    }

    /// <summary>Sends the identity-addressed pin edit (DocId + TaskId, no block position); the server writes through best-effort and re-pushes.</summary>
    private void SendEditPinnedTask(Guid docId, Guid taskId, string text)
    {
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeEditPinnedTaskMessage
        {
            DocId = docId.ToByteArray(),
            TaskId = taskId.ToByteArray(),
            Text = text,
        });
    }

    /// <summary>Disposes every Pin Tab focus node and clears the buffers (from <see cref="OnGuiClosed"/>).</summary>
    private void DisposePinState()
    {
        foreach (var node in pinFocusNodes.Values) node.Dispose();
        pinFocusNodes.Clear();
        pinEditBuffer.Clear();
    }
}
