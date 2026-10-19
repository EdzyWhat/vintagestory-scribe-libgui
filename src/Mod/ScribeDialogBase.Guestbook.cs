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
    /// <summary>Guestbook tab content: a three-column visitor table, read-only except the player's own note.
    /// Virtual so other blocks can override; reads from <see cref="IScribeDocumentHost.Guestbook"/>.</summary>
    protected virtual Widget BuildVisitorsContent()
    {
        var colors      = ResolveTheme(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        float bodySize  = ScribeRowConstants.BaseWindowFontSize
            * ScribePlayerSettings.ClampFontScale(modSystem.MySettings.WindowFontScale);
        float dateSize  = bodySize * 0.7f;
        float noteSize  = bodySize * 0.8f;
        string taskFont = modSystem.MySettings.TaskFontFamily;
        var headerStyle = new TextStyle { FontFamily = ScribeRowControlNudge.TitleFontFamily, Weight = FontWeight.Bold, FontSize = bodySize, Color = colors.OnSurface };
        // Visitor names inherit the tab's layout size; date/note are smaller and set their own.
        var bodyStyle   = new TextStyle { Color = colors.OnSurface };
        var dateStyle   = new TextStyle { FontSize = ScribeTaskFont.LayoutSize(taskFont, dateSize), Color = colors.OnSurface with { W = colors.OnSurface.W * 0.8f } };
        var myName = capi.World.Player.PlayerName;

        var entries = host.Guestbook.Entries;

        // One focus node per own entry so each note field is caret-isolated. Mirrors SyncPinFocusNodes.
        SyncGuestbookFocusNodes();

        // Newest-first display (entries are stored oldest-first in the store).
        var rows = new List<Widget>(entries.Count);
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            var entry = entries[i];
            Widget noteSlot;
            if (entry.PlayerName == myName)
            {
                string current = entry.Note;
                string lastSent = entry.Note;
                string entryDate = entry.InGameDate; // capture for the blur send
                noteSlot = new Expanded(
                    new ScribeMultilineField(
                        // Stable per-row identity so LibGUI routes focus/state to the right sibling field.
                        key: new ValueKey<string>(GuestbookNoteKey(entry)),
                        initialText: entry.Note,
                        focusNode: _guestbookNoteFocusNodes[GuestbookNoteKey(entry)],
                        fontSize: noteSize,
                        fontFamily: ScribeTaskFont.Resolve(modSystem.MySettings.TaskFontFamily),
                        padY: 6f,
                        // Same focus-border seam as the task rows.
                        focusBorderColor: InputFocusBorderColor(colors),
                        maxLength: GuestbookStore.MaxNoteLength,
                        onChanged: text => current = text,
                        onBlur: () =>
                        {
                            var trimmed = current.Trim();
                            if (trimmed == lastSent) return;
                            lastSent = trimmed;
                            capi.Network.GetChannel(ScribeModSystem.NetworkChannelName).SendPacket(
                                new ScribeEditGuestbookNoteMessage
                                {
                                    DocIdBytes = host.Document.DocId.ToByteArray(),
                                    InGameDate = entryDate,
                                    Note = trimmed,
                                });
                        }),
                    flex: 5);
            }
            else
            {
                var noteStyle = new TextStyle
                {
                    FontSize = ScribeTaskFont.LayoutSize(taskFont, noteSize),
                    Color = colors.OnSurface,
                };
                noteSlot = new Expanded(
                    ScribeTaskFont.OffsetWrap(taskFont, noteSize, new Text(entry.Note, noteStyle)),
                    flex: 5);
            }

            rows.Add(new Padding(
                EdgeInsets.Only(bottom: 3f),
                new Row(children: new Widget[]
                {
                    new Expanded(
                        new Padding(EdgeInsets.Only(left: 10f),
                            new Column(
                                spacing: 0,
                                mainAxisSize: MainAxisSize.Min,
                                crossAxisAlignment: CrossAxisAlignment.Stretch,
                                children: new Widget[]
                                {
                                    ScribeTaskFont.OffsetWrap(taskFont, bodySize, new Text(entry.PlayerName, bodyStyle)),
                                    ScribeTaskFont.OffsetWrap(taskFont, dateSize, new Text(GuestbookDisplayDate(entry), dateStyle)),
                                })),
                        flex: 3),
                    noteSlot,
                })));
        }

        // Top padding lives inside the scroll region so the divider above sits flush against the viewport.
        Widget body = entries.Count == 0
            ? new Center(child: ScribeTaskFont.OffsetWrap(taskFont, bodySize,
                new Text(Lang.Get("scribe:scribe-guestbook-empty"), bodyStyle)))
            : new Scrollbar(controller: sharedScrollController,
                child: new SingleChildScrollView(controller: sharedScrollController,
                    child: new Padding(EdgeInsets.Only(top: 8f), child: new Column(children: rows.ToArray(), mainAxisSize: MainAxisSize.Min))))
              { AutoHide = false };

        // Column headers; Guest Book alone has an extra leading divider above them.
        Widget columnHeaders = new Row(children: new Widget[]
        {
            new Expanded(new Padding(EdgeInsets.Only(left: 10f), new Text(Lang.Get("scribe:scribe-guestbook-col-visitor"), headerStyle)), flex: 3),
            new Expanded(new Text(Lang.Get("scribe:scribe-guestbook-col-note"),    headerStyle), flex: 5),
        });
        Widget header = ScribeTabHeader.Build(colors, RowStyle,
            "scribe:scribe-tab-guestbook", "scribe:scribe-tab-subtitle-guestbook",
            modSystem.VisualTuning.ShowSubtitleRow,
            columnHeaders, hasLeadingDivider: true);

        // Roots the tab in the Task Text Font; headerStyle keeps its explicit title family, and the own-note field
        // keeps its explicit task font (custom RenderBox that doesn't read DefaultTextStyle).
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
                })));
    }

    /// <summary>Stable identity for an own guestbook entry: <c>(PlayerName, InGameDate)</c> joined by a NUL, which
    /// can't appear in either. Keys the row's <see cref="ValueKey{T}"/> and its focus node.</summary>
    private static string GuestbookNoteKey(GuestbookEntry entry) => $"{entry.PlayerName}\0{entry.InGameDate}";

    /// <summary>Viewer-locale calendar date when the entry carries a real timestamp; otherwise the
    /// stored identity string (v1 blobs have no timestamp to rebuild from).</summary>
    private string GuestbookDisplayDate(GuestbookEntry entry)
        => entry.InGameTimestamp is { } ts
            ? NotebookHost.FormatDateFromTimestamp(capi.World, ts)
            : entry.InGameDate;

    /// <summary>Keeps <see cref="_guestbookNoteFocusNodes"/> in sync with the local player's own entries (add new,
    /// dispose gone, reuse the rest so a caret survives a rebuild). Mirrors <see cref="SyncPinFocusNodes"/>.</summary>
    private void SyncGuestbookFocusNodes()
    {
        var myName = capi.World.Player.PlayerName;
        var live = host.Guestbook.Entries
            .Where(e => e.PlayerName == myName)
            .Select(GuestbookNoteKey)
            .ToHashSet();

        foreach (var key in _guestbookNoteFocusNodes.Keys.ToList())
        {
            if (!live.Contains(key))
            {
                _guestbookNoteFocusNodes[key].Dispose();
                _guestbookNoteFocusNodes.Remove(key);
            }
        }
        foreach (var key in live)
        {
            if (!_guestbookNoteFocusNodes.ContainsKey(key))
                _guestbookNoteFocusNodes[key] = new FocusNode();
        }
    }
}
