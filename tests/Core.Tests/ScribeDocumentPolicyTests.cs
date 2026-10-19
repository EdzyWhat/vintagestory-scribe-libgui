using Scribe.Core;

namespace Scribe.Core.Tests;

// Tests for the per-tier document cap applied at the host/editor mutation boundary.
// Each test maps to a WHEN/THEN scenario in the scribe-document-policy spec.
public class ScribeDocumentPolicyTests
{
    // --- Unlimited (the default every existing host reports) ---

    [Fact]
    public void Unlimited_PermitsAnyTaskCount()
    {
        var policy = ScribeDocumentPolicy.Unlimited;

        Assert.True(policy.CanAdd(0));
        Assert.True(policy.CanAdd(9));
        Assert.True(policy.CanAdd(1000));
    }

    [Fact]
    public void Unlimited_PermitsAnyPinCount()
    {
        var policy = ScribeDocumentPolicy.Unlimited;

        Assert.True(policy.CanPin(0));
        Assert.True(policy.CanPin(50));
    }

    [Fact]
    public void Unlimited_IsNotReadOnly()
    {
        Assert.False(ScribeDocumentPolicy.Unlimited.ReadOnly);
        Assert.Null(ScribeDocumentPolicy.Unlimited.MaxBlocks);
        Assert.Null(ScribeDocumentPolicy.Unlimited.MaxPins);
    }

    // --- Tablet preset: 15 blocks of ANY kind, 1 pin ---
    // CanAdd/CanHold receive the document's total BlockCount (blocks of any kind); the "…Tasks" in method names is a misnomer.

    [Fact]
    public void Tablet_CapsAtFifteenTasks()
    {
        var policy = ScribeDocumentPolicy.Tablet;

        Assert.True(policy.CanAdd(14));  // 14 present → a 15th is allowed
        Assert.False(policy.CanAdd(15)); // 15 present → a 16th is refused
        Assert.False(policy.CanAdd(16)); // over the cap (defensive) stays refused
    }

    [Fact]
    public void Tablet_CapsAtOnePin()
    {
        var policy = ScribeDocumentPolicy.Tablet;

        Assert.True(policy.CanPin(0));  // no pins yet → first pin allowed
        Assert.False(policy.CanPin(1)); // one pin present → a second is refused
    }

    [Fact]
    public void Tablet_PermitsAddingUpToTheCapFromEmpty()
    {
        var policy = ScribeDocumentPolicy.Tablet;

        for (int present = 0; present < 15; present++)
            Assert.True(policy.CanAdd(present));
        Assert.False(policy.CanAdd(15));
    }

    // --- Boundary/robustness ---

    [Fact]
    public void CanAdd_TreatsNegativeCountAsZero()
    {
        Assert.True(ScribeDocumentPolicy.Tablet.CanAdd(-5));
    }

    [Fact]
    public void ReadOnly_RefusesAddingAndPinningEvenWhenUncapped()
    {
        var policy = new ScribeDocumentPolicy { ReadOnly = true };

        Assert.False(policy.CanAdd(0));
        // ReadOnly gates pins too: a hard/fired tablet denies CanAdd AND CanPin.
        Assert.False(policy.CanPin(0));
    }

    // --- UneditableTablet preset: a hard or fired tablet denies all mutation ---

    [Fact]
    public void UneditableTablet_DeniesAddingRegardlessOfCount()
    {
        var policy = ScribeDocumentPolicy.UneditableTablet;

        Assert.False(policy.CanAdd(0));
        Assert.False(policy.CanAdd(5));
    }

    [Fact]
    public void UneditableTablet_DeniesPinningRegardlessOfCount()
    {
        var policy = ScribeDocumentPolicy.UneditableTablet;

        Assert.False(policy.CanPin(0));
        Assert.False(policy.CanPin(5));
    }

    [Fact]
    public void UneditableTablet_IsReadOnly()
    {
        Assert.True(ScribeDocumentPolicy.UneditableTablet.ReadOnly);
    }

    // --- CanHold: the copy/paste capacity check (fits N incoming blocks, not "room for one more") ---
    // CanHold is an inclusive <= against the cap (can the document hold N blocks in total), unlike CanAdd's strict <; also denies when read-only.

    [Fact]
    public void Unlimited_CanHoldAnyTaskCount()
    {
        var policy = ScribeDocumentPolicy.Unlimited;

        Assert.True(policy.CanHold(0));
        Assert.True(policy.CanHold(10));
        Assert.True(policy.CanHold(1000));
    }

    [Fact]
    public void Tablet_CanHoldUpToAndIncludingTheCap()
    {
        var policy = ScribeDocumentPolicy.Tablet;

        Assert.True(policy.CanHold(0));
        Assert.True(policy.CanHold(14));
        Assert.True(policy.CanHold(15));  // exactly the cap fits (inclusive, unlike CanAdd)
        Assert.False(policy.CanHold(16)); // one over the cap does not
    }

    [Fact]
    public void UneditableTablet_CanHoldNothing()
    {
        var policy = ScribeDocumentPolicy.UneditableTablet;

        Assert.False(policy.CanHold(0));
        Assert.False(policy.CanHold(5));
    }

    [Fact]
    public void ReadOnly_CanHoldNothingEvenWhenUncapped()
    {
        var policy = new ScribeDocumentPolicy { ReadOnly = true };

        Assert.False(policy.CanHold(0));
    }

    [Fact]
    public void CanHold_TreatsNegativeCountAsZero()
    {
        Assert.True(ScribeDocumentPolicy.Tablet.CanHold(-5));
    }

    // --- The cap lives at the boundary, NOT in ScribeDocument ---

    [Fact]
    public void ScribeDocument_AddTask_StaysUncappedRegardlessOfPolicy()
    {
        // The document model never consults a policy; enforcement is the host/editor's job.
        var doc = new ScribeDocument();
        for (int i = 0; i < 20; i++)
            Assert.True(doc.AddTask($"task {i}"));

        Assert.Equal(20, doc.Blocks.Count);
    }
}
