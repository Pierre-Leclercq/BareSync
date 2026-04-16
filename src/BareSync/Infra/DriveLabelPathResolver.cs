namespace BareSync.Infra;

internal enum DriveLabelResolutionStatus
{
    NotApplicable,
    Resolved,
    NotFound,
    Ambiguous
}

internal sealed record DriveLabelResolutionResult(
    DriveLabelResolutionStatus Status,
    string ResolvedPath,
    IReadOnlyList<string> CandidateRoots)
{
    public static DriveLabelResolutionResult NotApplicable(string path) =>
        new(DriveLabelResolutionStatus.NotApplicable, path, Array.Empty<string>());

    public static DriveLabelResolutionResult Resolved(string path) =>
        new(DriveLabelResolutionStatus.Resolved, path, Array.Empty<string>());

    public static DriveLabelResolutionResult NotFound(string originalPath) =>
        new(DriveLabelResolutionStatus.NotFound, originalPath, Array.Empty<string>());

    public static DriveLabelResolutionResult Ambiguous(string originalPath, IReadOnlyList<string> candidateRoots) =>
        new(DriveLabelResolutionStatus.Ambiguous, originalPath, candidateRoots);
}

internal sealed record DriveLabelEntry(string Label, string RootPath);

internal static class DriveLabelPathResolver
{
    public static DriveLabelResolutionResult Resolve(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return DriveLabelResolutionResult.NotApplicable(path);
        }

        var entries = GetDriveLabelEntries();
        return Resolve(path, entries);
    }

    internal static DriveLabelResolutionResult Resolve(
        string path,
        IReadOnlyList<DriveLabelEntry> entries,
        Func<string, bool>? directoryExists = null,
        Func<string, bool>? fileExists = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return DriveLabelResolutionResult.NotApplicable(path);
        }

        var trimmed = path.Trim();
        if (Path.IsPathRooted(trimmed) || trimmed.StartsWith(".", StringComparison.OrdinalIgnoreCase))
        {
            return DriveLabelResolutionResult.NotApplicable(path);
        }

        var separatorIndex = trimmed.IndexOfAny(new[] { '\\', '/' });
        if (separatorIndex <= 0)
        {
            return DriveLabelResolutionResult.NotApplicable(path);
        }

        var label = trimmed[..separatorIndex].Trim();
        if (label.Length == 0 || label.Contains(':'))
        {
            return DriveLabelResolutionResult.NotApplicable(path);
        }

        var remainder = trimmed[(separatorIndex + 1)..].TrimStart('\\', '/');
        var matches = entries
            .Where(entry => string.Equals(entry.Label, label, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0)
        {
            return DriveLabelResolutionResult.NotFound(path);
        }

        if (matches.Count == 1)
        {
            return DriveLabelResolutionResult.Resolved(BuildPath(matches[0].RootPath, remainder));
        }

        directoryExists ??= Directory.Exists;
        fileExists ??= File.Exists;

        var candidatePaths = matches
            .Select(match => BuildPath(match.RootPath, remainder))
            .ToList();

        var existingMatches = candidatePaths
            .Where(candidate => directoryExists(candidate) || fileExists(candidate))
            .ToList();

        if (existingMatches.Count == 1)
        {
            return DriveLabelResolutionResult.Resolved(existingMatches[0]);
        }

        var parentMatches = candidatePaths
            .Where(candidate =>
            {
                var parent = Path.GetDirectoryName(candidate);
                return !string.IsNullOrWhiteSpace(parent)
                    && (directoryExists(parent) || fileExists(parent));
            })
            .ToList();

        if (parentMatches.Count == 1)
        {
            return DriveLabelResolutionResult.Resolved(parentMatches[0]);
        }

        var roots = matches
            .Select(match => Path.GetPathRoot(match.RootPath) ?? match.RootPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(root => root, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return DriveLabelResolutionResult.Ambiguous(path, roots);
    }

    private static string BuildPath(string rootPath, string remainder)
    {
        if (string.IsNullOrWhiteSpace(remainder))
        {
            return rootPath;
        }

        var normalizedRemainder = remainder
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

        return Path.Combine(rootPath, normalizedRemainder);
    }

    private static IReadOnlyList<DriveLabelEntry> GetDriveLabelEntries()
    {
        var entries = new List<DriveLabelEntry>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady)
                {
                    continue;
                }

                var label = (drive.VolumeLabel ?? string.Empty).Trim();
                if (label.Length == 0)
                {
                    continue;
                }

                entries.Add(new DriveLabelEntry(label, drive.RootDirectory.FullName));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return entries;
    }
}
