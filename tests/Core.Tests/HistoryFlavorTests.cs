using Scribe.Core;

namespace Scribe.Core.Tests;

public class HistoryFlavorTests
{
    [Fact]
    public void Slot_PositiveSeed_IsRemainder()
    {
        Assert.Equal(3, HistoryFlavor.Slot(13, 10));
        Assert.Equal(0, HistoryFlavor.Slot(20, 10));
    }

    [Fact]
    public void Slot_NegativeSeed_UsesAbsoluteValue()
    {
        Assert.Equal(3, HistoryFlavor.Slot(-13, 10));
        Assert.Equal(1, HistoryFlavor.Slot(-1, 2));
    }

    [Fact]
    public void Slot_ZeroSeed_IsZero()
        => Assert.Equal(0, HistoryFlavor.Slot(0, 7));

    [Fact]
    public void Slot_OversizedSeed_StaysInRange()
    {
        Assert.Equal(1, HistoryFlavor.Slot(int.MaxValue, 2)); // 2147483647 % 2
        Assert.InRange(HistoryFlavor.Slot(int.MaxValue, 5), 0, 4);
    }

    [Fact]
    public void Slot_IntMinValue_DoesNotThrowAndStaysInRange()
    {
        int slot = HistoryFlavor.Slot(int.MinValue, 10);
        Assert.InRange(slot, 0, 9);
    }

    [Fact]
    public void Slot_EmptyPool_ReturnsZero()
    {
        Assert.Equal(0, HistoryFlavor.Slot(42, 0));
        Assert.Equal(0, HistoryFlavor.Slot(42, -3));
    }

    [Fact]
    public void Slot_TwoKeyPool_NeverOutOfRange()
    {
        for (int seed = -100; seed <= 100; seed++)
            Assert.InRange(HistoryFlavor.Slot(seed, 2), 0, 1);

        Assert.InRange(HistoryFlavor.Slot(int.MaxValue, 2), 0, 1);
        Assert.InRange(HistoryFlavor.Slot(int.MinValue, 2), 0, 1);
    }
}

public class HistoryPvpVerbTests
{
    [Fact]
    public void ResolveKey_OmitsEnglishBowVerb_FallsThroughToLocaleGeneric()
    {
        // Viewer locale has no tool-bow and no damage-piercing — only generic-0 and generic-1.
        // English's "pincushioned" (scribe-pvp-verb-tool-bow) must not count as a hit.
        bool Has(string key) =>
            key is "scribe:scribe-pvp-verb-generic-0" or "scribe:scribe-pvp-verb-generic-1";

        string resolved = HistoryPvpVerb.ResolveKey("bow", "piercing", seed: 99, genericPoolSize: 2, Has);

        Assert.Equal("scribe:scribe-pvp-verb-generic-1", resolved); // 99 % 2 = 1
        Assert.DoesNotContain("tool-bow", resolved);
        Assert.DoesNotContain("pincushioned", resolved);
    }

    [Fact]
    public void ResolveKey_UsesToolWhenLocaleHasIt()
    {
        bool Has(string key) => key == "scribe:scribe-pvp-verb-tool-sword";
        string resolved = HistoryPvpVerb.ResolveKey("sword", "slashing", seed: 0, genericPoolSize: 2, Has);
        Assert.Equal("scribe:scribe-pvp-verb-tool-sword", resolved);
    }

    [Fact]
    public void ResolveKey_EmptyGenericPool_FallsBackToBluntAttack()
    {
        bool Has(string _) => false;
        string resolved = HistoryPvpVerb.ResolveKey("bow", "piercing", seed: 3, genericPoolSize: 0, Has);
        Assert.Equal(HistoryPvpVerb.FallbackKey, resolved);
    }
}
