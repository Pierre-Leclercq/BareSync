namespace BareSync.UI;

internal sealed class OperationScreen : IScreen
{
    private readonly string _header;
    private readonly string _operation;
    private readonly int _processed;
    private readonly int _total;
    private readonly string? _lastLine;
    private readonly TimeSpan _elapsed;
    private readonly string? _currentItem;

    public OperationScreen(string operation, int processed, int total, string? lastLine, TimeSpan elapsed, string? currentItem)
        : this("** BareSync **", operation, processed, total, lastLine, elapsed, currentItem)
    {
    }

    public OperationScreen(string header, string operation, int processed, int total, string? lastLine, TimeSpan elapsed, string? currentItem)
    {
        _header = string.IsNullOrWhiteSpace(header) ? "** BareSync **" : header;
        _operation = operation ?? string.Empty;
        _processed = processed;
        _total = total;
        _lastLine = lastLine;
        _elapsed = elapsed;
        _currentItem = currentItem;
    }

    public ScreenModel Build()
    {
        var bodyLines = new List<string>
        {
            $"Operation: {_operation}",
            string.Empty,
            BuildProgressLine(),
            $"Elapsed: {FormatElapsed(_elapsed)}"
        };

        if (!string.IsNullOrWhiteSpace(_lastLine))
        {
            bodyLines.Add($"Last: {_lastLine}");
        }

        if (!string.IsNullOrWhiteSpace(_currentItem))
        {
            bodyLines.Add($"Current: {TruncatePath(_currentItem!)}");
        }

        return new ScreenModel
        {
            Header = _header,
            BodyLines = bodyLines
        };
    }

    private string BuildProgressLine()
    {
        return _total < 0
            ? $"Progress: Processed {_processed}"
            : $"Progress: Processed {_processed}/{_total}";
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        return $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    private static string TruncatePath(string path)
    {
        var width = GetConsoleWidth();
        if (width <= 0)
        {
            return path;
        }

        var labelLength = "Current: ".Length;
        var available = Math.Max(8, width - labelLength);
        if (path.Length <= available)
        {
            return path;
        }

        return "..." + path.Substring(path.Length - (available - 3));
    }

    private static int GetConsoleWidth()
    {
        try
        {
            if (Bare.Primitive.UI.UiConsole.IsOutputRedirected)
            {
                return 80;
            }

            return Bare.Primitive.UI.UiConsole.WindowWidth;
        }
        catch
        {
            return 80;
        }
    }
}
