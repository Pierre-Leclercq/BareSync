namespace BareSync.UI;

internal sealed class OperationResult
{
    public string StatusLine { get; init; } = string.Empty;
    public string? LogPath { get; init; }
    public string? ReportPath { get; init; }
    public bool SuccessOrWarningFlag { get; init; } = true;
}
