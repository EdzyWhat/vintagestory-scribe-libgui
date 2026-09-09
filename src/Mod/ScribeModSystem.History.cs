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

    /// <summary>Known boss entity code prefixes and the lang key of the narrative line recorded for the
    /// BossKill event. Checked via entity.Code.Path.StartsWith so variant suffixes (-pristine, -corrupted,
    /// etc.) match. The lang string takes the slayer's name as {0} and becomes the entry's Detail (the
    /// whole descriptive sentence), so ActorName is left empty for boss kills — see OnEntityDeath.
    ///
    /// MORE BOSSES ARE EXPECTED. Each boss needs its OWN narrative sentence — the current lines are
    /// boss-specific ("descended into darkness", "climbed the tower"), not a fill-in-the-blank template.
    /// To add one: (1) add a `scribe-history-boss-&lt;name&gt;` key to lang/en.json with the full sentence
    /// and a {0} for the slayer, (2) add a `(prefix, "scribe:scribe-history-boss-&lt;name&gt;")` row here.
    /// No other code changes are needed.</summary>
    private static readonly (string Prefix, string LangKey)[] BossTable =
    {
        ("eidolon", "scribe:scribe-history-boss-eidolon"),
        ("erel",    "scribe:scribe-history-boss-erel"),
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
                // (deaths, kills, storms) as a notebook through the identical write-through path.
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

    /// <summary>Lazily-discovered, cached size of the <c>scribe-mob-death-N</c> flavor pool — shared by
    /// <see cref="BuildDeathMessage"/> and the dev seeder's <c>SeedMobDeathMessage</c> so the discovery
    /// logic exists in exactly one place. The pool is static shipped content, so the count never
    /// changes mid-session and is safe to cache for the life of the running server.</summary>
    private int? _mobDeathPoolSize;

    /// <summary>Discovers and caches the size of the <c>scribe-mob-death-N</c> flavor pool by probing
    /// upward from index 0 with <see cref="Lang.HasTranslation"/> — NOT <see cref="Lang.Get"/>. Every
    /// pool entry contains <c>{0}</c>/<c>{1}</c> placeholders, so a zero-arg <c>Lang.Get</c> probe
    /// would format the template with no arguments and throw inside <c>TryFormat</c> on every probe
    /// (caught internally by the engine, but logged as an [Error]+[Warning] pair every time). Use
    /// <c>HasTranslation</c> here for any future existence-probed key too — it resolves to a plain
    /// cache lookup and never calls <c>Format</c>, so it cannot reach that exception/warning path
    /// regardless of how many placeholders the target template has.
    /// <c>findWildcarded: false</c> matches the exact-key intent of the probe; <c>logErrors: false</c>
    /// avoids a "Lang key not found" debug line firing on the one expected miss that ends the loop.
    /// </summary>
    private int GetMobDeathPoolSize()
    {
        if (_mobDeathPoolSize is int cached) return cached;
        int poolSize = 0;
        while (Lang.HasTranslation($"scribe:scribe-mob-death-{poolSize}", findWildcarded: false, logErrors: false))
            poolSize++;
        _mobDeathPoolSize = poolSize;
        return poolSize;
    }

    private void OnEntityDeath(Vintagestory.API.Common.Entities.Entity entity, Vintagestory.API.Common.DamageSource dmg)
    {
        if (sapi is null) return;

        // ── Boss kill ──
        foreach (var (prefix, langKey) in BossTable)
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
                    // The whole descriptive sentence lives in Detail (ActorName empty); the History row
                    // shows Detail alone when ActorName is empty, so no "Name — " prefix is prepended.
                    host.History.TryAddEntry(new Scribe.Core.HistoryEntry
                    {
                        Kind            = Scribe.Core.HistoryEventKind.BossKill,
                        Detail          = Lang.Get(langKey, player.PlayerName),
                        InGameDate      = NotebookHost.FormatDate(sapi),
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
        // "attacker is a different player" predicate drives both the victim's message and the
        // killer's PvpKill entry, so both symptoms are fixed by one condition.
        IServerPlayer? killer = null;
        // Materialize the killer's carried notebooks once so we can both (a) index the generic-verb
        // pool off one of them and (b) record the PvpKill on ALL of them.
        List<IHistoryRecordable> killerHosts = new();
        if (dmg?.GetCauseEntity() is Vintagestory.API.Common.EntityPlayer killerEntity
            && killerEntity.Player is IServerPlayer k && k.PlayerUID != sp.PlayerUID)
        {
            killer      = k;
            killerHosts = FindAllCarriedNotebookRecords(k).ToList();
        }

        // Materialize the victim's carried-notebook list up front too (mirroring killerHosts above),
        // so we can gate ALL message construction below on "does anyone relevant carry a Notebook"
        // before doing any of that work, and reuse this same list for the write loop further down —
        // mirroring the pattern the BossKill branch already uses (its Lang.Get lives inside the
        // per-notebook loop). A document present in the victim's last-known snapshot (fix-death-
        // history-inventory-race) ALSO counts as "relevant" even when the live scan comes up empty —
        // that empty-live-scan case is exactly the race this fallback exists to catch, so it must not
        // be treated the same as "nobody ever had a notebook".
        List<IHistoryRecordable> victimHosts = FindAllCarriedNotebookRecords(sp).ToList();
        bool victimHasSnapshot = _lastKnownCarriedDocIds.TryGetValue(sp.PlayerUID, out var lastKnownDocIds)
            && lastKnownDocIds.Count > 0;
        if (victimHosts.Count == 0 && killerHosts.Count == 0 && !victimHasSnapshot) return;

        // Captured once, at the actual moment of death, so a queued fallback entry (flushed later)
        // sorts into its true chronological position rather than wherever the flush happens to land.
        double deathTimestamp = sapi.World.Calendar.TotalDays;

        // A PvP death names the killer with a weapon-aware verb; any other death reconstructs a
        // full narrated sentence (mob-death flavor pool, else vanilla environmental deathmsg). Every
        // branch produces a self-contained sentence that already names the victim, so the entry
        // leaves ActorName empty and puts the whole sentence in Detail — the History row prepends
        // "ActorName — " otherwise, which would print the player's name twice (see the BossKill
        // path, which is empty-ActorName for the same reason).
        //
        // For PvP, each notebook reads from ITS OWN owner's perspective, so the two logs diverge:
        //   • the victim's Death log is victim-first & passive:  "Junkmuffin was slain by Raptor."
        //   • the killer's PvpKill log is killer-first & active: "Raptor slew Junkmuffin."
        // Both come from the same resolved verb key (active verb vs. its passive participle).
        string deathMsg;   // victim-first — the victim's Death entry
        string? killMsg = null; // killer-first — the killer's PvpKill entry (PvP only)
        if (killer is not null)
        {
            // The generic-pool cursor reads the killer's existing PvpKill count; use their first
            // notebook as the reference. (Different carried notebooks may hold different counts, but
            // the verb is cosmetic flavor — one reference is fine, and all get the same final line.)
            string verbKey = ResolvePvpVerbKey((Vintagestory.API.Common.EntityPlayer)dmg!.GetCauseEntity(), dmg, killerHosts.FirstOrDefault());
            deathMsg = Lang.Get("scribe:scribe-pvp-death-message", sp.PlayerName, VerbParticiple(verbKey), killer.PlayerName);
            killMsg  = Lang.Get("scribe:scribe-pvp-kill-message",  killer.PlayerName, VerbActive(verbKey), sp.PlayerName);
        }
        else
        {
            deathMsg = BuildDeathMessage(sp.PlayerName, dmg);
        }

        // Record the Death on EVERY notebook the victim carries, not just the first found.
        foreach (var nbHost in victimHosts)
        {
            nbHost.History.TryAddEntry(new Scribe.Core.HistoryEntry
            {
                Kind            = Scribe.Core.HistoryEventKind.Death,
                Detail          = deathMsg,
                InGameDate      = NotebookHost.FormatDate(sapi),
                InGameTimestamp = deathTimestamp,
            });
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
                pendingHistoryStore?.Enqueue(docId, new Scribe.Core.HistoryEntry
                {
                    Kind            = Scribe.Core.HistoryEventKind.Death,
                    Detail          = deathMsg,
                    InGameDate      = NotebookHost.FormatDate(sapi),
                    InGameTimestamp = deathTimestamp,
                });
            }
        }

        // ── PvP kill — record the killer-first message on every notebook the killer carries ──
        // Victim-only fallback: nothing evicts the KILLER's inventory during their own kill (they are
        // not the one dying), so there is no race to guard here — this stays live-scan-only.
        if (killer is not null && killMsg is not null)
        {
            foreach (var killerHost in killerHosts)
            {
                killerHost.History.TryAddEntry(new Scribe.Core.HistoryEntry
                {
                    Kind            = Scribe.Core.HistoryEventKind.PvpKill,
                    Detail          = killMsg,
                    InGameDate      = NotebookHost.FormatDate(sapi),
                    InGameTimestamp = deathTimestamp,
                });
                killerHost.FlushHistory();
            }
        }
    }

    /// <summary>
    /// Resolves the lang KEY of a weapon-aware PvP kill verb by a 3-tier fallback, best signal first
    /// (see design.md): (1) the killer's held-item <c>Collectible.Tool</c> (<c>EnumTool</c>) →
    /// <c>scribe:scribe-pvp-verb-tool-&lt;tool&gt;</c>; (2) else <c>dmg.Type</c> →
    /// <c>scribe:scribe-pvp-verb-damage-&lt;type&gt;</c>; (3) else the generic no-repeat pool
    /// <c>scribe:scribe-pvp-verb-generic-N</c>, indexed off the killer notebook's existing PvpKill
    /// count so successive kills rotate without a <c>Random</c>. Tier 1 is the only accurate signal
    /// for vanilla melee (vanilla hardcodes melee <c>dmg.Type</c> to Blunt).
    ///
    /// Returns the KEY (not the resolved string) so the caller can look up BOTH the active verb
    /// (killer-first kill message) and its passive participle (victim-first death message) via
    /// <see cref="VerbActive"/> / <see cref="VerbParticiple"/>.
    /// </summary>
    private static string ResolvePvpVerbKey(
        Vintagestory.API.Common.EntityPlayer killerEntity,
        Vintagestory.API.Common.DamageSource dmg,
        IHistoryRecordable? killerHost)
    {
        // Tier 1 — weapon category from the killer's currently-held item.
        var tool = killerEntity.RightHandItemSlot?.Itemstack?.Collectible?.Tool;
        if (tool is not null)
        {
            string toolKey = $"scribe:scribe-pvp-verb-tool-{tool.ToString()!.ToLowerInvariant()}";
            if (TryLang(toolKey, out _)) return toolKey;
        }

        // Tier 2 — damage type (catches modded weapons that set a type but no tool).
        string dmgKey = $"scribe:scribe-pvp-verb-damage-{dmg.Type.ToString().ToLowerInvariant()}";
        if (TryLang(dmgKey, out _)) return dmgKey;

        // Tier 3 — generic pool, size discovered by probing upward from -0. Rotate by the killer's
        // existing PvpKill count so the next kill picks a different verb (no immediate repeat).
        int poolSize = 0;
        while (TryLang($"scribe:scribe-pvp-verb-generic-{poolSize}", out _)) poolSize++;
        if (poolSize == 0) return "scribe:scribe-pvp-verb-damage-bluntattack"; // defensive; keys ship with the mod
        int priorKills = killerHost?.History.Entries.Count(e => e.Kind == Scribe.Core.HistoryEventKind.PvpKill) ?? 0;
        return $"scribe:scribe-pvp-verb-generic-{priorKills % poolSize}";
    }

    /// <summary>The active past-tense verb for the killer-first kill message ("Raptor <b>slashed</b>
    /// Junkmuffin") — just the resolved key's own value.</summary>
    private static string VerbActive(string verbKey) => Lang.Get(verbKey);

    /// <summary>The passive participle for the victim-first death message ("Junkmuffin was
    /// <b>slain</b> by Raptor"). Uses a <c>&lt;key&gt;-participle</c> override when one exists, else
    /// falls back to the active verb (correct for "shot"/"slashed"/"bashed"/… which are identical in
    /// both forms; only "slew" → "slain" ships an override).</summary>
    private static string VerbParticiple(string verbKey)
        => TryLang($"{verbKey}-participle", out string participle) ? participle : Lang.Get(verbKey);

    /// <summary>
    /// <see cref="Lang.Get"/> with the key-echo miss check used throughout this file: returns false
    /// (and echoes the key) when no translation exists, so callers can fall through to another tier.
    /// </summary>
    private static bool TryLang(string key, out string value)
    {
        value = Lang.Get(key);
        return value != key;
    }

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
        string strength = "";
        string date = "";
        if (stormSys is not null)
        {
            bool nowActive = stormSys.StormData.nowStormActive;
            rising = nowActive && !_stormWasActive;
            _stormWasActive = nowActive;

            if (rising)
            {
                // Localize the strength word (Light/Medium/Heavy) via a per-value key; the raw enum
                // name is a developer token, not player prose. An unknown future value echoes its own
                // name as a fallback.
                string strengthName = stormSys.StormData.nextStormStrength.ToString();
                string strengthKey  = "scribe:storm-strength-" + strengthName.ToLowerInvariant();
                strength = Lang.Get(strengthKey);
                if (strength == strengthKey) strength = strengthName; // key-echo miss → raw name
                date = NotebookHost.FormatDate(sapi);
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
                        Detail          = strength,
                        InGameDate      = date,
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

    /// <summary>
    /// Builds the Detail sentence for a non-PvP death. When a creature dealt the killing blow
    /// (resolved via <c>GetCauseEntity()</c>, which covers melee), we pick a flavored line from our
    /// own <c>scribe:scribe-mob-death-N</c> pool and name the creature with the entity's own
    /// <c>GetPrefixAndCreatureName()</c> — so it is always the correct variant ("a nightmare
    /// drifter", "a brown bear"), unlike vanilla's <c>deathmsg-drifter-*</c> keys which only exist
    /// for the surface drifter. Environmental deaths (fall/fire/hunger/…) keep vanilla's
    /// <c>deathmsg-{cause}-{N}</c> reconstruction. The returned sentence always names the victim, so
    /// callers store it in Detail with an empty ActorName.
    /// </summary>
    private string BuildDeathMessage(string playerName, Vintagestory.API.Common.DamageSource? dmg)
    {
        if (dmg is null) return Lang.Get("scribe:death-generic", playerName);

        // Resolve the attacker via GetCauseEntity() (CauseEntity ?? SourceEntity): SourceEntity is
        // null for melee, so reading it alone drops melee attackers into the "died." fallback.
        var causeEntity = dmg.GetCauseEntity();
        if (causeEntity is not null)
        {
            // Creature kill — flavored line from our pool + the creature's own display name, so every
            // variant reads correctly (vanilla ships bespoke deathmsg keys for almost no creatures).
            string creature = causeEntity.GetPrefixAndCreatureName();
            int poolSize = GetMobDeathPoolSize();
            if (poolSize > 0)
            {
                int idx = sapi!.World.Rand.Next(poolSize);
                return Lang.Get($"scribe:scribe-mob-death-{idx}", playerName, creature);
            }
            return Lang.Get("scribe:death-slain-by", playerName, creature); // defensive; keys ship with the mod
        }

        // Environmental death — rebuild the vanilla deathmsg-{cause}-{N} string the way vanilla does.
        string cause = dmg.Source.ToString().ToLowerInvariant().Replace("_", "-"); // e.g. "fall", "fire"
        // Try variant counts 1..4 and pick from available. Use a hash of the player name to
        // deterministically pick the same variant as vanilla's random (close enough for a chronicle).
        int hash = Math.Abs(playerName.GetHashCode());
        for (int maxN = 4; maxN >= 1; maxN--)
        {
            string key = $"deathmsg-{cause}-{(hash % maxN) + 1}";
            string msg = Vintagestory.API.Config.Lang.Get(key, playerName);
            if (msg != key) return msg; // Lang.Get returns the key unchanged on a miss
        }
        return Lang.Get("scribe:death-generic", playerName);
    }

}
