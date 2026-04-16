namespace BareSync.UI;

internal sealed class ScreenModel
{
    public string Header { get; init; } = string.Empty;
    public List<string> BodyLines { get; init; } = new();
    public List<string> FooterLines { get; init; } = new();
}
