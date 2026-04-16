using System.Globalization;
using System.IO.Hashing;
using System.Text;
using BareSync.App.Common;
using BareSync.Domain;
using BareSync.Infra;
using BareSync.UI;

namespace BareSync.App;

internal static class SyncOneWay
{
    private const int DestOnlySampleLimit = 20;
    private const string IndexReadErrorCrcMarker = "ERROR";
    private static readonly object RunFileLock = new();

    public static async Task<SyncSummary> RunAsync(
        AppConfig config,
        CancellationToken ct,
        bool dryRun = false,
        IProgress<ProgressInfo>? progress = null)
    {
        ct.ThrowIfCancellationRequested();

        var statusLine = string.Empty;
        var runStartedUtc = DateTime.UtcNow;
        var baseDir = GetApplicationBaseDirectory();
        var logsDir = Path.Combine(baseDir, "log");
        var reportsDir = Path.Combine(baseDir, "Reports");

        try
        {
            Directory.CreateDirectory(logsDir);
            Directory.CreateDirectory(reportsDir);
        }
        catch (Exception ex)
        {
            statusLine = $"Failed to create log/report directories: {ex.Message}";
            return new SyncSummary(
                0,
                0,
                0,
                0,
                0,
                1,
                0,
                string.Empty,
                string.Empty,
                "FAIL",
                statusLine);
        }

        var (runId, logFile, reportPath) = GetRunFilePaths(
            logsDir,
            reportsDir,
            runStartedUtc);
        using var logger = new SimpleFileLogger(logFile);
        using var sleepInhibitLease = dryRun ? null : PowerInhibitor.AcquireSleepInhibitLease(logger);

        logger.Info("Starting one-way sync");
        logger.Info($"RunId={runId}");
        logger.Info($"SourceRoot={config.SourceRoot}");
        logger.Info($"MirrorRoot={config.MirrorRoot}");
        logger.Info($"MirrorMode={config.Mirror.ToString().ToLowerInvariant()}");
        logger.Info($"SourceIndexCsvPath={config.SourceIndexCsvPath}");
        logger.Info($"DestIndexCsvPath={config.DestIndexCsvPath}");
        logger.Warn("Destination files may be overwritten.");

        if (string.IsNullOrWhiteSpace(config.SourceRoot) || !Directory.Exists(config.SourceRoot))
        {
            var message = $"SourceRoot does not exist: {config.SourceRoot}";
            logger.Error(message);
            statusLine = message;
            return new SyncSummary(
                0,
                0,
                0,
                0,
                0,
                1,
                0,
                logFile,
                string.Empty,
                "FAIL",
                statusLine);
        }

        if (string.IsNullOrWhiteSpace(config.MirrorRoot) || !Directory.Exists(config.MirrorRoot))
        {
            var message = $"MirrorRoot does not exist: {config.MirrorRoot}";
            logger.Error(message);
            statusLine = message;
            return new SyncSummary(
                0,
                0,
                0,
                0,
                0,
                1,
                0,
                logFile,
                string.Empty,
                "FAIL",
                statusLine);
        }

        if (string.IsNullOrWhiteSpace(config.SourceIndexCsvPath))
        {
            var message = $"Source index not found: {config.SourceIndexCsvPath}";
            logger.Error(message);
            statusLine = message;
            return new SyncSummary(
                0,
                0,
                0,
                0,
                0,
                1,
                0,
                logFile,
                string.Empty,
                "FAIL",
                statusLine);
        }

        try
        {
            var sourceIndexExists = File.Exists(config.SourceIndexCsvPath);
            var sourceRefreshIncremental = sourceIndexExists;
            logger.Info(sourceRefreshIncremental
                ? "Smart refreshing source index..."
                : "Source index missing; rebuilding source index...");
            var sourceRefreshProgress = progress ?? new Progress<ProgressInfo>(_ => { });
            await IndexRefreshService.RefreshIndexAsync(
                    config.SourceRoot,
                    config.SourceIndexCsvPath,
                    ct,
                    sourceRefreshProgress,
                    sourceRefreshIncremental
                        ? "Smart refreshing CRC indexes (source)..."
                        : "Rebuilding CRC indexes (source)...",
                    incremental: sourceRefreshIncremental,
                    config)
                .ConfigureAwait(false);
            logger.Info($"Source index written to: {config.SourceIndexCsvPath}");
        }
        catch (Exception ex)
        {
            logger.Error($"Failed to refresh source index: {ex.Message}");
            statusLine = "Failed to refresh source index.";
            return new SyncSummary(
                0,
                0,
                0,
                0,
                0,
                1,
                0,
                logFile,
                string.Empty,
                "FAIL",
                statusLine);
        }

        Dictionary<string, IndexRow> sourceIndex;
        try
        {
            sourceIndex = await CsvIndexReader.ReadAsync(config.SourceIndexCsvPath, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.Error($"Failed to read source index: {ex.Message}");
            statusLine = "Failed to read source index.";
            return new SyncSummary(
                0,
                0,
                0,
                0,
                0,
                1,
                0,
                logFile,
                string.Empty,
                "FAIL",
                statusLine);
        }

        var destIndexExisted = !string.IsNullOrWhiteSpace(config.DestIndexCsvPath)
            && File.Exists(config.DestIndexCsvPath);
        var destIndex = new Dictionary<string, IndexRow>(StringComparer.Ordinal);
        if (destIndexExisted)
        {
            try
            {
                destIndex = await CsvIndexReader.ReadAsync(config.DestIndexCsvPath, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.Error($"Failed to read destination index: {ex.Message}");
                statusLine = "Failed to read destination index.";
                return new SyncSummary(
                    sourceIndex.Count,
                    0,
                    0,
                    0,
                    0,
                    1,
                    0,
                    logFile,
                    string.Empty,
                    "FAIL",
                    statusLine);
            }
        }

        var decisions = BuildDecisions(sourceIndex, destIndex, logger, config.MirrorRoot, config.Mirror);
        var applyOperation = GetOperationLabel("Applying sync decisions...", dryRun);
        progress?.Report(new ProgressInfo
        {
            Processed = 0,
            Total = -1,
            OperationTitle = applyOperation
        });

        var sourceDirectoryPaths = new HashSet<string>(
            sourceIndex
                .Where(entry => entry.Value.EntryKind == IndexEntryKind.Directory)
                .Select(entry => entry.Key),
            StringComparer.Ordinal);

        var applySummary = await ApplyDecisionsAsync(
                decisions,
                config.SourceRoot,
                config.MirrorRoot,
                sourceDirectoryPaths,
                dryRun,
                logger,
                ct,
                progress)
            .ConfigureAwait(false);

        var destOnlyCount = applySummary.DestOnlyCount;
        var copyAttemptedCount = applySummary.CopyAttemptedCount;
        var copiedCount = applySummary.CopiedCount;
        var deleteAttemptedCount = applySummary.DeleteAttemptedCount;
        var deletedCount = applySummary.DeletedCount;
        var wouldDeleteCount = applySummary.WouldDeleteCount;
        var errorCount = applySummary.ErrorCount;
        var warnCount = applySummary.WarnCount;

        if (!dryRun && !string.IsNullOrWhiteSpace(config.DestIndexCsvPath))
        {
            try
            {
                var destIndexParent = Path.GetDirectoryName(config.DestIndexCsvPath);
                if (!string.IsNullOrWhiteSpace(destIndexParent))
                {
                    Directory.CreateDirectory(destIndexParent);
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Failed to ensure destination index directory: {ex.Message}");
                errorCount++;
            }
        }

        var destIndexPostCount = destIndex.Count;
        var destIndexRefreshSkipped = false;
        var destIndexRefreshNote = string.Empty;
        var modifiedDestFiles = applySummary.ModifiedFiles ?? Array.Empty<SyncedDestEntry>();
        var deletedDestFiles = applySummary.DeletedFiles ?? Array.Empty<string>();

        if (dryRun)
        {
            destIndexRefreshSkipped = true;
            destIndexRefreshNote = "Dry run enabled.";
            logger.Info("Destination index refresh skipped because dry run is enabled.");
        }
        else if (string.IsNullOrWhiteSpace(config.DestIndexCsvPath))
        {
            destIndexRefreshSkipped = true;
            destIndexRefreshNote = "DestIndexCsvPath is empty.";
            logger.Warn("Destination index refresh skipped because DestIndexCsvPath is empty.");
        }
        else if (!Directory.Exists(config.MirrorRoot))
        {
            destIndexRefreshSkipped = true;
            destIndexRefreshNote = $"MirrorRoot does not exist: {config.MirrorRoot}";
            logger.Warn($"Destination index refresh skipped because MirrorRoot does not exist: {config.MirrorRoot}");
        }
        else if (modifiedDestFiles.Count == 0 && deletedDestFiles.Count == 0)
        {
            destIndexRefreshSkipped = true;
            destIndexRefreshNote = "No destination changes to index.";
            logger.Info("Destination index update skipped because no changes were applied.");
        }
        else
        {
            try
            {
                logger.Info("Refreshing destination index...");
                progress?.Report(new ProgressInfo
                {
                    Processed = 0,
                    Total = modifiedDestFiles.Count + deletedDestFiles.Count,
                    OperationTitle = "Refreshing destination index..."
                });

                destIndexPostCount = await UpdateDestIndexAfterSyncAsync(
                        config,
                        modifiedDestFiles,
                        deletedDestFiles,
                        ct,
                        progress)
                    .ConfigureAwait(false);
                logger.Info($"Destination index written to: {config.DestIndexCsvPath}");
            }
            catch (Exception ex)
            {
                logger.Warn(
                    $"Destination index patch failed for '{config.DestIndexCsvPath}' ({ex.GetType().Name}), falling back to smart refresh: {ex.Message}");
                try
                {
                    destIndexPostCount = await IndexRefreshService.RefreshIndexAsync(
                            config.MirrorRoot,
                            config.DestIndexCsvPath,
                            ct,
                            progress ?? new Progress<ProgressInfo>(_ => { }),
                            "Smart refreshing destination index...",
                            incremental: true,
                            config)
                        .ConfigureAwait(false);
                    logger.Info($"Destination index smart refreshed at: {config.DestIndexCsvPath}");
                }
                catch (Exception refreshEx)
                {
                    logger.Error($"Failed to refresh destination index after patch failure: {refreshEx.Message}");
                    errorCount++;
                }
            }
        }

        var successRate = CalculateSuccessRate(copiedCount, copyAttemptedCount);
        var finalStatus = GetFinalStatus(errorCount, destOnlyCount, warnCount);
        var statusLabel = GetStatusLabel(finalStatus);
        var statusExplanation = GetStatusExplanation(errorCount, destOnlyCount, warnCount);

        var report = new StringBuilder();
        report.AppendLine("BareSync One-Way Sync Report");
        report.AppendLine($"RunId: {runId}");
        report.AppendLine($"RunDateUtc: {runStartedUtc:O}");
        report.AppendLine($"SourceRoot: {config.SourceRoot}");
        report.AppendLine($"MirrorRoot: {config.MirrorRoot}");
        report.AppendLine($"Mirror mode: {config.Mirror.ToString().ToLowerInvariant()}");
        report.AppendLine($"SourceIndexCsvPath: {config.SourceIndexCsvPath}");
        report.AppendLine($"DestIndexCsvPath: {config.DestIndexCsvPath}");
        report.AppendLine($"LogFilePath: {logFile}");
        report.AppendLine("Summary:");
        if (dryRun)
        {
            report.AppendLine("Dry run: true");
        }
        report.AppendLine($"Source index entries: {sourceIndex.Count}");
        report.AppendLine($"Destination index entries (post-sync): {destIndexPostCount}");
        report.AppendLine($"Destination index existed: {destIndexExisted.ToString().ToLowerInvariant()}");
        if (destIndexRefreshSkipped)
        {
            report.AppendLine($"Destination index refresh skipped: {destIndexRefreshNote}");
        }
        report.AppendLine($"Copy attempted: {copyAttemptedCount}");
        report.AppendLine($"Copied: {copiedCount}");
        report.AppendLine($"Delete attempted: {deleteAttemptedCount}");
        report.AppendLine($"Deleted: {deletedCount}");
        if (dryRun)
        {
            report.AppendLine($"Would delete: {wouldDeleteCount}");
        }
        report.AppendLine($"Destination-only entries: {destOnlyCount}");
        report.AppendLine($"Timestamp warnings: {warnCount}");
        report.AppendLine($"Errors: {errorCount}");
        report.AppendLine($"Success rate: {successRate.ToString("0.##", CultureInfo.InvariantCulture)}%");
        report.AppendLine();
        report.AppendLine(statusLabel);
        if (!string.IsNullOrWhiteSpace(statusExplanation))
        {
            report.AppendLine(statusExplanation);
        }
        report.AppendLine();

        try
        {
            await File.WriteAllTextAsync(
                    reportPath,
                    report.ToString(),
                    new UTF8Encoding(false),
                    ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.Error($"Failed to write report: {ex.Message}");
            statusLine = "Failed to write report.";
            errorCount++;
            finalStatus = GetFinalStatus(errorCount, destOnlyCount, warnCount);
            statusLabel = GetStatusLabel(finalStatus);
        }

        logger.Info($"Source index entries: {sourceIndex.Count}");
        logger.Info($"Destination index entries (post-sync): {destIndexPostCount}");
        logger.Info($"Destination index existed: {destIndexExisted.ToString().ToLowerInvariant()}");

        logger.Info($"Copy attempted: {copyAttemptedCount}");
        logger.Info($"Copied: {copiedCount}");
        logger.Info($"Delete attempted: {deleteAttemptedCount}");
        logger.Info($"Deleted: {deletedCount}");
        if (dryRun)
        {
            logger.Info($"Would delete: {wouldDeleteCount}");
        }
        logger.Info($"Destination-only entries: {destOnlyCount}");
        logger.Info($"Timestamp warnings: {warnCount}");
        logger.Info($"Errors: {errorCount}");
        logger.Info($"Final status: {statusLabel}");
        if (!string.IsNullOrWhiteSpace(statusExplanation))
        {
            logger.Info($"Final status explanation: {statusExplanation}");
        }
        logger.Info("One-way sync completed.");

        if (string.IsNullOrWhiteSpace(statusLine))
        {
            statusLine = statusLabel;
        }

        return new SyncSummary(
            sourceIndex.Count,
            destIndexPostCount,
            destOnlyCount,
            copyAttemptedCount,
            copiedCount,
            errorCount,
            warnCount,
            logFile,
            reportPath,
            statusLabel,
            statusLine,
            modifiedDestFiles,
            deletedDestFiles,
            deleteAttemptedCount,
            deletedCount,
            wouldDeleteCount);
    }

    internal static List<SyncDecision> BuildDecisions(
        IReadOnlyDictionary<string, IndexRow> sourceIndex,
        IReadOnlyDictionary<string, IndexRow> destinationIndex,
        SimpleFileLogger logger,
        string mirrorRoot,
        bool mirror = false)
    {
        var decisions = new List<SyncDecision>(sourceIndex.Count + destinationIndex.Count);
        var existenceChecks = 0;
        var sourceReadErrorCount = 0;
        var sourceReadErrorSamples = new List<string>();

        var destOnlyCount = 0;
        var destOnlySamples = new List<string>();
        foreach (var destPath in destinationIndex.Keys)
        {
            if (sourceIndex.ContainsKey(destPath))
            {
                continue;
            }

            destOnlyCount++;
            if (destOnlySamples.Count < DestOnlySampleLimit)
            {
                destOnlySamples.Add(destPath);
            }

            var destEntryKind = destinationIndex[destPath].EntryKind;
            var deleteType = destEntryKind == IndexEntryKind.Directory
                ? SyncDecisionType.DeleteDirectory
                : SyncDecisionType.Delete;

            decisions.Add(new SyncDecision(
                destPath,
                mirror ? deleteType : SyncDecisionType.DestOnly,
                mirror ? SyncDecisionReason.DestOnlyDelete : SyncDecisionReason.DestOnly));
        }

        if (destOnlyCount > 0)
        {
            if (mirror)
            {
                logger.Warn($"Destination-only entries scheduled for deletion: {destOnlyCount}");
            }
            else
            {
                logger.Warn($"Destination-only entries: {destOnlyCount}");
            }

            foreach (var sample in destOnlySamples)
            {
                if (mirror)
                {
                    logger.Info($"Destination-only (delete): {sample}");
                }
                else
                {
                    logger.Info($"Destination-only: {sample}");
                }
            }
        }

        foreach (var pair in sourceIndex)
        {
            var relativePath = pair.Key;
            var srcRow = pair.Value;
            var srcIsDirectory = srcRow.EntryKind == IndexEntryKind.Directory;

            if (!srcIsDirectory && IsIndexReadErrorCrc(srcRow.Crc64Hex))
            {
                sourceReadErrorCount++;
                if (sourceReadErrorSamples.Count < DestOnlySampleLimit)
                {
                    sourceReadErrorSamples.Add(relativePath);
                }

                decisions.Add(new SyncDecision(
                    relativePath,
                    SyncDecisionType.Skip,
                    SyncDecisionReason.SourceReadError));
                continue;
            }

            IndexRow? destRow = null;
            if (destinationIndex.TryGetValue(relativePath, out var destValue))
            {
                destRow = destValue;
            }

            if (destRow is null)
            {
                decisions.Add(new SyncDecision(
                    relativePath,
                    srcIsDirectory ? SyncDecisionType.CreateDirectory : SyncDecisionType.Copy,
                    srcIsDirectory ? SyncDecisionReason.MissingDirectoryInDest : SyncDecisionReason.MissingInDest));
                continue;
            }

            var destIsDirectory = destRow.Value.EntryKind == IndexEntryKind.Directory;

            if (srcIsDirectory)
            {
                if (!destIsDirectory)
                {
                    decisions.Add(new SyncDecision(
                        relativePath,
                        SyncDecisionType.CreateDirectory,
                        SyncDecisionReason.TypeMismatch));
                    continue;
                }

                var destFullDirectoryPath = GetDestinationFullPath(mirrorRoot, relativePath);
                existenceChecks++;
                if (!Directory.Exists(destFullDirectoryPath))
                {
                    decisions.Add(new SyncDecision(
                        relativePath,
                        SyncDecisionType.CreateDirectory,
                        SyncDecisionReason.MissingDirectoryInDest));
                    continue;
                }

                decisions.Add(new SyncDecision(
                    relativePath,
                    SyncDecisionType.Skip,
                    SyncDecisionReason.CrcSame));
                continue;
            }

            if (destIsDirectory)
            {
                decisions.Add(new SyncDecision(
                    relativePath,
                    SyncDecisionType.Copy,
                    SyncDecisionReason.TypeMismatch));
                continue;
            }

            if (!string.Equals(srcRow.Crc64Hex, destRow.Value.Crc64Hex, StringComparison.OrdinalIgnoreCase))
            {
                decisions.Add(new SyncDecision(
                    relativePath,
                    SyncDecisionType.Copy,
                    SyncDecisionReason.CrcChanged));
                continue;
            }

            var destFullPath = GetDestinationFullPath(mirrorRoot, relativePath);
            existenceChecks++;
            if (!File.Exists(destFullPath))
            {
                decisions.Add(new SyncDecision(
                    relativePath,
                    SyncDecisionType.Copy,
                    SyncDecisionReason.MissingInDest));
                continue;
            }

            if (srcRow.LastWriteTimeUtc != destRow.Value.LastWriteTimeUtc)
            {
                logger.Info($"CRC same but timestamp differs, scheduling copy: {relativePath}");
                decisions.Add(new SyncDecision(
                    relativePath,
                    SyncDecisionType.Copy,
                    SyncDecisionReason.TimestampMismatch));
                continue;
            }

            decisions.Add(new SyncDecision(
                relativePath,
                SyncDecisionType.Skip,
                SyncDecisionReason.CrcSame));
        }

        if (sourceReadErrorCount > 0)
        {
            logger.Warn($"Source entries skipped because unreadable during source index refresh: {sourceReadErrorCount}");
            foreach (var sample in sourceReadErrorSamples)
            {
                logger.Info($"Source unreadable (skip): {sample}");
            }
        }

        logger.Info($"Destination existence checks: {existenceChecks}");
        return decisions;
    }

    internal static async Task<SyncSummary> ApplyDecisionsAsync(
        List<SyncDecision> decisions,
        string sourceRoot,
        string mirrorRoot,
        HashSet<string> sourceDirectoryPaths,
        bool dryRun,
        SimpleFileLogger logger,
        CancellationToken ct,
        IProgress<ProgressInfo>? progress = null)
    {
        var sourceCount = 0;
        var destOnlyCount = 0;
        var warnCount = 0;
        var copyAttemptedCount = 0;
        var copiedCount = 0;
        var deleteAttemptedCount = 0;
        var deletedCount = 0;
        var wouldDeleteCount = 0;
        var errorCount = 0;
        var total = decisions.Count;
        var processed = 0;
        string? lastAction = null;
        var modifiedEntries = new List<SyncedDestEntry>();
        var deletedFiles = new List<string>();

        progress?.Report(new ProgressInfo
        {
            Processed = processed,
            Total = total,
            LastLine = lastAction
        });

        foreach (var decision in decisions)
        {
            ct.ThrowIfCancellationRequested();
            string? actionLabel = null;

            if (decision.Type == SyncDecisionType.DestOnly)
            {
                destOnlyCount++;
                processed++;
                progress?.Report(new ProgressInfo
                {
                    Processed = processed,
                    Total = total,
                    LastLine = lastAction
                });
                continue;
            }

            if (decision.Type == SyncDecisionType.Skip && decision.Reason == SyncDecisionReason.SourceReadError)
            {
                sourceCount++;
                warnCount++;
                var relativePath = decision.RelativePath;
                actionLabel = $"Skipping unreadable source {relativePath}";
                logger.Warn($"Skipping copy because source entry is unreadable in source index (CRC=ERROR): {relativePath}");

                processed++;
                if (!string.IsNullOrWhiteSpace(actionLabel))
                {
                    lastAction = actionLabel;
                }

                progress?.Report(new ProgressInfo
                {
                    Processed = processed,
                    Total = total,
                    LastLine = lastAction
                });
                continue;
            }

            if (decision.Type == SyncDecisionType.Delete)
            {
                deleteAttemptedCount++;
                var relativePath = decision.RelativePath;
                var relativeOsPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
                var destFull = Path.Combine(mirrorRoot, relativeOsPath);
                if (dryRun)
                {
                    wouldDeleteCount++;
                    logger.Info($"DRY RUN: Would delete (destination-only): {relativePath}");
                    actionLabel = $"Would delete {relativePath}";
                }
                else
                {
                    actionLabel = $"Deleting {relativePath}";
                    try
                    {
                        if (File.Exists(destFull))
                        {
                            EnsureDestinationWritable(destFull);
                            File.Delete(destFull);
                            deletedCount++;
                            logger.Info($"Deleted (destination-only): {relativePath}");
                        }
                        else
                        {
                            logger.Info($"Destination-only already absent on disk, pruning index entry: {relativePath}");
                        }

                        deletedFiles.Add(relativePath);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.Error($"Failed to delete destination-only '{destFull}': {ex}");
                        errorCount++;
                    }
                }

                processed++;
                if (!string.IsNullOrWhiteSpace(actionLabel))
                {
                    lastAction = actionLabel;
                }

                progress?.Report(new ProgressInfo
                {
                    Processed = processed,
                    Total = total,
                    LastLine = lastAction
                });
                continue;
            }

            if (decision.Type == SyncDecisionType.DeleteDirectory)
            {
                deleteAttemptedCount++;
                var relativePath = decision.RelativePath;
                var relativeOsPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
                var destFull = Path.Combine(mirrorRoot, relativeOsPath);
                if (dryRun)
                {
                    wouldDeleteCount++;
                    logger.Info($"DRY RUN: Would delete empty directory (destination-only): {relativePath}");
                    actionLabel = $"Would delete directory {relativePath}";
                }
                else
                {
                    actionLabel = $"Deleting directory {relativePath}";
                    try
                    {
                        var (deletedRelativePaths, deletedDirectoryCount, blockedByNonEmptyAtStart) =
                            DeleteEmptyDestinationDirectoryCascade(
                                mirrorRoot,
                                relativePath,
                                sourceDirectoryPaths,
                                logger);

                        if (blockedByNonEmptyAtStart)
                        {
                            warnCount++;
                        }

                        deletedCount += deletedDirectoryCount;
                        foreach (var deletedRelativePath in deletedRelativePaths)
                        {
                            deletedFiles.Add(deletedRelativePath);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.Error($"Failed to delete destination-only directory '{destFull}': {ex}");
                        errorCount++;
                    }
                }

                processed++;
                if (!string.IsNullOrWhiteSpace(actionLabel))
                {
                    lastAction = actionLabel;
                }

                progress?.Report(new ProgressInfo
                {
                    Processed = processed,
                    Total = total,
                    LastLine = lastAction
                });
                continue;
            }

            if (decision.Type == SyncDecisionType.CreateDirectory)
            {
                sourceCount++;
                var relativePath = decision.RelativePath;
                var relativeOsPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
                var destFull = Path.Combine(mirrorRoot, relativeOsPath);

                if (dryRun)
                {
                    logger.Info($"DRY RUN: Would create empty directory: {relativePath}");
                    actionLabel = $"Would create directory {relativePath}";
                }
                else
                {
                    actionLabel = $"Creating directory {relativePath}";
                    try
                    {
                        if (File.Exists(destFull))
                        {
                            EnsureDestinationWritable(destFull);
                            File.Delete(destFull);
                        }

                        Directory.CreateDirectory(destFull);
                        var info = new DirectoryInfo(destFull);
                        modifiedEntries.Add(new SyncedDestEntry(
                            relativePath,
                            0,
                            info.LastWriteTimeUtc,
                            string.Empty,
                            IndexEntryKind.Directory));
                        logger.Info($"Created empty directory: {relativePath}");
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.Error($"Failed to create directory '{destFull}': {ex}");
                        errorCount++;
                    }
                }

                processed++;
                if (!string.IsNullOrWhiteSpace(actionLabel))
                {
                    lastAction = actionLabel;
                }

                progress?.Report(new ProgressInfo
                {
                    Processed = processed,
                    Total = total,
                    LastLine = lastAction
                });
                continue;
            }

            sourceCount++;

            if (decision.Type == SyncDecisionType.Copy)
            {
                copyAttemptedCount++;
                var relativePath = decision.RelativePath;
                if (dryRun)
                {
                    var reasonLabel = GetCopyReasonLabel(decision.Reason);
                    logger.Info($"DRY RUN: Would copy ({reasonLabel}): {relativePath}");
                    actionLabel = $"Would copy {relativePath}";
                }
                else
                {
                    actionLabel = $"Copying {relativePath}";
                    var relativeOsPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
                    var sourceFull = Path.Combine(sourceRoot, relativeOsPath);
                    var destFull = Path.Combine(mirrorRoot, relativeOsPath);
                    if (!File.Exists(sourceFull))
                    {
                        warnCount++;
                        actionLabel = $"Skipping missing source {relativePath}";
                        logger.Warn($"Skipping copy because source file is absent on disk at copy time: {relativePath}");
                    }
                    else
                    {
                        try
                        {
                            if (Directory.Exists(destFull))
                            {
                                if (Directory.EnumerateFileSystemEntries(destFull).Any())
                                {
                                    throw new IOException($"Destination path is a non-empty directory: {destFull}");
                                }

                                Directory.Delete(destFull, recursive: false);
                            }

                            var destParent = Path.GetDirectoryName(destFull);
                            if (!string.IsNullOrWhiteSpace(destParent))
                            {
                                Directory.CreateDirectory(destParent);
                            }

                            var sourceInfo = new FileInfo(sourceFull);
                            var crc64Hex = await CopyFileWithCrcAsync(sourceFull, destFull, ct).ConfigureAwait(false);
                            File.SetLastWriteTimeUtc(destFull, sourceInfo.LastWriteTimeUtc);
                            var destInfo = new FileInfo(destFull);

                            modifiedEntries.Add(new SyncedDestEntry(
                                relativePath,
                                destInfo.Length,
                                destInfo.LastWriteTimeUtc,
                                crc64Hex,
                                IndexEntryKind.File));
                            copiedCount++;
                            var reasonLabel = GetCopyReasonLabel(decision.Reason);
                            logger.Info($"Copied ({reasonLabel}): {relativePath}");
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            logger.Error($"Failed to copy '{sourceFull}' to '{destFull}': {ex}");
                            errorCount++;
                        }
                    }
                }
            }

            processed++;
            if (!string.IsNullOrWhiteSpace(actionLabel))
            {
                lastAction = actionLabel;
            }

            progress?.Report(new ProgressInfo
            {
                Processed = processed,
                Total = total,
                LastLine = lastAction
            });
        }

        return new SyncSummary(
            sourceCount,
            0,
            destOnlyCount,
            copyAttemptedCount,
            copiedCount,
            errorCount,
            warnCount,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            modifiedEntries,
            deletedFiles,
            deleteAttemptedCount,
            deletedCount,
            wouldDeleteCount);
    }

    internal static async Task<int> UpdateDestIndexAfterSyncAsync(
        AppConfig config,
        IReadOnlyList<SyncedDestEntry> touchedEntries,
        IReadOnlyList<string> deletedFiles,
        CancellationToken ct,
        IProgress<ProgressInfo>? progress = null)
    {
        ct.ThrowIfCancellationRequested();

        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var destIndexPath = config.DestIndexCsvPath;

        Dictionary<string, IndexRow> rows;
        if (File.Exists(destIndexPath))
        {
            var existing = await CsvIndexReader.ReadAsync(destIndexPath, ct).ConfigureAwait(false);
            rows = new Dictionary<string, IndexRow>(existing, comparer);
        }
        else
        {
            rows = new Dictionary<string, IndexRow>(comparer);
        }

        long maxId = -1;
        foreach (var row in rows.Values)
        {
            if (row.Id > maxId)
            {
                maxId = row.Id;
            }
        }

        var total = touchedEntries.Count + deletedFiles.Count;
        var processed = 0;

        foreach (var deleted in deletedFiles)
        {
            ct.ThrowIfCancellationRequested();
            rows.Remove(deleted);
            processed++;
            progress?.Report(new ProgressInfo
            {
                Processed = processed,
                Total = total,
                OperationTitle = "Refreshing destination index...",
                CurrentItem = deleted,
                LastLine = "Removing from index"
            });
        }

        foreach (var entry in touchedEntries)
        {
            ct.ThrowIfCancellationRequested();

            var id = rows.TryGetValue(entry.DestRelativePath, out var existing)
                ? existing.Id
                : ++maxId;

            rows[entry.DestRelativePath] = new IndexRow(
                id,
                entry.DestRelativePath,
                entry.Crc64Hex,
                entry.SizeBytes,
                entry.LastWriteTimeUtc,
                entry.EntryKind);

            processed++;
            progress?.Report(new ProgressInfo
            {
                Processed = processed,
                Total = total,
                OperationTitle = "Refreshing destination index...",
                CurrentItem = entry.DestRelativePath
            });
        }

        await WriteUpdatedIndexAsync(destIndexPath, rows, comparer, ct).ConfigureAwait(false);

        progress?.Report(new ProgressInfo
        {
            Processed = total,
            Total = total,
            OperationTitle = "Refreshing destination index..."
        });

        return rows.Count;
    }

    internal static async Task<string> CopyFileWithCrcAsync(
        string sourcePath,
        string destPath,
        CancellationToken ct)
    {
        const int maxAttempts = 4;
        var delayMs = 120;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                return await CopyFileWithCrcOnceAsync(sourcePath, destPath, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < maxAttempts && IsTransientFileAccessError(ex))
            {
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
                delayMs *= 2;
            }
        }

        return await CopyFileWithCrcOnceAsync(sourcePath, destPath, ct).ConfigureAwait(false);
    }

    private static async Task<string> CopyFileWithCrcOnceAsync(
        string sourcePath,
        string destPath,
        CancellationToken ct)
    {
        const int bufferSize = 64 * 1024;

        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        EnsureDestinationWritable(destPath);

        await using var dest = new FileStream(
            destPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var crc64 = new Crc64();
        var buffer = new byte[bufferSize];

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            crc64.Append(buffer.AsSpan(0, read));
            await dest.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }

        await dest.FlushAsync(ct).ConfigureAwait(false);
        dest.Flush(flushToDisk: true);

        var hash = crc64.GetHashAndReset();
        return Convert.ToHexString(hash);
    }

    private static void EnsureDestinationWritable(string destPath)
    {
        if (!File.Exists(destPath))
        {
            return;
        }

        var attrs = File.GetAttributes(destPath);
        if ((attrs & FileAttributes.ReadOnly) == 0)
        {
            return;
        }

        File.SetAttributes(destPath, attrs & ~FileAttributes.ReadOnly);
    }

    private static bool IsIndexReadErrorCrc(string? crc64Hex)
    {
        return string.Equals(crc64Hex, IndexReadErrorCrcMarker, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTransientFileAccessError(Exception ex)
    {
        if (ex is UnauthorizedAccessException)
        {
            return true;
        }

        if (ex is IOException ioEx)
        {
            const int sharingViolation = 32;
            const int lockViolation = 33;
            var win32Code = ioEx.HResult & 0xFFFF;
            return win32Code is sharingViolation or lockViolation;
        }

        return false;
    }

    internal static string GetOperationLabel(string baseLabel, bool dryRun)
    {
        return dryRun ? $"Dry run: {baseLabel}" : baseLabel;
    }

    internal static SyncFinalStatus GetFinalStatus(int errorCount, int destOnlyCount, int warnCount)
    {
        if (errorCount > 0)
        {
            return SyncFinalStatus.Fail;
        }

        if (destOnlyCount > 0 || warnCount > 0)
        {
            return SyncFinalStatus.Warning;
        }

        return SyncFinalStatus.Success;
    }

    internal static double CalculateSuccessRate(int copiedCount, int copyAttemptedCount)
    {
        if (copyAttemptedCount == 0)
        {
            return 100;
        }

        return copiedCount * 100.0 / copyAttemptedCount;
    }

    internal static string GetStatusExplanation(int errorCount, int destOnlyCount, int warnCount)
    {
        if (errorCount == 0 && destOnlyCount > 0 && warnCount == 0)
        {
            return "WARNING is due to destination-only entries detected (these files are not copied by design).";
        }

        return string.Empty;
    }

    private static async Task WriteUpdatedIndexAsync(
        string destIndexPath,
        IReadOnlyDictionary<string, IndexRow> rows,
        IComparer<string> comparer,
        CancellationToken ct)
    {
        var keys = rows.Keys.ToList();
        keys.Sort(comparer);

        var workPath = $"{destIndexPath}.work";
        var parent = Path.GetDirectoryName(destIndexPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        await using (var stream = new FileStream(
                         workPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 4096,
                         FileOptions.Asynchronous | FileOptions.SequentialScan))
        await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            await writer.WriteLineAsync(CsvIndexWriter.Header).ConfigureAwait(false);

            foreach (var key in keys)
            {
                ct.ThrowIfCancellationRequested();

                var row = rows[key];
                var relativeDir = NormalizeRelativeDir(Path.GetDirectoryName(row.RelativePath) ?? string.Empty);
                var entry = new FileIndexEntry
                {
                    Id = row.Id,
                    EntryKind = row.EntryKind,
                    RelativeDir = relativeDir,
                    FileName = Path.GetFileName(row.RelativePath),
                    Crc64Hex = row.Crc64Hex,
                    SizeBytes = row.SizeBytes,
                    LastWriteTimeUtc = new DateTimeOffset(row.LastWriteTimeUtc, TimeSpan.Zero)
                };

                await writer.WriteLineAsync(CsvIndexWriter.FormatLine(entry)).ConfigureAwait(false);
            }

            await writer.FlushAsync().ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }

        try
        {
            ReplaceFile(workPath, destIndexPath);
        }
        catch (Exception ex)
        {
            throw new IOException(
                $"Failed to replace destination index file. WorkPath='{workPath}', DestinationPath='{destIndexPath}'.",
                ex);
        }
    }

    private static string NormalizeRelativeDir(string relativeDir)
    {
        if (string.IsNullOrWhiteSpace(relativeDir))
        {
            return string.Empty;
        }

        return relativeDir.Replace('\\', '/');
    }

    private static void ReplaceFile(string sourcePath, string destinationPath)
    {
        AtomicFileOperations.ReplaceFile(sourcePath, destinationPath);
    }

    internal static (string RunId, string LogPath, string ReportPath) GetRunFilePaths(
        string logsDir,
        string reportsDir,
        DateTime runStartedUtc)
    {
        var datePart = runStartedUtc.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var counter = 1;

        lock (RunFileLock)
        {
            while (true)
            {
                var runId = counter == 1 ? datePart : $"{datePart}_{counter}";
                var logCandidate = Path.Combine(logsDir, $"baresync_sync_{runId}.log");
                var reportCandidate = Path.Combine(reportsDir, $"baresync_report_{runId}.txt");

                var logReserved = false;
                try
                {
                    CreateEmptyFileExclusive(logCandidate);
                    logReserved = true;

                    CreateEmptyFileExclusive(reportCandidate);
                    return (runId, logCandidate, reportCandidate);
                }
                catch (IOException)
                {
                    if (logReserved)
                    {
                        TryDeleteIfExists(logCandidate);
                    }
                }

                counter++;
            }
        }
    }

    internal static string GetUniquePath(string directory, string baseName, string extensionWithDot)
    {
        var candidate = Path.Combine(directory, baseName + extensionWithDot);
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        for (var i = 2; ; i++)
        {
            candidate = Path.Combine(directory, $"{baseName}_{i}{extensionWithDot}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static string GetStatusLabel(SyncFinalStatus status)
    {
        return status switch
        {
            SyncFinalStatus.Success => "SUCCESS",
            SyncFinalStatus.Warning => "WARNING",
            SyncFinalStatus.Fail => "FAIL",
            _ => "FAIL"
        };
    }

    private static string GetCopyReasonLabel(SyncDecisionReason reason)
    {
        return reason switch
        {
            SyncDecisionReason.MissingInDest => "missing",
            SyncDecisionReason.MissingDirectoryInDest => "missing directory",
            SyncDecisionReason.CrcChanged => "crc changed",
            SyncDecisionReason.TypeMismatch => "type mismatch",
            SyncDecisionReason.TimestampMismatch => "timestamp changed",
            _ => "unknown"
        };
    }

    private static void CreateEmptyFileExclusive(string path)
    {
        using var _ = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read);
    }

    private static void TryDeleteIfExists(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best effort cleanup for reserved files.
        }
    }

    private static string GetApplicationBaseDirectory()
    {
        return AppContext.BaseDirectory;
    }

    private static string GetDestinationFullPath(string mirrorRoot, string relativePath)
    {
        var relativeOsPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(mirrorRoot, relativeOsPath);
    }

    private static (List<string> DeletedRelativePaths, int DeletedDirectoryCount, bool BlockedByNonEmptyAtStart)
        DeleteEmptyDestinationDirectoryCascade(
            string mirrorRoot,
            string startRelativePath,
            HashSet<string> sourceDirectoryPaths,
            SimpleFileLogger logger)
    {
        var deletedRelativePaths = new List<string>();
        var deletedDirectoryCount = 0;

        var mirrorRootFull = Path.GetFullPath(mirrorRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var startFullPath = GetDestinationFullPath(mirrorRoot, startRelativePath);

        if (!Directory.Exists(startFullPath))
        {
            logger.Info($"Destination-only directory already absent on disk, pruning index entry: {startRelativePath}");
            deletedRelativePaths.Add(startRelativePath);
            return (deletedRelativePaths, deletedDirectoryCount, false);
        }

        if (Directory.EnumerateFileSystemEntries(startFullPath).Any())
        {
            logger.Warn($"Cannot delete destination-only directory because it is not empty: {startRelativePath}");
            return (deletedRelativePaths, deletedDirectoryCount, true);
        }

        var currentFullPath = startFullPath;
        var currentRelativePath = startRelativePath;
        var isFirstDeleted = true;

        while (true)
        {
            Directory.Delete(currentFullPath, recursive: false);
            deletedDirectoryCount++;
            deletedRelativePaths.Add(currentRelativePath);

            if (isFirstDeleted)
            {
                logger.Info($"Deleted empty directory (destination-only): {currentRelativePath}");
                isFirstDeleted = false;
            }
            else
            {
                logger.Info($"Deleted empty parent directory (destination-only): {currentRelativePath}");
            }

            var parentDirectory = Directory.GetParent(currentFullPath);
            if (parentDirectory is null)
            {
                break;
            }

            var parentFullPath = parentDirectory.FullName
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (!IsPathInsideRoot(parentFullPath, mirrorRootFull) || string.Equals(parentFullPath, mirrorRootFull, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (!Directory.Exists(parentFullPath))
            {
                break;
            }

            if (Directory.EnumerateFileSystemEntries(parentFullPath).Any())
            {
                logger.Info($"Stopping parent directory cleanup because directory is not empty: {NormalizeRelativePath(mirrorRootFull, parentFullPath)}");
                break;
            }

            var parentRelativePath = NormalizeRelativePath(mirrorRootFull, parentFullPath);
            if (sourceDirectoryPaths.Contains(parentRelativePath))
            {
                logger.Info($"Stopping parent directory cleanup because directory exists in source: {parentRelativePath}");
                break;
            }

            currentFullPath = parentFullPath;
            currentRelativePath = parentRelativePath;
        }

        return (deletedRelativePaths, deletedDirectoryCount, false);
    }

    private static bool IsPathInsideRoot(string path, string rootPath)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalizedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRoot = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(normalizedPath, normalizedRoot, comparison))
        {
            return true;
        }

        return normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison);
    }

    private static string NormalizeRelativePath(string rootPath, string fullPath)
    {
        var relativePath = Path.GetRelativePath(rootPath, fullPath);
        return relativePath.Replace('\\', '/');
    }
}

internal readonly record struct SyncDecision(
    string RelativePath,
    SyncDecisionType Type,
    SyncDecisionReason Reason);

internal readonly record struct SyncedDestEntry(
    string DestRelativePath,
    long SizeBytes,
    DateTime LastWriteTimeUtc,
    string Crc64Hex,
    IndexEntryKind EntryKind);

internal enum SyncDecisionType
{
    Copy,
    Skip,
    Delete,
    CreateDirectory,
    DeleteDirectory,
    DestOnly
}

internal enum SyncDecisionReason
{
    MissingInDest,
    MissingDirectoryInDest,
    CrcChanged,
    CrcSame,
    TypeMismatch,
    TimestampMismatch,
    SourceReadError,
    DestOnlyDelete,
    DestOnly
}

internal enum SyncFinalStatus
{
    Success,
    Warning,
    Fail
}

internal readonly record struct SyncSummary(
    int SourceCount,
    int DestCount,
    int DestOnlyCount,
    int CopyAttemptedCount,
    int CopiedCount,
    int ErrorCount,
    int WarnCount,
    string LogFilePath,
    string ReportFilePath,
    string StatusLabel,
    string StatusLine,
    IReadOnlyList<SyncedDestEntry>? ModifiedFiles = null,
    IReadOnlyList<string>? DeletedFiles = null,
    int DeleteAttemptedCount = 0,
    int DeletedCount = 0,
    int WouldDeleteCount = 0);
