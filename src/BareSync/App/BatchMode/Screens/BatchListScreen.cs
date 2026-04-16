using BareSync.Domain;
using BareSync.Infra;
using BareSync.UI;

namespace BareSync.App.BatchMode.Screens;

/// <summary>
/// S2.2 Batch List screen - Displays paginated list of batches.
/// </summary>
internal sealed class BatchListScreen
{
    private const int PageSize = 9;

    /// <summary>
    /// Shows the batch list and returns the selected descriptor, or null if back.
    /// </summary>
    public BatchStorageDescriptor? Show(BatchStorageLoader loader, string appDataRoot, MenuStatus? lastStatus = null)
    {
        var pageIndex = 0;
        
        while (true)
        {
            var entries = loader.LoadAll(appDataRoot);
            var totalPages = Math.Max(1, (int)Math.Ceiling(entries.Count / (double)PageSize));
            if (pageIndex >= totalPages)
            {
                pageIndex = totalPages - 1;
            }

            var pageEntries = entries
                .Skip(pageIndex * PageSize)
                .Take(PageSize)
                .ToList();

            UiInteraction.Clear();
            Bare.Primitive.UI.UiConsole.WriteLine("** Batch / List **");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine($"Page {pageIndex + 1}/{totalPages}");

            if (entries.Count == 0)
            {
                Bare.Primitive.UI.UiConsole.WriteLine("(no batches)");
            }
            else
            {
                for (var index = 0; index < pageEntries.Count; index++)
                {
                    var entry = pageEntries[index];
                    Bare.Primitive.UI.UiConsole.WriteLine(
                        $"{index + 1}. Name: {entry.Name} | Id: {entry.Id} | status={entry.Status}");
                }
            }

            Bare.Primitive.UI.UiConsole.WriteLine();
            if (lastStatus is not null && !string.IsNullOrWhiteSpace(lastStatus.StatusLine))
            {
                Bare.Primitive.UI.UiConsole.WriteLine($"Last status: {lastStatus.StatusLine}");
                Bare.Primitive.UI.UiConsole.WriteLine();
            }

            Bare.Primitive.UI.UiConsole.WriteLine("** Menu **");
            Bare.Primitive.UI.UiConsole.WriteLine();

            var maxOption = 0;
            if (entries.Count > 0)
            {
                Bare.Primitive.UI.UiConsole.WriteLine("1. Open batch");
                maxOption = 1;
            }

            if (totalPages > 1)
            {
                Bare.Primitive.UI.UiConsole.WriteLine("2. Next page");
                Bare.Primitive.UI.UiConsole.WriteLine("3. Previous page");
                maxOption = 3;
            }

            Bare.Primitive.UI.UiConsole.WriteLine("0. Back");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.Write("Select an option: ");
            var selection = UiInteraction.ReadMenuDigit(0, Math.Max(maxOption, 1));

            switch (selection)
            {
                case 0:
                    return null; // Back
                case 1:
                    if (pageEntries.Count == 0)
                    {
                        break;
                    }

                    var selectedIndex = PromptBatchSelection(pageEntries.Count);
                    if (selectedIndex == 0)
                    {
                        break;
                    }

                    return pageEntries[selectedIndex - 1];
                case 2:
                    if (totalPages > 1)
                    {
                        pageIndex = (pageIndex + 1) % totalPages;
                    }
                    break;
                case 3:
                    if (totalPages > 1)
                    {
                        pageIndex = pageIndex == 0 ? totalPages - 1 : pageIndex - 1;
                    }
                    break;
            }
        }
    }

    private static int PromptBatchSelection(int pageCount)
    {
        while (true)
        {
            Bare.Primitive.UI.UiConsole.Write($"Select batch number (1..{pageCount}, 0 to cancel): ");
            var input = Bare.Primitive.UI.UiConsole.ReadLine();
            if (input is null)
            {
                return 0;
            }

            if (string.IsNullOrWhiteSpace(input))
            {
                continue;
            }

            if (int.TryParse(input.Trim(), out var value) && value >= 0 && value <= pageCount)
            {
                return value;
            }
        }
    }
}
