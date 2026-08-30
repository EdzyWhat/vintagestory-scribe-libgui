using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Scribe;

/// <summary>
/// Ambient particle indicator for an unseen assignment (add-assignment-and-quest-support §8.4,
/// design.md Decision 9): a sparse field of warm amber motes, with an occasional full-hue "rainbow"
/// accent, floating above an Inbox-capable block entity while the local player has at least one unseen
/// received assignment. Player-specific and client-side only — <see cref="IWorldAccessor.SpawnParticles"/>
/// is called with no <c>dualCallByPlayer</c>, so nothing is broadcast to (or influenced by) any other
/// client. Manually triggered (not <c>Block.ParticleProperties</c>, which is engine-driven,
/// unconditional, and visible to every nearby player regardless of who has an unseen assignment).
///
/// <para>Uses <see cref="AdvancedParticleProperties"/> (not <see cref="SimpleParticleProperties"/>)
/// specifically for its <c>HsvaColor</c> field — a <c>NatFloat[4]</c> (H/S/V/A) resampled per spawned
/// particle, giving the "mean ± variance" color spread design.md calls for. The 1-in-5 full-range-hue
/// accent can't be expressed as a single continuous distribution (no bimodal <c>NatFloat</c>
/// distribution fits "mostly narrow amber, occasionally anywhere on the wheel"), so each tick's total
/// mote count is split into two separate <see cref="IWorldAccessor.SpawnParticles(IParticlePropertiesProvider,IPlayer)"/>
/// calls sharing every other property — one amber-band batch, one full-hue accent batch.</para>
/// </summary>
internal static class ScribeAssignmentParticleEmitter
{
    /// <summary>How close (blocks) the player must be to a block entity for its indicator to consider
    /// spawning — playtest-tunable, not final.</summary>
    public const double DetectionRadius = 6.0;

    // Base (amber/gold) HSV band, 0-255 scale matching VS's own range (design.md Decision 9).
    private const float BaseHue = 32f, BaseHueVar = 8f;
    private const float BaseSat = 200f, BaseSatVar = 25f;
    private const float BaseVal = 250f, BaseValVar = 20f;
    private const float BaseAlpha = 180f, BaseAlphaVar = 40f;

    /// <summary>Fraction of each tick's spawned motes that get a randomized full-range hue instead of
    /// the base amber band (starting ratio ~1-in-5, tunable).</summary>
    private const float RainbowRatio = 0.2f;

    private const float LifeLengthAvg = 2f, LifeLengthVar = 0.5f;
    private const float SizeAvg = 0.12f, SizeVar = 0.04f;

    /// <summary>Slight NEGATIVE gravity so motes float upward rather than fall/drip (design.md Decision 9).</summary>
    private const float GravityEffect = -0.006f;

    private static readonly Random Rand = new();

    /// <summary>Spawns this tick's mote batch centered just above <paramref name="pos"/>'s reading
    /// surface. Call only after confirming the trigger condition (an unseen assignment) and proximity —
    /// this method itself does no gating, so a caller-side gate (see
    /// <see cref="BlockEntityScribeWritingStation"/>'s tick listener) controls WHEN it fires.</summary>
    public static void SpawnAt(ICoreClientAPI capi, BlockPos pos)
    {
        // 1-3 sparse motes per tick (design.md: "low spawn quantity — sparse motes, not a fountain").
        int total = 1 + Rand.Next(3);
        int rainbowCount = (int)MathF.Round(total * RainbowRatio);
        int baseCount = total - rainbowCount;

        var minPos = new Vec3d(pos.X + 0.2, pos.Y + 0.85, pos.Z + 0.2);
        var maxPos = new Vec3d(pos.X + 0.8, pos.Y + 1.25, pos.Z + 0.8);

        if (baseCount > 0)
            capi.World.SpawnParticles(BuildBatch(minPos, maxPos, baseCount,
                NatFloat.createUniform(BaseHue, BaseHueVar)));
        if (rainbowCount > 0)
            capi.World.SpawnParticles(BuildBatch(minPos, maxPos, rainbowCount,
                NatFloat.createUniform(128f, 128f))); // full 0-255 range, uniform across the whole wheel
    }

    private static AdvancedParticleProperties BuildBatch(Vec3d minPos, Vec3d maxPos, int quantity, NatFloat hue)
    {
        var props = new AdvancedParticleProperties
        {
            HsvaColor = new[]
            {
                hue,
                NatFloat.createUniform(BaseSat, BaseSatVar),
                NatFloat.createUniform(BaseVal, BaseValVar),
                NatFloat.createUniform(BaseAlpha, BaseAlphaVar),
            },
            GravityEffect = NatFloat.createUniform(GravityEffect, 0f),
            LifeLength = NatFloat.createUniform(LifeLengthAvg, LifeLengthVar),
            Quantity = NatFloat.createUniform(quantity, 0f),
            Size = NatFloat.createUniform(SizeAvg, SizeVar),
            ParticleModel = EnumParticleModel.Quad,
            TerrainCollision = false,
            DieInAir = false,
            SelfPropelled = false,
            PosOffset = new[]
            {
                NatFloat.createUniform(0f, (float)((maxPos.X - minPos.X) / 2)),
                NatFloat.createUniform(0f, (float)((maxPos.Y - minPos.Y) / 2)),
                NatFloat.createUniform(0f, (float)((maxPos.Z - minPos.Z) / 2)),
            },
            Velocity = new[]
            {
                NatFloat.createUniform(0f, 0.01f),
                NatFloat.createUniform(0.01f, 0.01f),
                NatFloat.createUniform(0f, 0.01f),
            },
        };
        props.basePos.Set((minPos.X + maxPos.X) / 2, (minPos.Y + maxPos.Y) / 2, (minPos.Z + maxPos.Z) / 2);
        return props;
    }
}
