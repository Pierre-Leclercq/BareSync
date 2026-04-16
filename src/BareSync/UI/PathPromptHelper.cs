using Bare.Infrastructure.UI.Pickers;
using BareSync.Domain;

namespace BareSync.UI;

internal static class PathPromptHelper
{
    public static string? PickDirectory(string title, string? currentValue)
    {
        return PathPicker.PickDirectory(title, currentValue);
    }

    public static string? PickIndexCsvPath(
        string title,
        string? rootPath,
        string? currentValue,
        string defaultFileName,
        bool preferDefaultFileName = false)
    {
        var startDir = !string.IsNullOrWhiteSpace(rootPath) && Directory.Exists(rootPath)
            ? rootPath
            : Environment.CurrentDirectory;

        var suggestedFileName = ResolveSuggestedFileName(currentValue, defaultFileName, preferDefaultFileName);

        return PathPicker.PickFile(
            title,
            startDir,
            suggestedFileName);
    }

    public static string? PickDefaultSourceIndexCsvPath(
        string title,
        string? rootPath,
        string? currentValue,
        bool preferDefaultFileName = false)
    {
        return PickIndexCsvPath(title, rootPath, currentValue, AppConfig.DefaultSourceIndexCsvFileName, preferDefaultFileName);
    }

    public static string? PickDefaultDestIndexCsvPath(
        string title,
        string? rootPath,
        string? currentValue,
        bool preferDefaultFileName = false)
    {
        return PickIndexCsvPath(title, rootPath, currentValue, AppConfig.DefaultDestIndexCsvFileName, preferDefaultFileName);
    }

    public static string? PickFilePath(string title, string? currentValue, string defaultFileName, bool preferDefaultFileName = false)
    {
        string startDir;
        try
        {
            var candidate = Path.GetDirectoryName(currentValue);
            startDir = !string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate)
                ? candidate
                : Environment.CurrentDirectory;
        }
        catch
        {
            startDir = Environment.CurrentDirectory;
        }

        var suggestedFileName = ResolveSuggestedFileName(currentValue, defaultFileName, preferDefaultFileName);

        return PathPicker.PickFile(
            title,
            startDir,
            suggestedFileName);
    }

    internal static string ResolveSuggestedFileName(
        string? currentValue,
        string defaultFileName,
        bool preferDefaultFileName)
    {
        var currentFileName = Path.GetFileName(currentValue ?? string.Empty);

        var preferred = preferDefaultFileName
            ? defaultFileName
            : currentFileName;

        if (!string.IsNullOrWhiteSpace(preferred))
        {
            return preferred;
        }

        return preferDefaultFileName ? currentFileName : defaultFileName;
    }
}