using System.Text.RegularExpressions;
using BareSync.Domain;

namespace BareSync.Infra;

internal sealed class PathExclusionMatcher
{
    private readonly HashSet<string> _excludeFileNames;
    private readonly HashSet<string> _excludeDirectoryNames;
    private readonly List<Regex> _excludePathGlobRegexes;

    private PathExclusionMatcher(
        HashSet<string> excludeFileNames,
        HashSet<string> excludeDirectoryNames,
        List<Regex> excludePathGlobRegexes)
    {
        _excludeFileNames = excludeFileNames;
        _excludeDirectoryNames = excludeDirectoryNames;
        _excludePathGlobRegexes = excludePathGlobRegexes;
    }

    public static PathExclusionMatcher Create(AppConfig? config)
    {
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        var fileNames = NormalizeNameList(
            config?.ExcludeFileNames,
            AppConfig.CreateDefaultExcludeFileNames(),
            comparer);

        var directoryNames = NormalizeNameList(
            config?.ExcludeDirectoryNames,
            AppConfig.CreateDefaultExcludeDirectoryNames(),
            comparer);

        var globPatterns = NormalizeGlobList(config?.ExcludePathGlobs);
        var regexOptions = RegexOptions.CultureInvariant | RegexOptions.Compiled;
        if (OperatingSystem.IsWindows())
        {
            regexOptions |= RegexOptions.IgnoreCase;
        }

        var globRegexes = new List<Regex>(globPatterns.Count);
        foreach (var pattern in globPatterns)
        {
            globRegexes.Add(CompileGlob(pattern, regexOptions));
        }

        return new PathExclusionMatcher(fileNames, directoryNames, globRegexes);
    }

    public bool ShouldExcludeFile(string fileName, string relativePath)
    {
        if (_excludeFileNames.Contains(fileName))
        {
            return true;
        }

        if (HasExcludedDirectoryInPath(relativePath, forDirectoryPath: false))
        {
            return true;
        }

        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        return IsGlobExcluded(normalizedRelativePath)
            || IsGlobExcluded(fileName);
    }

    public bool ShouldExcludeDirectory(string directoryName, string relativePath)
    {
        if (_excludeDirectoryNames.Contains(directoryName))
        {
            return true;
        }

        if (HasExcludedDirectoryInPath(relativePath, forDirectoryPath: true))
        {
            return true;
        }

        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        return IsGlobExcluded(normalizedRelativePath)
            || IsGlobExcluded($"{normalizedRelativePath}/")
            || IsGlobExcluded(directoryName);
    }

    public bool ShouldExcludeRelativePath(string relativePath, IndexEntryKind entryKind)
    {
        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        if (string.IsNullOrEmpty(normalizedRelativePath))
        {
            return false;
        }

        if (entryKind == IndexEntryKind.Directory)
        {
            var directoryName = GetLeafName(normalizedRelativePath);
            return ShouldExcludeDirectory(directoryName, normalizedRelativePath);
        }

        var fileName = GetLeafName(normalizedRelativePath);
        return ShouldExcludeFile(fileName, normalizedRelativePath);
    }

    private bool IsGlobExcluded(string value)
    {
        if (_excludePathGlobRegexes.Count == 0 || string.IsNullOrEmpty(value))
        {
            return false;
        }

        foreach (var regex in _excludePathGlobRegexes)
        {
            if (regex.IsMatch(value))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasExcludedDirectoryInPath(string relativePath, bool forDirectoryPath)
    {
        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        if (string.IsNullOrEmpty(normalizedRelativePath))
        {
            return false;
        }

        var segments = normalizedRelativePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        var endExclusive = forDirectoryPath
            ? segments.Length
            : segments.Length - 1;

        for (var i = 0; i < endExclusive; i++)
        {
            if (_excludeDirectoryNames.Contains(segments[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetLeafName(string normalizedRelativePath)
    {
        var separatorIndex = normalizedRelativePath.LastIndexOf('/');
        if (separatorIndex < 0)
        {
            return normalizedRelativePath;
        }

        return separatorIndex == normalizedRelativePath.Length - 1
            ? string.Empty
            : normalizedRelativePath[(separatorIndex + 1)..];
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        return string.IsNullOrWhiteSpace(relativePath)
            ? string.Empty
            : relativePath.Replace('\\', '/').Trim();
    }

    private static HashSet<string> NormalizeNameList(
        List<string>? values,
        IReadOnlyList<string> defaults,
        IEqualityComparer<string> comparer)
    {
        var normalized = new HashSet<string>(comparer);
        var source = values ?? [.. defaults];
        foreach (var value in source)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            normalized.Add(value.Trim());
        }

        return normalized;
    }

    private static List<string> NormalizeGlobList(List<string>? values)
    {
        if (values is null)
        {
            return [];
        }

        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var dedup = new HashSet<string>(comparer);
        var normalized = new List<string>(values.Count);

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var pattern = value.Trim().Replace('\\', '/');
            if (pattern.Length == 0 || !dedup.Add(pattern))
            {
                continue;
            }

            normalized.Add(pattern);
        }

        return normalized;
    }

    private static Regex CompileGlob(string pattern, RegexOptions options)
    {
        const string doublestarToken = "__BARESYNC_DOUBLESTAR__";

        var escaped = Regex.Escape(pattern)
            .Replace(@"\*\*", doublestarToken)
            .Replace(@"\*", "[^/]*")
            .Replace(@"\?", "[^/]")
            .Replace(doublestarToken, ".*");

        return new Regex($"^{escaped}$", options);
    }
}
