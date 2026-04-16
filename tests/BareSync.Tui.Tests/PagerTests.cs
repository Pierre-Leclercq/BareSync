using Xunit;

namespace BareSync.Tui.Tests;

public sealed class PagerTests
{
    [Fact]
    public void Pager_ComputesPageCounts()
    {
        var items = Enumerable.Range(0, 45).ToList();
        var pager = new Pager<int>(items, 20);

        Assert.Equal(45, pager.TotalItems);
        Assert.Equal(3, pager.TotalPages);
        Assert.Equal(1, pager.CurrentPage);
    }

    [Fact]
    public void Pager_NavigatesPages()
    {
        var items = Enumerable.Range(0, 5).ToList();
        var pager = new Pager<int>(items, 2);

        Assert.True(pager.Next());
        Assert.Equal(2, pager.CurrentPage);
        Assert.True(pager.Next());
        Assert.Equal(3, pager.CurrentPage);
        Assert.False(pager.Next());
        Assert.True(pager.Previous());
        Assert.Equal(2, pager.CurrentPage);
        Assert.False(pager.GoTo(0));
        Assert.False(pager.GoTo(4));
        Assert.True(pager.GoTo(1));
    }

    [Fact]
    public void Pager_MapsSelectionToIndex()
    {
        var items = Enumerable.Range(0, 25).ToList();
        var pager = new Pager<int>(items, 10);

        Assert.True(pager.GoTo(2));
        Assert.Equal(10, pager.MapSelectionToIndex(1));
        Assert.Equal(19, pager.MapSelectionToIndex(10));
        Assert.Null(pager.MapSelectionToIndex(11));

        Assert.True(pager.GoTo(3));
        Assert.Equal(20, pager.MapSelectionToIndex(1));
        Assert.Equal(24, pager.MapSelectionToIndex(5));
        Assert.Null(pager.MapSelectionToIndex(6));
    }

    [Fact]
    public void Pager_HandlesEmptyItems()
    {
        var items = Array.Empty<int>();
        var pager = new Pager<int>(items, 10);

        Assert.Equal(1, pager.TotalPages);
        Assert.Empty(pager.GetCurrentPageItems());
        Assert.Null(pager.MapSelectionToIndex(1));
    }

    [Fact]
    public void Pager_CurrentPageItemsMatchSelection()
    {
        var items = Enumerable.Range(0, 12).ToList();
        var pager = new Pager<int>(items, 5);

        Assert.True(pager.GoTo(2));

        var pageItems = pager.GetCurrentPageItems();
        Assert.Equal(new[] { 5, 6, 7, 8, 9 }, pageItems);

        var selection = 3;
        var selected = pageItems[selection - 1];
        Assert.Equal(7, selected);
    }
}
