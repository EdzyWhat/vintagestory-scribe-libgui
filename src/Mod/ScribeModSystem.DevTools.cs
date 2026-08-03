using System;
using System.Collections.Generic;
using Gui.Rendering;             // SkiaAssetLoader
using Gui.Rendering.Text;        // FontRegistry, FontWeight
using Gui.Sound;                 // ISoundPlayer, SoundPlayer (UI click sound)
using Scribe.Core;
using SkiaSharp;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace Scribe;

public sealed partial class ScribeModSystem
{
    /// <summary>
    /// Dev-only client command: <c>/scripttf &lt;target&gt; &lt;prop&gt; &lt;value&gt;</c>
    /// Mutates the held Notebook item's model transforms live so rotation/scale/translation
    /// can be tuned in-game. Prints the full current transform block after each change so
    /// the result can be pasted directly into scribenotebook.json.
    ///
    /// <c>target</c>: tp | ground | gui | fp
    /// <c>prop</c>:   rx | ry | rz | tx | ty | tz | scale
    /// </summary>
    private static void RegisterNotebookTuneCommand(ICoreClientAPI api)
    {
        api.ChatCommands.Create("scripttf")
            .WithDescription("[scribe dev] Tune Notebook item model transforms live. Usage: /scripttf <tp|ground|gui|fp> <rx|ry|rz|tx|ty|tz|scale> <value>")
            .WithArgs(
                api.ChatCommands.Parsers.WordRange("target", "tp", "ground", "gui", "fp"),
                api.ChatCommands.Parsers.WordRange("prop", "rx", "ry", "rz", "tx", "ty", "tz", "scale"),
                api.ChatCommands.Parsers.Float("value"))
            .HandleWith(args =>
            {
                var slot = api.World.Player.InventoryManager.ActiveHotbarSlot;
                if (slot?.Itemstack?.Collectible is not ItemScribeNotebook item)
                    return TextCommandResult.Error("Hold the Notebook in your active hotbar slot first.");

                string target = (string)args[0];
                string prop   = (string)args[1];
                float  value  = (float)args[2];

                var tf = target switch
                {
                    "tp"     => item.TpHandTransform,
                    "ground" => item.GroundTransform,
                    "gui"    => item.GuiTransform,
                    "fp"     => item.FpHandTransform,
                    _        => null,
                };

                if (tf is null)
                    return TextCommandResult.Error($"Unknown target '{target}'.");

                switch (prop)
                {
                    case "rx":    tf.Rotation.X = value; break;
                    case "ry":    tf.Rotation.Y = value; break;
                    case "rz":    tf.Rotation.Z = value; break;
                    case "tx":    tf.Translation.X = value; break;
                    case "ty":    tf.Translation.Y = value; break;
                    case "tz":    tf.Translation.Z = value; break;
                    case "scale": tf.ScaleXYZ.Set(value, value, value); break;
                    default:      return TextCommandResult.Error($"Unknown prop '{prop}'.");
                }

                // Force a re-render of the held item.
                slot.MarkDirty();

                return TextCommandResult.Success(
                    $"[scribe] {target}: rotation=({tf.Rotation.X:0.##}, {tf.Rotation.Y:0.##}, {tf.Rotation.Z:0.##})  " +
                    $"translation=({tf.Translation.X:0.##}, {tf.Translation.Y:0.##}, {tf.Translation.Z:0.##})  " +
                    $"scale={tf.ScaleXYZ.X:0.##}");
            });
    }

    /// <summary>Dev-only client-side surface for the cuneiform font (add-cuneiform-glyph-font, Proposal A):
    /// <c>/cuneiform [text]</c> opens the <see cref="GuiDialogCuneiformHarness"/>, which renders the given
    /// (or a default) demo string through <see cref="CuneiformText"/> at several sizes plus an animated
    /// reveal. It exists ONLY to prove the render/layout path and the disable-cuneiform fallback in-game
    /// before any tablet item/dialog exists — it is behind no player-facing feature. Reuses a single cached
    /// dialog instance, reopening it with fresh text so repeated runs don't leak windows.</summary>
    private void RegisterCuneiformHarnessCommand(ICoreClientAPI api)
    {
        api.ChatCommands.Create("cuneiform")
            .WithDescription("[scribe dev] Open the cuneiform font harness. Usage: /cuneiform [demo text]")
            .WithArgs(api.ChatCommands.Parsers.OptionalAll("text"))
            .HandleWith(args =>
            {
                string text = args[0] as string ?? "";

                // Close any prior harness so the window rebuilds with the new demo text (the dialog captures
                // its text at construction).
                cuneiformHarness?.TryClose();
                cuneiformHarness?.Dispose();
                cuneiformHarness = new GuiDialogCuneiformHarness(api, this, text);
                cuneiformHarness.TryOpen();

                return TextCommandResult.Success("[scribe] Cuneiform harness opened.");
            });
    }

    /// <summary>Dev-only client command for tuning the per-material cuneiform outer glow live
    /// (add-tablet-clay-type-themes, task 5): <c>.cuneiformglow &lt;strength|blur|polarity|reset&gt; [value]</c>.
    /// It mutates the in-memory override in <see cref="CuneiformGlowTable"/> (nothing is persisted) and
    /// forces every open tablet dialog to rebuild so the change shows without reopening. The tuned values
    /// are then baked back into the per-material seeds by hand (task 6.4). Client-registered (<c>.</c>
    /// prefix), like the <c>.cuneiform</c> harness — glow is a pure render concern with no server state.
    ///
    /// <para>Usage: <c>strength 0.55</c> sets the halo alpha; <c>blur 0.09</c> sets the blur as a fraction
    /// of the em; <c>polarity dark|light</c> flips the halo dark (for light ink) or back to light;
    /// <c>reset</c> clears all overrides back to the baked seeds.</para></summary>
    private void RegisterCuneiformGlowCommand(ICoreClientAPI api)
    {
        api.ChatCommands.Create("cuneiformglow")
            .WithDescription("[scribe dev] Tune the cuneiform outer glow live. Usage: .cuneiformglow <strength|blur|polarity|reset> [value]")
            .WithArgs(
                api.ChatCommands.Parsers.WordRange("dial", "strength", "blur", "polarity", "reset"),
                api.ChatCommands.Parsers.OptionalAll("value"))
            .HandleWith(args =>
            {
                string dial = (string)args[0];
                string value = (args[1] as string ?? "").Trim();

                CuneiformGlow effective;
                switch (dial)
                {
                    case "reset":
                        CuneiformGlowTable.ResetOverride();
                        effective = CuneiformGlowTable.For("clay-fire");
                        break;

                    case "strength":
                        if (!float.TryParse(value, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out float strength))
                            return TextCommandResult.Error("Usage: .cuneiformglow strength <0..1>");
                        effective = CuneiformGlowTable.SetOverride(strength: strength, blurFraction: null, darkPolarity: null);
                        break;

                    case "blur":
                        if (!float.TryParse(value, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out float blur))
                            return TextCommandResult.Error("Usage: .cuneiformglow blur <fraction-of-em, e.g. 0.09>");
                        effective = CuneiformGlowTable.SetOverride(strength: null, blurFraction: blur, darkPolarity: null);
                        break;

                    case "polarity":
                        bool? dark = value switch
                        {
                            "dark" => true,
                            "light" => false,
                            _ => null,
                        };
                        if (dark is null)
                            return TextCommandResult.Error("Usage: .cuneiformglow polarity <dark|light>");
                        effective = CuneiformGlowTable.SetOverride(strength: null, blurFraction: null, darkPolarity: dark);
                        break;

                    default:
                        return TextCommandResult.Error($"Unknown dial '{dial}'.");
                }

                // Rebuild every open tablet so the new glow shows without reopening (mirrors how a
                // settings change repaints the tablet). Other dialogs don't paint cuneiform glow, so they
                // are left alone.
                int rebuilt = 0;
                foreach (var dlg in api.Gui.OpenedGuis)
                {
                    if (dlg is GuiDialogScribeTablet tablet)
                    {
                        tablet.ForceRebuild();
                        rebuilt++;
                    }
                }

                return TextCommandResult.Success(
                    $"[scribe] glow → color=(#{ToHex(effective.Color)}, a={effective.Color.W:0.##}), " +
                    $"blur={effective.BlurFraction:0.###}×em. Rebuilt {rebuilt} open tablet(s).");
            });
    }

    /// <summary>Format the RGB of a glow color as <c>RRGGBB</c> hex for the command echo (alpha is reported
    /// separately as the strength dial).</summary>
    private static string ToHex(OpenTK.Mathematics.Vector4 c) =>
        $"{(int)System.Math.Round(c.X * 255):X2}{(int)System.Math.Round(c.Y * 255):X2}{(int)System.Math.Round(c.Z * 255):X2}";

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
            .EndSubCommand();
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

        history.TryAddEntry(new Scribe.Core.HistoryEntry
        {
            Kind = Scribe.Core.HistoryEventKind.Crafted, InGameDate = date(14),
        });
        history.TryAddEntry(new Scribe.Core.HistoryEntry
        {
            Kind = Scribe.Core.HistoryEventKind.PickedUp, ActorName = "Alrik", InGameDate = date(14),
        });
        history.TryAddEntry(new Scribe.Core.HistoryEntry
        {
            Kind = Scribe.Core.HistoryEventKind.TemporalStorm, Detail = "Medium", InGameDate = date(11),
        });
        // Every combat entry carries its whole sentence in Detail (ActorName empty) so the History
        // row does not prepend "Name — " and print the name twice — same convention as BossKill and
        // the live OnEntityDeath path. Two PvP entries (bow death, sword kill) showcase both weapon
        // tiers, and two mob deaths (Nightmare Drifter, brown bear) showcase the flavored creature
        // pool with correct variant names. All reuse the live lang keys, so the demo can never drift.
        history.TryAddEntry(new Scribe.Core.HistoryEntry
        {
            Kind = Scribe.Core.HistoryEventKind.Death,
            Detail = SeedMobDeathMessage(victim: "Alrik", creatureCode: "drifter-nightmare"), InGameDate = date(12),
        });
        history.TryAddEntry(new Scribe.Core.HistoryEntry
        {
            Kind = Scribe.Core.HistoryEventKind.Death,
            Detail = SeedPvpDeathMessage(killer: "Gorm", weaponTool: "bow", victim: "Alrik"), InGameDate = date(9),
        });
        history.TryAddEntry(new Scribe.Core.HistoryEntry
        {
            Kind = Scribe.Core.HistoryEventKind.Death,
            Detail = SeedMobDeathMessage(victim: "Alrik", creatureCode: "bear-brown-adult-male"), InGameDate = date(7),
        });
        history.TryAddEntry(new Scribe.Core.HistoryEntry
        {
            Kind = Scribe.Core.HistoryEventKind.BossKill,
            Detail = Lang.Get("scribe:scribe-history-boss-eidolon", "Alrik"), InGameDate = date(6),
        });
        history.TryAddEntry(new Scribe.Core.HistoryEntry
        {
            Kind = Scribe.Core.HistoryEventKind.TemporalStorm, Detail = "Heavy", InGameDate = date(4),
        });
        history.TryAddEntry(new Scribe.Core.HistoryEntry
        {
            Kind = Scribe.Core.HistoryEventKind.PvpKill,
            Detail = SeedPvpKillMessage(killer: "Alrik", weaponTool: "sword", victim: "Gorm"), InGameDate = date(2),
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
    /// the live <see cref="BuildDeathMessage"/> path uses. The creature is named from vanilla's own
    /// <c>prefixandcreature-&lt;code&gt;</c> key (matching <c>Entity.GetPrefixAndCreatureName()</c>),
    /// so the seed reads with the correct variant ("a nightmare drifter", "a brown bear") without a
    /// live entity. The pool index is derived from the creature code so the demo is stable per run.</summary>
    private static string SeedMobDeathMessage(string victim, string creatureCode)
    {
        string creature = Lang.GetMatching($"game:prefixandcreature-{creatureCode}");
        int poolSize = 0;
        while (Lang.Get($"scribe:scribe-mob-death-{poolSize}") != $"scribe:scribe-mob-death-{poolSize}") poolSize++;
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
