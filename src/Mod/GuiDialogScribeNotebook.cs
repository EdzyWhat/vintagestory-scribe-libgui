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

    // ── Manual History entry state (add-custom-history-entries) ──────────────────────────────
    // One FocusNode per Manual entry the local player currently owns AND may edit — the pending
    // unsent draft (if any) plus every already-created Manual entry authored by this player.
    // Mirrors ScribeDialogBase's _guestbookNoteFocusNodes, keyed by EntryId instead of a natural
    // (PlayerName, InGameDate) key, since a player can own many Manual entries at once.
    private readonly Dictionary<Guid, FocusNode> _manualFocusNodes = new();
    // Live (not-yet-committed) text per owned Manual entry, keyed the same way — kept on the
    // dialog itself (not a per-build closure local) so OnGuiClosed can commit any in-flight edit
    // on close, the third of the design's three commit triggers (Enter / blur / dialog close).
    private readonly Dictionary<Guid, string> _manualLiveText = new();
    private readonly Dictionary<Guid, string> _manualLastSent = new();
    // The one pending "Add Entry" draft not yet confirmed by the server, if any. A second "Add
    // Entry" click while this is set just refocuses it rather than starting a second draft.
    private Guid? _manualDraftId;
    // True once the draft's first non-empty commit has been sent (Add), so later commits for the
    // same EntryId send SetText instead — set independently of the server's round-trip completing.
    private bool _manualDraftSent;
    // One-shot: the freshly-minted draft row autofocuses its field on the build right after
    // "Add Entry" is clicked, then never again (mirrors ScribeDialogBase.Layout.cs's
    // autoFocusRowOnRebuild idiom).
    private bool _manualDraftAutoFocus;

    public GuiDialogScribeNotebook(IScribeDocumentHost host, ICoreClientAPI capi)
        : base(new BlockPos(0), host, capi)
    {
        capi.Event.AfterActiveSlotChanged += OnActiveSlotChanged;
        _hotbar = capi.World.Player.InventoryManager.GetOwnInventory(GlobalConstants.hotBarInvClassName);
        if (_hotbar != null)
            _hotbar.SlotModified += OnHotbarSlotModified;
    }

    /// <summary>Disable the engine's frame-by-frame range check. Notebooks are not proximity-bound
    /// to a block position, so we never want the dialog to auto-close based on distance.</summary>
    protected override double InteractionRange => double.MaxValue;

    protected override string EmptyHintLangKey => "scribe:scribe-gui-edit-hint-notebook";

    /// <summary>The Notebook grants editor access immediately without a server round-trip — there is
    /// no lock to contend over when only one player can hold an item at a time. Seed the scratch from
    /// the host's current document so existing tasks and title are preserved when entering editor mode.</summary>
    protected override void RequestEditorAccess()
    {
        EnterEditorMode(ScribeDocumentCodec.Serialize(host.Document));
    }

    /// <summary>No editor lock on a Notebook — nothing to release.</summary>
    protected override void SendReleaseLockPacket() { }

    /// <summary>Extends the base capture set with Manual History entry fields (add-custom-history-
    /// entries): without this, typing into one leaks movement/hotbar keys to the character controller,
    /// because <see cref="_manualFocusNodes"/> lives on this subclass and the base
    /// <see cref="ScribeDialogBase.CaptureAllInputs"/> only knows about editor rows, Pin Tab rows, and
    /// Guestbook notes. Still gated on <see cref="Vintagestory.API.Client.GuiDialog.Focused"/> for the
    /// same reason the base override is (fix-settings-numeric-arrow-focus-leak).</summary>
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

    /// <summary>Builds the History tab content for the Notebook — a newest-first list of automatically
    /// recorded (read-only) and player-authored <c>Manual</c> (add-custom-history-entries) entries, plus
    /// a persistent "Add Entry" control.</summary>
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

        // The pending draft retires the moment its real entry syncs back from the server — from then
        // on the entries loop below renders it like any other Manual entry this player authored.
        if (_manualDraftId is { } pendingId
            && entries.Any(e => e.Kind == HistoryEventKind.Manual && e.EntryId == pendingId))
        {
            _manualDraftId = null;
            _manualDraftSent = false;
        }
        SyncManualFocusNodes(entries, myName);

        bool autoFocusDraft = _manualDraftAutoFocus;
        _manualDraftAutoFocus = false; // one-shot

        // Family inherited from the tab's DefaultTextStyle ancestor (below): bodyStyle drops its explicit
        // FontFamily; kind/date carried no family and now follow the task font too (approved change).
        string taskFont = modSystem.MySettings.TaskFontFamily;
        var bodyStyle = new TextStyle { FontSize = ScribeTaskFont.LayoutSize(taskFont, bodySize), Color = colors.OnSurface };
        var kindStyle = new TextStyle { FontSize = ScribeTaskFont.LayoutSize(taskFont, kindSize), Color = colors.OnSurface with { W = colors.OnSurface.W * 0.65f }, Weight = FontWeight.SemiBold };
        var dateStyle = new TextStyle { FontSize = ScribeTaskFont.LayoutSize(taskFont, dateSize), Color = colors.OnSurface with { W = colors.OnSurface.W * 0.55f } };

        // Bare row content, collected WITHOUT the bottom-padding/divider wrapper — deferred to a second
        // pass below so "is this the last row" can be answered once the full list is known (a faint
        // divider separates adjacent entries but never leads or trails the list; add-custom-history-
        // entries 7.5).
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
                    entryId: entryId, actorName: entry.ActorName, inGameDate: entry.InGameDate,
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
                        new Text(entry.InGameDate, dateStyle),
                    }),
                    new Padding(EdgeInsets.Only(left: 8f),
                        new Text(entry.ActorName.Length > 0
                            ? $"{entry.ActorName}{(entry.Detail.Length > 0 ? " — " + entry.Detail : "")}"
                            : entry.Detail,
                            bodyStyle)),
                }));
        }

        // Second pass: wrap every row but the last in a faint bottom border (20%-opacity "Ink" —
        // ScribeTheme's own name for OnSurface/OnBackground), then apply the bottom-padding gap to all.
        // Each non-last row gets its OWN 6px gap between its content and the divider line (so the text
        // never touches the line it's sitting above), on top of the pre-existing 6px gap after the line
        // (before the next row's subtitle) — 12px total between one row's text and the next row's
        // subtitle, 6px between one row's text and its own divider (2026-08-30 playtest feedback).
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

        // A pending draft counts as "not empty" so the compose row never flashes under the empty-state
        // prompt (add-custom-history-entries 4.7).
        Widget body = rows.Count == 0
            ? new Center(child: new Text(Lang.Get("scribe:scribe-gui-history-empty"), bodyStyle))
            : new Scrollbar(controller: sharedScrollController,
                child: new SingleChildScrollView(controller: sharedScrollController,
                    child: new Column(
                        children: rows.ToArray(),
                        mainAxisSize: MainAxisSize.Min,
                        crossAxisAlignment: CrossAxisAlignment.Stretch)))
              { AutoHide = false };

        // "Add Entry" button: same size/layout as the Read view's "Task Editor" footer button
        // (ScribeReadContent.BuildFooterButtons) — a bare Button under Padding(Symmetric(horizontal:
        // 0.04·W)), placed directly as a child of this tab's own CrossAxisAlignment.Stretch Column. The
        // Column's Stretch gives this non-flex child a TIGHT width constraint (tab width minus the 4%
        // insets on each side), and RenderBox.Constrain snaps the button's box to exactly that width —
        // the same mechanism that makes "Task Editor" span (almost) the full tab rather than shrink-wrap.
        // FontSize fixed at 14 (not window-scaled) to match ScribeReadContent.switchTextStyle, per the mod
        // author's explicit ask.
        Widget addEntryButton = new Button(
            child: new Text(Lang.Get("scribe:scribe-gui-history-add"),
                new TextStyle { FontSize = 14, FontFamily = ScribeTaskFont.ButtonFamily, Color = colors.OnPrimary }),
            onTap: _ => StartNewManualEntryDraft());
        Widget addEntryRow = new Padding(
            EdgeInsets.Symmetric(horizontal: 0.04f * host.GetLayout(modSystem.MySettings.PixelArtSize).W),
            child: addEntryButton);

        // Root the History tab subtree in the player's Task Text Font + window-scaled base size
        // (adopt-libgui-31-improvements). Body/kind/date Text widgets all inherit the family from here.
        return ScribeTextDefaults.Wrap(modSystem.MySettings.TaskFontFamily, bodySize, new Padding(
            EdgeInsets.All(10),
            new Column(
                spacing: 8,
                crossAxisAlignment: CrossAxisAlignment.Stretch,
                mainAxisSize: MainAxisSize.Max,
                children: new Widget[] { new Divider(), new Expanded(body), addEntryRow })));
    }

    /// <summary>Keeps <see cref="_manualFocusNodes"/> (and its paired live/last-sent text dictionaries)
    /// in sync with the currently-owned-and-editable Manual entry set: the pending draft (if any) plus
    /// every Manual entry authored by <paramref name="myName"/>. Mirrors
    /// <see cref="ScribeDialogBase.SyncGuestbookFocusNodes"/>, keyed by <see cref="HistoryEntry.EntryId"/>.</summary>
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

    /// <summary>"Add Entry": mints a new local-only draft and rebuilds so it renders autofocused, or —
    /// if a draft is already pending — just reclaims focus on it rather than ever starting a second one
    /// (add-custom-history-entries 4.2).</summary>
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

    /// <summary>Shared commit path for a Manual entry's field — reached from Enter, blur, AND
    /// <see cref="OnGuiClosed"/> (the design's three commit triggers). Sends
    /// <see cref="ScribeAddHistoryEntryMessage"/> the first time the pending draft commits non-empty
    /// text, else <see cref="ScribeSetHistoryEntryTextMessage"/> for every later edit to an
    /// already-created entry. A draft that never receives any text is simply never sent — nothing to
    /// discard server-side (add-custom-history-entries Decision 4).</summary>
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

    /// <summary>Delete button handler. An unsent draft (never given text, so the server has never heard
    /// of it) is discarded purely locally; an already-created entry sends
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
        // Close when the player switches the active hand AWAY from THIS notebook — keyed on the document's
        // stable DocId, not merely "still some Scribe item." Switching to a DIFFERENT Scribe item (a second
        // notebook, a tablet) must still close this dialog; only a hotbar reorder that keeps the same item
        // active leaves it open (its DocId is unchanged). See ActiveHandItemHostsThisDocument.
        if (!ActiveHandItemHostsThisDocument())
            TryClose();
    }

    private void OnHotbarSlotModified(int slotId)
    {
        // An in-place content re-sync of the STILL-HELD active slot (not a hand-switch) must use a
        // presence-only check, NOT the strict DocId identity guard OnActiveSlotChanged runs
        // (fix-item-dialog-first-open-flicker). A first open of a not-yet-crafted item makes the server
        // re-sync the stack WITHOUT the client-generated document, so its DocId no longer matches — the
        // strict guard closed the dialog one frame after opening, and only a second right-click stuck.
        // Since the physical item never left the hand, close only if it stopped being a Scribe item at all.
        // See ActiveHandHoldsAnyScribeDocumentItem for the full rationale.
        if (slotId == capi.World.Player.InventoryManager.ActiveHotbarSlotNumber
            && !ActiveHandHoldsAnyScribeDocumentItem())
            TryClose();
    }

    public override void OnGuiClosed()
    {
        capi.Event.AfterActiveSlotChanged -= OnActiveSlotChanged;
        if (_hotbar != null)
            _hotbar.SlotModified -= OnHotbarSlotModified;
        // Dialog closing is the third of the design's three commit triggers (Enter / blur / close) —
        // flush any in-flight, uncommitted Manual entry text before tearing the fields down. A draft
        // that never received any text is a no-op here (CommitManualEntry never sends for it).
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

    /// <summary>Notebook saves use <see cref="ScribeNotebookSaveMessage"/> so the server can write
    /// directly into the player's chosen ItemStack (addressed by slot identity, see
    /// <see cref="ScribeDialogBase.BuildItemSavePacket"/>) rather than routing through a block entity.</summary>
    protected override void SendFlushPacket(byte[] documentBytes)
    {
        capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(BuildItemSavePacket(documentBytes));
    }
}

/// <summary>One Manual History entry row (the pending draft, or an already-created entry): the
/// uneditable kind/date line every entry uses (its kind label merged with the author's name, "
/// <c>{ActorName}'s Note</c>"), then a second line holding either an editable
/// <see cref="ScribeMultilineField"/> (own entry) or plain read-only text (someone else's). A
/// <see cref="StatefulWidget"/> (not a plain method, unlike every other History row) purely so it can
/// own a hover bool via <see cref="State{T}.SetState"/> — its delete button floats over the text/input
/// line's right edge, shown ONLY while the pointer hovers that line specifically (not the kind/date
/// line above it), mirroring <see cref="ScribeEditorContent"/>'s row hover-reveal exactly (add-custom-
/// history-entries Decisions 7-8). Never a grip/drag handle or a pin control, draft or created.</summary>
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

    /// <summary>Internal vertical padding baked into the field, matching what's passed to
    /// <see cref="ScribeMultilineField.PadY"/> below — also feeds the button's vertical-centering math,
    /// which must agree with the field's own padding to land the button mid-line.</summary>
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

        // STRUCTURAL STABILITY (mirrors ScribeEditorContent's own row-hover note): index 0 of the Stack
        // is ALWAYS this Container wrapping the field/text, regardless of hover — only the trailing
        // Positioned button mounts/unmounts, so the field's live State (caret, in-progress text) never
        // gets torn down by a mouse move.
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

        // Hover region wraps ONLY the text/input line, not the kind/date line above it — a player asked
        // for the button to appear specifically when hovering the text, not "anywhere on the entry"
        // (2026-08-30 AskUserQuestion).
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
