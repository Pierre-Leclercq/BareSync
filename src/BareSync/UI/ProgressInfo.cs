namespace BareSync.UI;

public sealed class ProgressInfo
{
    public int Processed { get; init; }
    public int Total { get; init; } = -1;
    public string? LastLine { get; init; }
    public string OperationTitle { get; init; } = string.Empty;
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    public string? CurrentItem { get; init; }
}
