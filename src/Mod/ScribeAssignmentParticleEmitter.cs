using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Scribe;

/// <summary>
/// Ambient particle indicator for an unseen assignment: sparse warm amber motes with an occasional full-hue
/// accent, floating above an Inbox-capable block entity while the local player has an unseen received
/// assignment. Client-side and player-specific: <see cref="IWorldAccessor.SpawnParticles"/> is called with no
/// <c>dualCallByPlayer</c>, so nothing is broadcast. Manually triggered because <c>Block.ParticleProperties</c>
/// is unconditional and visible to every nearby player.
///
/// <para>Uses <see cref="AdvancedParticleProperties"/> for its per-particle <c>HsvaColor</c>. The full-hue
/// accent can't be a single <c>NatFloat</c> distribution, so each tick's mote count is split into two
/// <see cref="IWorldAccessor.SpawnParticles(IParticlePropertiesProvider,IPlayer)"/> calls sharing every other
/// property: an amber-band batch and a full-hue batch.</para>
/// </summary>
public sealed class ScribeAssignmentParticleEmitter
{
    /// <summary>How close (blocks) the player must be for the indicator to consider spawning.
    /// Author-tunable via <see cref="ScribeVisualTuning"/>.</summary>
    public readonly double DetectionRadius;

    // Base (amber/gold) HSV band, 0-255 scale matching VS.
    private const float BaseHue = 32f, BaseHueVar = 8f;
    private const float BaseSat = 200f, BaseSatVar = 25f;
    private const float BaseVal = 250f, BaseValVar = 20f;
    private const float BaseAlpha = 180f, BaseAlphaVar = 40f;

    /// <summary>Fraction of motes given a random full-range hue instead of amber. Author-tunable via <see cref="ScribeVisualTuning"/>.</summary>
    private readonly float RainbowRatio;

    /// <summary>Scales the base 1-3-per-tick mote count. Author-tunable via <see cref="ScribeVisualTuning"/>.</summary>
    private readonly float CountMultiplier;

    /// <summary>One-time multiplier on the tick the proximity+unseen trigger first turns true (see
    /// <see cref="BlockEntityScribeWritingStation"/>'s tick listener), so the field looks established
    /// immediately. Roughly fills the steady-state population: ~2.6 x (2s lifetime / 1.5s tick) ~ 3.5x one
    /// tick's spawn. Author-tunable via <see cref="ScribeVisualTuning"/>.</summary>
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

    /// <summary>Slight negative gravity so motes float upward; with <see cref="VelocityYAvg"/> it sets how
    /// far the field rises over <see cref="LifeLengthAvg"/>.</summary>
    private const float GravityEffect = -0.004f;

    /// <summary>Upward drift speed (mean, variance); see <see cref="GravityEffect"/>.</summary>
    private const float VelocityYAvg = 0.0067f, VelocityYVar = 0.0067f;

    private static readonly Random Rand = new();

    /// <summary>Spawns this tick's mote batch around <paramref name="pos"/>'s vertical midpoint (inside the
    /// block's own cell, so a block covering the top half doesn't hide the field). Does no gating; the caller
    /// (see <see cref="BlockEntityScribeWritingStation"/>'s tick listener) decides when it fires.
    /// <paramref name="seedBurst"/> spawns a larger one-time batch on the first tick after the trigger turns true.</summary>
    public void SpawnAt(ICoreClientAPI capi, BlockPos pos, bool seedBurst = false)
    {
        var minPos = new Vec3d(pos.X + 0.2, pos.Y + 0.35, pos.Z + 0.2);
        var maxPos = new Vec3d(pos.X + 0.8, pos.Y + 0.65, pos.Z + 0.8);
        SpawnBatch(capi, minPos, maxPos, seedBurst);
    }

    /// <summary>Same field centered on an arbitrary world position, for the Task Notice proximity ping
    /// (a dropped <c>EntityItem</c> has fractional coordinates).</summary>
    public void SpawnAt(ICoreClientAPI capi, Vec3d center, bool seedBurst = false)
    {
        var minPos = new Vec3d(center.X - 0.3, center.Y, center.Z - 0.3);
        var maxPos = new Vec3d(center.X + 0.3, center.Y + 0.3, center.Z + 0.3);
        SpawnBatch(capi, minPos, maxPos, seedBurst);
    }

    private void SpawnBatch(ICoreClientAPI capi, Vec3d minPos, Vec3d maxPos, bool seedBurst)
    {
        // 1-3 sparse motes per tick, scaled by CountMultiplier and, on entry, SeedBurstMultiplier.
        float scale = CountMultiplier * (seedBurst ? SeedBurstMultiplier : 1f);
        int total = (int)MathF.Round((1 + Rand.Next(3)) * scale);
        int rainbowCount = (int)MathF.Round(total * RainbowRatio);
        int baseCount = total - rainbowCount;

        if (baseCount > 0)
            capi.World.SpawnParticles(BuildBatch(minPos, maxPos, baseCount,
                NatFloat.createUniform(BaseHue, BaseHueVar)));
        if (rainbowCount > 0)
            capi.World.SpawnParticles(BuildBatch(minPos, maxPos, rainbowCount,
                NatFloat.createUniform(128f, 128f))); // full 0-255 hue range
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
