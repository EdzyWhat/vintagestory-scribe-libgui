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
    // ── History chronicle ────────────────────────────────────────────────────────────────────────

    private bool _stormWasActive;

    /// <summary>Per-player snapshot of the document ids carried ON-PERSON (via <see cref="FindCarriedNotebooks"/>, never the
    /// CarryOn-inclusive <see cref="FindAllCarriedNotebookRecords"/>) as of the last <see cref="OnHistoryScanTick"/>. Only that tick
    /// writes it, so its value going into a death cannot be invalidated by a sibling <c>OnEntityDeath</c> subscriber running earlier in the
    /// same dispatch. The Death branch of <see cref="OnEntityDeath"/> uses it to detect an evicted document. <c>internal</c> so
    /// the integration suite can seed it.</summary>
    internal readonly Dictionary<string, HashSet<Guid>> _lastKnownCarriedDocIds = new();

    /// <summary>Known boss entity code prefixes and the boss-key stored in a live BossKill's <see cref="Scribe.Core.HistoryEntry.RefCode"/>.
    /// Matched by <c>entity.Code.Path.StartsWith</c> so variant suffixes match. Display formats <c>scribe:scribe-history-boss-{key}</c>
    /// with the slayer's name; ActorName stays empty so the row does not prepend "Name — ".
    /// To add a boss: add a boss-specific `scribe-history-boss-&lt;name&gt;` sentence (with {0} for the slayer) to lang/en.json, then a
    /// `(prefix, "name")` row here.</summary>
    private static readonly (string Prefix, string BossKey)[] BossTable =
    {
        ("eidolon", "eidolon"),
        ("erel",    "erel"),
    };

    /// <summary>Inventory <see cref="Vintagestory.API.Common.InventoryBase.ClassName"/>s that are an
    /// <see cref="Vintagestory.API.Common.InventoryBasePlayer"/> but must be excluded from history recording: <c>creative</c> holds
    /// infinite *template* stacks (writing history there puts phantom entries on every future copy), and <c>ground</c> is transient
    /// block-drop staging. Every other <c>InventoryBasePlayer</c> (hotbar, backpack, worn slots, cursor, crafting grid, mod-added bags)
    /// counts as carried. A name-only denylist would be unsafe: a chest/oven/trader dialog's temporary inventory is a plain
    /// <see cref="Vintagestory.API.Common.InventoryGeneric"/>, which the type check excludes.</summary>
    private static readonly HashSet<string> DeniedInventoryClasses = new()
    {
        GlobalConstants.creativeInvClassName, // "creative"
        GlobalConstants.groundInvClassName,   // "ground"
    };

    /// <summary>Yields a server-attached <see cref="NotebookHost"/> for EVERY Notebook/Clockmaker/Tablet stack carried on-person (any
    /// <see cref="Vintagestory.API.Common.InventoryBasePlayer"/> except <see cref="DeniedInventoryClasses"/>), so a live history event
    /// is recorded on all of them. The type check excludes transiently-opened external containers.</summary>
    private IEnumerable<NotebookHost> FindCarriedNotebooks(IServerPlayer player)
    {
        if (sapi is null) yield break;
        foreach (var inv in player.InventoryManager.InventoriesOrdered)
        {
            if (inv is not InventoryBasePlayer) continue;
            if (DeniedInventoryClasses.Contains(inv.ClassName)) continue;
            foreach (var slot in inv)
            {
                if (slot.Itemstack?.Collectible is not IScribeDocumentItem) continue;
                // TabletHost derives from NotebookHost, so a tablet records the same history. Runs every ~10s even on a documentless stack
                // (a brand-new notebook still records events); safe because neither constructing a host nor AttachServerContext's
                // PickedUp bookkeeping writes a document (see NotebookHost.RecordPickedUpIfNew).
                var host = slot.Itemstack.Collectible is ItemScribeTablet
                    ? new TabletHost(slot)
                    : new NotebookHost(slot);
                host.AttachServerContext(sapi, player);
                yield return host;
            }
        }
    }

    /// <summary>The first carried Notebook, or null; for single-target uses (demo seeder, killer-notebook lookup).</summary>
    private NotebookHost? FindNotebookInInventory(IServerPlayer player)
        => FindCarriedNotebooks(player).FirstOrDefault();

    /// <summary>Every Notebook/Tablet a player carries, on-person (<see cref="FindCarriedNotebooks"/>) plus, when CarryOn is installed,
    /// inside a container carried via it (<see cref="CarryOnBridge"/>). Every live history-recording site should walk this.</summary>
    private IEnumerable<IHistoryRecordable> FindAllCarriedNotebookRecords(IServerPlayer player)
    {
        foreach (var host in FindCarriedNotebooks(player)) yield return host;
        if (carryOnBridge is not null)
        {
            foreach (var carried in carryOnBridge.FindCarriedNotebooks(player.Entity))
                yield return carried;
        }
    }

    private void OnEntityDeath(Vintagestory.API.Common.Entities.Entity entity, Vintagestory.API.Common.DamageSource dmg)
    {
        if (sapi is null) return;

        // ── Boss kill ──
        foreach (var (prefix, bossKey) in BossTable)
        {
            if (!entity.Code.Path.StartsWith(prefix)) continue;
            var deathPos = entity.Pos.XYZ;
            foreach (var player in sapi.World.AllOnlinePlayers.OfType<IServerPlayer>())
            {
                double dist = player.Entity.Pos.XYZ.DistanceTo(deathPos);
                if (dist > 100) continue;
                // Record on every carried notebook.
                foreach (var host in FindAllCarriedNotebookRecords(player))
                {
                    // ActorName stays empty so the row does not prepend "Name — " to a sentence that already names the slayer.
                    host.History.TryAddEntry(new Scribe.Core.HistoryEntry
                    {
                        Kind            = Scribe.Core.HistoryEventKind.BossKill,
                        Schema          = Scribe.Core.HistorySchema.Live,
                        SubjectName     = player.PlayerName,
                        RefCode         = bossKey,
                        InGameTimestamp = sapi.World.Calendar.TotalDays,
                    });
                    host.FlushHistory();
                }
            }
            return;
        }

        // ── Player death ──
        if (entity is not Vintagestory.API.Common.EntityPlayer ep) return;
        if (ep.Player is not IServerPlayer sp) return;

        // Resolve the attacker via GetCauseEntity() (CauseEntity ?? SourceEntity): SourceEntity is null for melee, the common PvP case.
        IServerPlayer? killer = null;
        Vintagestory.API.Common.EntityPlayer? killerEntity = null;
        // Materialized once to seed the generic-verb pool off one notebook and record the PvpKill on all of them.
        List<IHistoryRecordable> killerHosts = new();
        if (dmg?.GetCauseEntity() is Vintagestory.API.Common.EntityPlayer ke
            && ke.Player is IServerPlayer k && k.PlayerUID != sp.PlayerUID)
        {
            killer       = k;
            killerEntity = ke;
            killerHosts  = FindAllCarriedNotebookRecords(k).ToList();
        }

        // Materialize the victim's hosts up front to gate fact capture and reuse for the write loop. A document in the last-known
        // snapshot also counts as relevant even when the live scan is empty: that empty scan is exactly the race the fallback catches.
        List<IHistoryRecordable> victimHosts = FindAllCarriedNotebookRecords(sp).ToList();
        bool victimHasSnapshot = _lastKnownCarriedDocIds.TryGetValue(sp.PlayerUID, out var lastKnownDocIds)
            && lastKnownDocIds.Count > 0;
        if (victimHosts.Count == 0 && killerHosts.Count == 0 && !victimHasSnapshot) return;

        // Captured at the moment of death so a queued fallback entry (flushed later) sorts chronologically.
        double deathTimestamp = sapi.World.Calendar.TotalDays;

        // Store facts, not a finished sentence. ActorName stays empty (the sentence already names the victim). For PvP both rows share one
        // stored signal; display applies the victim-first template to Death and the killer-first one to PvpKill.
        HistoryEntry deathEntry;
        HistoryEntry? pvpEntry = null;
        if (killer is not null && killerEntity is not null)
        {
            var tool = killerEntity.RightHandItemSlot?.Itemstack?.Collectible?.Tool;
            string toolName = tool is not null ? tool.ToString()!.ToLowerInvariant() : "";
            string damageName = dmg!.Type.ToString().ToLowerInvariant();
            // Seed is cosmetic: the killer's PvpKill count from their first notebook; all notebooks get the same facts.
            int seed = killerHosts.FirstOrDefault()?.History.Entries.Count(e => e.Kind == Scribe.Core.HistoryEventKind.PvpKill) ?? 0;
            deathEntry = LivePvpFacts(Scribe.Core.HistoryEventKind.Death, sp.PlayerName, killer.PlayerName, toolName, damageName, seed, deathTimestamp);
            pvpEntry   = LivePvpFacts(Scribe.Core.HistoryEventKind.PvpKill, sp.PlayerName, killer.PlayerName, toolName, damageName, seed, deathTimestamp);
        }
        else
        {
            deathEntry = LiveNonPvpDeathFacts(sp.PlayerName, dmg, deathTimestamp);
        }

        // Record the Death on every carried notebook.
        foreach (var nbHost in victimHosts)
        {
            nbHost.History.TryAddEntry(CopyLive(deathEntry));
            nbHost.FlushHistory();
        }

        // A document in the victim's on-person snapshot but NOT written by the live scan was evicted during this death dispatch (e.g. another
        // mod moving it into a corpse first): queue its Death entry. Never queue one the live scan found (double-write). On-person only,
        // matching the snapshot (not CarryOn-sourced).
        if (victimHasSnapshot)
        {
            var liveOnPersonDocIds = new HashSet<Guid>(
                victimHosts.OfType<NotebookHost>().Select(h => h.Document.DocId));
            foreach (var docId in lastKnownDocIds!)
            {
                if (liveOnPersonDocIds.Contains(docId)) continue;
                pendingHistoryStore?.Enqueue(docId, CopyLive(deathEntry));
            }
        }

        // ── PvP kill ── No eviction race for the KILLER (they are not the one dying), so this stays live-scan-only.
        if (pvpEntry is not null)
        {
            foreach (var killerHost in killerHosts)
            {
                killerHost.History.TryAddEntry(CopyLive(pvpEntry));
                killerHost.FlushHistory();
            }
        }
    }

    private static HistoryEntry LivePvpFacts(
        Scribe.Core.HistoryEventKind kind, string victim, string killer,
        string tool, string damage, int seed, double timestamp)
        => new()
        {
            Kind            = kind,
            Schema          = Scribe.Core.HistorySchema.Live,
            SubjectName     = victim,
            OtherName       = killer,
            RefCode         = tool,
            RefCode2        = damage,
            FlavorSeed      = seed,
            InGameTimestamp = timestamp,
        };

    private HistoryEntry LiveNonPvpDeathFacts(string victim, Vintagestory.API.Common.DamageSource? dmg, double timestamp)
    {
        var causeEntity = dmg?.GetCauseEntity();
        if (causeEntity is not null)
        {
            return new HistoryEntry
            {
                Kind            = Scribe.Core.HistoryEventKind.Death,
                Schema          = Scribe.Core.HistorySchema.Live,
                SubjectName     = victim,
                RefCode         = causeEntity.Code.Domain + ":" + causeEntity.Code.Path,
                FlavorSeed      = sapi!.World.Rand.Next(),
                InGameTimestamp = timestamp,
            };
        }

        string cause = dmg is null ? "" : dmg.Source.ToString().ToLowerInvariant().Replace("_", "-");
        return new HistoryEntry
        {
            Kind            = Scribe.Core.HistoryEventKind.Death,
            Schema          = Scribe.Core.HistorySchema.Live,
            SubjectName     = victim,
            RefCode         = cause,
            FlavorSeed      = victim.GetHashCode(),
            InGameTimestamp = timestamp,
        };
    }

    private static HistoryEntry CopyLive(HistoryEntry src) => new()
    {
        Kind            = src.Kind,
        Schema          = src.Schema,
        SubjectName     = src.SubjectName,
        OtherName       = src.OtherName,
        RefCode         = src.RefCode,
        RefCode2        = src.RefCode2,
        FlavorSeed      = src.FlavorSeed,
        InGameTimestamp = src.InGameTimestamp,
    };

    /// <summary>
    /// Periodic sweep (every 10s): (1) storm rising-edge write via <see cref="FindAllCarriedNotebookRecords"/> (CarryOn included);
    /// (2) carried-document snapshot + pending-entry flush, on-person only (the corpse-mod race can only evict on-person slots, and
    /// polling the reflection-based CarryOn bridge every firing is wasteful). <c>internal</c> so integration tests can fire it directly.
    /// </summary>
    internal void OnHistoryScanTick(float _)
    {
        if (sapi is null) return;

        // ── (1) Storm rising-edge detection ──
        var stormSys = sapi.ModLoader.GetModSystem<Vintagestory.GameContent.SystemTemporalStability>();
        bool rising = false;
        string strengthToken = "";
        if (stormSys is not null)
        {
            bool nowActive = stormSys.StormData.nowStormActive;
            rising = nowActive && !_stormWasActive;
            _stormWasActive = nowActive;

            if (rising)
            {
                // Store the strength token (light/medium/heavy); the History tab localizes it.
                strengthToken = stormSys.StormData.nextStormStrength.ToString().ToLowerInvariant();
            }
        }

        foreach (var player in sapi.World.AllOnlinePlayers.OfType<IServerPlayer>())
        {
            if (rising)
            {
                // Record on every carried notebook.
                foreach (var host in FindAllCarriedNotebookRecords(player))
                {
                    host.History.TryAddEntry(new Scribe.Core.HistoryEntry
                    {
                        Kind            = Scribe.Core.HistoryEventKind.TemporalStorm,
                        Schema          = Scribe.Core.HistorySchema.Live,
                        RefCode         = strengthToken,
                        InGameTimestamp = sapi.World.Calendar.TotalDays,
                    });
                    host.FlushHistory();
                }
            }

            // ── (2) Carried-document snapshot + pending-entry flush (on-person only) ──
            var carriedDocIds = new HashSet<Guid>();
            foreach (var host in FindCarriedNotebooks(player))
            {
                carriedDocIds.Add(host.Document.DocId);

                if (pendingHistoryStore is null) continue;
                var queued = pendingHistoryStore.TakeAll(host.Document.DocId);
                if (queued.Count == 0) continue;
                foreach (var entry in queued) host.History.TryAddEntry(entry);
                host.FlushHistory();
            }
            _lastKnownCarriedDocIds[player.PlayerUID] = carriedDocIds;
        }
    }

}
