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
using Vintagestory.API.Config;   // Lang, GlobalConstants
using Vintagestory.API.MathTools;  // BlockPos

namespace Scribe;

public abstract partial class ScribeDialogBase
{
    // ---------------- Pin Tab (scribe-pin-editor) ----------------

    /// <summary>The Pin Tab body: the player's pins across every document (in pin-list order, no row cap),
    /// each row editable by default reusing the editor's <see cref="ScribeEditRow"/> rendering but sourced
    /// from <see cref="ScribeModSystem.MyPins"/>, plus the completion-policy picker. Focus is coordinated
    /// through the dialog-owned <see cref="pinFocusNodes"/> (keyed by TaskId) the same way the editor
    /// coordinates its index-keyed nodes.</summary>
    private Widget BuildPinnedContent()
    {
        SyncPinFocusNodes();

        // Same driving width the rest of the dialog uses, so the policy picker's inset tracks the layout.
        float layoutW = host.GetLayout(modSystem.MySettings.PixelArtSize).W;

        Guid? autoFocus = autoFocusPinTaskId;
        autoFocusPinTaskId = null; // one-shot

        // Apply sink ordering only when the policy actually sinks done pins. Under Keep, done pins
        // hold their position (same behaviour as the HUD's SunkForOrder). Unpin/Delete remove the
        // pin entirely, so ordering is moot for them and raw order is fine.
        var policy = modSystem.MySettings.CompletionPolicy;
        bool sinkOrder = policy is ScribeCompletionPolicy.Sink or ScribeCompletionPolicy.UnpinSink;
        var orderedPins = sinkOrder
            ? ScribePinOrdering.ForDisplay(modSystem.MyPins)
            : (IReadOnlyList<ScribePinnedRef>)modSystem.MyPins;

        // Each row's text seeds from its live edit buffer if one is in flight (a keystroke mid-resync),
        // else the authoritative server snapshot — the Pin Tab's equivalent of the editor re-seeding from
        // its scratch doc across a ForceRebuild (which fully unmounts + remounts the field).
        var rows = orderedPins
            .Select(p => new ScribePinRowData(
                p.OwnerDocId, p.TaskId, p.LastKnownDone,
                pinEditBuffer.TryGetValue(p.TaskId, out var buffered) ? buffered : p.LastKnownText))
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
            onReorder: OnPinReorder,
            completionPolicy: modSystem.MySettings.CompletionPolicy,
            onCompletionPolicyChanged: p => modSystem.UpdateMySettings(s => s.CompletionPolicy = p),
            // Match the title row's horizontal inset (left: 10 + 0.04·W, right: 0.04·W) so the picker lines
            // up with the title band; W comes from the same ScribeLayout the rest of the dialog uses.
            policyPickerPadding: EdgeInsets.Only(left: 10 + 0.04f * layoutW, right: 0.04f * layoutW),
            style: RowStyle,
            scrollController: sharedScrollController);
    }

    /// <summary>Keep <see cref="pinFocusNodes"/> in sync with the current pin set: add a node for each new
    /// pin, dispose+drop nodes for pins that left, and prune stale edit buffers. Each node carries a
    /// listener that tracks the focused row and commits the row being left on a row→row focus move (the
    /// editor's <see cref="OnRowFocusChanged"/> pattern, keyed by TaskId).</summary>
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

        // Drop edit buffers for pins no longer present so the dictionary can't grow unbounded.
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

    /// <summary>Live text-change from a focused Pin Tab field: buffer it (write-through, so a resync
    /// rebuild re-seeds from the buffer, not the stale snapshot). No network send yet — the edit commits on
    /// blur/Enter (<see cref="CommitPinTextEdit"/>).</summary>
    private void OnPinTextChanged(Guid taskId, string text)
    {
        pinEditBuffer[taskId] = text;
    }

    /// <summary>Commit a Pin Tab row's buffered text edit: send the identity-addressed edit if the text
    /// changed from the server snapshot and is non-blank, then drop the buffer. A blank/whitespace-only
    /// edit is dropped WITHOUT sending (the server would reject it anyway — spec "blank edit is rejected");
    /// the field re-seeds from the unchanged snapshot on the next rebuild. Called on blur, Enter, and a
    /// row→row focus move.</summary>
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

    /// <summary>Pin Tab checkbox: complete the task by identity with NO undo delay (unlike the HUD) — send
    /// the completion immediately under the player's current policy. Reuses the existing
    /// <see cref="ScribeCompleteTaskMessage"/> (the server toggles store-first, applies the policy, and
    /// re-pushes, which lands in <see cref="OnMyPinsChanged"/>).</summary>
    private void OnPinCompleteTask(Guid docId, Guid taskId)
    {
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeCompleteTaskMessage
        {
            DocId = docId.ToByteArray(),
            TaskId = taskId.ToByteArray(),
            Policy = (byte)modSystem.MySettings.CompletionPolicy,
        });
    }

    /// <summary>Pin Tab delete control: delete the underlying task by identity (a standalone action, not a
    /// completion side effect) via <see cref="ScribeDeleteTaskMessage"/>. Drop any in-flight edit buffer so
    /// a stale commit can't fire after the row is gone.</summary>
    private void OnPinDeleteTask(Guid docId, Guid taskId)
    {
        pinEditBuffer.Remove(taskId);
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeDeleteTaskMessage
        {
            DocId = docId.ToByteArray(),
            TaskId = taskId.ToByteArray(),
        });
    }

    /// <summary>Pin Tab unpin control: remove only the pin (the task survives), via the existing
    /// <see cref="ScribeSetPinMessage"/> with <c>Pinned = false</c> — no block resolution needed.</summary>
    private void OnPinUnpinTask(Guid docId, Guid taskId)
    {
        pinEditBuffer.Remove(taskId);
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeSetPinMessage
        {
            DocId = docId.ToByteArray(),
            TaskId = taskId.ToByteArray(),
            Pinned = false,
        });
    }

    /// <summary>Pin Tab drag-reorder drop: send the whole new pin order (permuting the current list so the
    /// pin at <paramref name="from"/> lands at <paramref name="to"/>) via <see cref="ScribeReorderPinsMessage"/>.
    /// The server permutes only this player's list and re-pushes. A move-to-same index is a no-op.</summary>
    private void OnPinReorder(int from, int to)
    {
        var pins = modSystem.MyPins;
        if (from == to || from < 0 || to < 0 || from >= pins.Count || to >= pins.Count) return;

        var order = pins.Select(p => (p.OwnerDocId, p.TaskId)).ToList();
        var moved = order[from];
        order.RemoveAt(from);
        order.Insert(to, moved);

        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeReorderPinsMessage
        {
            DocIds = order.Select(o => o.Item1.ToByteArray()).ToList(),
            TaskIds = order.Select(o => o.Item2.ToByteArray()).ToList(),
        });
    }

    /// <summary>Fire the identity-addressed pin-edit message. The document's DocId + the task's TaskId fully
    /// address the edit; no block position is sent. The server writes through (best-effort) and re-pushes.</summary>
    private void SendEditPinnedTask(Guid docId, Guid taskId, string text)
    {
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(new ScribeEditPinnedTaskMessage
        {
            DocId = docId.ToByteArray(),
            TaskId = taskId.ToByteArray(),
            Text = text,
        });
    }

    /// <summary>Dispose every Pin Tab focus node and clear the buffers (called from
    /// <see cref="OnGuiClosed"/>).</summary>
    private void DisposePinState()
    {
        foreach (var node in pinFocusNodes.Values) node.Dispose();
        pinFocusNodes.Clear();
        pinEditBuffer.Clear();
    }
}
