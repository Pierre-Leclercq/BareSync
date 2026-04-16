using Bare.Primitive.UI;

namespace BareSync.UI;

internal sealed class OperationRunnerOptions
{
    public string Header { get; init; } = "** BareSync **";
    public string OperationTitle { get; init; } = string.Empty;
    public IRefreshThrottle? Throttle { get; init; }
    public IEscapeSignal? EscapeSignal { get; init; }
    public IUiOutput? UiOutput { get; init; }
    public RenderMode RenderMode { get; init; } = RenderMode.None;
    public bool ClearAtStart { get; init; } = true;
    public bool ClearAtEnd { get; init; }
    public bool ShowLastLine { get; init; } = true;
}
