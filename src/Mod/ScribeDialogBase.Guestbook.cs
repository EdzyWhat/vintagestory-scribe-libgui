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
    /// <summary>Guestbook tab content: a read-only (except own note) three-column visitor table.
    /// Virtual so the Desk or other future blocks can override if needed; the default reads from
    /// <see cref="IScribeDocumentHost.Guestbook"/> and needs no override for standard blocks.</summary>
    protected virtual Widget BuildVisitorsContent()
    {
        var colors      = ScribeTheme.For(modSystem.MySettings.PixelArtDisplay).ColorScheme;
        float bodySize  = ScribeRowConstants.BaseWindowFontSize
            * ScribePlayerSettings.ClampFontScale(modSystem.MySettings.WindowFontScale);
        float dateSize  = bodySize * 0.7f;
        float noteSize  = bodySize * 0.8f;
        var headerStyle = new TextStyle { FontFamily = ScribeRowControlNudge.TitleFontFamily, Weight = FontWeight.Bold, FontSize = bodySize, Color = colors.OnSurface };
        var bodyStyle   = new TextStyle { FontSize = bodySize, Color = colors.OnSurface };
        var dateStyle   = new TextStyle { FontSize = dateSize, Color = colors.OnSurface with { W = colors.OnSurface.W * 0.8f } };
        var myName = capi.World.Player.PlayerName;

        var entries = host.Guestbook.Entries;

        // Fresh focus node each rebuild so CaptureAllInputs can track it.
        _guestbookNoteFocusNode?.Dispose();
        _guestbookNoteFocusNode = new FocusNode();

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
                noteSlot = new Expanded(
                    new ScribeMultilineField(
                        initialText: entry.Note,
                        focusNode: _guestbookNoteFocusNode,
                        fontSize: noteSize,
                        fontFamily: ScribeTaskFont.Resolve(modSystem.MySettings.TaskFontFamily),
                        padY: 6f,
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
                                    Note = trimmed,
                                });
                        }),
                    flex: 5);
            }
            else
            {
                var noteStyle = new TextStyle { FontSize = noteSize, Color = colors.OnSurface };
                noteSlot = new Expanded(new Text(entry.Note, noteStyle), flex: 5);
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
                                    new Text(entry.PlayerName, bodyStyle),
                                    new Text(entry.InGameDate, dateStyle),
                                })),
                        flex: 3),
                    noteSlot,
                })));
        }

        Widget body = entries.Count == 0
            ? new Center(child: new Text(Lang.Get("scribe:scribe-guestbook-empty"), bodyStyle))
            : new Scrollbar(controller: sharedScrollController,
                child: new SingleChildScrollView(controller: sharedScrollController,
                    child: new Column(children: rows.ToArray(), mainAxisSize: MainAxisSize.Min)))
              { AutoHide = false };

        // Root the Guestbook tab subtree in the player's Task Text Font + window-scaled base size
        // (adopt-libgui-31-improvements). Visitor names/dates/other-players' notes inherit the family
        // here (approved change). headerStyle keeps its explicit Caudex (TitleFontFamily) — a non-default
        // family wins over the inherited one under Merge. The own-note ScribeMultilineField keeps its
        // explicit task font (custom RenderBox that doesn't read DefaultTextStyle).
        return ScribeTextDefaults.Wrap(modSystem.MySettings.TaskFontFamily, bodySize, new Padding(
            EdgeInsets.All(10),
            new Column(
                spacing: 8,
                crossAxisAlignment: CrossAxisAlignment.Stretch,
                mainAxisSize: MainAxisSize.Max,
                children: new Widget[]
                {
                    new Divider(),
                    new Row(children: new Widget[]
                    {
                        new Expanded(new Padding(EdgeInsets.Only(left: 10f), new Text(Lang.Get("scribe:scribe-guestbook-col-visitor"), headerStyle)), flex: 3),
                        new Expanded(new Text(Lang.Get("scribe:scribe-guestbook-col-note"),    headerStyle), flex: 5),
                    }),
                    new Divider(),
                    new Expanded(body),
                })));
    }
}
