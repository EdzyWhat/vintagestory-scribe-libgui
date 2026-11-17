using Scribe.Core;
using static Scribe.Core.ScribeRowMenuAction;

namespace Scribe.Core.Tests;

// Tests for which actions a document row's context menu offers (task-row-context-menu spec).
public class ScribeRowMenuEntriesTests
{
    [Fact]
    public void Read_Editable_OffersEditPinAndDelete()
    {
        Assert.Equal(new[] { Edit, Pin, Delete },
            ScribeRowMenuEntries.For(ScribeRowMenuView.Read, readOnly: false, pinLive: true, isPinned: false));
    }

    [Fact]
    public void Read_Editable_PinnedRowOffersUnpin()
    {
        Assert.Equal(new[] { Edit, Unpin, Delete },
            ScribeRowMenuEntries.For(ScribeRowMenuView.Read, readOnly: false, pinLive: true, isPinned: true));
    }

    [Fact]
    public void Read_ReadOnlyTablet_OffersOnlyPin()
    {
        Assert.Equal(new[] { Pin },
            ScribeRowMenuEntries.For(ScribeRowMenuView.Read, readOnly: true, pinLive: true, isPinned: false));
        Assert.Equal(new[] { Unpin },
            ScribeRowMenuEntries.For(ScribeRowMenuView.Read, readOnly: true, pinLive: true, isPinned: true));
    }

    [Fact]
    public void Read_ReadOnlyWithoutLivePin_OffersNothing()
    {
        Assert.Empty(ScribeRowMenuEntries.For(ScribeRowMenuView.Read, readOnly: true, pinLive: false, isPinned: false));
    }

    [Fact]
    public void Read_EditableWithoutLivePin_OmitsPin()
    {
        Assert.Equal(new[] { Edit, Delete },
            ScribeRowMenuEntries.For(ScribeRowMenuView.Read, readOnly: false, pinLive: false, isPinned: false));
    }

    [Theory]
    [InlineData(false, new[] { Pin, Delete })]
    [InlineData(true, new[] { Unpin, Delete })]
    public void Editor_OffersPinOrUnpinAndDelete_NeverEdit(bool isPinned, ScribeRowMenuAction[] expected)
    {
        Assert.Equal(expected,
            ScribeRowMenuEntries.For(ScribeRowMenuView.Editor, readOnly: false, pinLive: true, isPinned: isPinned));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PinTab_OffersUnpinAndDelete(bool isPinned)
    {
        Assert.Equal(new[] { Unpin, Delete },
            ScribeRowMenuEntries.For(ScribeRowMenuView.PinTab, readOnly: false, pinLive: true, isPinned: isPinned));
    }
}
