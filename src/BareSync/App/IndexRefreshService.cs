using BareSync.App.Common;
using BareSync.Domain;
using BareSync.Infra;
using BareSync.UI;

namespace BareSync.App;

internal static class IndexRefreshService
{
    internal static async Task<OperationResult> RefreshIndexesWithProgressAsync(
        AppConfig config,
        CancellationToken ct,
        IProgress<ProgressInfo> progress,
        bool incremental)
    {
        using var sleepInhibitLease = PowerInhibitor.AcquireSleepInhibitLease();

        var sourceCount = await RefreshIndexAsync(
                config.SourceRoot,
                config.SourceIndexCsvPath,
                ct,
                progress,
                incremental ? "Smart refreshing CRC indexes (source)..." : "Refreshing CRC indexes (source)...",
                incremental,
                config)
            .ConfigureAwait(false);

        await PauseBetweenStagesIfNeededAsync(config, ct).ConfigureAwait(false);

        progress.Report(new ProgressInfo
        {
            Processed = 0,
            Total = -1,
            OperationTitle = incremental
                ? "Smart refreshing CRC indexes (destination)..."
                : "Refreshing CRC indexes (destination)..."
        });

        await RefreshIndexAsync(
                config.MirrorRoot,
                config.DestIndexCsvPath,
                ct,
                progress,
                incremental ? "Smart refreshing CRC indexes (destination)..." : "Refreshing CRC indexes (destination)...",
                incremental,
                config)
            .ConfigureAwait(false);

        var statusLine = sourceCount switch
        {
            -1 => $"Destination index written to: {config.DestIndexCsvPath} (Source folder does not exist)",
            0 => $"Destination index written to: {config.DestIndexCsvPath} (Source folder is empty)",
            _ => $"Destination index written to: {config.DestIndexCsvPath}"
        };

        return new OperationResult
        {
            StatusLine = statusLine
        };
    }

    internal static void DeleteIndexArtifacts(string indexPath)
    {
        if (string.IsNullOrWhiteSpace(indexPath))
        {
            return;
        }

        var paths = new[]
        {
            indexPath,
            $"{indexPath}.work",
            $"{indexPath}.checkpoint"
        };

        foreach (var path in paths)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }

    internal static void DeleteResumeArtifacts(string indexPath)
    {
        if (string.IsNullOrWhiteSpace(indexPath))
        {
            return;
        }

        var paths = new[]
        {
            $"{indexPath}.work",
            $"{indexPath}.checkpoint"
        };

        foreach (var path in paths)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }

    internal static async Task<int> PruneMissingEntriesFromIndexAsync(
        string root,
        string indexPath,
        CancellationToken ct,
        IProgress<ProgressInfo>? progress = null,
        string? operationTitle = null)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(root)
            || string.IsNullOrWhiteSpace(indexPath)
            || !Directory.Exists(root)
            || !File.Exists(indexPath))
        {
            return 0;
        }

        Dictionary<string, IndexRow> rows;
        try
        {
            rows = await CsvIndexReader.ReadAsync(indexPath, ct).ConfigureAwait(false);
        }
        catch
        {
            return 0;
        }

        if (rows.Count == 0)
        {
            return 0;
        }

        var keptIndex = new FileIndex();
        var total = rows.Count;
        var processed = 0;
        var removed = 0;

        foreach (var pair in rows.OrderBy(pair => pair.Value.Id).ThenBy(pair => pair.Key, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();

            if (!string.IsNullOrWhiteSpace(operationTitle))
            {
                progress?.Report(new ProgressInfo
                {
                    Processed = processed,
                    Total = total,
                    OperationTitle = operationTitle,
                    CurrentItem = pair.Key
                });
            }

            var row = pair.Value;
            var fullPath = ResolveIndexedPath(root, pair.Key);
            var exists = row.EntryKind == IndexEntryKind.Directory
                ? Directory.Exists(fullPath)
                : File.Exists(fullPath);

            if (exists)
            {
                keptIndex.Add(ToFileIndexEntry(row));
            }
            else
            {
                removed++;
            }

            processed++;

            if (!string.IsNullOrWhiteSpace(operationTitle))
            {
                progress?.Report(new ProgressInfo
                {
                    Processed = processed,
                    Total = total,
                    OperationTitle = operationTitle,
                    CurrentItem = pair.Key
                });
            }
        }

        if (removed <= 0)
        {
            return 0;
        }

        await CsvIndexWriter.WriteAsync(keptIndex, Path.GetFullPath(indexPath), ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(operationTitle))
        {
            progress?.Report(new ProgressInfo
            {
                Processed = total,
                Total = total,
                OperationTitle = operationTitle,
                LastLine = $"Pruned {removed} stale index entr{(removed == 1 ? "y" : "ies")}."
            });
        }

        return removed;
    }

    internal static async Task<int> RefreshIndexAsync(
        string root,
        string indexPath,
        CancellationToken ct,
        IProgress<ProgressInfo> progress,
        string operationTitle,
        bool incremental,
        AppConfig config)
    {
        // Check if source folder exists and has any entries (files or subdirectories)
        if (!Directory.Exists(root))
        {
            return -1; // Signal that folder doesn't exist
        }

        var hasAnyEntries = Directory.EnumerateFileSystemEntries(root, "*", SearchOption.TopDirectoryOnly).Any();
        if (!hasAnyEntries)
        {
            if (!string.IsNullOrWhiteSpace(indexPath))
            {
                DeleteIndexArtifacts(indexPath);
                var emptyIndex = new FileIndex();
                await CsvIndexWriter.WriteAsync(emptyIndex, Path.GetFullPath(indexPath), ct)
                    .ConfigureAwait(false);
            }

            return 0; // Signal that folder exists but is empty
        }

        var workPath = $"{indexPath}.work";
        var checkpointPath = $"{indexPath}.checkpoint";

        var indexExists = File.Exists(indexPath);
        var workExists = File.Exists(workPath);
        var checkpointExists = File.Exists(checkpointPath);

        var indexValid = false;
        if (indexExists)
        {
            try
            {
                using var reader = new StreamReader(indexPath);
                var header = reader.ReadLine();
                header = header?.TrimStart('\uFEFF');
                indexValid = header == CsvIndexWriter.Header;
            }
            catch
            {
                indexValid = false;
            }
        }

        var useIncrementalRefresh = incremental && indexExists && indexValid;
        if (incremental && !useIncrementalRefresh)
        {
            var rebuildReason = indexExists
                ? $"Index invalid, rebuilding from scratch: {indexPath}"
                : $"Index missing, rebuilding from scratch: {indexPath}";

            if (workExists)
            {
                try { File.Delete(workPath); } catch { }
                workExists = false;
            }

            if (checkpointExists)
            {
                try { File.Delete(checkpointPath); } catch { }
                checkpointExists = false;
            }

            progress.Report(new ProgressInfo
            {
                Processed = 0,
                Total = -1,
                OperationTitle = operationTitle,
                LastLine = rebuildReason
            });
        }

        if (indexValid && !workExists && !checkpointExists)
        {
            if (!incremental)
            {
                // Return existing count from valid index
                try
                {
                    var existingRows = await CsvIndexReader.ReadAsync(indexPath, ct).ConfigureAwait(false);
                    return existingRows.Count;
                }
                catch
                {
                    // Fall through to rebuild
                }
            }
        }

        if (workExists != checkpointExists)
        {
            var message = "Resume state incomplete (one file missing), rebuilding from scratch.";
            progress.Report(new ProgressInfo
            {
                Processed = 0,
                Total = -1,
                LastLine = message
            });

            if (workExists)
            {
                try { File.Delete(workPath); } catch { }
            }
            if (checkpointExists)
            {
                try { File.Delete(checkpointPath); } catch { }
            }
        }

        var fullIndexPath = Path.GetFullPath(indexPath);
        Action<int, int, string?, string?> progressCb =
            (processed, total, lastLine, currentItem) =>
                progress.Report(new ProgressInfo
                {
                    Processed = processed,
                    Total = total,
                    LastLine = lastLine,
                    CurrentItem = currentItem,
                    OperationTitle = operationTitle
                });

        if (useIncrementalRefresh)
        {
            return await FileScanner.BuildIndexIncrementalAsync(
                    root,
                    fullIndexPath,
                    fullIndexPath,
                    config,
                    ct,
                    progressCb,
                    config.IndexCheckpointEveryFiles,
                    config.IndexCheckpointMinIntervalMs,
                    config.IndexIoCooldownMs)
                .ConfigureAwait(false);
        }

        return await FileScanner.RebuildIndexResumableAsync(
                root,
                fullIndexPath,
                fullIndexPath,
                config,
                ct,
                progressCb,
                config.IndexCheckpointEveryFiles,
                config.IndexCheckpointMinIntervalMs,
                config.IndexIoCooldownMs)
            .ConfigureAwait(false);
    }

    private static string ResolveIndexedPath(string root, string relativePath)
    {
        var normalizedRelativePath = relativePath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        return Path.GetFullPath(Path.Combine(root, normalizedRelativePath));
    }

    private static FileIndexEntry ToFileIndexEntry(IndexRow row)
    {
        var normalizedRelativePath = row.RelativePath.Replace('\\', '/');
        var separatorIndex = normalizedRelativePath.LastIndexOf('/');
        var relativeDir = separatorIndex <= 0
            ? string.Empty
            : normalizedRelativePath[..separatorIndex];
        var fileName = separatorIndex < 0
            ? normalizedRelativePath
            : normalizedRelativePath[(separatorIndex + 1)..];

        var utcDateTime = row.LastWriteTimeUtc.Kind == DateTimeKind.Utc
            ? row.LastWriteTimeUtc
            : DateTime.SpecifyKind(row.LastWriteTimeUtc, DateTimeKind.Utc);

        return new FileIndexEntry
        {
            Id = row.Id,
            EntryKind = row.EntryKind,
            RelativeDir = relativeDir,
            FileName = fileName,
            Crc64Hex = row.Crc64Hex,
            SizeBytes = row.SizeBytes,
            LastWriteTimeUtc = new DateTimeOffset(utcDateTime)
        };
    }

    private static async Task PauseBetweenStagesIfNeededAsync(AppConfig config, CancellationToken ct)
    {
        if (config.IndexForceGcBetweenStages)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        if (config.IndexInterStageCooldownMs > 0)
        {
            await Task.Delay(config.IndexInterStageCooldownMs, ct).ConfigureAwait(false);
        }
    }
}
