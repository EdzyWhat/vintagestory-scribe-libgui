using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Scribe;

/// <summary>
/// Ambient particle indicator for an unseen assignment: a sparse field of warm amber motes, with an occasional full-hue "rainbow"
/// accent, floating above an Inbox-capable block entity while the local player has at least one unseen
/// received assignment. Player-specific and client-side only — <see cref="IWorldAccessor.SpawnParticles"/>
/// is called with no <c>dualCallByPlayer</c>, so nothing is broadcast to (or influenced by) any other
/// client. Manually triggered (not <c>Block.ParticleProperties</c>, which is engine-driven,
/// unconditional, and visible to every nearby player regardless of who has an unseen assignment).
///
/// <para>Uses <see cref="AdvancedParticleProperties"/> (not <see cref="SimpleParticleProperties"/>)
/// specifically for its <c>HsvaColor</c> field — a <c>NatFloat[4]</c> (H/S/V/A) resampled per spawned
/// particle, giving a "mean ± variance" color spread. The full-range-hue
/// accent can't be expressed as a single continuous distribution (no bimodal <c>NatFloat</c>
/// distribution fits "mostly narrow amber, occasionally anywhere on the wheel"), so each tick's total
/// mote count is split into two separate <see cref="IWorldAccessor.SpawnParticles(IParticlePropertiesProvider,IPlayer)"/>
/// calls sharing every other property — one amber-band batch, one full-hue accent batch.</para>
/// </summary>
public sealed class ScribeAssignmentParticleEmitter
{
    /// <summary>How close (blocks) the player must be to a block entity for its indicator to consider
    /// spawning. Author-tunable via <see cref="ScribeVisualTuning"/>.</summary>
    public readonly double DetectionRadius;

    // Base (amber/gold) HSV band, 0-255 scale matching VS's own range.
    private const float BaseHue = 32f, BaseHueVar = 8f;
    private const float BaseSat = 200f, BaseSatVar = 25f;
    private const float BaseVal = 250f, BaseValVar = 20f;
    private const float BaseAlpha = 180f, BaseAlphaVar = 40f;

    /// <summary>Fraction of each tick's spawned motes that get a randomized full-range hue instead of
    /// the base amber band. Author-tunable via <see cref="ScribeVisualTuning"/>.</summary>
    private readonly float RainbowRatio;

    /// <summary>Scales the base 1-3-per-tick mote count. Author-tunable via
    /// <see cref="ScribeVisualTuning"/>.</summary>
    private readonly float CountMultiplier;

    /// <summary>One-time multiplier applied on the tick a player's proximity+unseen-assignment trigger
    /// first turns true this session (see <see cref="BlockEntityScribeWritingStation"/>'s tick
    /// listener), so the ambient field looks already-established the moment the player turns toward the
    /// block instead of visibly accruing over the first several ticks.
    /// Sized to roughly fill the steady-state population in one shot: steady-state count is
    /// approximately (mean per-tick spawn) × (mean lifetime ÷ tick interval) ≈ 2.6 × (2s ÷ 1.5s) ≈ 3.5×
    /// a single tick's spawn. Author-tunable via <see cref="ScribeVisualTuning"/>.</summary>
    public readonly float SeedBurstMultiplier;

    public ScribeAssignmentParticleEmitter(ScribeVisualTuning tuning)
    {
        DetectionRadius = tuning.DetectionRadius;
        RainbowRatio = tuning.RainbowRatio;
        CountMultiplier = tuning.CountMultiplier;
        SeedBurstMultiplier = tuning.SeedBurstMultiplier;
    }

    private const float LifeLengthAvg = 2f, LifeLengthVar = 0.5f;
    private const float SizeAvg = 0.12f, SizeVar = 0.04f;

    /// <summary>Slight NEGATIVE gravity so motes float upward rather than fall/drip.
    /// Together with the Y velocity (<see cref="VelocityYAvg"/>) it sets how far the field rises over
    /// <see cref="LifeLengthAvg"/>.</summary>
    private const float GravityEffect = -0.004f;

    /// <summary>Upward drift speed (mean, variance) — see <see cref="GravityEffect"/>'s remarks.</summary>
    private const float VelocityYAvg = 0.0067f, VelocityYVar = 0.0067f;

    private static readonly Random Rand = new();

    /// <summary>Spawns this tick's mote batch centered around <paramref name="pos"/>'s vertical midpoint
    /// (inside the block's own cell, so something blocking the top half of the block does not hide the
    /// whole field). Call only after confirming the trigger
    /// condition (an unseen assignment) and proximity — this method itself does no gating, so a
    /// caller-side gate (see <see cref="BlockEntityScribeWritingStation"/>'s tick listener) controls WHEN
    /// it fires. <paramref name="seedBurst"/> is set on the first tick after the trigger turns true,
    /// spawning a larger one-time batch so the field doesn't need several ticks to build up to its
    /// steady-state density.</summary>
    public void SpawnAt(ICoreClientAPI capi, BlockPos pos, bool seedBurst = false)
    {
        var minPos = new Vec3d(pos.X + 0.2, pos.Y + 0.35, pos.Z + 0.2);
        var maxPos = new Vec3d(pos.X + 0.8, pos.Y + 0.65, pos.Z + 0.8);
        SpawnBatch(capi, minPos, maxPos, seedBurst);
    }

    /// <summary>Same field, centered on an arbitrary world position rather than a block's own cell —
    /// used by the Task Notice proximity ping, whose found position may be a dropped
    /// <c>EntityItem</c>'s fractional coordinates rather than a block-aligned one.</summary>
    public void SpawnAt(ICoreClientAPI capi, Vec3d center, bool seedBurst = false)
    {
        var minPos = new Vec3d(center.X - 0.3, center.Y, center.Z - 0.3);
        var maxPos = new Vec3d(center.X + 0.3, center.Y + 0.3, center.Z + 0.3);
        SpawnBatch(capi, minPos, maxPos, seedBurst);
    }

    private void SpawnBatch(ICoreClientAPI capi, Vec3d minPos, Vec3d maxPos, bool seedBurst)
    {
        // 1-3 sparse motes per tick (low spawn quantity — sparse motes, not a fountain),
        // scaled by CountMultiplier and, on entry, SeedBurstMultiplier.
        float scale = CountMultiplier * (seedBurst ? SeedBurstMultiplier : 1f);
        int total = (int)MathF.Round((1 + Rand.Next(3)) * scale);
        int rainbowCount = (int)MathF.Round(total * RainbowRatio);
        int baseCount = total - rainbowCount;

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
                NatFloat.createUniform(VelocityYAvg, VelocityYVar),
                NatFloat.createUniform(0f, 0.01f),
            },
        };
        props.basePos.Set((minPos.X + maxPos.X) / 2, (minPos.Y + maxPos.Y) / 2, (minPos.Z + maxPos.Z) / 2);
        return props;
    }
}
