using System;
using System.Collections.Generic;
using Scribe.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace Scribe;

public sealed partial class ScribeModSystem
{
    // ── Demo-content seeding (dev/creative tool) ────────────────────────────────────────────────────

    /// <summary>Fictional visitor names for seeded Lectern guestbooks (≤16 chars, like real player names).</summary>
    private static readonly string[] SeedVisitorNames =
    {
        "Alrik", "Brenna", "Cael", "Dagny", "Emeric", "Fenna", "Gorm", "Hilde",
    };

    /// <summary>Sample tasks for a seeded document (mixed done/undone).</summary>
    private static readonly (string Text, bool Done)[] SeedTasks =
    {
        ("Smelt copper for the new forge",        true),
        ("Trade furs at the trader up the coast",  true),
        ("Repair the north wall breach",           true),
        ("Plant flax in the east field",           false),
        ("Brew a batch of cheese",                 false),
        ("Find a temporal gear for the mechanism", false),
        ("Stock up on arrows before the storm",    false),
        ("Map the cave system below the cellar",   false),
        ("Tame a pair of hens",                    true),
        ("Cook a meal for the feast",              false),
        ("Reinforce the cellar door",              false),
        ("Chart the road to the ruins",            false),
    };

    /// <summary>Sample note sections for a seeded document.</summary>
    private static readonly string[] SeedNotes =
    {
        "The trader north of here pays best for hides — bring at least a dozen.",
        "Storm season is close. Keep temporal gears and cured meat stocked.",
    };

    /// <summary>Registers the server-side dev commands under <c>/scribe</c> (seed, tablet, external). Server-side because the
    /// stores are server-authoritative; double-gated by the <c>controlserver</c> privilege plus a creative-mode check.
    /// <c>seed</c> applies History only to Notebooks and Guestbook only to Lecterns; inapplicable combinations are reported, not errored.</summary>
    private void RegisterSeedCommand(ICoreServerAPI api)
    {
        var parsers = api.ChatCommands.Parsers;
        api.ChatCommands.Create("scribe")
            .WithDescription("[scribe dev] Scribe developer commands.")
            .RequiresPrivilege(Privilege.controlserver)
            .RequiresPlayer()
            .BeginSubCommand("seed")
                .WithDescription("[scribe dev] Seed demo content into a Notebook or looked-at Lectern. " +
                    "Usage: /scribe seed <tasks|notes|history|guestbook|all> [notebook|lectern]")
                .WithArgs(
                    parsers.WordRange("what", "tasks", "notes", "history", "guestbook", "all"),
                    parsers.OptionalWordRange("target", "notebook", "lectern"))
                .HandleWith(OnSeedCommand)
            .EndSubCommand()
            .BeginSubCommand("tablet")
                .WithDescription("[scribe dev] Set the held Scribe Tablet's life-cycle state. " +
                    "Usage: /scribe tablet <wet|hard|fired>")
                .WithArgs(parsers.WordRange("state", "wet", "hard", "fired"))
                .HandleWith(OnTabletCommand)
            .EndSubCommand()
            .BeginSubCommand("external")
                .WithDescription("[scribe dev] Call the public TryCreateExternalTask API as if another mod " +
                    "invoked it, for iterating on / screenshotting the result. Usage: " +
                    "/scribe external <title>|<bodyText>|<extraInfo> — pipe-separated; leave a segment blank " +
                    "to pass null for it, e.g. \"/scribe external Feed the chickens||from Notice Board\". " +
                    "Type \\n where you want a line break (chat has no real newline key).")
                .WithArgs(parsers.OptionalAll("titleBodyExtra"))
                .HandleWith(OnExternalTaskCommand)
            .EndSubCommand();
    }

    /// <summary>Handler for <c>/scribe external &lt;title&gt;|&lt;bodyText&gt;|&lt;extraInfo&gt;</c>: a dev harness that calls the real
    /// <see cref="TryCreateExternalTask"/> API. Fields are pipe-separated (chat has no quoted-argument parser); a blank or
    /// missing segment becomes <c>null</c>, as a real caller omitting it would.</summary>
    private TextCommandResult OnExternalTaskCommand(TextCommandCallingArgs args)
    {
        if (sapi is null) return TextCommandResult.Error("Server not ready.");
        if (args.Caller.Player is not IServerPlayer player)
            return TextCommandResult.Error("This command must be run by a player.");
        if (player.WorldData.CurrentGameMode != EnumGameMode.Creative)
            return TextCommandResult.Error("/scribe external is only available in creative mode.");

        string raw = (args[0] as string ?? "").Trim();
        if (raw.Length == 0)
            return TextCommandResult.Error(
                "Usage: /scribe external <title>|<bodyText>|<extraInfo> (pipe-separated, blank = null)");

        string[] parts = raw.Split('|');
        string? title = parts.Length > 0 ? UnescapeTypedNewlines(NullIfBlank(parts[0])) : null;
        string? bodyText = parts.Length > 1 ? UnescapeTypedNewlines(NullIfBlank(parts[1])) : null;
        string? extraInfo = parts.Length > 2 ? UnescapeTypedNewlines(NullIfBlank(parts[2])) : null;

        bool ok = TryCreateExternalTask(player, title, bodyText, extraInfo);
        return ok
            ? TextCommandResult.Success(
                $"[scribe] TryCreateExternalTask succeeded — title={FormatArgEcho(title)}, " +
                $"bodyText={FormatArgEcho(bodyText)}, extraInfo={FormatArgEcho(extraInfo)}. " +
                "Open your Scribe item to see the result.")
            : TextCommandResult.Error(
                "[scribe] TryCreateExternalTask returned false — see the in-game error notice for why " +
                "(no Scribe item carried, all carried items locked, or the target is full).");
    }

    private static string? NullIfBlank(string s)
    {
        s = s.Trim();
        return s.Length == 0 ? null : s;
    }

    /// <summary>Chat can't type a real newline, so the literal two-character <c>\n</c> is converted to <c>'\n'</c> here. Only this
    /// dev harness needs it; a real calling mod passes a genuine multi-line string.</summary>
    private static string? UnescapeTypedNewlines(string? s) => s?.Replace("\\n", "\n");

    private static string FormatArgEcho(string? value) => value is null ? "null" : $"\"{value}\"";

    /// <summary>Handler for <c>/scribe tablet &lt;wet|hard|fired&gt;</c>: swaps the caller's HELD tablet to the requested state
    /// (<see cref="ItemScribeTablet.BuildStateVariant"/>), carrying the document/history across. Runs server-side where the held
    /// item lives. Going wet/hard from FIRED bypasses the normally-permanent fired rule, a deliberate testing override that is reported.</summary>
    private TextCommandResult OnTabletCommand(TextCommandCallingArgs args)
    {
        if (sapi is null) return TextCommandResult.Error("Server not ready.");
        if (args.Caller.Player is not IServerPlayer player)
            return TextCommandResult.Error("This command must be run by a player.");
        if (player.WorldData.CurrentGameMode != EnumGameMode.Creative)
            return TextCommandResult.Error(Lang.Get("scribe:cmd-tablet-creative-only"));

        var slot = FindHeldTablet(player);
        if (slot is null)
            return TextCommandResult.Error(Lang.Get("scribe:cmd-tablet-no-tablet"));

        string stateArg = (string)args[0];
        var target = stateArg switch
        {
            "hard"  => TabletState.Hard,
            "fired" => TabletState.Fired,
            _       => TabletState.Wet,
        };

        // Read before the swap so a fired → wet/hard override can be flagged in the result.
        bool wasFired = ItemScribeTablet.ReadFired(slot.Itemstack);

        var swapped = ItemScribeTablet.BuildStateVariant(slot.Itemstack, target, sapi.World);
        if (swapped is null)
            // Null for a real tablet means no sibling variant (wax has no -hard/-fired).
            return TextCommandResult.Error(Lang.Get("scribe:cmd-tablet-wax-cannot", stateArg));

        slot.Itemstack = swapped;
        slot.MarkDirty();

        bool overrode = wasFired && target != TabletState.Fired;
        return TextCommandResult.Success(overrode
            ? Lang.Get("scribe:cmd-tablet-set-override", stateArg)
            : Lang.Get("scribe:cmd-tablet-set", stateArg));
    }

    /// <summary>The calling player's held Scribe Tablet: active hotbar slot first, then offhand; null if neither.</summary>
    private static ItemSlot? FindHeldTablet(IServerPlayer player)
    {
        var active = player.InventoryManager?.ActiveHotbarSlot;
        if (active?.Itemstack?.Collectible is ItemScribeTablet) return active;
        var offhand = player.Entity?.LeftHandItemSlot;
        if (offhand?.Itemstack?.Collectible is ItemScribeTablet) return offhand;
        return null;
    }

    private TextCommandResult OnSeedCommand(TextCommandCallingArgs args)
    {
        if (sapi is null) return TextCommandResult.Error("Server not ready.");
        if (args.Caller.Player is not IServerPlayer player)
            return TextCommandResult.Error("This command must be run by a player.");
        if (player.WorldData.CurrentGameMode != EnumGameMode.Creative)
            return TextCommandResult.Error("/scribe seed is only available in creative mode.");

        string what   = (string)args[0];
        string target = args[1] as string ?? "auto";

        // Resolve the seed target: an explicit lectern/notebook, or auto (looked-at lectern else held notebook).
        var lectern = ResolveLookedAtLectern(player);
        NotebookHost? notebook = null;

        bool useLectern;
        switch (target)
        {
            case "lectern":
                if (lectern is null)
                    return TextCommandResult.Error("Look at a Scribe Lectern to seed it.");
                useLectern = true;
                break;
            case "notebook":
                notebook = FindNotebookInInventory(player);
                if (notebook is null)
                    return TextCommandResult.Error("Hold a Notebook (or Clockmaker's Notebook) to seed it.");
                useLectern = false;
                break;
            default: // auto
                if (lectern is not null)
                {
                    useLectern = true;
                }
                else
                {
                    notebook = FindNotebookInInventory(player);
                    if (notebook is null)
                        return TextCommandResult.Error(
                            "No target: look at a Scribe Lectern, or hold a Notebook, then run /scribe seed again.");
                    useLectern = false;
                }
                break;
        }

        return useLectern
            ? SeedLectern(lectern!, what)
            : SeedNotebook(notebook!, what);
    }

    /// <summary>The <see cref="BlockEntityScribeLectern"/> the player is looking at, or null.</summary>
    private BlockEntityScribeLectern? ResolveLookedAtLectern(IServerPlayer player)
    {
        var pos = player.CurrentBlockSelection?.Position;
        if (pos is null || sapi is null) return null;
        return sapi.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityScribeLectern;
    }

    private TextCommandResult SeedNotebook(NotebookHost host, string what)
    {
        if (what == "guestbook")
            return TextCommandResult.Error("A Notebook has no Guestbook — that is a Lectern feature.");

        bool seedTasks = what is "tasks" or "all";
        // "all" excludes notes: a seeded demo document should show tasks, not note sections.
        bool seedNotes = what is "notes";
        bool seedHistory = what is "history" or "all";

        if (seedTasks) SeedDocumentTasks(host.Document);
        if (seedNotes) SeedDocumentNotes(host.Document);
        if (seedHistory) SeedHistory(host.History);

        host.Flush();

        var did = new List<string>();
        if (seedTasks) did.Add("tasks");
        if (seedNotes) did.Add("notes");
        if (seedHistory) did.Add("history");
        return TextCommandResult.Success($"[scribe] Seeded Notebook: {string.Join(", ", did)}.");
    }

    private TextCommandResult SeedLectern(BlockEntityScribeLectern lectern, string what)
    {
        bool seedTasks = what is "tasks" or "all";
        // "all" excludes notes (see SeedNotebook).
        bool seedNotes = what is "notes";
        bool seedGuestbook = what is "guestbook" or "all";
        bool wantHistory = what == "history";
        if (wantHistory)
            return TextCommandResult.Error("A Lectern has no History — that is a Notebook feature.");

        if (seedTasks) SeedDocumentTasks(lectern.Document);
        if (seedNotes) SeedDocumentNotes(lectern.Document);
        // Guestbook seeds itself and marks dirty separately.
        if (seedTasks || seedNotes) lectern.MarkDirty(redrawOnClient: true);
        if (seedGuestbook) SeedGuestbookOn(lectern);

        var did = new List<string>();
        if (seedTasks) did.Add("tasks");
        if (seedNotes) did.Add("notes");
        if (seedGuestbook) did.Add("guestbook");
        return TextCommandResult.Success(
            $"[scribe] Seeded Lectern: {string.Join(", ", did)}. Reopen the lectern to see it.");
    }

    private static void SeedDocumentTasks(Scribe.Core.ScribeDocument doc)
    {
        foreach (var (text, done) in SeedTasks)
        {
            doc.AddTask(text);
            if (done)
            {
                // Flip the just-appended task by index.
                doc.ToggleTask(doc.Blocks.Count - 1);
            }
        }
    }

    private static void SeedDocumentNotes(Scribe.Core.ScribeDocument doc)
    {
        foreach (var note in SeedNotes)
            doc.AddTextSection(note);
    }

    /// <summary>Seeds History entries dated across recent in-game days so the tab reads like a chronicle.</summary>
    private void SeedHistory(Scribe.Core.HistoryStore history)
    {
        if (sapi is null) return;
        string date(int daysAgo) => NotebookHost.FormatDateDaysAgo(sapi, daysAgo);
        double timestamp(int daysAgo) => NotebookHost.CalendarTotalDaysAgo(sapi, daysAgo);

        history.TryAddEntry(new Scribe.Core.HistoryEntry
        {
            Kind = Scribe.Core.HistoryEventKind.Crafted, InGameDate = date(14), InGameTimestamp = timestamp(14),
        });
        history.TryAddEntry(new Scribe.Core.HistoryEntry
        {
            Kind = Scribe.Core.HistoryEventKind.PickedUp, ActorName = "Alrik", InGameDate = date(14), InGameTimestamp = timestamp(14),
        });
        history.TryAddEntry(new Scribe.Core.HistoryEntry
        {
            Kind = Scribe.Core.HistoryEventKind.TemporalStorm,
            Schema = Scribe.Core.HistorySchema.Live,
            RefCode = "medium",
            InGameTimestamp = timestamp(11),
        });
        // Live combat/storm rows store facts (ActorName empty) so the tab formats them in the viewer's locale, like the live OnEntityDeath path.
        history.TryAddEntry(new Scribe.Core.HistoryEntry
        {
            Kind = Scribe.Core.HistoryEventKind.Death,
            Schema = Scribe.Core.HistorySchema.Live,
            SubjectName = "Alrik",
            RefCode = "game:drifter-nightmare",
            FlavorSeed = 0,
            InGameTimestamp = timestamp(12),
        });
        history.TryAddEntry(new Scribe.Core.HistoryEntry
        {
            Kind = Scribe.Core.HistoryEventKind.Death,
            Schema = Scribe.Core.HistorySchema.Live,
            SubjectName = "Alrik",
            OtherName = "Gorm",
            RefCode = "bow",
            RefCode2 = "piercingattack",
            FlavorSeed = 0,
            InGameTimestamp = timestamp(9),
        });
        history.TryAddEntry(new Scribe.Core.HistoryEntry
        {
            Kind = Scribe.Core.HistoryEventKind.Death,
            Schema = Scribe.Core.HistorySchema.Live,
            SubjectName = "Alrik",
            RefCode = "game:bear-brown-adult-male",
            FlavorSeed = 1,
            InGameTimestamp = timestamp(7),
        });
        history.TryAddEntry(new Scribe.Core.HistoryEntry
        {
            Kind = Scribe.Core.HistoryEventKind.BossKill,
            Schema = Scribe.Core.HistorySchema.Live,
            SubjectName = "Alrik",
            RefCode = "eidolon",
            InGameTimestamp = timestamp(6),
        });
        history.TryAddEntry(new Scribe.Core.HistoryEntry
        {
            Kind = Scribe.Core.HistoryEventKind.TemporalStorm,
            Schema = Scribe.Core.HistorySchema.Live,
            RefCode = "heavy",
            InGameTimestamp = timestamp(4),
        });
        history.TryAddEntry(new Scribe.Core.HistoryEntry
        {
            Kind = Scribe.Core.HistoryEventKind.PvpKill,
            Schema = Scribe.Core.HistorySchema.Live,
            SubjectName = "Gorm",
            OtherName = "Alrik",
            RefCode = "sword",
            RefCode2 = "slashingattack",
            FlavorSeed = 0,
            InGameTimestamp = timestamp(2),
        });
    }

    /// <summary>Seeds fictional guestbook visitors via the server-only <see cref="BlockEntityScribeLectern.SeedGuestbook"/> seam.</summary>
    private void SeedGuestbookOn(BlockEntityScribeLectern lectern)
    {
        if (sapi is null) return;
        var notes = new[] { "Fine work on the roof!", "Left three loaves in the chest.", null, "Back next season." };
        var entries = new List<(string, string, string?, double)>();
        for (int i = 0; i < SeedVisitorNames.Length; i++)
        {
            int daysAgo = (SeedVisitorNames.Length - i) * 2;
            entries.Add((SeedVisitorNames[i], NotebookHost.FormatDateDaysAgo(sapi, daysAgo),
                i < notes.Length ? notes[i] : null, NotebookHost.CalendarTotalDaysAgo(sapi, daysAgo)));
        }
        lectern.SeedGuestbook(entries);
    }

}
