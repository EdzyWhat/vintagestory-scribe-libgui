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

    /// <summary>Per-player snapshot of the Notebook/Tablet document ids carried ON-PERSON (via
    /// <see cref="FindCarriedNotebooks"/> — never the CarryOn-inclusive
    /// <see cref="FindAllCarriedNotebookRecords"/>) as of the last <see cref="OnHistoryScanTick"/>
    /// firing. Populated ONLY by that tick, on its own cadence, so its value going into any given
    /// death is fixed before that death's own event dispatch begins — it cannot be invalidated by a
    /// sibling <c>OnEntityDeath</c> subscriber that runs earlier in the same dispatch (see
    /// fix-death-history-inventory-race design.md, "A periodic snapshot, not a live re-check").
    /// Consulted by the Death branch of <see cref="OnEntityDeath"/> to detect a document that was
    /// evicted by something else during the same death dispatch. <c>internal</c> (not
    /// <c>private</c>) solely so the integration suite can seed a snapshot directly rather than
    /// waiting out a real tick, via the project's own <c>InternalsVisibleTo("Integration.Tests")</c>.
    /// </summary>
    internal readonly Dictionary<string, HashSet<Guid>> _lastKnownCarriedDocIds = new();

    /// <summary>Known boss entity code prefixes and the boss-key stored in a live BossKill's
    /// <see cref="Scribe.Core.HistoryEntry.RefCode"/>. Checked via entity.Code.Path.StartsWith so
    /// variant suffixes (-pristine, -corrupted, etc.) match. Display formats
    /// <c>scribe:scribe-history-boss-{key}</c> with the slayer's name; ActorName is left empty so
    /// the History row does not prepend "Name — ".
    ///
    /// MORE BOSSES ARE EXPECTED. Each boss needs its OWN narrative sentence — the current lines are
    /// boss-specific ("descended into darkness", "climbed the tower"), not a fill-in-the-blank template.
    /// To add one: (1) add a `scribe-history-boss-&lt;name&gt;` key to lang/en.json with the full sentence
    /// and a {0} for the slayer, (2) add a `(prefix, "name")` row here. No other code changes are needed.</summary>
    private static readonly (string Prefix, string BossKey)[] BossTable =
    {
        ("eidolon", "eidolon"),
        ("erel",    "erel"),
    };

    /// <summary>The two inventory <see cref="Vintagestory.API.Common.InventoryBase.ClassName"/>s
    /// that ARE the engine's own <see cref="Vintagestory.API.Common.InventoryBasePlayer"/> (i.e.
    /// genuinely "on the player" per that class's own doc-comment) but must still be excluded from
    /// history recording, for reasons unrelated to each other: <c>creative</c>
    /// (<see cref="GlobalConstants.creativeInvClassName"/>) holds infinite *template* stacks, and
    /// writing history into one mutates the template so every future copy carries phantom entries
    /// (the observed "new notebook auto-populates past kills" bug); <c>ground</c>
    /// (<see cref="GlobalConstants.groundInvClassName"/>) is transient block-drop staging, not
    /// on-person. Everything else that is an <c>InventoryBasePlayer</c> — hotbar, backpack, worn
    /// character slots, the mouse-cursor drag slot, the crafting grid, and any inventory a mod adds
    /// directly to the player's own inventory manager (e.g. a skill/ability bonus bag) — counts as
    /// carried automatically, without Scribe needing to recognize its <c>ClassName</c> in advance.
    /// See <c>fix-carried-notebook-detection</c>'s design.md for the full rationale, including why a
    /// name-only denylist (no type check) would be unsafe: a chest/oven/trader dialog a player merely
    /// has open nearby is also temporarily added to <c>InventoriesOrdered</c>, but as a plain
    /// <see cref="Vintagestory.API.Common.InventoryGeneric"/> (not an <c>InventoryBasePlayer</c>), so
    /// the type check is what excludes those without needing to know their names.</summary>
    private static readonly HashSet<string> DeniedInventoryClasses = new()
    {
        GlobalConstants.creativeInvClassName, // "creative"
        GlobalConstants.groundInvClassName,   // "ground"
    };

    /// <summary>Yields a server-attached <see cref="NotebookHost"/> for EVERY Notebook stack the
    /// player is carrying on their person — any <see cref="Vintagestory.API.Common.InventoryBasePlayer"/>
    /// inventory except the two denied in <see cref="DeniedInventoryClasses"/> — so a live history
    /// event (death, storm, boss kill) is recorded on ALL of them, not just the first found. Matches
    /// BOTH <see cref="ItemScribeNotebook"/> and its sibling <see cref="ItemClockmakerNotebook"/> —
    /// both carry a document + history store. The type check (not just a name-based filter) is what
    /// keeps this safe: it excludes transiently-opened external containers (chests, ovens, traders —
    /// plain <see cref="Vintagestory.API.Common.InventoryGeneric"/>, temporarily added to
    /// <c>InventoriesOrdered</c> while their dialog is open) without needing to know their
    /// <c>ClassName</c>s, while still excluding the creative inventory specifically (whose template
    /// stacks the old allow-list-less "walk everything" logic once mutated, silently accumulating
    /// phantom entries on every future copy).</summary>
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
                // TabletHost derives from NotebookHost, so a carried tablet records the same history
                // (deaths, kills, storms) as a notebook through the identical write-through path. This
                // runs unconditionally every ~10s for every online player, including on a documentless
                // stack (a brand-new notebook still needs to record Death/Storm/PvpKill/BossKill) — safe
                // because neither constructing a host nor AttachServerContext's PickedUp bookkeeping
                // writes a document to the stack (see NotebookHost.RecordPickedUpIfNew).
                var host = slot.Itemstack.Collectible is ItemScribeTablet
                    ? new TabletHost(slot)
                    : new NotebookHost(slot);
                host.AttachServerContext(sapi, player);
                yield return host;
            }
        }
    }

    /// <summary>Convenience wrapper: the first carried Notebook, or null. Used where a single target
    /// is wanted (the demo seeder, and the killer-notebook lookup whose PvpKill entry is a single
    /// record). Live recorders that must fan out to every notebook use
    /// <see cref="FindCarriedNotebooks"/> directly.</summary>
    private NotebookHost? FindNotebookInInventory(IServerPlayer player)
        => FindCarriedNotebooks(player).FirstOrDefault();

    /// <summary>Fans out to every Notebook/Tablet a player carries, from BOTH sources: on-person
    /// (<see cref="FindCarriedNotebooks"/>) and, when the CarryOn mod is installed, inside a
    /// container the player is currently carrying via it (<see cref="CarryOnBridge"/>). This is what
    /// every live history-recording site (Death, PvpKill, TemporalStorm, BossKill) should walk,
    /// rather than <see cref="FindCarriedNotebooks"/> alone, so a Notebook stashed in a carried chest
    /// records history exactly like one in a pocket.</summary>
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
                // Record on EVERY notebook the player carries, not just the first found.
                foreach (var host in FindAllCarriedNotebookRecords(player))
                {
                    // Live facts: slayer name + boss key. ActorName stays empty so the History row
                    // does not prepend "Name — " onto a sentence that already names the slayer.
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

        // Resolve the attacker via GetCauseEntity() (CauseEntity ?? SourceEntity) so melee kills
        // are attributed — SourceEntity is null for melee, which is the common PvP case. A single
        // "attacker is a different player" predicate drives both the victim's Death and the
        // killer's PvpKill entry.
        IServerPlayer? killer = null;
        Vintagestory.API.Common.EntityPlayer? killerEntity = null;
        // Materialize the killer's carried notebooks once so we can both (a) seed the generic-verb
        // pool off one of them and (b) record the PvpKill on ALL of them.
        List<IHistoryRecordable> killerHosts = new();
        if (dmg?.GetCauseEntity() is Vintagestory.API.Common.EntityPlayer ke
            && ke.Player is IServerPlayer k && k.PlayerUID != sp.PlayerUID)
        {
            killer       = k;
            killerEntity = ke;
            killerHosts  = FindAllCarriedNotebookRecords(k).ToList();
        }

        // Materialize the victim's carried-notebook list up front too (mirroring killerHosts above),
        // so we can gate ALL fact capture below on "does anyone relevant carry a Notebook"
        // before doing any of that work, and reuse this same list for the write loop further down.
        // A document present in the victim's last-known snapshot (fix-death-history-inventory-race)
        // ALSO counts as "relevant" even when the live scan comes up empty — that empty-live-scan
        // case is exactly the race this fallback exists to catch, so it must not be treated the
        // same as "nobody ever had a notebook".
        List<IHistoryRecordable> victimHosts = FindAllCarriedNotebookRecords(sp).ToList();
        bool victimHasSnapshot = _lastKnownCarriedDocIds.TryGetValue(sp.PlayerUID, out var lastKnownDocIds)
            && lastKnownDocIds.Count > 0;
        if (victimHosts.Count == 0 && killerHosts.Count == 0 && !victimHasSnapshot) return;

        // Captured once, at the actual moment of death, so a queued fallback entry (flushed later)
        // sorts into its true chronological position rather than wherever the flush happens to land.
        double deathTimestamp = sapi.World.Calendar.TotalDays;

        // Store facts, not a finished sentence. ActorName stays empty: the displayed sentence
        // already names the victim, and the History row would prepend "ActorName — " otherwise.
        //
        // For PvP both rows share one stored signal (names + tool/damage + seed). Display applies
        // the victim-first passive template to Death and the killer-first active template to PvpKill.
        HistoryEntry deathEntry;
        HistoryEntry? pvpEntry = null;
        if (killer is not null && killerEntity is not null)
        {
            var tool = killerEntity.RightHandItemSlot?.Itemstack?.Collectible?.Tool;
            string toolName = tool is not null ? tool.ToString()!.ToLowerInvariant() : "";
            string damageName = dmg!.Type.ToString().ToLowerInvariant();
            // Generic-pool cursor reads the killer's existing PvpKill count from their first
            // notebook. Different carried notebooks may hold different counts, but the seed is
            // cosmetic flavor — one reference is fine, and all get the same facts.
            int seed = killerHosts.FirstOrDefault()?.History.Entries.Count(e => e.Kind == Scribe.Core.HistoryEventKind.PvpKill) ?? 0;
            deathEntry = LivePvpFacts(Scribe.Core.HistoryEventKind.Death, sp.PlayerName, killer.PlayerName, toolName, damageName, seed, deathTimestamp);
            pvpEntry   = LivePvpFacts(Scribe.Core.HistoryEventKind.PvpKill, sp.PlayerName, killer.PlayerName, toolName, damageName, seed, deathTimestamp);
        }
        else
        {
            deathEntry = LiveNonPvpDeathFacts(sp.PlayerName, dmg, deathTimestamp);
        }

        // Record the Death on EVERY notebook the victim carries, not just the first found.
        foreach (var nbHost in victimHosts)
        {
            nbHost.History.TryAddEntry(CopyLive(deathEntry));
            nbHost.FlushHistory();
        }

        // A document that was in the victim's last-known on-person snapshot but was NOT among the
        // documents the live scan above just wrote to has been evicted by something else during this
        // same death dispatch (e.g. another mod's own OnEntityDeath handler moving it into a corpse
        // before this one ran) — queue its Death entry instead of losing it. Never queue a document
        // the live scan DID find (it was already written above; queuing it too would double-write).
        // Scoped to on-person documents only (NotebookHost, not the CarryOn-sourced
        // CarryOnBridge.CarriedNotebookRef) — the snapshot itself is on-person-only, per design.md.
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

        // ── PvP kill — record the same facts on every notebook the killer carries ──
        // Victim-only fallback: nothing evicts the KILLER's inventory during their own kill (they are
        // not the one dying), so there is no race to guard here — this stays live-scan-only.
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
    /// Dual-purpose periodic sweep (10s — widened from the original storm-only tick's 5s,
    /// fix-death-history-inventory-race): (1) the original storm rising-edge detection/write,
    /// completely unchanged in scope (still walks <see cref="FindAllCarriedNotebookRecords"/>, so a
    /// CarryOn-carried notebook keeps recording storms exactly as it always has); and (2) a shared
    /// carried-document snapshot + pending-entry flush, added by this change. (2) is scoped to
    /// <see cref="FindCarriedNotebooks"/> (on-person only) — the corpse-mod race this fallback exists
    /// for can only evict on-person slots (see design.md), so there is no reason to also poll the
    /// reflection-based CarryOn bridge here for every online player every firing. Renamed from
    /// <c>OnStormTick</c> to reflect that it is no longer storm-only. <c>internal</c> (not
    /// <c>private</c>) solely so the integration suite can fire it directly rather than waiting out
    /// the real 10s interval, via the project's own <c>InternalsVisibleTo("Integration.Tests")</c>.
    /// </summary>
    internal void OnHistoryScanTick(float _)
    {
        if (sapi is null) return;

        // ── (1) Storm rising-edge detection — unchanged responsibility, unchanged scope ──
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
                // Record on EVERY notebook the player carries, not just the first found.
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
