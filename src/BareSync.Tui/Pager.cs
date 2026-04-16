namespace BareSync.Tui;

public sealed class Pager<T>
{
    private readonly IReadOnlyList<T> _items;

    public Pager(IReadOnlyList<T> items, int pageSize)
    {
        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }

        _items = items ?? throw new ArgumentNullException(nameof(items));
        PageSize = pageSize;
        TotalItems = _items.Count;
        TotalPages = Math.Max(1, (TotalItems + PageSize - 1) / PageSize);
        CurrentPage = 1;
    }

    public int PageSize { get; }

    public int TotalItems { get; }

    public int TotalPages { get; }

    public int CurrentPage { get; private set; }

    public IReadOnlyList<T> GetCurrentPageItems()
    {
        return GetPageItems(CurrentPage);
    }

    public IReadOnlyList<T> GetPageItems(int pageNumber)
    {
        var page = ClampPage(pageNumber);
        var startIndex = (page - 1) * PageSize;
        var remaining = TotalItems - startIndex;
        var count = remaining <= 0 ? 0 : Math.Min(PageSize, remaining);

        if (count == 0)
        {
            return Array.Empty<T>();
        }

        var pageItems = new T[count];
        for (var i = 0; i < count; i++)
        {
            pageItems[i] = _items[startIndex + i];
        }

        return pageItems;
    }

    public bool Next()
    {
        if (CurrentPage >= TotalPages)
        {
            return false;
        }

        CurrentPage++;
        return true;
    }

    public bool Previous()
    {
        if (CurrentPage <= 1)
        {
            return false;
        }

        CurrentPage--;
        return true;
    }

    public bool GoTo(int pageNumber)
    {
        if (pageNumber < 1 || pageNumber > TotalPages)
        {
            return false;
        }

        CurrentPage = pageNumber;
        return true;
    }

    public int? MapSelectionToIndex(int selectionNumber)
    {
        if (selectionNumber < 1 || selectionNumber > PageSize)
        {
            return null;
        }

        var index = (CurrentPage - 1) * PageSize + (selectionNumber - 1);
        if (index < 0 || index >= TotalItems)
        {
            return null;
        }

        return index;
    }

    private int ClampPage(int pageNumber)
    {
        if (pageNumber < 1)
        {
            return 1;
        }

        return pageNumber > TotalPages ? TotalPages : pageNumber;
    }
}
