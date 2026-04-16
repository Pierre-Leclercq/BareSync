using System.Text.Json;
using BareSync.Domain;

namespace BareSync.Infra;

internal static class ConfigService
{
    private const string AppSettingsPathOverrideEnvVar = "BARESYNC_APPSETTINGS_PATH";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private sealed class PersistedAppConfig
    {
        public string SourceRoot { get; init; } = string.Empty;
        public string MirrorRoot { get; init; } = string.Empty;
        public bool Mirror { get; init; } = false;
        public string SourceIndexCsvPath { get; init; } = string.Empty;
        public string DestIndexCsvPath { get; init; } = string.Empty;
        public string EncryptedOutputRoot { get; init; } = string.Empty;
        public string RestoreRoot { get; init; } = string.Empty;
        public RestoreSmartMode RestoreSmartMode { get; init; } = RestoreSmartMode.Smart;
        public bool LogDebug { get; init; } = false;
        public List<string> ExcludeFileNames { get; init; } = AppConfig.CreateDefaultExcludeFileNames();
        public List<string> ExcludeDirectoryNames { get; init; } = AppConfig.CreateDefaultExcludeDirectoryNames();
        public List<string> ExcludePathGlobs { get; init; } = [];
        public int IndexCheckpointEveryFiles { get; init; } = 250;
        public int IndexCheckpointMinIntervalMs { get; init; } = 1500;
        public int IndexIoCooldownMs { get; init; } = 0;
        public int IndexInterStageCooldownMs { get; init; } = 1500;
        public bool IndexForceGcBetweenStages { get; init; } = true;
    }

    public static AppConfig LoadOrCreate(out bool created)
    {
        var configPath = ResolveConfigPath();
        created = false;

        AppConfig loaded;
        if (!File.Exists(configPath))
        {
            loaded = new AppConfig();
            WriteSkeleton(configPath, loaded);
            created = true;
        }
        else if (!TryReadConfig(configPath, out loaded))
        {
            loaded = new AppConfig();
        }

        var hasLegacyOutputCsv = !string.IsNullOrWhiteSpace(loaded.OutputCsvFileName);

        NormalizePaths(loaded);

        ApplyIndexDefaults(loaded, hasLegacyOutputCsv);

        return loaded;
    }

    public static bool Save(AppConfig config)
    {
        var configPath = ResolveConfigPath();

        try
        {
            NormalizePaths(config);
            var json = JsonSerializer.Serialize(ToPersisted(config), SerializerOptions);
            File.WriteAllText(configPath, json);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    public static IReadOnlyList<ConfigValidationError> Validate(AppConfig config)
    {
        return Validate(config, ResolveDriveLabelPath);
    }

    internal static IReadOnlyList<ConfigValidationError> Validate(
        AppConfig config,
        Func<string, DriveLabelResolutionResult> resolvePath)
    {
        NormalizePaths(config);
        ApplyIndexDefaults(config, allowLegacyOutputCsv: false);

        var errors = new List<ConfigValidationError>();

        var sourceRootResolution = resolvePath(config.SourceRoot);
        if (sourceRootResolution.Status == DriveLabelResolutionStatus.Resolved)
        {
            config.SourceRoot = sourceRootResolution.ResolvedPath;
        }

        var mirrorRootResolution = resolvePath(config.MirrorRoot);
        if (mirrorRootResolution.Status == DriveLabelResolutionStatus.Resolved)
        {
            config.MirrorRoot = mirrorRootResolution.ResolvedPath;
        }

        if (string.IsNullOrWhiteSpace(config.SourceRoot))
        {
            errors.Add(new ConfigValidationError("SourceRoot", "SourceRoot is required."));
        }
        else if (sourceRootResolution.Status == DriveLabelResolutionStatus.NotFound)
        {
            errors.Add(new ConfigValidationError("SourceRoot", $"SourceRoot drive name not found: {sourceRootResolution.ResolvedPath}"));
        }
        else if (sourceRootResolution.Status == DriveLabelResolutionStatus.Ambiguous)
        {
            errors.Add(new ConfigValidationError(
                "SourceRoot",
                $"SourceRoot drive name is ambiguous: {sourceRootResolution.ResolvedPath} (candidates: {string.Join(", ", sourceRootResolution.CandidateRoots)})"));
        }
        else if (!Directory.Exists(config.SourceRoot))
        {
            errors.Add(new ConfigValidationError("SourceRoot", $"SourceRoot does not exist: {config.SourceRoot}"));
        }

        if (string.IsNullOrWhiteSpace(config.MirrorRoot))
        {
            errors.Add(new ConfigValidationError("MirrorRoot", "MirrorRoot is required."));
        }
        else if (mirrorRootResolution.Status == DriveLabelResolutionStatus.NotFound)
        {
            errors.Add(new ConfigValidationError("MirrorRoot", $"MirrorRoot drive name not found: {mirrorRootResolution.ResolvedPath}"));
        }
        else if (mirrorRootResolution.Status == DriveLabelResolutionStatus.Ambiguous)
        {
            errors.Add(new ConfigValidationError(
                "MirrorRoot",
                $"MirrorRoot drive name is ambiguous: {mirrorRootResolution.ResolvedPath} (candidates: {string.Join(", ", mirrorRootResolution.CandidateRoots)})"));
        }
        else if (!Directory.Exists(config.MirrorRoot))
        {
            errors.Add(new ConfigValidationError("MirrorRoot", $"MirrorRoot does not exist: {config.MirrorRoot}"));
        }

        var sourceIndexResolution = resolvePath(config.SourceIndexCsvPath);
        if (sourceIndexResolution.Status == DriveLabelResolutionStatus.Resolved)
        {
            config.SourceIndexCsvPath = sourceIndexResolution.ResolvedPath;
        }

        var destIndexResolution = resolvePath(config.DestIndexCsvPath);
        if (destIndexResolution.Status == DriveLabelResolutionStatus.Resolved)
        {
            config.DestIndexCsvPath = destIndexResolution.ResolvedPath;
        }

        if (string.IsNullOrWhiteSpace(config.SourceIndexCsvPath))
        {
            errors.Add(new ConfigValidationError("SourceIndexCsvPath", "SourceIndexCsvPath is required."));
        }
        else if (sourceIndexResolution.Status == DriveLabelResolutionStatus.NotFound)
        {
            errors.Add(new ConfigValidationError("SourceIndexCsvPath", $"SourceIndexCsvPath drive name not found: {sourceIndexResolution.ResolvedPath}"));
        }
        else if (sourceIndexResolution.Status == DriveLabelResolutionStatus.Ambiguous)
        {
            errors.Add(new ConfigValidationError(
                "SourceIndexCsvPath",
                $"SourceIndexCsvPath drive name is ambiguous: {sourceIndexResolution.ResolvedPath} (candidates: {string.Join(", ", sourceIndexResolution.CandidateRoots)})"));
        }
        else if (!IsValidCsvPath(config.SourceIndexCsvPath))
        {
            errors.Add(new ConfigValidationError(
                "SourceIndexCsvPath",
                "SourceIndexCsvPath must be a .csv file name or full path (no ..)."));
        }

        if (string.IsNullOrWhiteSpace(config.DestIndexCsvPath))
        {
            errors.Add(new ConfigValidationError("DestIndexCsvPath", "DestIndexCsvPath is required."));
        }
        else if (destIndexResolution.Status == DriveLabelResolutionStatus.NotFound)
        {
            errors.Add(new ConfigValidationError("DestIndexCsvPath", $"DestIndexCsvPath drive name not found: {destIndexResolution.ResolvedPath}"));
        }
        else if (destIndexResolution.Status == DriveLabelResolutionStatus.Ambiguous)
        {
            errors.Add(new ConfigValidationError(
                "DestIndexCsvPath",
                $"DestIndexCsvPath drive name is ambiguous: {destIndexResolution.ResolvedPath} (candidates: {string.Join(", ", destIndexResolution.CandidateRoots)})"));
        }
        else if (!IsValidCsvPath(config.DestIndexCsvPath))
        {
            errors.Add(new ConfigValidationError(
                "DestIndexCsvPath",
                "DestIndexCsvPath must be a .csv file name or full path (no ..)."));
        }

        if (config.IndexCheckpointEveryFiles <= 0)
        {
            errors.Add(new ConfigValidationError(
                "IndexCheckpointEveryFiles",
                "IndexCheckpointEveryFiles must be > 0."));
        }

        if (config.IndexCheckpointMinIntervalMs < 0)
        {
            errors.Add(new ConfigValidationError(
                "IndexCheckpointMinIntervalMs",
                "IndexCheckpointMinIntervalMs must be >= 0."));
        }

        if (config.IndexIoCooldownMs < 0)
        {
            errors.Add(new ConfigValidationError(
                "IndexIoCooldownMs",
                "IndexIoCooldownMs must be >= 0."));
        }

        if (config.IndexInterStageCooldownMs < 0)
        {
            errors.Add(new ConfigValidationError(
                "IndexInterStageCooldownMs",
                "IndexInterStageCooldownMs must be >= 0."));
        }

        return errors;
    }

    private static bool TryReadConfig(string configPath, out AppConfig config)
    {
        try
        {
            var json = File.ReadAllText(configPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                config = new AppConfig();
                return false;
            }

            var loaded = JsonSerializer.Deserialize<AppConfig>(json, SerializerOptions);
            if (loaded is null)
            {
                config = new AppConfig();
                return false;
            }

            config = loaded;
            return true;
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }

        config = new AppConfig();
        return false;
    }

    private static string ResolveConfigPath()
    {
        var overridePath = Environment.GetEnvironmentVariable(AppSettingsPathOverrideEnvVar);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return Path.GetFullPath(overridePath);
        }

        return Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    }

    private static void WriteSkeleton(string configPath, AppConfig config)
    {
        try
        {
            var json = JsonSerializer.Serialize(ToPersisted(config), SerializerOptions);
            File.WriteAllText(configPath, json);
        }
        catch (IOException)
        {
        }
    }

    private static void NormalizePaths(AppConfig config)
    {
        config.SourceRoot = NormalizeRootPath(config.SourceRoot);
        config.MirrorRoot = NormalizeRootPath(config.MirrorRoot);
        config.SourceIndexCsvPath = NormalizeRootPath(config.SourceIndexCsvPath);
        config.DestIndexCsvPath = NormalizeRootPath(config.DestIndexCsvPath);
        config.OutputCsvFileName = NormalizeRootPath(config.OutputCsvFileName);
        config.EncryptedOutputRoot = NormalizeRootPath(config.EncryptedOutputRoot);
        config.RestoreRoot = NormalizeRootPath(config.RestoreRoot);
        NormalizeExclusions(config);
        if (!Enum.IsDefined(config.RestoreSmartMode))
        {
            config.RestoreSmartMode = RestoreSmartMode.Smart;
        }
    }

    private static PersistedAppConfig ToPersisted(AppConfig config)
    {
        return new PersistedAppConfig
        {
            SourceRoot = config.SourceRoot,
            MirrorRoot = config.MirrorRoot,
            Mirror = config.Mirror,
            SourceIndexCsvPath = config.SourceIndexCsvPath,
            DestIndexCsvPath = config.DestIndexCsvPath,
            EncryptedOutputRoot = config.EncryptedOutputRoot,
            RestoreRoot = config.RestoreRoot,
            RestoreSmartMode = config.RestoreSmartMode,
            LogDebug = config.LogDebug,
            ExcludeFileNames = [.. config.ExcludeFileNames],
            ExcludeDirectoryNames = [.. config.ExcludeDirectoryNames],
            ExcludePathGlobs = [.. config.ExcludePathGlobs],
            IndexCheckpointEveryFiles = config.IndexCheckpointEveryFiles,
            IndexCheckpointMinIntervalMs = config.IndexCheckpointMinIntervalMs,
            IndexIoCooldownMs = config.IndexIoCooldownMs,
            IndexInterStageCooldownMs = config.IndexInterStageCooldownMs,
            IndexForceGcBetweenStages = config.IndexForceGcBetweenStages
        };
    }

    private static void NormalizeExclusions(AppConfig config)
    {
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        config.ExcludeFileNames = NormalizeStringList(
            config.ExcludeFileNames,
            AppConfig.CreateDefaultExcludeFileNames(),
            comparer,
            normalizePathSeparators: false);

        config.ExcludeDirectoryNames = NormalizeStringList(
            config.ExcludeDirectoryNames,
            AppConfig.CreateDefaultExcludeDirectoryNames(),
            comparer,
            normalizePathSeparators: false);

        config.ExcludePathGlobs = NormalizeStringList(
            config.ExcludePathGlobs,
            [],
            comparer,
            normalizePathSeparators: true);
    }

    private static List<string> NormalizeStringList(
        List<string>? values,
        IReadOnlyList<string> defaultValues,
        IEqualityComparer<string> comparer,
        bool normalizePathSeparators)
    {
        var source = values ?? [.. defaultValues];
        var dedup = new HashSet<string>(comparer);
        var normalized = new List<string>();

        foreach (var raw in source)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var value = raw.Trim();
            if (normalizePathSeparators)
            {
                value = value.Replace('\\', '/');
            }

            if (value.Length == 0 || !dedup.Add(value))
            {
                continue;
            }

            normalized.Add(value);
        }

        return normalized;
    }

    private static void ApplyIndexDefaults(AppConfig config, bool allowLegacyOutputCsv)
    {
        if (allowLegacyOutputCsv
            && string.IsNullOrWhiteSpace(config.SourceIndexCsvPath)
            && !string.IsNullOrWhiteSpace(config.OutputCsvFileName))
        {
            config.SourceIndexCsvPath = config.OutputCsvFileName;
        }

        if (string.IsNullOrWhiteSpace(config.SourceIndexCsvPath)
            && Directory.Exists(config.SourceRoot))
        {
            config.SourceIndexCsvPath = Path.Combine(
                config.SourceRoot,
                AppConfig.DefaultSourceIndexCsvFileName);
        }

        if (string.IsNullOrWhiteSpace(config.DestIndexCsvPath)
            && Directory.Exists(config.MirrorRoot))
        {
            config.DestIndexCsvPath = Path.Combine(
                config.MirrorRoot,
                AppConfig.DefaultDestIndexCsvFileName);
        }
    }

    private static bool IsValidCsvPath(string csvPath)
    {
        if (!csvPath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (csvPath.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        var name = Path.GetFileName(csvPath);
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        if (Path.IsPathRooted(csvPath))
        {
            var parent = Path.GetDirectoryName(csvPath);
            return !string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent);
        }

        return csvPath.IndexOf(Path.DirectorySeparatorChar) < 0
            && csvPath.IndexOf(Path.AltDirectorySeparatorChar) < 0;
    }

    private static string NormalizeRootPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        if (OperatingSystem.IsWindows())
        {
            var resolution = ResolveDriveLabelPath(path);
            if (resolution.Status == DriveLabelResolutionStatus.Resolved)
            {
                return resolution.ResolvedPath;
            }

            return path;
        }

        return TryConvertWindowsPath(path, out var converted) ? converted : path;
    }

    internal static DriveLabelResolutionResult ResolveDriveLabelPath(string path)
    {
        return DriveLabelPathResolver.Resolve(path);
    }

    private static bool TryConvertWindowsPath(string path, out string converted)
    {
        converted = path;
        if (path.Length < 3)
        {
            return false;
        }

        var driveLetter = path[0];
        if (!char.IsLetter(driveLetter) || path[1] != ':' || (path[2] != '\\' && path[2] != '/'))
        {
            return false;
        }

        var remainder = path.Substring(2).Replace('\\', '/').TrimStart('/');
        converted = Path.Combine("/mnt", char.ToLowerInvariant(driveLetter).ToString(), remainder);
        return true;
    }
}
