using BareSync.Domain;
using BareSync.Infra;
using BareSync.UI;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BareSync.App;

internal sealed class EncryptedFolderService
{
    private const string EncryptedIndexFileName = ".baresync.encindex.bse";
    private const string EncryptedDataFileExtension = ".bse";

    private const string ArchiveMagic = "BSE2";
    private const byte ArchiveFormatVersion = 1;
    private const int ArchiveSaltLength = 16;
    private const int ArchiveIvLength = 16;
    private const int ArchiveMacLength = 32;
    private const int DefaultKdfIterations = 210_000;

    internal async Task<(List<EncryptedIndexEntry> Entries, List<EncryptedDataPlanItem> DataPlan)> BuildEncryptedPlanAsync(
        AppConfig config,
        IProgress<ProgressInfo>? progress,
        CancellationToken ct,
        SimpleFileLogger? logger = null)
    {
        logger?.Info($"Building encrypted plan: SourceRoot={config.SourceRoot}, SourceIndexCsvPath={config.SourceIndexCsvPath}");
        await RefreshSourceIndexForEncryptedOperationsAsync(config, progress, ct).ConfigureAwait(false);

        Bare.Primitive.UI.UiConsole.WriteLine("Loading source index...");
        var sourceIndex = await CsvIndexReader.ReadAsync(config.SourceIndexCsvPath, ct).ConfigureAwait(false);
        Bare.Primitive.UI.UiConsole.WriteLine($"Source index entries: {sourceIndex.Count}");
        logger?.Info($"Source index loaded: entries={sourceIndex.Count}");

        Bare.Primitive.UI.UiConsole.WriteLine("Building encrypted index payload...");
        var entries = new List<EncryptedIndexEntry>(sourceIndex.Count);
        var dataPlan = new List<EncryptedDataPlanItem>(sourceIndex.Count);
        foreach (var pair in sourceIndex)
        {
            ct.ThrowIfCancellationRequested();

            var relativePath = NormalizeRelativePath(pair.Key);
            var row = pair.Value;
            var obfuscatedPath = BuildObfuscatedRelativePath(relativePath);
            var obfuscatedFileName = Path.GetFileName(obfuscatedPath);
            var destinationRelativePath = obfuscatedPath;
            logger?.Debug($"Plan entry: kind={row.EntryKind}, original='{relativePath}', obfuscated='{obfuscatedPath}'");

            entries.Add(new EncryptedIndexEntry(
                row.Id,
                obfuscatedPath,
                relativePath,
                row.SizeBytes,
                row.LastWriteTimeUtc,
                row.Crc64Hex,
                row.EntryKind));

            if (row.EntryKind == IndexEntryKind.File)
            {
                dataPlan.Add(new EncryptedDataPlanItem(
                    relativePath,
                    obfuscatedFileName,
                    destinationRelativePath,
                    row.SizeBytes));
            }
        }

        logger?.Info($"Encrypted plan built: indexEntries={entries.Count}, fileArchives={dataPlan.Count}");

        return (entries, dataPlan);
    }

    internal Task<(List<EncryptedIndexEntry> Entries, List<EncryptedDataPlanItem> DataPlan)> BuildEncryptedPlanAsync(
        AppConfig config,
        CancellationToken ct)
    {
        return BuildEncryptedPlanAsync(config, progress: null, ct, logger: null);
    }

    internal Task<(List<EncryptedIndexEntry> Entries, List<EncryptedDataPlanItem> DataPlan)> BuildEncryptedPlanAsync(
        AppConfig config,
        CancellationToken ct,
        SimpleFileLogger? logger)
    {
        return BuildEncryptedPlanAsync(config, progress: null, ct, logger);
    }

    internal void PrintEncryptedDataPlan(
        IReadOnlyList<EncryptedDataPlanItem> dataPlan,
        string encryptedOutputRoot,
        int previewCount = 10)
    {
        if (dataPlan.Count == 0)
        {
            Bare.Primitive.UI.UiConsole.WriteLine("No data files found in source index; nothing to plan for encryption.");
            return;
        }

        var totalBytes = dataPlan.Sum(item => item.SizeBytes);
        var previewItems = dataPlan.Take(previewCount).ToList();

        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.WriteLine("Encrypted data plan (no data files processed yet):");
        Bare.Primitive.UI.UiConsole.WriteLine($"Planned files: {dataPlan.Count}");
        Bare.Primitive.UI.UiConsole.WriteLine($"Total bytes: {totalBytes:N0}");
        Bare.Primitive.UI.UiConsole.WriteLine($"Destination root: {encryptedOutputRoot}");
        Bare.Primitive.UI.UiConsole.WriteLine("Storage strategy: one encrypted archive per source file (native .NET codec).");
        Bare.Primitive.UI.UiConsole.WriteLine($"Showing first {previewItems.Count} planned items:");

        foreach (var item in previewItems)
        {
            Bare.Primitive.UI.UiConsole.WriteLine($"  {item.SourceRelativePath} -> {item.ObfuscatedFileName}{EncryptedDataFileExtension} (dest rel: {item.DestinationRelativePath}, {item.SizeBytes:N0} bytes)");
        }

        if (dataPlan.Count > previewItems.Count)
        {
            Bare.Primitive.UI.UiConsole.WriteLine($"  ... {dataPlan.Count - previewItems.Count} more planned items.");
        }
    }

    internal async Task<MenuStatus?> CreateEncryptedIndexAsync(
        AppConfig config,
        string password,
        CancellationToken ct,
        SimpleFileLogger? logger = null)
    {
        var plan = await BuildEncryptedPlanAsync(config, progress: null, ct, logger).ConfigureAwait(false);
        return await CreateEncryptedIndexAsync(config, password, plan.Entries, plan.DataPlan, ct, logger).ConfigureAwait(false);
    }

    internal async Task<MenuStatus?> CreateEncryptedIndexAsync(
        AppConfig config,
        string password,
        IReadOnlyList<EncryptedIndexEntry> entries,
        IReadOnlyList<EncryptedDataPlanItem> dataPlan,
        CancellationToken ct,
        SimpleFileLogger? logger = null)
    {
        ct.ThrowIfCancellationRequested();
        var outputRoot = config.EncryptedOutputRoot;
        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            return new MenuStatus
            {
                StatusLine = "EncryptedOutputRoot is not set."
            };
        }

        logger?.Info($"Creating encrypted index: outputRoot={outputRoot}, fileArchives={dataPlan.Count}, indexEntries={entries.Count}");

        var entriesByOriginalPath = BuildEntriesByOriginalPath(entries);
        if (dataPlan.Count > 0)
        {
            var archiveResult = await OperationRunner.RunAsync(
                new OperationRunnerOptions
                {
                    OperationTitle = "Creating encrypted archives...",
                    RenderMode = RenderMode.Progress,
                    ClearAtStart = false
                },
                ct,
                (progress, token) => CreateEncryptedDataArchivesAsync(
                    dataPlan,
                    outputRoot,
                    config.SourceRoot,
                    password,
                    token,
                    progress,
                    entriesByOriginalPath,
                    logger))
                .ConfigureAwait(false);

            Bare.Primitive.UI.UiConsole.WriteLine(archiveResult.StatusLine);
            if (!archiveResult.SuccessOrWarningFlag)
            {
                return new MenuStatus
                {
                    StatusLine = archiveResult.StatusLine,
                    SuccessOrWarningFlag = false,
                    LogPath = archiveResult.LogPath ?? logger?.FullLogPath
                };
            }

            logger?.Info($"Encrypted archive creation result: {archiveResult.StatusLine}");
        }
        else
        {
            Bare.Primitive.UI.UiConsole.WriteLine("No data entries to encrypt; skipping archive creation.");
            logger?.Info("No data entries to encrypt; archive creation skipped.");
        }

        var updatedEntries = BuildEntriesFromMap(entries, entriesByOriginalPath);

        var writeIndexResult = await WriteEncryptedIndexArchiveAsync(
                outputRoot,
                password,
                updatedEntries,
                ct,
                logger)
            .ConfigureAwait(false);
        if (!writeIndexResult.SuccessOrWarningFlag)
        {
            return new MenuStatus
            {
                StatusLine = writeIndexResult.StatusLine,
                SuccessOrWarningFlag = false,
                LogPath = logger?.FullLogPath
            };
        }

        var mirrorDeletedArchives = 0;
        var mirrorDeleteFailures = 0;
        if (config.Mirror)
        {
            var expectedArchives = BuildExpectedEncryptedArchiveSet(dataPlan);
            (mirrorDeletedArchives, mirrorDeleteFailures) = DeleteObsoleteManagedEncryptedArchives(
                outputRoot,
                expectedArchives);

            if (mirrorDeletedArchives > 0)
            {
                Bare.Primitive.UI.UiConsole.WriteLine($"Mirror cleanup: deleted {mirrorDeletedArchives} obsolete encrypted archive(s).");
            }

            if (mirrorDeleteFailures > 0)
            {
                Bare.Primitive.UI.UiConsole.WriteLine($"Mirror cleanup warning: failed to delete {mirrorDeleteFailures} obsolete encrypted archive(s).");
            }

            logger?.Info($"Mirror cleanup after create: deleted={mirrorDeletedArchives}, failures={mirrorDeleteFailures}");
        }

        var statusLine = writeIndexResult.StatusLine;
        if (config.Mirror)
        {
            statusLine += $" (mirror deleted {mirrorDeletedArchives} obsolete archive(s)";
            if (mirrorDeleteFailures > 0)
            {
                statusLine += $", {mirrorDeleteFailures} delete failure(s)";
            }

            statusLine += ").";
        }

        return new MenuStatus
        {
            StatusLine = statusLine,
            SuccessOrWarningFlag = true,
            LogPath = logger?.FullLogPath
        };
    }

    internal async Task<OperationResult> RefreshEncryptedFolderAsync(
        AppConfig config,
        string password,
        IProgress<ProgressInfo> progress,
        CancellationToken ct,
        SimpleFileLogger? logger = null)
    {
        logger?.Info($"Starting encrypted refresh: SourceRoot={config.SourceRoot}, EncryptedOutputRoot={config.EncryptedOutputRoot}, Mirror={config.Mirror}");
        var outputRoot = config.EncryptedOutputRoot;
        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            return new OperationResult
            {
                SuccessOrWarningFlag = false,
                StatusLine = "EncryptedOutputRoot is not set."
            };
        }

        if (string.IsNullOrWhiteSpace(config.SourceRoot))
        {
            return new OperationResult
            {
                SuccessOrWarningFlag = false,
                StatusLine = "SourceRoot is not set."
            };
        }

        (List<EncryptedIndexEntry> Entries, List<EncryptedDataPlanItem> DataPlan) sourcePlan;
        try
        {
            sourcePlan = await BuildEncryptedPlanAsync(config, progress, ct, logger).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new OperationResult
            {
                SuccessOrWarningFlag = false,
                StatusLine = ex.Message
            };
        }
        var sourceEntriesByPath = BuildEntriesByOriginalPath(sourcePlan.Entries);

        var loadResult = await TryLoadEncryptedIndexAsync(outputRoot, password, ct).ConfigureAwait(false);
        if (loadResult.Error is not null || loadResult.Payload is null)
        {
            if (IsMissingEncryptedIndexError(loadResult.Error))
            {
                var createStatus = await CreateEncryptedIndexAsync(
                        config,
                        password,
                        sourcePlan.Entries,
                        sourcePlan.DataPlan,
                        ct,
                        logger)
                    .ConfigureAwait(false);

                if (createStatus is null)
                {
                    return new OperationResult
                    {
                        SuccessOrWarningFlag = false,
                        StatusLine = "Encrypted index not found and rebuild failed."
                    };
                }

                var suffix = string.IsNullOrWhiteSpace(createStatus.StatusLine)
                    ? string.Empty
                    : $" {createStatus.StatusLine}";

                return new OperationResult
                {
                    SuccessOrWarningFlag = createStatus.SuccessOrWarningFlag,
                    StatusLine = $"Encrypted index missing; rebuilt destination from refreshed source index.{suffix}",
                    LogPath = createStatus.LogPath ?? logger?.FullLogPath,
                    ReportPath = createStatus.ReportPath
                };
            }

            return new OperationResult
            {
                SuccessOrWarningFlag = false,
                StatusLine = loadResult.Error ?? "Failed to load encrypted index.",
                LogPath = logger?.FullLogPath
            };
        }

        var destEntries = loadResult.Payload.Entries ?? Array.Empty<EncryptedIndexEntry>();
        var destEntriesByPath = BuildEntriesByOriginalPath(destEntries);
        var filesToUpsert = new List<EncryptedDataPlanItem>();
        var unchangedFiles = 0;
        var sourceFiles = 0;

        foreach (var item in sourcePlan.DataPlan)
        {
            ct.ThrowIfCancellationRequested();
            sourceFiles++;

            var key = NormalizeRelativePath(item.SourceRelativePath);
            if (!sourceEntriesByPath.TryGetValue(key, out var sourceEntry))
            {
                logger?.Debug($"Refresh skip: source entry not found in map for '{item.SourceRelativePath}'");
                continue;
            }

            if (!destEntriesByPath.TryGetValue(key, out var destEntry)
                || !AreEntriesEquivalentForEncryption(sourceEntry, destEntry))
            {
                filesToUpsert.Add(item);
                logger?.Debug($"Refresh decision: UPSERT '{item.SourceRelativePath}'");
            }
            else
            {
                unchangedFiles++;
                logger?.Debug($"Refresh decision: UNCHANGED '{item.SourceRelativePath}'");
            }
        }

        var deletedCandidates = 0;
        if (config.Mirror)
        {
            foreach (var pair in destEntriesByPath)
            {
                ct.ThrowIfCancellationRequested();
                if (sourceEntriesByPath.ContainsKey(pair.Key))
                {
                    continue;
                }

                if (!IsDirectoryIndexEntry(pair.Value))
                {
                    deletedCandidates++;
                }
            }
        }

        var totalSteps = filesToUpsert.Count + deletedCandidates;
        var completedSteps = 0;

        progress.Report(new ProgressInfo
        {
            Processed = 0,
            Total = totalSteps,
            OperationTitle = "Refreshing encrypted folder..."
        });

        if (filesToUpsert.Count > 0)
        {
            var archiveResult = await CreateEncryptedDataArchivesAsync(
                    filesToUpsert,
                    outputRoot,
                    config.SourceRoot,
                    password,
                    ct,
                    progress,
                    sourceEntriesByPath,
                    logger)
                .ConfigureAwait(false);
            if (!archiveResult.SuccessOrWarningFlag)
            {
                return new OperationResult
                {
                    SuccessOrWarningFlag = false,
                    StatusLine = archiveResult.StatusLine,
                    LogPath = archiveResult.LogPath ?? logger?.FullLogPath,
                    ReportPath = archiveResult.ReportPath
                };
            }

            completedSteps += filesToUpsert.Count;
        }

        var deletedArchives = 0;
        var deleteFailures = 0;
        if (config.Mirror && deletedCandidates > 0)
        {
            foreach (var pair in destEntriesByPath)
            {
                ct.ThrowIfCancellationRequested();
                if (sourceEntriesByPath.ContainsKey(pair.Key) || IsDirectoryIndexEntry(pair.Value))
                {
                    continue;
                }

                var archivePath = Path.Combine(outputRoot, pair.Value.ObfuscatedName + EncryptedDataFileExtension);
                try
                {
                    if (File.Exists(archivePath))
                    {
                        EnsureFileWritable(archivePath);
                        File.Delete(archivePath);
                        deletedArchives++;
                        logger?.Debug($"Refresh mirror delete: removed stale archive '{archivePath}'");
                    }
                    else
                    {
                        logger?.Debug($"Refresh mirror delete: archive already absent '{archivePath}'");
                    }
                }
                catch
                {
                    deleteFailures++;
                    logger?.Warn($"Refresh mirror delete failed: '{archivePath}'");
                }

                progress.Report(new ProgressInfo
                {
                    Processed = ++completedSteps,
                    Total = totalSteps,
                    OperationTitle = "Refreshing encrypted folder...",
                    CurrentItem = pair.Value.OriginalRelativePath
                });
            }
        }

        var updatedEntries = BuildEntriesFromMap(sourcePlan.Entries, sourceEntriesByPath);
        var writeIndexResult = await WriteEncryptedIndexArchiveAsync(
                outputRoot,
                password,
                updatedEntries,
                ct,
                logger)
            .ConfigureAwait(false);
        if (!writeIndexResult.SuccessOrWarningFlag)
        {
            return new OperationResult
            {
                SuccessOrWarningFlag = false,
                StatusLine = writeIndexResult.StatusLine,
                LogPath = writeIndexResult.LogPath ?? logger?.FullLogPath,
                ReportPath = writeIndexResult.ReportPath
            };
        }

        var statusLine = $"Encrypted folder refreshed: upserted {filesToUpsert.Count} file(s), unchanged {unchangedFiles}, source files {sourceFiles}";
        if (config.Mirror)
        {
            statusLine += $", deleted {deletedArchives} stale archive(s)";
            if (deleteFailures > 0)
            {
                statusLine += $", {deleteFailures} delete failure(s)";
            }
        }

        if (!string.IsNullOrWhiteSpace(writeIndexResult.StatusLine))
        {
            statusLine += $". {writeIndexResult.StatusLine}";
        }

        return new OperationResult
        {
            SuccessOrWarningFlag = deleteFailures == 0,
            StatusLine = statusLine,
            LogPath = logger?.FullLogPath
        };
    }

    internal async Task<(EncryptedIndexPayload? Payload, string? Error)> TryLoadEncryptedIndexAsync(
        string encryptedOutputRoot,
        string password,
        CancellationToken ct)
    {
        var indexArchivePath = Path.Combine(encryptedOutputRoot, EncryptedIndexFileName);
        if (!File.Exists(indexArchivePath))
        {
            return (null, $"Encrypted index not found: {indexArchivePath}");
        }

        try
        {
            await using var plaintext = new MemoryStream();
            var decryptResult = await TryDecryptArchiveToStreamAsync(indexArchivePath, password, plaintext, ct).ConfigureAwait(false);
            if (decryptResult is not null)
            {
                return (null, decryptResult);
            }

            plaintext.Position = 0;
            using var reader = new StreamReader(plaintext, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var json = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            var payload = JsonSerializer.Deserialize<EncryptedIndexPayload>(json);
            if (payload is null)
            {
                return (null, "Failed to parse encrypted index.");
            }

            return (payload, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (null, $"Failed to load encrypted index: {ex.Message}");
        }
    }

    internal static bool IsNativeBseArchive(string archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var header = new byte[4];
            var read = stream.Read(header, 0, header.Length);
            if (read != header.Length)
            {
                return false;
            }

            var magic = Encoding.ASCII.GetString(header);
            return string.Equals(magic, ArchiveMagic, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    internal async Task<string?> ValidateArchivePasswordAsync(
        string archivePath,
        string password,
        CancellationToken ct)
    {
        return await TryDecryptArchiveToStreamAsync(archivePath, password, Stream.Null, ct).ConfigureAwait(false);
    }

    internal async Task<(EncryptedIndexEntry? Entry, string? Error)> TryResolveEntryForArchiveAsync(
        string encryptedOutputRoot,
        string archivePath,
        string password,
        CancellationToken ct)
    {
        var loadResult = await TryLoadEncryptedIndexAsync(encryptedOutputRoot, password, ct).ConfigureAwait(false);
        if (loadResult.Error is not null || loadResult.Payload is null)
        {
            return (null, loadResult.Error ?? "Failed to load encrypted index.");
        }

        if (string.IsNullOrWhiteSpace(archivePath)
            || !archivePath.EndsWith(EncryptedDataFileExtension, StringComparison.OrdinalIgnoreCase))
        {
            return (null, "Unsupported archive file path.");
        }

        var relativeArchivePath = NormalizeRelativePath(Path.GetRelativePath(encryptedOutputRoot, archivePath));
        if (!relativeArchivePath.EndsWith(EncryptedDataFileExtension, StringComparison.OrdinalIgnoreCase)
            || relativeArchivePath.Length <= EncryptedDataFileExtension.Length)
        {
            return (null, "Archive path is outside encrypted root.");
        }

        var obfuscatedName = relativeArchivePath[..^EncryptedDataFileExtension.Length];
        var entry = (loadResult.Payload.Entries ?? Array.Empty<EncryptedIndexEntry>())
            .FirstOrDefault(e =>
                !IsDirectoryIndexEntry(e)
                && string.Equals(
                    NormalizeRelativePath(e.ObfuscatedName),
                    obfuscatedName,
                    GetPathComparison()));

        return (entry, null);
    }

    internal async Task<OperationResult> ExtractSingleEncryptedArchiveAsync(
        string archivePath,
        string password,
        string destinationFilePath,
        string? expectedCrc64Hex,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
        {
            return new OperationResult
            {
                SuccessOrWarningFlag = false,
                StatusLine = $"Encrypted archive not found: {archivePath}"
            };
        }

        if (string.IsNullOrWhiteSpace(destinationFilePath))
        {
            return new OperationResult
            {
                SuccessOrWarningFlag = false,
                StatusLine = "Destination path is empty."
            };
        }

        var destinationDir = Path.GetDirectoryName(destinationFilePath);
        if (!string.IsNullOrWhiteSpace(destinationDir))
        {
            Directory.CreateDirectory(destinationDir);
        }

        var tempPath = destinationFilePath + ".work";
        try
        {
            if (File.Exists(tempPath))
            {
                EnsureFileWritable(tempPath);
                File.Delete(tempPath);
            }

            await using (var outStream = new FileStream(
                             tempPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             1024 * 64,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var decryptError = await TryDecryptArchiveToStreamAsync(archivePath, password, outStream, ct).ConfigureAwait(false);
                if (decryptError is not null)
                {
                    return new OperationResult
                    {
                        SuccessOrWarningFlag = false,
                        StatusLine = $"Failed to extract archive: {decryptError}"
                    };
                }
            }

            if (!string.IsNullOrWhiteSpace(expectedCrc64Hex))
            {
                var restoredCrc64Hex = await Crc64Service.ComputeCrc64HexAsync(tempPath, ct).ConfigureAwait(false);
                if (!string.Equals(restoredCrc64Hex, expectedCrc64Hex, StringComparison.OrdinalIgnoreCase))
                {
                    return new OperationResult
                    {
                        SuccessOrWarningFlag = false,
                        StatusLine = $"CRC mismatch after extract. Expected {expectedCrc64Hex}, got {restoredCrc64Hex}."
                    };
                }
            }

            File.Move(tempPath, destinationFilePath, overwrite: true);
            return new OperationResult
            {
                SuccessOrWarningFlag = true,
                StatusLine = $"Extracted 1 file to: {destinationFilePath}"
            };
        }
        finally
        {
            TryDeleteFileIfExists(tempPath);
        }
    }

    internal async Task<OperationResult> RestoreEncryptedFilesAsync(
        AppConfig config,
        string password,
        IProgress<ProgressInfo> progress,
        CancellationToken ct,
        SimpleFileLogger? logger = null)
    {
        logger?.Info($"Starting encrypted restore: EncryptedOutputRoot={config.EncryptedOutputRoot}, RestoreRoot={config.RestoreRoot}, Mirror={config.Mirror}");
        var outputRoot = config.EncryptedOutputRoot;
        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            return new OperationResult
            {
                SuccessOrWarningFlag = false,
                StatusLine = "EncryptedOutputRoot is not set.",
                LogPath = logger?.FullLogPath
            };
        }

        var restoreRoot = config.RestoreRoot;
        if (string.IsNullOrWhiteSpace(restoreRoot))
        {
            return new OperationResult
            {
                SuccessOrWarningFlag = false,
                StatusLine = "RestoreRoot is not set.",
                LogPath = logger?.FullLogPath
            };
        }

        var loadResult = await TryLoadEncryptedIndexAsync(outputRoot, password, ct).ConfigureAwait(false);
        if (loadResult.Error is not null || loadResult.Payload is null)
        {
            return new OperationResult
            {
                SuccessOrWarningFlag = false,
                StatusLine = loadResult.Error ?? "Failed to load encrypted index.",
                LogPath = logger?.FullLogPath
            };
        }

        var entries = loadResult.Payload.Entries ?? Array.Empty<EncryptedIndexEntry>();
        var totalEntries = entries.Count;

        progress.Report(new ProgressInfo
        {
            Processed = 0,
            Total = totalEntries,
            OperationTitle = "Restoring encrypted files..."
        });

        var expectedRestoreFiles = new HashSet<string>(GetPathComparer());
        var expectedRestoreDirectories = new HashSet<string>(GetPathComparer());
        var restoredFiles = 0;
        var skippedFiles = 0;
        var skippedTimestampAligned = 0;
        var skippedTimestampAlignFailures = 0;

        for (var i = 0; i < totalEntries; i++)
        {
            ct.ThrowIfCancellationRequested();

            var entry = entries[i];
            progress.Report(new ProgressInfo
            {
                Processed = i,
                Total = totalEntries,
                OperationTitle = "Restoring encrypted files...",
                CurrentItem = entry.OriginalRelativePath
            });

            if (string.IsNullOrWhiteSpace(entry.OriginalRelativePath))
            {
                return new OperationResult
                {
                    SuccessOrWarningFlag = false,
                    StatusLine = "Encrypted index missing original paths; cannot restore.",
                    LogPath = logger?.FullLogPath
                };
            }

            var normalizedOriginalPath = NormalizeRelativePath(entry.OriginalRelativePath);
            var isDirectoryEntry = IsDirectoryIndexEntry(entry);
            if (isDirectoryEntry)
            {
                expectedRestoreDirectories.Add(normalizedOriginalPath);
            }
            else
            {
                expectedRestoreFiles.Add(normalizedOriginalPath);
            }

            var targetPath = Path.Combine(restoreRoot, normalizedOriginalPath);
            logger?.Debug($"Restore processing: kind={(isDirectoryEntry ? "Directory" : "File")}, original='{entry.OriginalRelativePath}', obfuscated='{entry.ObfuscatedName}'");
            if (isDirectoryEntry)
            {
                Directory.CreateDirectory(targetPath);
                logger?.Debug($"Restore directory created: '{targetPath}'");

                var processedDirectory = i + 1;
                progress.Report(new ProgressInfo
                {
                    Processed = processedDirectory,
                    Total = totalEntries,
                    OperationTitle = "Restoring encrypted files...",
                    CurrentItem = entry.OriginalRelativePath
                });

                continue;
            }

            var archivePath = Path.Combine(outputRoot, entry.ObfuscatedName + EncryptedDataFileExtension);
            if (!File.Exists(archivePath))
            {
                logger?.Error($"Restore failed (archive missing): original='{entry.OriginalRelativePath}', archive='{archivePath}'");
                return new OperationResult
                {
                    SuccessOrWarningFlag = false,
                    StatusLine = $"Encrypted archive not found for '{entry.OriginalRelativePath}': {archivePath}",
                    LogPath = logger?.FullLogPath
                };
            }

            var targetDir = Path.GetDirectoryName(targetPath);
            if (string.IsNullOrWhiteSpace(targetDir))
            {
                targetDir = restoreRoot;
            }

            if (!string.IsNullOrWhiteSpace(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            var skipRestore = await TryShouldSkipRestoreAsync(
                    targetPath,
                    entry,
                    config.RestoreSmartMode,
                    ct)
                .ConfigureAwait(false);
            if (skipRestore)
            {
                skippedFiles++;
                logger?.Debug($"Restore skip ({config.RestoreSmartMode}): '{entry.OriginalRelativePath}'");

                if (TryAlignFileLastWriteUtc(targetPath, entry.LastWriteTimeUtc, logger, entry.OriginalRelativePath))
                {
                    skippedTimestampAligned++;
                }
                else
                {
                    skippedTimestampAlignFailures++;
                }

                var skippedProcessed = i + 1;
                progress.Report(new ProgressInfo
                {
                    Processed = skippedProcessed,
                    Total = totalEntries,
                    OperationTitle = "Restoring encrypted files...",
                    CurrentItem = entry.OriginalRelativePath
                });

                continue;
            }

            var tempTargetPath = targetPath + ".work";
            try
            {
                if (File.Exists(tempTargetPath))
                {
                    EnsureFileWritable(tempTargetPath);
                    File.Delete(tempTargetPath);
                }

                await using (var outStream = new FileStream(
                                 tempTargetPath,
                                 FileMode.Create,
                                 FileAccess.Write,
                                 FileShare.None,
                                 1024 * 64,
                                 FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    var decryptError = await TryDecryptArchiveToStreamAsync(archivePath, password, outStream, ct).ConfigureAwait(false);
                    if (decryptError is not null)
                    {
                        logger?.Error($"Restore decrypt failed: original='{entry.OriginalRelativePath}', archive='{archivePath}', reason='{decryptError}'");
                        return new OperationResult
                        {
                            SuccessOrWarningFlag = false,
                            StatusLine = $"Failed to restore {entry.OriginalRelativePath}: {decryptError}",
                            LogPath = logger?.FullLogPath
                        };
                    }
                }

                if (!File.Exists(tempTargetPath))
                {
                    logger?.Error($"Restore failed: temp file missing after decrypt for '{entry.OriginalRelativePath}'");
                    return new OperationResult
                    {
                        SuccessOrWarningFlag = false,
                        StatusLine = $"Expected restored file not found: {targetPath}",
                        LogPath = logger?.FullLogPath
                    };
                }

                if (!string.IsNullOrWhiteSpace(entry.Crc64Hex))
                {
                    var restoredCrc64Hex = await Crc64Service.ComputeCrc64HexAsync(tempTargetPath, ct).ConfigureAwait(false);
                    if (!string.Equals(restoredCrc64Hex, entry.Crc64Hex, StringComparison.OrdinalIgnoreCase))
                    {
                        logger?.Error($"Restore CRC mismatch: original='{entry.OriginalRelativePath}', expected='{entry.Crc64Hex}', actual='{restoredCrc64Hex}'");
                        return new OperationResult
                        {
                            SuccessOrWarningFlag = false,
                            StatusLine = $"CRC mismatch after restore for {entry.OriginalRelativePath}. Expected {entry.Crc64Hex}, got {restoredCrc64Hex}.",
                            LogPath = logger?.FullLogPath
                        };
                    }
                }

                File.Move(tempTargetPath, targetPath, overwrite: true);
                _ = TryAlignFileLastWriteUtc(targetPath, entry.LastWriteTimeUtc, logger, entry.OriginalRelativePath);

                restoredFiles++;
                logger?.Debug($"Restore file completed: '{entry.OriginalRelativePath}' -> '{targetPath}'");
            }
            finally
            {
                TryDeleteFileIfExists(tempTargetPath);
            }

            var processed = i + 1;
            progress.Report(new ProgressInfo
            {
                Processed = processed,
                Total = totalEntries,
                OperationTitle = "Restoring encrypted files...",
                CurrentItem = entry.OriginalRelativePath
            });
        }

        var mirrorDeletedFiles = 0;
        var mirrorDeleteFailures = 0;
        if (config.Mirror)
        {
            (mirrorDeletedFiles, mirrorDeleteFailures) = DeleteRestoreFilesNotInIndex(
                restoreRoot,
                expectedRestoreFiles,
                expectedRestoreDirectories);
            if (mirrorDeletedFiles > 0)
            {
                Bare.Primitive.UI.UiConsole.WriteLine($"Mirror cleanup: deleted {mirrorDeletedFiles} stale restored file(s).");
            }

            if (mirrorDeleteFailures > 0)
            {
                Bare.Primitive.UI.UiConsole.WriteLine($"Mirror cleanup warning: failed to delete {mirrorDeleteFailures} stale restored file(s).");
            }

            logger?.Info($"Restore mirror cleanup: deleted={mirrorDeletedFiles}, failures={mirrorDeleteFailures}");
        }

        var successStatus =
            $"Restored {restoredFiles} file(s), skipped {skippedFiles}, total indexed files {expectedRestoreFiles.Count} to: {restoreRoot}";

        if (skippedFiles > 0)
        {
            successStatus += $", skipped timestamp aligned {skippedTimestampAligned}";
            if (skippedTimestampAlignFailures > 0)
            {
                successStatus += $", skipped timestamp align failures {skippedTimestampAlignFailures}";
            }
        }

        if (config.Mirror)
        {
            successStatus += $" (mirror deleted {mirrorDeletedFiles} stale file(s)";
            if (mirrorDeleteFailures > 0)
            {
                successStatus += $", {mirrorDeleteFailures} delete failure(s)";
            }

            successStatus += ").";
        }

        return new OperationResult
        {
            SuccessOrWarningFlag = mirrorDeleteFailures == 0,
            StatusLine = successStatus,
            LogPath = logger?.FullLogPath
        };
    }

    private static HashSet<string> BuildExpectedEncryptedArchiveSet(
        IReadOnlyList<EncryptedDataPlanItem> dataPlan)
    {
        var expected = new HashSet<string>(GetPathComparer());
        foreach (var item in dataPlan)
        {
            var relativeArchivePath = NormalizeRelativePath(item.DestinationRelativePath + EncryptedDataFileExtension);
            expected.Add(relativeArchivePath);
        }

        return expected;
    }

    private static (int DeletedCount, int FailedCount) DeleteObsoleteManagedEncryptedArchives(
        string encryptedOutputRoot,
        HashSet<string> expectedArchives)
    {
        if (!Directory.Exists(encryptedOutputRoot))
        {
            return (0, 0);
        }

        var deletedCount = 0;
        var failedCount = 0;
        var indexArchiveRelPath = NormalizeRelativePath(EncryptedIndexFileName);

        foreach (var archivePath in Directory.EnumerateFiles(encryptedOutputRoot, $"*{EncryptedDataFileExtension}", SearchOption.AllDirectories))
        {
            var relativeArchivePath = NormalizeRelativePath(Path.GetRelativePath(encryptedOutputRoot, archivePath));
            if (string.Equals(relativeArchivePath, indexArchiveRelPath, GetPathComparison()))
            {
                continue;
            }

            if (expectedArchives.Contains(relativeArchivePath))
            {
                continue;
            }

            if (!IsManagedEncryptedArchivePath(relativeArchivePath))
            {
                continue;
            }

            try
            {
                EnsureFileWritable(archivePath);
                File.Delete(archivePath);
                deletedCount++;
            }
            catch
            {
                failedCount++;
            }
        }

        TryDeleteEmptyDirectories(encryptedOutputRoot);
        return (deletedCount, failedCount);
    }

    private static (int DeletedCount, int FailedCount) DeleteRestoreFilesNotInIndex(
        string restoreRoot,
        HashSet<string> expectedRelativePaths,
        HashSet<string> expectedDirectoryPaths)
    {
        if (!Directory.Exists(restoreRoot))
        {
            return (0, 0);
        }

        var deletedCount = 0;
        var failedCount = 0;
        foreach (var filePath in Directory.EnumerateFiles(restoreRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = NormalizeRelativePath(Path.GetRelativePath(restoreRoot, filePath));
            if (expectedRelativePaths.Contains(relativePath))
            {
                continue;
            }

            try
            {
                EnsureFileWritable(filePath);
                File.Delete(filePath);
                deletedCount++;
            }
            catch
            {
                failedCount++;
            }
        }

        TryDeleteEmptyDirectories(restoreRoot, expectedDirectoryPaths);
        return (deletedCount, failedCount);
    }

    private static bool IsManagedEncryptedArchivePath(string relativeArchivePath)
    {
        var normalized = NormalizeRelativePath(relativeArchivePath);
        if (!normalized.EndsWith(EncryptedDataFileExtension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var withoutExtension = normalized[..^EncryptedDataFileExtension.Length];
        var segments = withoutExtension.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        foreach (var segment in segments)
        {
            if (!IsHexSha256Segment(segment))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsHexSha256Segment(string segment)
    {
        if (segment.Length != 64)
        {
            return false;
        }

        foreach (var c in segment)
        {
            var isHex = (c >= '0' && c <= '9')
                || (c >= 'a' && c <= 'f')
                || (c >= 'A' && c <= 'F');
            if (!isHex)
            {
                return false;
            }
        }

        return true;
    }

    private static void TryDeleteEmptyDirectories(
        string rootPath,
        HashSet<string>? preservedRelativeDirectories = null)
    {
        if (!Directory.Exists(rootPath))
        {
            return;
        }

        var directories = Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories)
            .OrderByDescending(path => path.Length)
            .ToList();

        foreach (var directoryPath in directories)
        {
            try
            {
                if (preservedRelativeDirectories is not null)
                {
                    var relativePath = NormalizeRelativePath(Path.GetRelativePath(rootPath, directoryPath));
                    if (preservedRelativeDirectories.Contains(relativePath))
                    {
                        continue;
                    }
                }

                if (!Directory.EnumerateFileSystemEntries(directoryPath).Any())
                {
                    Directory.Delete(directoryPath);
                }
            }
            catch
            {
            }
        }
    }

    private static void EnsureFileWritable(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var attrs = File.GetAttributes(path);
        if ((attrs & FileAttributes.ReadOnly) != 0)
        {
            File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
        }
    }

    private static bool TryAlignFileLastWriteUtc(
        string targetPath,
        DateTime expectedLastWriteTimeUtc,
        SimpleFileLogger? logger,
        string? originalRelativePath)
    {
        try
        {
            File.SetLastWriteTimeUtc(targetPath, expectedLastWriteTimeUtc);
            logger?.Debug($"Restore timestamp aligned: '{originalRelativePath}' -> '{expectedLastWriteTimeUtc:O}'");
            return true;
        }
        catch (Exception ex)
        {
            logger?.Warn($"Restore timestamp alignment failed for '{originalRelativePath}' at '{targetPath}': {ex.Message}");
            return false;
        }
    }

    private static StringComparer GetPathComparer()
    {
        return OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    }

    private static StringComparison GetPathComparison()
    {
        return OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    private async Task<OperationResult> CreateEncryptedDataArchivesAsync(
        IReadOnlyList<EncryptedDataPlanItem> dataPlan,
        string encryptedOutputRoot,
        string sourceRoot,
        string password,
        CancellationToken ct,
        IProgress<ProgressInfo> progress,
        Dictionary<string, EncryptedIndexEntry> entriesByOriginalPath,
        SimpleFileLogger? logger = null)
    {
        if (dataPlan.Count == 0)
        {
            logger?.Info("No encrypted data archives to create.");
            return new OperationResult
            {
                StatusLine = "No archives needed.",
                SuccessOrWarningFlag = true,
                LogPath = logger?.FullLogPath
            };
        }

        const string operationTitle = "Creating encrypted archives...";
        var checkpointPath = Path.Combine(encryptedOutputRoot, ".baresync.createenc.checkpoint");
        var checkpointWorkPath = $"{checkpointPath}.work";

        logger?.Info($"Starting encrypted data archive creation: outputRoot={encryptedOutputRoot}, sourceRoot={sourceRoot}, items={dataPlan.Count}");

        Directory.CreateDirectory(encryptedOutputRoot);

        var resumeIndex = await ReadCheckpointAsync(checkpointPath, ct).ConfigureAwait(false);
        resumeIndex = Math.Clamp(resumeIndex, 0, dataPlan.Count);
        if (resumeIndex >= dataPlan.Count)
        {
            resumeIndex = 0;
        }

        logger?.Info($"Archive creation checkpoint: resumeIndex={resumeIndex}, checkpointPath={checkpointPath}");

        var processed = resumeIndex;
        ReportArchiveProgress(progress, operationTitle, processed, dataPlan.Count, processed < dataPlan.Count ? dataPlan[processed].SourceRelativePath : null);

        for (var i = resumeIndex; i < dataPlan.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var item = dataPlan[i];
            var archivePath = Path.Combine(encryptedOutputRoot, item.DestinationRelativePath + EncryptedDataFileExtension);
            var tempArchivePath = archivePath + ".work";
            logger?.Debug($"Archive item start: index={i + 1}/{dataPlan.Count}, source='{item.SourceRelativePath}', archive='{archivePath}'");
            var archiveDir = Path.GetDirectoryName(archivePath);
            if (!string.IsNullOrEmpty(archiveDir))
            {
                Directory.CreateDirectory(archiveDir);
            }

            TryDeleteFileIfExists(tempArchivePath);

            var sourceRelativePath = NormalizeRelativePath(item.SourceRelativePath);
            var sourceFullPath = Path.Combine(sourceRoot, sourceRelativePath);
            if (!File.Exists(sourceFullPath))
            {
                logger?.Error($"Archive creation failed (source missing): '{sourceFullPath}'");
                return new OperationResult
                {
                    SuccessOrWarningFlag = false,
                    StatusLine = $"Source file not found for encryption: {sourceFullPath}",
                    LogPath = logger?.FullLogPath
                };
            }

            var sourceFileInfo = new FileInfo(sourceFullPath);
            var sourceSizeBytes = sourceFileInfo.Length;
            var sourceLastWriteUtc = sourceFileInfo.LastWriteTimeUtc;
            var sourceCrc64Hex = await Crc64Service.ComputeCrc64HexAsync(sourceFullPath, ct).ConfigureAwait(false);
            logger?.Debug($"Archive source snapshot: '{item.SourceRelativePath}', size={sourceSizeBytes}, lastWriteUtc={sourceLastWriteUtc:O}, crc64={sourceCrc64Hex}");

            if (entriesByOriginalPath.TryGetValue(sourceRelativePath, out var currentEntry))
            {
                entriesByOriginalPath[sourceRelativePath] = currentEntry with
                {
                    SizeBytes = sourceSizeBytes,
                    LastWriteTimeUtc = sourceLastWriteUtc,
                    Crc64Hex = sourceCrc64Hex,
                    EntryKind = IndexEntryKind.File
                };
                logger?.Debug($"Archive index entry updated: '{item.SourceRelativePath}'");
            }

            var encryptError = await TryEncryptFileAsync(sourceFullPath, tempArchivePath, password, ct).ConfigureAwait(false);
            if (encryptError is not null)
            {
                TryDeleteFileIfExists(tempArchivePath);
                logger?.Error($"Archive encryption failed: source='{item.SourceRelativePath}', reason='{encryptError}'");
                return new OperationResult
                {
                    SuccessOrWarningFlag = false,
                    StatusLine = $"Encryption failed for {item.SourceRelativePath}: {encryptError}",
                    LogPath = logger?.FullLogPath
                };
            }

            if (!File.Exists(tempArchivePath))
            {
                logger?.Error($"Archive temp output missing after encryption: '{tempArchivePath}'");
                return new OperationResult
                {
                    SuccessOrWarningFlag = false,
                    StatusLine = $"Encrypted archive missing for {item.SourceRelativePath} (temp not found).",
                    LogPath = logger?.FullLogPath
                };
            }

            try
            {
                File.Move(tempArchivePath, archivePath, overwrite: true);
                logger?.Debug($"Archive finalized: '{archivePath}'");
            }
            catch (Exception ex)
            {
                TryDeleteFileIfExists(tempArchivePath);
                logger?.Error($"Failed to finalize archive for '{item.SourceRelativePath}': {ex.Message}", ex);
                return new OperationResult
                {
                    SuccessOrWarningFlag = false,
                    StatusLine = $"Failed to finalize archive for {item.SourceRelativePath}.",
                    LogPath = logger?.FullLogPath
                };
            }

            try
            {
                var verifyInfo = new FileInfo(sourceFullPath);
                if (verifyInfo.Length != sourceSizeBytes || verifyInfo.LastWriteTimeUtc != sourceLastWriteUtc)
                {
                    logger?.Warn($"Source changed during archive creation: '{item.SourceRelativePath}'");
                    return new OperationResult
                    {
                        SuccessOrWarningFlag = false,
                        StatusLine = $"Source file changed during encryption: {item.SourceRelativePath}. Please rerun refresh.",
                        LogPath = logger?.FullLogPath
                    };
                }
            }
            catch (Exception ex)
            {
                logger?.Error($"Source stability verification failed: '{item.SourceRelativePath}': {ex.Message}", ex);
                return new OperationResult
                {
                    SuccessOrWarningFlag = false,
                    StatusLine = $"Failed to verify source stability during encryption: {item.SourceRelativePath}.",
                    LogPath = logger?.FullLogPath
                };
            }

            processed = i + 1;

            if (!await TryWriteCheckpointAsync(checkpointPath, checkpointWorkPath, processed, ct).ConfigureAwait(false))
            {
                logger?.Error($"Failed to persist archive checkpoint: '{checkpointPath}'");
                return new OperationResult
                {
                    SuccessOrWarningFlag = false,
                    StatusLine = $"Failed to persist checkpoint at {checkpointPath}.",
                    LogPath = logger?.FullLogPath
                };
            }

            ReportArchiveProgress(progress, operationTitle, processed, dataPlan.Count, item.SourceRelativePath);
            logger?.Debug($"Archive item completed: index={processed}/{dataPlan.Count}, source='{item.SourceRelativePath}'");
        }

        if (!await TryWriteCheckpointAsync(checkpointPath, checkpointWorkPath, processed, ct).ConfigureAwait(false))
        {
            logger?.Error($"Failed to persist final archive checkpoint: '{checkpointPath}'");
            return new OperationResult
            {
                SuccessOrWarningFlag = false,
                StatusLine = $"Failed to persist checkpoint at {checkpointPath}.",
                LogPath = logger?.FullLogPath
            };
        }

        var checkpointCleanupWarning = string.Empty;
        if (!TryDeleteCheckpointArtifacts(checkpointPath, checkpointWorkPath))
        {
            checkpointCleanupWarning = $" Warning: failed to delete checkpoint artifacts at {checkpointPath}.";
            logger?.Warn($"Archive checkpoint cleanup warning: '{checkpointPath}'");
        }

        logger?.Info($"Encrypted archive creation completed: processed={processed}/{dataPlan.Count}");

        return new OperationResult
        {
            StatusLine = $"Encrypted archives created ({processed}/{dataPlan.Count}).{checkpointCleanupWarning}",
            SuccessOrWarningFlag = true,
            LogPath = logger?.FullLogPath
        };
    }

    private async Task<OperationResult> WriteEncryptedIndexArchiveAsync(
        string outputRoot,
        string password,
        IReadOnlyList<EncryptedIndexEntry> entries,
        CancellationToken ct,
        SimpleFileLogger? logger = null)
    {
        logger?.Info($"Writing encrypted index archive: outputRoot={outputRoot}, entries={entries.Count}");
        var payload = new EncryptedIndexPayload(entries);
        var json = JsonSerializer.Serialize(payload);
        var plaintextBytes = Encoding.UTF8.GetBytes(json);

        Directory.CreateDirectory(outputRoot);
        var outputArchive = Path.Combine(outputRoot, EncryptedIndexFileName);
        var tempArchive = outputArchive + ".work";

        TryDeleteFileIfExists(tempArchive);

        await using var plaintext = new MemoryStream(plaintextBytes, writable: false);
        var error = await TryEncryptStreamAsync(plaintext, tempArchive, password, ct).ConfigureAwait(false);
        if (error is not null)
        {
            TryDeleteFileIfExists(tempArchive);
            logger?.Error($"Encrypted index creation failed: {error}");
            return new OperationResult
            {
                SuccessOrWarningFlag = false,
                StatusLine = $"Encrypted index creation failed: {error}",
                LogPath = logger?.FullLogPath
            };
        }

        try
        {
            File.Move(tempArchive, outputArchive, overwrite: true);
        }
        catch (Exception ex)
        {
            TryDeleteFileIfExists(tempArchive);
            logger?.Error($"Failed to finalize encrypted index archive: {ex.Message}", ex);
            return new OperationResult
            {
                SuccessOrWarningFlag = false,
                StatusLine = $"Failed to finalize encrypted index archive: {ex.Message}",
                LogPath = logger?.FullLogPath
            };
        }

        logger?.Info($"Encrypted index archive written: {outputArchive}");

        return new OperationResult
        {
            SuccessOrWarningFlag = true,
            StatusLine = $"Encrypted index written to: {outputArchive}",
            LogPath = logger?.FullLogPath
        };
    }

    private static Dictionary<string, EncryptedIndexEntry> BuildEntriesByOriginalPath(IReadOnlyList<EncryptedIndexEntry> entries)
    {
        var map = new Dictionary<string, EncryptedIndexEntry>(GetPathComparer());
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.OriginalRelativePath))
            {
                continue;
            }

            var key = NormalizeRelativePath(entry.OriginalRelativePath);
            map[key] = entry;
        }

        return map;
    }

    private static List<EncryptedIndexEntry> BuildEntriesFromMap(
        IReadOnlyList<EncryptedIndexEntry> originalOrder,
        Dictionary<string, EncryptedIndexEntry> entriesByOriginalPath)
    {
        var list = new List<EncryptedIndexEntry>(originalOrder.Count);
        foreach (var original in originalOrder)
        {
            if (string.IsNullOrWhiteSpace(original.OriginalRelativePath))
            {
                list.Add(original);
                continue;
            }

            var key = NormalizeRelativePath(original.OriginalRelativePath);
            if (entriesByOriginalPath.TryGetValue(key, out var updated))
            {
                list.Add(updated);
            }
            else
            {
                list.Add(original);
            }
        }

        return list;
    }

    private static bool AreEntriesEquivalentForEncryption(
        EncryptedIndexEntry source,
        EncryptedIndexEntry destination)
    {
        if (IsDirectoryIndexEntry(source) && IsDirectoryIndexEntry(destination))
        {
            return true;
        }

        if (IsDirectoryIndexEntry(source) != IsDirectoryIndexEntry(destination))
        {
            return false;
        }

        return string.Equals(source.Crc64Hex, destination.Crc64Hex, StringComparison.OrdinalIgnoreCase)
            && source.SizeBytes == destination.SizeBytes;
    }

    private static async Task<int> ReadCheckpointAsync(string checkpointPath, CancellationToken ct)
    {
        if (!File.Exists(checkpointPath))
        {
            return 0;
        }

        try
        {
            var text = await File.ReadAllTextAsync(checkpointPath, Encoding.UTF8, ct).ConfigureAwait(false);
            if (int.TryParse(text.Trim(), out var index) && index >= 0)
            {
                return index;
            }
        }
        catch
        {
        }

        return 0;
    }

    private static async Task<bool> TryWriteCheckpointAsync(
        string checkpointPath,
        string checkpointWorkPath,
        int nextIndex,
        CancellationToken ct)
    {
        try
        {
            var payload = nextIndex.ToString();
            await File.WriteAllTextAsync(checkpointWorkPath, payload, Encoding.UTF8, ct).ConfigureAwait(false);
            File.Move(checkpointWorkPath, checkpointPath, overwrite: true);
            return true;
        }
        catch
        {
            TryDeleteFileIfExists(checkpointWorkPath);
            return false;
        }
    }

    private static bool TryDeleteCheckpointArtifacts(string checkpointPath, string checkpointWorkPath)
    {
        var ok = true;
        ok &= TryDeleteFileIfExists(checkpointPath);
        ok &= TryDeleteFileIfExists(checkpointWorkPath);
        return ok;
    }

    private static bool TryDeleteFileIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                EnsureFileWritable(path);
                File.Delete(path);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string?> TryEncryptFileAsync(string sourcePath, string encryptedPath, string password, CancellationToken ct)
    {
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 64,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        return await TryEncryptStreamAsync(source, encryptedPath, password, ct).ConfigureAwait(false);
    }

    private async Task<string?> TryEncryptStreamAsync(Stream plaintextStream, string encryptedPath, string password, CancellationToken ct)
    {
        try
        {
            var salt = RandomNumberGenerator.GetBytes(ArchiveSaltLength);
            var iv = RandomNumberGenerator.GetBytes(ArchiveIvLength);
            var (encryptionKey, macKey) = DeriveKeys(password, salt, DefaultKdfIterations);

            await using var output = new FileStream(
                encryptedPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                1024 * 64,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            long macOffset;
            long cipherTextStart;
            WriteArchiveHeader(output, salt, iv, DefaultKdfIterations, out macOffset, out cipherTextStart);

            using var hmac = new HMACSHA256(macKey);
            using (var hmacWrite = new HmacWriteStream(output, hmac))
            using (var aes = Aes.Create())
            {
                aes.Key = encryptionKey;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                await using (var crypto = new CryptoStream(hmacWrite, aes.CreateEncryptor(), CryptoStreamMode.Write, leaveOpen: true))
                await using (var compressor = new BrotliStream(crypto, CompressionLevel.Fastest, leaveOpen: true))
                {
                    plaintextStream.Position = plaintextStream.CanSeek ? 0 : plaintextStream.Position;
                    await plaintextStream.CopyToAsync(compressor, 1024 * 64, ct).ConfigureAwait(false);
                    await compressor.FlushAsync(ct).ConfigureAwait(false);
                }
            }

            hmac.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            var tag = hmac.Hash ?? Array.Empty<byte>();
            if (tag.Length != ArchiveMacLength)
            {
                return "Internal MAC generation error.";
            }

            output.Position = macOffset;
            await output.WriteAsync(tag, ct).ConfigureAwait(false);
            await output.FlushAsync(ct).ConfigureAwait(false);
            output.Position = cipherTextStart;
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ex.Message;
        }
    }

    private async Task<string?> TryDecryptArchiveToStreamAsync(
        string encryptedPath,
        string password,
        Stream plaintextOutput,
        CancellationToken ct)
    {
        try
        {
            await using var input = new FileStream(
                encryptedPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 64,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var header = ReadArchiveHeader(input);
            var (encryptionKey, macKey) = DeriveKeys(password, header.Salt, header.Iterations);
            var validMac = await VerifyArchiveMacAsync(input, header.CipherTextOffset, header.StoredMac, macKey, ct).ConfigureAwait(false);
            if (!validMac)
            {
                return "Wrong password or corrupted archive (integrity check failed).";
            }

            input.Position = header.CipherTextOffset;
            using var aes = Aes.Create();
            aes.Key = encryptionKey;
            aes.IV = header.IV;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            await using var crypto = new CryptoStream(input, aes.CreateDecryptor(), CryptoStreamMode.Read, leaveOpen: true);
            await using var decompressor = new BrotliStream(crypto, CompressionMode.Decompress, leaveOpen: true);

            await decompressor.CopyToAsync(plaintextOutput, 1024 * 64, ct).ConfigureAwait(false);
            await plaintextOutput.FlushAsync(ct).ConfigureAwait(false);
            return null;
        }
        catch (CryptographicException)
        {
            return "Wrong password or corrupted archive.";
        }
        catch (InvalidDataException ex)
        {
            return $"Corrupted compressed payload: {ex.Message}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ex.Message;
        }
    }

    private static (byte[] EncryptionKey, byte[] MacKey) DeriveKeys(string password, byte[] salt, int iterations)
    {
        var material = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 64);
        var encryptionKey = material[..32];
        var macKey = material[32..64];
        return (encryptionKey, macKey);
    }

    private static void WriteArchiveHeader(
        Stream output,
        byte[] salt,
        byte[] iv,
        int iterations,
        out long macOffset,
        out long cipherTextOffset)
    {
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes(ArchiveMagic));
        writer.Write(ArchiveFormatVersion);
        writer.Write(iterations);
        writer.Write((byte)salt.Length);
        writer.Write((byte)iv.Length);
        writer.Write(salt);
        writer.Write(iv);
        macOffset = output.Position;
        writer.Write(new byte[ArchiveMacLength]);
        cipherTextOffset = output.Position;
    }

    private static ArchiveHeader ReadArchiveHeader(Stream input)
    {
        using var reader = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);
        var magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (!string.Equals(magic, ArchiveMagic, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Unknown encrypted archive format.");
        }

        var version = reader.ReadByte();
        if (version != ArchiveFormatVersion)
        {
            throw new InvalidDataException($"Unsupported encrypted archive version: {version}.");
        }

        var iterations = reader.ReadInt32();
        if (iterations < 10_000)
        {
            throw new InvalidDataException("Invalid KDF iteration count.");
        }

        var saltLength = reader.ReadByte();
        var ivLength = reader.ReadByte();
        if (saltLength is <= 0 or > 64 || ivLength is <= 0 or > 32)
        {
            throw new InvalidDataException("Invalid archive header lengths.");
        }

        var salt = reader.ReadBytes(saltLength);
        var iv = reader.ReadBytes(ivLength);
        var storedMac = reader.ReadBytes(ArchiveMacLength);
        if (salt.Length != saltLength || iv.Length != ivLength || storedMac.Length != ArchiveMacLength)
        {
            throw new InvalidDataException("Unexpected end of archive header.");
        }

        return new ArchiveHeader(
            iterations,
            salt,
            iv,
            storedMac,
            input.Position);
    }

    private static async Task<bool> VerifyArchiveMacAsync(
        Stream input,
        long cipherTextOffset,
        byte[] expectedMac,
        byte[] macKey,
        CancellationToken ct)
    {
        input.Position = cipherTextOffset;
        using var hmac = new HMACSHA256(macKey);
        var buffer = new byte[1024 * 64];
        while (true)
        {
            var read = await input.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            hmac.TransformBlock(buffer, 0, read, null, 0);
        }

        hmac.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        var hash = hmac.Hash ?? Array.Empty<byte>();
        return hash.Length == expectedMac.Length && CryptographicOperations.FixedTimeEquals(hash, expectedMac);
    }

    private static string ObfuscatePath(string relativePath)
    {
        var bytes = Encoding.UTF8.GetBytes(relativePath);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static string BuildObfuscatedRelativePath(string relativePath)
    {
        var segments = relativePath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return ObfuscatePath(relativePath);
        }

        var obfuscatedSegments = segments.Select(ObfuscatePath);
        return string.Join(Path.DirectorySeparatorChar, obfuscatedSegments);
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        return relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
    }

    private static bool IsDirectoryIndexEntry(EncryptedIndexEntry entry)
    {
        return entry.EntryKind == IndexEntryKind.Directory
               || (entry.SizeBytes == 0 && string.IsNullOrWhiteSpace(entry.Crc64Hex));
    }

    private static async Task<bool> TryShouldSkipRestoreAsync(
        string targetPath,
        EncryptedIndexEntry entry,
        RestoreSmartMode mode,
        CancellationToken ct)
    {
        if (!File.Exists(targetPath))
        {
            return false;
        }

        FileInfo targetInfo;
        try
        {
            targetInfo = new FileInfo(targetPath);
        }
        catch
        {
            return false;
        }

        if (targetInfo.Length != entry.SizeBytes)
        {
            return false;
        }

        if (mode == RestoreSmartMode.FastSmart)
        {
            return targetInfo.LastWriteTimeUtc == entry.LastWriteTimeUtc;
        }

        if (string.IsNullOrWhiteSpace(entry.Crc64Hex))
        {
            return false;
        }

        var crc = await Crc64Service.ComputeCrc64HexAsync(targetPath, ct).ConfigureAwait(false);
        return string.Equals(crc, entry.Crc64Hex, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMissingEncryptedIndexError(string? error)
    {
        return !string.IsNullOrWhiteSpace(error)
               && error.StartsWith("Encrypted index not found:", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task RefreshSourceIndexForEncryptedOperationsAsync(
        AppConfig config,
        IProgress<ProgressInfo>? progress,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.SourceRoot))
        {
            throw new InvalidOperationException("SourceRoot is not set.");
        }

        if (string.IsNullOrWhiteSpace(config.SourceIndexCsvPath))
        {
            throw new InvalidOperationException("SourceIndexCsvPath is not set.");
        }

        if (!Directory.Exists(config.SourceRoot))
        {
            throw new InvalidOperationException($"Source folder not found: {config.SourceRoot}");
        }

        var sourceIndexExists = File.Exists(config.SourceIndexCsvPath);
        var sourceRefreshIncremental = sourceIndexExists;
        var operationTitle = sourceRefreshIncremental
            ? "Smart refreshing source index for encrypted operation..."
            : "Rebuilding source index for encrypted operation...";

        Bare.Primitive.UI.UiConsole.WriteLine(operationTitle);
        var effectiveProgress = progress ?? new Progress<ProgressInfo>(_ => { });
        await IndexRefreshService.RefreshIndexAsync(
                config.SourceRoot,
                config.SourceIndexCsvPath,
                ct,
                effectiveProgress,
                operationTitle,
                incremental: sourceRefreshIncremental,
                config)
            .ConfigureAwait(false);

        if (!File.Exists(config.SourceIndexCsvPath))
        {
            throw new InvalidOperationException($"Source index refresh failed: {config.SourceIndexCsvPath}");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ReportArchiveProgress(
        IProgress<ProgressInfo> progress,
        string operationTitle,
        int processed,
        int total,
        string? currentItem)
    {
        progress.Report(new ProgressInfo
        {
            Processed = processed,
            Total = total,
            OperationTitle = operationTitle,
            CurrentItem = currentItem
        });
    }

    internal sealed record EncryptedIndexPayload(IReadOnlyList<EncryptedIndexEntry> Entries);

    internal sealed record EncryptedIndexEntry(
        long Id,
        string ObfuscatedName,
        string OriginalRelativePath,
        long SizeBytes,
        DateTime LastWriteTimeUtc,
        string Crc64Hex,
        IndexEntryKind EntryKind = IndexEntryKind.File);

    internal sealed record EncryptedDataPlanItem(
        string SourceRelativePath,
        string ObfuscatedFileName,
        string DestinationRelativePath,
        long SizeBytes);

    private sealed record ArchiveHeader(
        int Iterations,
        byte[] Salt,
        byte[] IV,
        byte[] StoredMac,
        long CipherTextOffset);

    private sealed class HmacWriteStream : Stream
    {
        private readonly Stream _inner;
        private readonly HMAC _hmac;

        public HmacWriteStream(Stream inner, HMAC hmac)
        {
            _inner = inner;
            _hmac = hmac;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            await _inner.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => _inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count)
        {
            _hmac.TransformBlock(buffer, offset, count, null, 0);
            _inner.Write(buffer, offset, count);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.Length > 0)
            {
                var array = buffer.ToArray();
                _hmac.TransformBlock(array, 0, array.Length, null, 0);
                await _inner.WriteAsync(array, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}