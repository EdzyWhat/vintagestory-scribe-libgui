using System.Collections.Generic;
using System.Linq;
using Gui.Core.Layout;
using Gui.Rendering;
using Gui.Rendering.Text;
using Gui.Widgets.Basic;
using Gui.Widgets.Framework;
using Gui.Widgets.Input;
using Gui.Widgets.Layout;
using Gui.Widgets.Overlay;
using Gui.Widgets.Painting;
using Gui.Widgets.Scroll;
using OpenTK.Mathematics;
using Scribe.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Scribe;

/// <summary>
/// The Notebook item's dialog — a thin sealed subclass of <see cref="ScribeDialogBase"/>.
/// Differences from the Lectern:
/// <list type="bullet">
/// <item>No Guestbook tab (no Visitors nav button).</item>
/// <item>History tab — a chronicle of significant events recorded automatically and manually.</item>
/// <item>Editor access is always granted without a server round-trip (no lock contention on items).</item>
/// <item>Saves use <see cref="ScribeNotebookSaveMessage"/> instead of <see cref="ScribeEditDocumentMessage"/>.</item>
/// <item>Lock-release is a no-op (no editor lock on items).</item>
/// <item>Proximity auto-close disabled via <see cref="InteractionRange"/> override.</item>
/// </list>
/// </summary>
public class GuiDialogScribeNotebook : ScribeDialogBase
{
    private IInventory? _hotbar;

    // ── Manual History entry state ──────────────────────────────
    // One FocusNode per Manual entry the local player owns and may edit (pending draft + authored entries).
    private readonly Dictionary<Guid, FocusNode> _manualFocusNodes = new();
    // Uncommitted text per owned entry; held on the dialog so OnGuiClosed can commit in-flight edits.
    private readonly Dictionary<Guid, string> _manualLiveText = new();
    private readonly Dictionary<Guid, string> _manualLastSent = new();
    // The one pending "Add Entry" draft not yet confirmed by the server; a second click refocuses it.
    private Guid? _manualDraftId;
    // True once the draft's first non-empty commit is sent, so later commits send SetText instead.
    private bool _manualDraftSent;
    // One-shot: the new draft row autofocuses on the build right after "Add Entry".
    private bool _manualDraftAutoFocus;

    public GuiDialogScribeNotebook(IScribeDocumentHost host, ICoreClientAPI capi)
        : base(new BlockPos(0), host, capi)
    {
        capi.Event.AfterActiveSlotChanged += OnActiveSlotChanged;
        _hotbar = capi.World.Player.InventoryManager.GetOwnInventory(GlobalConstants.hotBarInvClassName);
        if (_hotbar != null)
            _hotbar.SlotModified += OnHotbarSlotModified;
    }

    /// <summary>Disables the engine's range check; notebooks are not bound to a block position.</summary>
    protected override double InteractionRange => double.MaxValue;

    /// <summary>Opts out of LibGUI's "floaty" world-anchored positioning: the dummy <c>BlockPos(0)</c>
    /// would pin the window to the world origin and drift with the camera. Null = screen-space placement.
    /// Inherited by the Clockmaker Notebook.</summary>
    protected override WorldAnchor? Anchor => null;

    protected override string EmptyHintLangKey => "scribe:scribe-gui-edit-hint-notebook";

    /// <summary>Grants editor access immediately (an item has no lock contention), seeding the scratch
    /// from the host's current document.</summary>
    protected override void RequestEditorAccess()
    {
        EnterEditorMode(ScribeDocumentCodec.Serialize(host.Document));
    }

    /// <summary>No editor lock on a Notebook — nothing to release.</summary>
    protected override void SendReleaseLockPacket() { }

    /// <summary>Adds Manual History entry fields to the base capture set; without this, typing in one
    /// leaks movement/hotbar keys to the character controller. Gated on
    /// <see cref="Vintagestory.API.Client.GuiDialog.Focused"/> like the base override.</summary>
    public override bool CaptureAllInputs()
        => base.CaptureAllInputs()
        || (Focused && _manualFocusNodes.Values.Any(n => n.HasFocus));

    /// <summary>Adds the History nav button between Pinned and Settings.</summary>
    protected override IEnumerable<Widget> GetExtraNavButtons()
    {
        var colors = ScribeTheme.For(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        yield return TitleButton(
            "scribehistory",
            "scribe-gui-nav-history",
            colors.OnSurfaceVariant,
            NavButtonSize,
            OnClickSwitchToHistory,
            boxShadows: NavButtonShadow,
            activeColor: IsHistoryView ? ScribeRowConstants.NavActiveHistory : null);
    }

    /// <summary>Builds the History tab: a newest-first list of automatic (read-only) and player-authored
    /// <c>Manual</c> entries, plus an "Add Entry" control.</summary>
    protected override Widget BuildHistoryContent()
    {
        var nbHost  = host as NotebookHost;
        var entries = nbHost?.History.Entries ?? Array.Empty<HistoryEntry>();
        var colors  = ScribeTheme.For(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        float scale = ScribePlayerSettings.ClampFontScale(modSystem.MySettings.WindowFontScale);
        float bodySize = ScribeRowConstants.BaseWindowFontSize * scale;
        float kindSize = bodySize * 0.72f;
        float dateSize = bodySize * 0.72f;
        float controlSize = ScribeRowConstants.RowCheckboxSize * scale;

        string myName = capi.World.Player.PlayerName;

        // The pending draft retires once its real entry syncs back from the server.
        if (_manualDraftId is { } pendingId
            && entries.Any(e => e.Kind == HistoryEventKind.Manual && e.EntryId == pendingId))
        {
            _manualDraftId = null;
            _manualDraftSent = false;
        }
        SyncManualFocusNodes(entries, myName);

        bool autoFocusDraft = _manualDraftAutoFocus;
        _manualDraftAutoFocus = false; // one-shot

        // Font family is inherited from the tab's DefaultTextStyle ancestor (below).
        string taskFont = modSystem.MySettings.TaskFontFamily;
        var bodyStyle = new TextStyle { FontSize = ScribeTaskFont.LayoutSize(taskFont, bodySize), Color = colors.OnSurface };
        var kindStyle = new TextStyle { FontSize = ScribeTaskFont.LayoutSize(taskFont, kindSize), Color = colors.OnSurface with { W = colors.OnSurface.W * 0.65f }, Weight = FontWeight.SemiBold };
        var dateStyle = new TextStyle { FontSize = ScribeTaskFont.LayoutSize(taskFont, dateSize), Color = colors.OnSurface with { W = colors.OnSurface.W * 0.55f } };

        // Bare row content; dividers are added in a second pass once the last row is known.
        var rowContents = new List<Widget>(entries.Count + 1);

        if (_manualDraftId is { } draftId)
        {
            rowContents.Add(new ScribeManualHistoryRow(
                entryId: draftId, actorName: myName, inGameDate: NotebookHost.FormatDate(capi.World),
                detailText: _manualLiveText.GetValueOrDefault(draftId, ""), isAuthor: true, autoFocus: autoFocusDraft,
                focusNode: _manualFocusNodes[draftId], focusBorderColor: InputFocusBorderColor(colors),
                kindStyle: kindStyle, dateStyle: dateStyle, bodyStyle: bodyStyle, colors: colors,
                bodySize: bodySize, taskFont: taskFont, controlSize: controlSize,
                onChanged: text => _manualLiveText[draftId] = text,
                onCommit: () => CommitManualEntry(draftId),
                onDelete: () => DeleteManualEntry(draftId)));
        }

        for (int i = entries.Count - 1; i >= 0; i--)
        {
            var entry = entries[i];
            if (entry.Kind == HistoryEventKind.Manual)
            {
                bool isAuthor = entry.ActorName == myName;
                Guid entryId = entry.EntryId;
                rowContents.Add(new ScribeManualHistoryRow(
                    entryId: entryId, actorName: entry.ActorName, inGameDate: HistoryDisplay.Date(entry, capi.World),
                    detailText: isAuthor ? _manualLiveText.GetValueOrDefault(entryId, entry.Detail) : entry.Detail,
                    isAuthor: isAuthor, autoFocus: false,
                    focusNode: isAuthor ? _manualFocusNodes[entryId] : null, focusBorderColor: InputFocusBorderColor(colors),
                    kindStyle: kindStyle, dateStyle: dateStyle, bodyStyle: bodyStyle, colors: colors,
                    bodySize: bodySize, taskFont: taskFont, controlSize: controlSize,
                    onChanged: text => _manualLiveText[entryId] = text,
                    onCommit: () => CommitManualEntry(entryId),
                    onDelete: () => DeleteManualEntry(entryId)));
                continue;
            }

            rowContents.Add(new Column(
                spacing: 2,
                crossAxisAlignment: CrossAxisAlignment.Stretch,
                mainAxisSize: MainAxisSize.Min,
                children: new Widget[]
                {
                    new Row(children: new Widget[]
                    {
                        new Expanded(
                            new Padding(EdgeInsets.Only(left: 6f), new Text(KindLabel(entry), kindStyle)),
                            flex: 1),
                        new Text(HistoryDisplay.Date(entry, capi.World), dateStyle),
                    }),
                    new Padding(EdgeInsets.Only(left: 8f),
                        new Text(HistoryDisplay.Body(entry), bodyStyle)),
                }));
        }

        // Second pass: every row but the last gets a faint bottom divider with a 6px gap above it;
        // every row gets a 6px gap below.
        Vector4 dividerColor = colors.OnSurface with { W = 0.15f };
        var rows = new List<Widget>(rowContents.Count);
        for (int idx = 0; idx < rowContents.Count; idx++)
        {
            Widget content = rowContents[idx];
            if (idx < rowContents.Count - 1)
                content = new Container(
                    style: new BoxStyle { Border = Border.Only(bottom: new BorderSide(1f, dividerColor)) },
                    child: new Padding(EdgeInsets.Only(bottom: 6f), content));
            rows.Add(new Padding(EdgeInsets.Only(bottom: 6f), content));
        }

        // The top padding is inside the scroll region so it scrolls away with the content.
        Widget body = rows.Count == 0
            ? new Center(child: new Text(Lang.Get("scribe:scribe-gui-history-empty"), bodyStyle))
            : new Scrollbar(controller: sharedScrollController,
                child: new SingleChildScrollView(controller: sharedScrollController,
                    child: new Padding(EdgeInsets.Only(top: 8f), child: new Column(
                        children: rows.ToArray(),
                        mainAxisSize: MainAxisSize.Min,
                        crossAxisAlignment: CrossAxisAlignment.Stretch))))
              { AutoHide = false };

        // Matches the Read view's "Task Editor" footer button: the Stretch Column gives this bare Button a
        // tight width (tab width minus 4% insets) so it spans the tab. FontSize is fixed at 14 (not
        // window-scaled) to match ScribeReadContent.switchTextStyle.
        Widget addEntryButton = new Button(
            child: new Text(Lang.Get("scribe:scribe-gui-history-add"),
                new TextStyle { FontSize = 14, FontFamily = ScribeTaskFont.ButtonFamily, Color = colors.OnPrimary }),
            onTap: _ => StartNewManualEntryDraft());
        Widget addEntryRow = new Padding(
            EdgeInsets.Only(top: 8f,
                left: 0.04f * host.GetLayout(modSystem.MySettings.PixelArtSize).W,
                right: 0.04f * host.GetLayout(modSystem.MySettings.PixelArtSize).W),
            child: addEntryButton);

        // No Row 3 controls here, so the divider follows directly after the subtitle.
        Widget header = ScribeTabHeader.Build(colors, RowStyle,
            "scribe:scribe-gui-nav-history", "scribe:scribe-gui-subtitle-history",
            modSystem.VisualTuning.ShowSubtitleRow);

        // Roots the subtree in the player's Task Text Font + window-scaled size; Text widgets inherit it.
        // Insets match the other tabs except top (4).
        return ScribeTextDefaults.Wrap(modSystem.MySettings.TaskFontFamily, bodySize, new Padding(
            EdgeInsets.Ltrb(10, 4, 10, 10),
            new Column(
                spacing: 0,
                crossAxisAlignment: CrossAxisAlignment.Stretch,
                mainAxisSize: MainAxisSize.Max,
                children: new Widget[]
                {
                    header,
                    new Expanded(child: body),
                    addEntryRow,
                })));
    }

    /// <summary>Keeps <see cref="_manualFocusNodes"/> and the live/last-sent text dictionaries in sync
    /// with the pending draft plus every Manual entry authored by <paramref name="myName"/>.</summary>
    private void SyncManualFocusNodes(IReadOnlyList<HistoryEntry> entries, string myName)
    {
        var live = new HashSet<Guid>(entries.Where(e => e.Kind == HistoryEventKind.Manual && e.ActorName == myName).Select(e => e.EntryId));
        if (_manualDraftId is { } draftId) live.Add(draftId);
        foreach (var key in _manualFocusNodes.Keys.ToList())
        {
            if (live.Contains(key)) continue;
            _manualFocusNodes[key].Dispose();
            _manualFocusNodes.Remove(key);
            _manualLiveText.Remove(key);
            _manualLastSent.Remove(key);
        }
        foreach (var key in live)
            if (!_manualFocusNodes.ContainsKey(key)) _manualFocusNodes[key] = new FocusNode();
    }

    /// <summary>"Add Entry": mints a local-only autofocused draft, or refocuses the pending one.</summary>
    private void StartNewManualEntryDraft()
    {
        if (_manualDraftId is { } existingId)
        {
            if (_manualFocusNodes.TryGetValue(existingId, out var existingNode)) existingNode.RequestFocus();
            return;
        }
        _manualDraftId = Guid.NewGuid();
        _manualDraftSent = false;
        _manualDraftAutoFocus = true;
        ForceRebuild();
    }

    /// <summary>Commit path for a Manual entry's field (Enter, blur, and <see cref="OnGuiClosed"/>).
    /// Sends <see cref="ScribeAddHistoryEntryMessage"/> on the draft's first non-empty commit, else
    /// <see cref="ScribeSetHistoryEntryTextMessage"/>. A draft that never gets text is never sent.</summary>
    private void CommitManualEntry(Guid entryId)
    {
        if (!_manualLiveText.TryGetValue(entryId, out var live)) return;
        var trimmed = live.Trim();
        var lastSent = _manualLastSent.GetValueOrDefault(entryId, "");
        if (trimmed == lastSent) return;

        bool needsCreate = entryId == _manualDraftId && !_manualDraftSent;
        if (needsCreate && trimmed.Length == 0) return; // never had text yet — nothing to send

        _manualLastSent[entryId] = trimmed;
        var nb = host as NotebookHost;
        string? targetInvId = nb?.SlotInventoryId is { } invId && nb.SlotId >= 0 ? invId : null;
        int targetSlotId = targetInvId is not null ? nb!.SlotId : 0;
        var channel = capi.Network.GetChannel(ScribeModSystem.NetworkChannelName);

        if (needsCreate)
        {
            _manualDraftSent = true;
            channel.SendPacket(new ScribeAddHistoryEntryMessage
            {
                DocIdBytes = host.Document.DocId.ToByteArray(),
                EntryId = entryId.ToByteArray(),
                Text = trimmed,
                TargetInventoryId = targetInvId,
                TargetSlotId = targetSlotId,
            });
        }
        else
        {
            channel.SendPacket(new ScribeSetHistoryEntryTextMessage
            {
                DocIdBytes = host.Document.DocId.ToByteArray(),
                EntryId = entryId.ToByteArray(),
                Text = trimmed,
                TargetInventoryId = targetInvId,
                TargetSlotId = targetSlotId,
            });
        }
    }

    /// <summary>An unsent draft is discarded locally; a created entry sends
    /// <see cref="ScribeDeleteHistoryEntryMessage"/> and waits for the server sync to drop its row.</summary>
    private void DeleteManualEntry(Guid entryId)
    {
        if (entryId == _manualDraftId && !_manualDraftSent)
        {
            _manualDraftId = null;
            _manualDraftSent = false;
            if (_manualFocusNodes.Remove(entryId, out var node)) node.Dispose();
            _manualLiveText.Remove(entryId);
            _manualLastSent.Remove(entryId);
            ForceRebuild();
            return;
        }

        var nb = host as NotebookHost;
        var msg = new ScribeDeleteHistoryEntryMessage
        {
            DocIdBytes = host.Document.DocId.ToByteArray(),
            EntryId = entryId.ToByteArray(),
        };
        if (nb?.SlotInventoryId is { } invId && nb.SlotId >= 0)
        {
            msg.TargetInventoryId = invId;
            msg.TargetSlotId = nb.SlotId;
        }
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(msg);
    }

    private static string KindLabel(HistoryEntry entry) => entry.Kind switch
    {
        HistoryEventKind.Crafted       => Lang.Get("scribe:scribe-gui-history-kind-crafted"),
        HistoryEventKind.PickedUp      => Lang.Get("scribe:scribe-gui-history-kind-pickedup"),
        HistoryEventKind.Death         => Lang.Get("scribe:scribe-gui-history-kind-death"),
        HistoryEventKind.PvpKill       => Lang.Get("scribe:scribe-gui-history-kind-pvpkill"),
        HistoryEventKind.BossKill      => Lang.Get("scribe:scribe-gui-history-kind-bosskill"),
        HistoryEventKind.TemporalStorm => Lang.Get("scribe:scribe-gui-history-kind-temporalstorm"),
        HistoryEventKind.Manual        => Lang.Get("scribe:scribe-gui-history-kind-manual", entry.ActorName),
        _                              => entry.Kind.ToString(),
    };

    private void OnActiveSlotChanged(ActiveSlotChangeEventArgs _)
    {
        // Keyed on the document's DocId: switching to a different Scribe item must close this dialog.
        if (!ActiveHandItemHostsThisDocument())
            TryClose();
    }

    private void OnHotbarSlotModified(int slotId)
    {
        // A re-sync of the still-held slot needs a presence-only check, not the DocId guard: a first
        // open of a not-yet-crafted item re-syncs the stack without the client-generated document, so
        // the DocId would mismatch and close the dialog a frame after opening.
        if (slotId == capi.World.Player.InventoryManager.ActiveHotbarSlotNumber
            && !ActiveHandHoldsAnyScribeDocumentItem())
            TryClose();
    }

    public override void OnGuiClosed()
    {
        capi.Event.AfterActiveSlotChanged -= OnActiveSlotChanged;
        if (_hotbar != null)
            _hotbar.SlotModified -= OnHotbarSlotModified;
        // Flush in-flight Manual entry text before tearing the fields down.
        foreach (var entryId in _manualLiveText.Keys.ToList())
            CommitManualEntry(entryId);
        foreach (var node in _manualFocusNodes.Values)
            node.Dispose();
        _manualFocusNodes.Clear();
        _manualLiveText.Clear();
        _manualLastSent.Clear();
        _manualDraftId = null;
        _manualDraftSent = false;
        base.OnGuiClosed();
    }

    /// <summary>Saves via <see cref="ScribeNotebookSaveMessage"/>, addressing the ItemStack by slot identity
    /// (see <see cref="ScribeDialogBase.BuildItemSavePacket"/>).</summary>
    protected override void SendFlushPacket(byte[] documentBytes)
    {
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(BuildItemSavePacket(documentBytes));
    }
}

/// <summary>One Manual History entry row (pending draft or created): a read-only kind/date line, then
/// an editable <see cref="ScribeMultilineField"/> (own entry) or read-only text. A
/// <see cref="StatefulWidget"/> only to own a hover bool: the delete button floats over the text line's
/// right edge while the pointer hovers that line (not the kind/date line). No grip or pin control.</summary>
internal sealed class ScribeManualHistoryRow : StatefulWidget
{
    public ScribeManualHistoryRow(
        Guid entryId, string actorName, string inGameDate, string detailText, bool isAuthor, bool autoFocus,
        FocusNode? focusNode, Vector4? focusBorderColor, TextStyle kindStyle, TextStyle dateStyle, TextStyle bodyStyle,
        ColorScheme colors, float bodySize, string taskFont, float controlSize,
        Action<string> onChanged, Action onCommit, Action onDelete, Gui.Widgets.Framework.Key? key = null)
        : base(key)
    {
        EntryId = entryId;
        ActorName = actorName;
        InGameDate = inGameDate;
        DetailText = detailText;
        IsAuthor = isAuthor;
        AutoFocus = autoFocus;
        FocusNode = focusNode;
        FocusBorderColor = focusBorderColor;
        KindStyle = kindStyle;
        DateStyle = dateStyle;
        BodyStyle = bodyStyle;
        Colors = colors;
        BodySize = bodySize;
        TaskFont = taskFont;
        ControlSize = controlSize;
        OnChanged = onChanged;
        OnCommit = onCommit;
        OnDelete = onDelete;
    }

    public Guid EntryId { get; }
    public string ActorName { get; }
    public string InGameDate { get; }
    public string DetailText { get; }
    public bool IsAuthor { get; }
    public bool AutoFocus { get; }
    public FocusNode? FocusNode { get; }
    public Vector4? FocusBorderColor { get; }
    public TextStyle KindStyle { get; }
    public TextStyle DateStyle { get; }
    public TextStyle BodyStyle { get; }
    public ColorScheme Colors { get; }
    public float BodySize { get; }
    public string TaskFont { get; }
    public float ControlSize { get; }
    public Action<string> OnChanged { get; }
    public Action OnCommit { get; }
    public Action OnDelete { get; }

    public override State CreateState() => new ScribeManualHistoryRowState();
}

internal sealed class ScribeManualHistoryRowState : State<ScribeManualHistoryRow>
{
    private bool hovered;

    /// <summary>Vertical padding passed to <see cref="ScribeMultilineField.PadY"/>; the button's
    /// vertical-centering math must agree with it.</summary>
    private const float FieldPadY = 4f;

    public override Widget Build(BuildContext context)
    {
        var w = Widget;
        Widget detailWidget = w.IsAuthor
            ? new ScribeMultilineField(
                key: new ValueKey<Guid>(w.EntryId),
                initialText: w.DetailText,
                focusNode: w.FocusNode,
                fontSize: w.BodySize,
                fontFamily: ScribeTaskFont.Resolve(w.TaskFont),
                padY: FieldPadY,
                focusBorderColor: w.FocusBorderColor,
                autoFocus: w.AutoFocus,
                maxLength: ScribeDocumentCodec.MaxTaskTextLength,
                onChanged: w.OnChanged,
                onBlur: w.OnCommit,
                onCommitAndAdvance: w.OnCommit)
            : new Text(w.DetailText, w.BodyStyle);

        // Index 0 of the Stack is always this Container; only the trailing button mounts/unmounts, so the
        // field's State (caret, text) survives hover changes.
        Widget textLine = new Container(style: new BoxStyle(), child: new Row(children: new Widget[] { new Expanded(detailWidget) }));

        var stackChildren = new List<Widget> { textLine };
        if (w.IsAuthor && hovered)
        {
            float boxHeight = w.ControlSize - ScribeRowButton.BoxShrink;
            float singleLineInputHeight = ScribeRowControlNudge.TextLineHeight(w.BodySize) + FieldPadY * 2f;
            float btnTop = MathF.Max(0f, singleLineInputHeight / 2f - boxHeight / 2f);
            stackChildren.Add(new Positioned(
                right: 5f, top: btnTop,
                child: new ScribeRowButton(
                    iconName: "scribeclose",
                    iconColor: w.Colors.Error,
                    size: w.ControlSize,
                    onTap: w.OnDelete)));
        }

        // Hover region wraps only the text/input line, not the kind/date line.
        Widget textLineRegion = w.IsAuthor
            ? new MouseRegion(
                onEnter: _ => { if (!hovered) SetState(() => hovered = true); },
                onExit: _ => { if (hovered) SetState(() => hovered = false); },
                child: new Stack(stackChildren))
            : new Stack(stackChildren);

        return new Column(
            spacing: 2,
            crossAxisAlignment: CrossAxisAlignment.Stretch,
            mainAxisSize: MainAxisSize.Min,
            children: new Widget[]
            {
                new Row(children: new Widget[]
                {
                    new Expanded(
                        new Padding(EdgeInsets.Only(left: 6f), new Text(Lang.Get("scribe:scribe-gui-history-kind-manual", w.ActorName), w.KindStyle)),
                        flex: 1),
                    new Text(w.InGameDate, w.DateStyle),
                }),
                new Padding(EdgeInsets.Only(left: 8f), textLineRegion),
            });
    }
}
