namespace BareSync.App.BatchMode;

/// <summary>
/// Result of a batch preflight check.
/// </summary>
internal sealed record BatchPreflightResult(
    bool Success,
    bool RequiresConfirmation,
    bool RequiresSecret,
    IReadOnlyList<string> Errors,
    IReadOnlyList<BareSync.Domain.BatchPreflightStepSummary> Steps,
    string BatchName,
    string BatchId);