namespace BareSync.App.BatchMode;

/// <summary>
/// Abstraction for path prompting operations to enable testing.
/// </summary>
public interface IPathPromptService
{
    string? PickDirectory(string title, string? defaultPath);
    string? PickDefaultSourceIndexCsvPath(string title, string sourceRoot, string? currentValue, string defaultFileName, bool preferDefaultFileName = false);
    string? PickDefaultDestIndexCsvPath(string title, string destRoot, string? currentValue, string defaultFileName, bool preferDefaultFileName = false);
    string? PickFilePath(string title, string? currentValue, string defaultFileName);
}

/// <summary>
/// Default implementation using PathPromptHelper.
/// </summary>
public class PathPromptService : IPathPromptService
{
    public string? PickDirectory(string title, string? defaultPath) =>
        BareSync.UI.PathPromptHelper.PickDirectory(title, defaultPath);

    public string? PickDefaultSourceIndexCsvPath(string title, string sourceRoot, string? currentValue, string defaultFileName, bool preferDefaultFileName = false) =>
        BareSync.UI.PathPromptHelper.PickIndexCsvPath(title, sourceRoot, currentValue, defaultFileName, preferDefaultFileName);

    public string? PickDefaultDestIndexCsvPath(string title, string destRoot, string? currentValue, string defaultFileName, bool preferDefaultFileName = false) =>
        BareSync.UI.PathPromptHelper.PickIndexCsvPath(title, destRoot, currentValue, defaultFileName, preferDefaultFileName);

    public string? PickFilePath(string title, string? currentValue, string defaultFileName) =>
        BareSync.UI.PathPromptHelper.PickFilePath(title, currentValue, defaultFileName);
}
