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

    /// <summary>Fictional visitor names for seeded Lectern guestbooks. Kept ≤16 chars each so they read
    /// as plausible player names in screenshots.</summary>
    private static readonly string[] SeedVisitorNames =
    {
        "Alrik", "Brenna", "Cael", "Dagny", "Emeric", "Fenna", "Gorm", "Hilde",
    };

    /// <summary>Sample tasks for a seeded document — a believable mix of done/undone chores and goals.</summary>
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

    /// <summary>Registers the server-side dev command <c>/scribe seed &lt;what&gt; [target]</c>, which
    /// populates a target Notebook or looked-at Lectern with believable demo content (tasks, notes,
    /// History on notebooks, Guestbook on lecterns) for screenshot/video capture. All three stores are
    /// server-authoritative, so this must be a server command. Double-gated: the <c>controlserver</c>
    /// privilege plus an in-handler creative-mode check. History seeds only the Notebook and Guestbook
    /// only the Lectern (they are hosted asymmetrically); inapplicable combinations are skipped and
    /// reported, never errored (design decisions 1–3).</summary>
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

    /// <summary>Handler for <c>/scribe external &lt;title&gt;|&lt;bodyText&gt;|&lt;extraInfo&gt;</c> — a
    /// dev-only harness that calls the real <see cref="TryCreateExternalTask"/> public API directly, so the
    /// author (and, over a shared screen/Discord call, other mod authors) can see exactly what each
    /// parameter produces without writing a throwaway calling mod. The three fields are pipe-separated on
    /// one line since chat commands have no quoted-multi-word-argument parser; a blank segment (or a
    /// missing trailing one) becomes <c>null</c>, matching what a real caller passing <c>null</c>/omitting
    /// an argument would produce. Same double-gate as the other dev subcommands
    /// (<c>controlserver</c> privilege on the root + creative-mode check here).</summary>
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

    /// <summary>Chat input has no way to type a real newline byte, so <c>/scribe external</c> asks the
    /// caller to type the literal two-character escape <c>\n</c> where they want a line break — converted
    /// here to a real <c>'\n'</c> before it ever reaches <see cref="TryCreateExternalTask"/>. Only this dev
    /// harness needs the conversion: a real calling mod hands over a genuine multi-line C# string, which
    /// already survives the mapper/codecs/render pipeline untouched (add-external-mod-task-api).</summary>
    private static string? UnescapeTypedNewlines(string? s) => s?.Replace("\\n", "\n");

    private static string FormatArgEcho(string? value) => value is null ? "null" : $"\"{value}\"";

    /// <summary>Handler for <c>/scribe tablet &lt;wet|hard|fired&gt;</c> (add-tablet-state-dev-command): set the
    /// calling player's HELD Scribe Tablet to the requested life-cycle state by swapping its <c>material</c>
    /// variant to the corresponding sibling and carrying the document/history across
    /// (<see cref="ItemScribeTablet.BuildStateVariant"/>). Server-authoritative — the held item lives in the
    /// server-side inventory, so the swap + <see cref="ItemSlot.MarkDirty"/> run here, exactly like the
    /// natural <c>Soften</c>/<c>DoSmelt</c> paths. Double-gated identically to <c>/scribe seed</c>
    /// (<c>controlserver</c> privilege on the root + this in-handler creative check). Reaching a
    /// <c>wet</c>/<c>hard</c> state from a FIRED tablet bypasses the normally-permanent fired rule; that is an
    /// intentional testing override and is reported as such (D3).</summary>
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

        // Capture whether the source was fired BEFORE the swap, so we can flag the deliberate override of the
        // permanent-fired rule (fired → wet/hard) in the result message.
        bool wasFired = ItemScribeTablet.ReadFired(slot.Itemstack);

        var swapped = ItemScribeTablet.BuildStateVariant(slot.Itemstack, target, sapi.World);
        if (swapped is null)
            // The only way BuildStateVariant returns null for a real tablet is a missing sibling variant —
            // i.e. a wax tablet asked to harden/fire (wax has no -hard/-fired sibling).
            return TextCommandResult.Error(Lang.Get("scribe:cmd-tablet-wax-cannot", stateArg));

        slot.Itemstack = swapped;
        slot.MarkDirty();

        bool overrode = wasFired && target != TabletState.Fired;
        return TextCommandResult.Success(overrode
            ? Lang.Get("scribe:cmd-tablet-set-override", stateArg)
            : Lang.Get("scribe:cmd-tablet-set", stateArg));
    }

    /// <summary>Resolve the calling player's currently held Scribe Tablet: the active hotbar slot first, then
    /// the offhand, so a dev toggle acts on whatever hand holds a tablet. Null when neither hand holds one.</summary>
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

    /// <summary>Auto-target helper: the <see cref="BlockEntityScribeLectern"/> the player is currently
    /// looking at, or null. Mirrors the block-selection lookup in <see cref="BlockScribeLectern"/>.</summary>
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
        // "all" intentionally excludes notes (only the explicit "notes" target seeds them) — a seeded
        // demo document should show tasks, not note sections.
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
        // "all" intentionally excludes notes (only the explicit "notes" target seeds them) — see SeedNotebook.
        bool seedNotes = what is "notes";
        bool seedGuestbook = what is "guestbook" or "all";
        bool wantHistory = what == "history";
        if (wantHistory)
            return TextCommandResult.Error("A Lectern has no History — that is a Notebook feature.");

        if (seedTasks) SeedDocumentTasks(lectern.Document);
        if (seedNotes) SeedDocumentNotes(lectern.Document);
        // Persist the document edits (guestbook seeds itself + marks dirty separately).
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
                // The task was appended last; flip its done flag via its index.
                doc.ToggleTask(doc.Blocks.Count - 1);
            }
        }
    }

    private static void SeedDocumentNotes(Scribe.Core.ScribeDocument doc)
    {
        foreach (var note in SeedNotes)
            doc.AddTextSection(note);
    }

    /// <summary>Seeds a spread of History entries dated across recent in-game days so the History tab
    /// reads like a lived-in chronicle rather than a single-day dump.</summary>
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
            Kind = Scribe.Core.HistoryEventKind.TemporalStorm, Detail = "Medium", InGameDate = date(11), InGameTimestamp = timestamp(11),
        });
        // Every combat entry carries its whole sentence in Detail (ActorName empty) so the History
        // row does not prepend "Name — " and print the name twice — same convention as BossKill and
        // the live OnEntityDeath path. Two PvP entries (bow death, sword kill) showcase both weapon
        // tiers, and two mob deaths (Nightmare Drifter, brown bear) showcase the flavored creature
        // pool with correct variant names. All reuse the live lang keys, so the demo can never drift.
        history.TryAddEntry(new Scribe.Core.HistoryEntry
        {
            Kind = Scribe.Core.HistoryEventKind.Death,
            Detail = SeedMobDeathMessage(victim: "Alrik", creatureCode: "drifter-nightmare"), InGameDate = date(12), InGameTimestamp = timestamp(12),
        });
        history.TryAddEntry(new Scribe.Core.HistoryEntry
        {
            Kind = Scribe.Core.HistoryEventKind.Death,
            Detail = SeedPvpDeathMessage(killer: "Gorm", weaponTool: "bow", victim: "Alrik"), InGameDate = date(9), InGameTimestamp = timestamp(9),
        });
        history.TryAddEntry(new Scribe.Core.HistoryEntry
        {
            Kind = Scribe.Core.HistoryEventKind.Death,
            Detail = SeedMobDeathMessage(victim: "Alrik", creatureCode: "bear-brown-adult-male"), InGameDate = date(7), InGameTimestamp = timestamp(7),
        });
        history.TryAddEntry(new Scribe.Core.HistoryEntry
        {
            Kind = Scribe.Core.HistoryEventKind.BossKill,
            Detail = Lang.Get("scribe:scribe-history-boss-eidolon", "Alrik"), InGameDate = date(6), InGameTimestamp = timestamp(6),
        });
        history.TryAddEntry(new Scribe.Core.HistoryEntry
        {
            Kind = Scribe.Core.HistoryEventKind.TemporalStorm, Detail = "Heavy", InGameDate = date(4), InGameTimestamp = timestamp(4),
        });
        history.TryAddEntry(new Scribe.Core.HistoryEntry
        {
            Kind = Scribe.Core.HistoryEventKind.PvpKill,
            Detail = SeedPvpKillMessage(killer: "Alrik", weaponTool: "sword", victim: "Gorm"), InGameDate = date(2), InGameTimestamp = timestamp(2),
        });
    }

    /// <summary>Builds a seeded PvP DEATH message (victim-first passive, for a victim's Death entry)
    /// from the same lang keys the live path uses — the <c>scribe:scribe-pvp-verb-tool-&lt;tool&gt;</c>
    /// verb's passive participle assembled into <c>scribe:scribe-pvp-death-message</c> — so demo
    /// content can never drift from real wording. <paramref name="weaponTool"/> is a lowercased
    /// <c>EnumTool</c> name (e.g. "bow"); no live entity is needed since we name the weapon category
    /// directly. Mirrors the live <see cref="VerbParticiple"/> death branch in <c>OnEntityDeath</c>.</summary>
    private static string SeedPvpDeathMessage(string killer, string weaponTool, string victim)
    {
        string verbKey = $"scribe:scribe-pvp-verb-tool-{weaponTool}";
        return Lang.Get("scribe:scribe-pvp-death-message", victim, VerbParticiple(verbKey), killer);
    }

    /// <summary>Builds a seeded PvP KILL message (killer-first active, for a killer's PvpKill entry).
    /// Companion to <see cref="SeedPvpDeathMessage"/>; mirrors the live <see cref="VerbActive"/> kill
    /// branch in <c>OnEntityDeath</c>.</summary>
    private static string SeedPvpKillMessage(string killer, string weaponTool, string victim)
    {
        string verbKey = $"scribe:scribe-pvp-verb-tool-{weaponTool}";
        return Lang.Get("scribe:scribe-pvp-kill-message", killer, VerbActive(verbKey), victim);
    }

    /// <summary>Builds a seeded mob-death message from the same <c>scribe:scribe-mob-death-N</c> pool
    /// the live <see cref="BuildDeathMessage"/> path uses, via the shared <see cref="GetMobDeathPoolSize"/>
    /// helper. The creature is named from vanilla's own <c>prefixandcreature-&lt;code&gt;</c> key
    /// (matching <c>Entity.GetPrefixAndCreatureName()</c>), so the seed reads with the correct variant
    /// ("a nightmare drifter", "a brown bear") without a live entity. The pool index is derived from
    /// the creature code so the demo is stable per run.</summary>
    private string SeedMobDeathMessage(string victim, string creatureCode)
    {
        string creature = Lang.GetMatching($"game:prefixandcreature-{creatureCode}");
        int poolSize = GetMobDeathPoolSize();
        int idx = poolSize > 0 ? Math.Abs(creatureCode.GetHashCode()) % poolSize : 0;
        return Lang.Get($"scribe:scribe-mob-death-{idx}", victim, creature);
    }

    /// <summary>Seeds fictional guestbook visitors (some with short notes) on a lectern, dated across
    /// recent in-game days via the server-only <see cref="BlockEntityScribeLectern.SeedGuestbook"/> seam.</summary>
    private void SeedGuestbookOn(BlockEntityScribeLectern lectern)
    {
        if (sapi is null) return;
        var notes = new[] { "Fine work on the roof!", "Left three loaves in the chest.", null, "Back next season." };
        var entries = new List<(string, string, string?)>();
        for (int i = 0; i < SeedVisitorNames.Length; i++)
        {
            entries.Add((SeedVisitorNames[i], NotebookHost.FormatDateDaysAgo(sapi, (SeedVisitorNames.Length - i) * 2),
                i < notes.Length ? notes[i] : null));
        }
        lectern.SeedGuestbook(entries);
    }

}
