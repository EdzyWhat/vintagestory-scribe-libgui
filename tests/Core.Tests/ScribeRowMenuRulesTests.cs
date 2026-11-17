using Scribe.Core;

namespace Core.Tests;

public class ScribeRowMenuRulesTests
{
    private static ScribeRowMenuItemKey Key(string label, bool? isChecked = null, bool close = true,
        bool sep = false, bool refreshable = true) => new(label, isChecked, close, sep, refreshable);

    [Fact]
    public void ToggleEntry_DoesNotCloseTheMenu_ButAnActionEntryDoes()
    {
        Assert.False(ScribeRowMenuRules.ClosesOnChoose(closeOnChoose: false, isChecked: true));
        Assert.False(ScribeRowMenuRules.ClosesOnChoose(closeOnChoose: true, isChecked: false));
        Assert.True(ScribeRowMenuRules.ClosesOnChoose(closeOnChoose: true, isChecked: null));
        Assert.False(ScribeRowMenuRules.ClosesOnChoose(closeOnChoose: false, isChecked: null));
    }

    [Fact]
    public void Refresh_IsNeeded_WhenALabelCheckedStateCloseFlagOrSeparatorChanges()
    {
        var open = new[] { Key("a"), Key("b", isChecked: false) };
        Assert.False(ScribeRowMenuRules.RefreshableEntriesChanged(open, new[] { Key("a"), Key("b", isChecked: false) }));
        Assert.True(ScribeRowMenuRules.RefreshableEntriesChanged(open, new[] { Key("a2"), Key("b", isChecked: false) }));
        Assert.True(ScribeRowMenuRules.RefreshableEntriesChanged(open, new[] { Key("a"), Key("b", isChecked: true) }));
        Assert.True(ScribeRowMenuRules.RefreshableEntriesChanged(open, new[] { Key("a", close: false), Key("b", isChecked: false) }));
        Assert.True(ScribeRowMenuRules.RefreshableEntriesChanged(open, new[] { Key("a", sep: true), Key("b", isChecked: false) }));
        Assert.True(ScribeRowMenuRules.RefreshableEntriesChanged(open, new[] { Key("a") }));
    }

    [Fact]
    public void Refresh_IgnoresNonRefreshableEntries()
    {
        var open = new[] { Key("a"), Key("pin", refreshable: false) };
        var fresh = new[] { Key("a"), Key("unpin", refreshable: false) };
        Assert.False(ScribeRowMenuRules.RefreshableEntriesChanged(open, fresh));
    }

    [Fact]
    public void ViewChange_ClosesTheMenu_OnlyWhenTheViewDiffers()
    {
        Assert.False(ScribeRowMenuRules.ClosesOnViewChange(ScribeRowMenuView.Read, ScribeRowMenuView.Read));
        Assert.True(ScribeRowMenuRules.ClosesOnViewChange(ScribeRowMenuView.Read, ScribeRowMenuView.Editor));
        Assert.True(ScribeRowMenuRules.ClosesOnViewChange(ScribeRowMenuView.Editor, ScribeRowMenuView.PinTab));
    }

    [Theory]
    [InlineData(true, false, ScribeRowMenuView.Read, false, false)]
    [InlineData(false, false, ScribeRowMenuView.Read, false, true)]   // row vanished
    [InlineData(true, true, ScribeRowMenuView.Read, false, true)]     // scrolled out
    [InlineData(true, false, ScribeRowMenuView.Read, true, true)]     // lock taken by another player
    [InlineData(true, false, ScribeRowMenuView.Editor, true, false)]  // the editor holder is the lock holder
    public void Picker_Closes_OnVanishScrollOrLockedRead(bool mounted, bool scrolled, ScribeRowMenuView view, bool locked, bool expected) =>
        Assert.Equal(expected, ScribeRowMenuRules.PickerShouldClose(mounted, scrolled, view, locked));
}
