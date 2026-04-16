using System;

namespace BareSync.Domain;

internal sealed class AppConfig
{
    public const string DefaultSourceIndexCsvFileName = "baresync_source_index.csv";
    public const string DefaultDestIndexCsvFileName = "baresync_dest_index.csv";

    public static List<string> CreateDefaultExcludeFileNames()
    {
        return
        [
            "Thumbs.db",
            "desktop.ini",
            "ehthumbs.db"
        ];
    }

    public static List<string> CreateDefaultExcludeDirectoryNames()
    {
        return
        [
            "$RECYCLE.BIN",
            "System Volume Information"
        ];
    }

    public string SourceRoot { get; set; } = string.Empty;

    public string MirrorRoot { get; set; } = string.Empty;

    // When true, destination-only files are deleted during one-way sync.
    public bool Mirror { get; set; } = false;

    public string SourceIndexCsvPath { get; set; } = string.Empty;

    public string DestIndexCsvPath { get; set; } = string.Empty;

    public string OutputCsvFileName { get; set; } = string.Empty;

    public string EncryptedOutputRoot { get; set; } = string.Empty;

    public string RestoreRoot { get; set; } = string.Empty;

    public RestoreSmartMode RestoreSmartMode { get; set; } = RestoreSmartMode.Smart;

    // When true, detailed per-item debug traces are written to operation logs.
    public bool LogDebug { get; set; } = false;

    // System/generated files that should be ignored during indexing and sync.
    public List<string> ExcludeFileNames { get; set; } = CreateDefaultExcludeFileNames();

    // Directory names to ignore recursively during indexing and sync.
    public List<string> ExcludeDirectoryNames { get; set; } = CreateDefaultExcludeDirectoryNames();

    // Optional simple wildcard patterns applied on normalized relative paths (using '/').
    public List<string> ExcludePathGlobs { get; set; } = [];

    // Indexing stability/performance tuning
    public int IndexCheckpointEveryFiles { get; set; } = 250;

    public int IndexCheckpointMinIntervalMs { get; set; } = 1500;

    public int IndexIoCooldownMs { get; set; } = 0;

    public int IndexInterStageCooldownMs { get; set; } = 1500;

    public bool IndexForceGcBetweenStages { get; set; } = true;
}
