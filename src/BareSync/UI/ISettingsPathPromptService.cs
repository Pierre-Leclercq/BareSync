namespace BareSync.UI;

internal interface ISettingsPathPromptService
{
    string? PickDirectory(string title, string? currentValue);
    string? PickIndexCsvPath(string title, string? rootPath, string? currentValue, string defaultFileName);
    string? PickFilePath(string title, string? currentValue, string defaultFileName);
}

internal sealed class SettingsPathPromptService : ISettingsPathPromptService
{
    public string? PickDirectory(string title, string? currentValue)
    {
        return PathPromptHelper.PickDirectory(title, currentValue);
    }

    public string? PickIndexCsvPath(string title, string? rootPath, string? currentValue, string defaultFileName)
    {
        return PathPromptHelper.PickIndexCsvPath(title, rootPath, currentValue, defaultFileName);
    }

    public string? PickFilePath(string title, string? currentValue, string defaultFileName)
    {
        return PathPromptHelper.PickFilePath(title, currentValue, defaultFileName);
    }
}