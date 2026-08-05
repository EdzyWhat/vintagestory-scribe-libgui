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

    // --- Tablet preset: 10 task blocks, 1 pin ---

    [Fact]
    public void Tablet_CapsAtTenTasks()
    {
        var policy = ScribeDocumentPolicy.Tablet;

        Assert.True(policy.CanAdd(9));   // 9 present → a 10th is allowed
        Assert.False(policy.CanAdd(10)); // 10 present → an 11th is refused
        Assert.False(policy.CanAdd(11)); // over the cap (defensive) stays refused
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

        for (int present = 0; present < 10; present++)
            Assert.True(policy.CanAdd(present));
        Assert.False(policy.CanAdd(10));
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
        // ReadOnly now gates pins too (tablet-firing: a hard/fired tablet denies CanAdd AND CanPin).
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

    // --- The cap lives at the boundary, NOT in ScribeDocument ---

    [Fact]
    public void ScribeDocument_AddTask_StaysUncappedRegardlessOfPolicy()
    {
        // The document model is tier-agnostic: it never consults a policy. Adding well past the
        // Tablet cap succeeds at the model layer; enforcement is the host/editor's job.
        var doc = new ScribeDocument();
        for (int i = 0; i < 20; i++)
            Assert.True(doc.AddTask($"task {i}"));

        Assert.Equal(20, doc.Blocks.Count);
    }
}
