using System.Globalization;
using System.Text;
using BareSync.Domain;

namespace BareSync.Infra;

internal static class FileScanner
{
    public static Task<FileIndex> BuildIndexAsync(
        string sourceRoot,
        string? ignoreFullPath,
        CancellationToken ct,
        Action<int, int, string?, string?>? progress = null)
    {
        return BuildIndexAsync(sourceRoot, ignoreFullPath, config: null, ct, progress);
    }

    public static async Task<FileIndex> BuildIndexAsync(
        string sourceRoot,
        string? ignoreFullPath,
        AppConfig? config,
        CancellationToken ct,
        Action<int, int, string?, string?>? progress = null)
    {
        ct.ThrowIfCancellationRequested();

        var fullSourceRoot = Path.GetFullPath(sourceRoot);
        var hasIgnorePath = !string.IsNullOrWhiteSpace(ignoreFullPath);
        var fullIgnorePath = hasIgnorePath
            ? Path.GetFullPath(ignoreFullPath!)
            : string.Empty;
        var ignoreSet = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        if (hasIgnorePath)
        {
            ignoreSet.Add(fullIgnorePath);
        }

        var exclusionMatcher = PathExclusionMatcher.Create(config);
        var items = EnumerateScanItems(fullSourceRoot, ignoreSet, exclusionMatcher, ct, progress);
        var total = items.Count;

        var index = new FileIndex();
        long id = 0;
        var processed = 0;

        if (total > 0)
        {
            progress?.Invoke(processed, total, null, items[0].FullPath);
        }
        else
        {
            progress?.Invoke(processed, total, null, fullSourceRoot);
        }

        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();

            long sizeBytes = 0;
            DateTimeOffset lastWriteUtc = DateTimeOffset.MinValue;
            var crc64Hex = string.Empty;
            string? lastMessage = null;

            progress?.Invoke(processed, total, null, item.FullPath);

            try
            {
                if (item.EntryKind == IndexEntryKind.File)
                {
                    var info = new FileInfo(item.FullPath);
                    sizeBytes = info.Length;
                    lastWriteUtc = info.LastWriteTimeUtc;
                    crc64Hex = await Crc64Service.ComputeCrc64HexAsync(item.FullPath, ct).ConfigureAwait(false);
                }
                else
                {
                    var info = new DirectoryInfo(item.FullPath);
                    sizeBytes = 0;
                    lastWriteUtc = info.LastWriteTimeUtc;
                    crc64Hex = string.Empty;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastMessage = $"Failed to read '{item.FullPath}': {ex.Message}";
                crc64Hex = "ERROR";
            }

            var entry = new FileIndexEntry
            {
                Id = id++,
                EntryKind = item.EntryKind,
                RelativeDir = item.RelativeDir,
                FileName = item.FileName,
                Crc64Hex = crc64Hex,
                SizeBytes = sizeBytes,
                LastWriteTimeUtc = lastWriteUtc
            };

            index.Add(entry);

            processed++;
            progress?.Invoke(processed, total, lastMessage, item.FullPath);
        }

        return index;
    }

    public static Task<int> RebuildIndexResumableAsync(
        string sourceRoot,
        string? ignoreFullPath,
        string fullCsvPath,
        CancellationToken ct,
        Action<int, int, string?, string?>? progress = null,
        int checkpointEveryFiles = 250,
        int checkpointMinIntervalMs = 1500,
        int ioCooldownMs = 0)
    {
        return RebuildIndexResumableAsync(
            sourceRoot,
            ignoreFullPath,
            fullCsvPath,
            config: null,
            ct,
            progress,
            checkpointEveryFiles,
            checkpointMinIntervalMs,
            ioCooldownMs);
    }

    public static async Task<int> RebuildIndexResumableAsync(
        string sourceRoot,
        string? ignoreFullPath,
        string fullCsvPath,
        AppConfig? config,
        CancellationToken ct,
        Action<int, int, string?, string?>? progress = null,
        int checkpointEveryFiles = 250,
        int checkpointMinIntervalMs = 1500,
        int ioCooldownMs = 0)
    {
        ct.ThrowIfCancellationRequested();

        checkpointEveryFiles = checkpointEveryFiles <= 0 ? 1 : checkpointEveryFiles;
        checkpointMinIntervalMs = checkpointMinIntervalMs < 0 ? 0 : checkpointMinIntervalMs;
        ioCooldownMs = ioCooldownMs < 0 ? 0 : ioCooldownMs;

        var fullSourceRoot = Path.GetFullPath(sourceRoot);
        var fullIndexPath = Path.GetFullPath(fullCsvPath);
        var workPath = $"{fullIndexPath}.work";
        var checkpointPath = $"{fullIndexPath}.checkpoint";
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        var ignorePaths = new HashSet<string>(comparer)
        {
            fullIndexPath,
            workPath,
            checkpointPath
        };

        if (!string.IsNullOrWhiteSpace(ignoreFullPath))
        {
            ignorePaths.Add(Path.GetFullPath(ignoreFullPath!));
        }

        var exclusionMatcher = PathExclusionMatcher.Create(config);
        var items = EnumerateScanItems(fullSourceRoot, ignorePaths, exclusionMatcher, ct, progress);

        var total = items.Count;
        var resume = TryLoadResumeState(
            checkpointPath,
            workPath,
            fullSourceRoot,
            fullIndexPath,
            comparison,
            items);

        if (resume is null)
        {
            TryDeleteIfExists(workPath);
            TryDeleteIfExists(checkpointPath);
        }

        var parent = Path.GetDirectoryName(fullIndexPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var processed = resume?.ProcessedCount ?? 0;
        var startedUtc = resume?.StartedUtc ?? DateTime.UtcNow;
        var existingRows = resume is null
            ? ExistingRowsState.Empty(comparer)
            : await LoadExistingRowsStateAsync(workPath, comparer, ct).ConfigureAwait(false);

        using var writer = new IndexRebuildWriter(workPath, checkpointPath, resume is not null);
        if (resume is null)
        {
            await writer.WriteHeaderAsync().ConfigureAwait(false);
        }

        if (total == 0)
        {
            await writer.FinalizeAsync(fullIndexPath).ConfigureAwait(false);
            return 0;
        }

        if (processed >= total)
        {
            await writer.FinalizeAsync(fullIndexPath).ConfigureAwait(false);
            return total;
        }

        string? nextPath = null;
        for (var i = 0; i < items.Count; i++)
        {
            if (!existingRows.Keys.Contains(items[i].RelativeKey))
            {
                nextPath = items[i].FullPath;
                break;
            }
        }

        if (nextPath is not null)
        {
            progress?.Invoke(processed, total, null, nextPath);
        }

        var id = Math.Max(processed, existingRows.MaxId + 1);
        var checkpointInterval = TimeSpan.FromMilliseconds(checkpointMinIntervalMs);
        var lastCheckpointUtc = DateTime.UtcNow;
        var rowsSinceCheckpoint = 0;
        var lastProcessedRelativeKey = string.Empty;

        for (var i = 0; i < items.Count; i++)
        {
            if (ct.IsCancellationRequested)
            {
                if (rowsSinceCheckpoint > 0 && !string.IsNullOrWhiteSpace(lastProcessedRelativeKey))
                {
                    await writer.FlushCheckpointAsync(new IndexCheckpoint(
                            fullSourceRoot,
                            fullIndexPath,
                            startedUtc,
                            lastProcessedRelativeKey,
                            processed))
                        .ConfigureAwait(false);
                }

                ct.ThrowIfCancellationRequested();
            }

            var item = items[i];

            if (existingRows.Keys.Contains(item.RelativeKey))
            {
                continue;
            }

            long sizeBytes = 0;
            DateTimeOffset lastWriteUtc = DateTimeOffset.MinValue;
            var crc64Hex = string.Empty;
            string? lastMessage = null;

            progress?.Invoke(processed, total, null, item.FullPath);

            try
            {
                if (item.EntryKind == IndexEntryKind.File)
                {
                    var info = new FileInfo(item.FullPath);
                    sizeBytes = info.Length;
                    lastWriteUtc = info.LastWriteTimeUtc;
                    crc64Hex = await Crc64Service.ComputeCrc64HexAsync(item.FullPath, ct).ConfigureAwait(false);
                }
                else
                {
                    var info = new DirectoryInfo(item.FullPath);
                    sizeBytes = 0;
                    lastWriteUtc = info.LastWriteTimeUtc;
                    crc64Hex = string.Empty;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastMessage = $"Failed to read '{item.FullPath}': {ex.Message}";
                crc64Hex = "ERROR";
            }

            var entry = new FileIndexEntry
            {
                Id = id++,
                EntryKind = item.EntryKind,
                RelativeDir = item.RelativeDir,
                FileName = item.FileName,
                Crc64Hex = crc64Hex,
                SizeBytes = sizeBytes,
                LastWriteTimeUtc = lastWriteUtc
            };

            await writer.AppendRowAsync(entry).ConfigureAwait(false);
            processed++;
            existingRows.Keys.Add(item.RelativeKey);
            lastProcessedRelativeKey = item.RelativeKey;
            rowsSinceCheckpoint++;
            var shouldCheckpointByRows = rowsSinceCheckpoint >= checkpointEveryFiles;
            var shouldCheckpointByTime = checkpointInterval == TimeSpan.Zero
                || DateTime.UtcNow - lastCheckpointUtc >= checkpointInterval;

            if (shouldCheckpointByRows || shouldCheckpointByTime)
            {
                await writer.FlushCheckpointAsync(new IndexCheckpoint(
                        fullSourceRoot,
                        fullIndexPath,
                        startedUtc,
                        item.RelativeKey,
                        processed))
                    .ConfigureAwait(false);

                rowsSinceCheckpoint = 0;
                lastCheckpointUtc = DateTime.UtcNow;
            }

            if (ioCooldownMs > 0)
            {
                try
                {
                    await Task.Delay(ioCooldownMs, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (rowsSinceCheckpoint > 0 && !string.IsNullOrWhiteSpace(lastProcessedRelativeKey))
                    {
                        await writer.FlushCheckpointAsync(new IndexCheckpoint(
                                fullSourceRoot,
                                fullIndexPath,
                                startedUtc,
                                lastProcessedRelativeKey,
                                processed))
                            .ConfigureAwait(false);
                    }

                    throw;
                }
            }

            progress?.Invoke(processed, total, lastMessage, item.FullPath);
        }

        await writer.FinalizeAsync(fullIndexPath).ConfigureAwait(false);
        return total;
    }

    public static Task<int> BuildIndexIncrementalAsync(
        string sourceRoot,
        string? ignoreFullPath,
        string fullCsvPath,
        CancellationToken ct,
        Action<int, int, string?, string?>? progress = null,
        int checkpointEveryFiles = 250,
        int checkpointMinIntervalMs = 1500,
        int ioCooldownMs = 0)
    {
        return BuildIndexIncrementalAsync(
            sourceRoot,
            ignoreFullPath,
            fullCsvPath,
            config: null,
            ct,
            progress,
            checkpointEveryFiles,
            checkpointMinIntervalMs,
            ioCooldownMs);
    }

    public static async Task<int> BuildIndexIncrementalAsync(
        string sourceRoot,
        string? ignoreFullPath,
        string fullCsvPath,
        AppConfig? config,
        CancellationToken ct,
        Action<int, int, string?, string?>? progress = null,
        int checkpointEveryFiles = 250,
        int checkpointMinIntervalMs = 1500,
        int ioCooldownMs = 0)
    {
        ct.ThrowIfCancellationRequested();

        checkpointEveryFiles = checkpointEveryFiles <= 0 ? 1 : checkpointEveryFiles;
        checkpointMinIntervalMs = checkpointMinIntervalMs < 0 ? 0 : checkpointMinIntervalMs;
        ioCooldownMs = ioCooldownMs < 0 ? 0 : ioCooldownMs;

        var fullSourceRoot = Path.GetFullPath(sourceRoot);
        var fullIndexPath = Path.GetFullPath(fullCsvPath);
        var workPath = $"{fullIndexPath}.work";
        var checkpointPath = $"{fullIndexPath}.checkpoint";
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        var ignorePaths = new HashSet<string>(comparer)
        {
            fullIndexPath,
            workPath,
            checkpointPath
        };

        if (!string.IsNullOrWhiteSpace(ignoreFullPath))
        {
            ignorePaths.Add(Path.GetFullPath(ignoreFullPath!));
        }

        var exclusionMatcher = PathExclusionMatcher.Create(config);
        var items = EnumerateScanItems(fullSourceRoot, ignorePaths, exclusionMatcher, ct, progress);

        var existingIndex = await LoadExistingIndexAsync(fullIndexPath, comparer, ct).ConfigureAwait(false);
        long maxExistingId = -1;
        foreach (var pair in existingIndex)
        {
            if (pair.Value.Id > maxExistingId)
            {
                maxExistingId = pair.Value.Id;
            }
        }

        var total = items.Count;
        var resume = TryLoadResumeState(
            checkpointPath,
            workPath,
            fullSourceRoot,
            fullIndexPath,
            comparison,
            items);

        if (resume is null)
        {
            TryDeleteIfExists(workPath);
            TryDeleteIfExists(checkpointPath);
        }

        var parent = Path.GetDirectoryName(fullIndexPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var processed = resume?.ProcessedCount ?? 0;
        var startedUtc = resume?.StartedUtc ?? DateTime.UtcNow;
        var existingRows = resume is null
            ? ExistingRowsState.Empty(comparer)
            : await LoadExistingRowsStateAsync(workPath, comparer, ct).ConfigureAwait(false);

        using var writer = new IndexRebuildWriter(workPath, checkpointPath, resume is not null);
        if (resume is null)
        {
            await writer.WriteHeaderAsync().ConfigureAwait(false);
        }

        if (total == 0)
        {
            await writer.FinalizeAsync(fullIndexPath).ConfigureAwait(false);
            return 0;
        }

        if (processed >= total)
        {
            await writer.FinalizeAsync(fullIndexPath).ConfigureAwait(false);
            return total;
        }

        string? nextPath = null;
        for (var i = 0; i < items.Count; i++)
        {
            if (!existingRows.Keys.Contains(items[i].RelativeKey))
            {
                nextPath = items[i].FullPath;
                break;
            }
        }

        if (nextPath is not null)
        {
            progress?.Invoke(processed, total, null, nextPath);
        }

        var nextId = Math.Max(Math.Max(maxExistingId, existingRows.MaxId) + 1, 0);
        var checkpointInterval = TimeSpan.FromMilliseconds(checkpointMinIntervalMs);
        var lastCheckpointUtc = DateTime.UtcNow;
        var rowsSinceCheckpoint = 0;
        var lastProcessedRelativeKey = string.Empty;

        for (var i = 0; i < items.Count; i++)
        {
            if (ct.IsCancellationRequested)
            {
                if (rowsSinceCheckpoint > 0 && !string.IsNullOrWhiteSpace(lastProcessedRelativeKey))
                {
                    await writer.FlushCheckpointAsync(new IndexCheckpoint(
                            fullSourceRoot,
                            fullIndexPath,
                            startedUtc,
                            lastProcessedRelativeKey,
                            processed))
                        .ConfigureAwait(false);
                }

                ct.ThrowIfCancellationRequested();
            }

            var item = items[i];

            if (existingRows.Keys.Contains(item.RelativeKey))
            {
                continue;
            }

            var hasExisting = existingIndex.TryGetValue(item.RelativeKey, out var existingRow);
            var idToUse = hasExisting ? existingRow.Id : nextId++;

            long sizeBytes = 0;
            DateTimeOffset lastWriteUtc = DateTimeOffset.MinValue;
            var crc64Hex = string.Empty;
            string? lastMessage = null;

            progress?.Invoke(processed, total, null, item.FullPath);

            try
            {
                if (item.EntryKind == IndexEntryKind.File)
                {
                    var info = new FileInfo(item.FullPath);
                    sizeBytes = info.Length;
                    lastWriteUtc = info.LastWriteTimeUtc;

                    var reuseCrc = hasExisting
                        && existingRow.EntryKind == IndexEntryKind.File
                        && existingRow.SizeBytes == sizeBytes
                        && existingRow.LastWriteTimeUtc == lastWriteUtc.UtcDateTime;

                    crc64Hex = reuseCrc
                        ? existingRow.Crc64Hex
                        : await Crc64Service.ComputeCrc64HexAsync(item.FullPath, ct).ConfigureAwait(false);
                }
                else
                {
                    var info = new DirectoryInfo(item.FullPath);
                    sizeBytes = 0;
                    lastWriteUtc = info.LastWriteTimeUtc;
                    crc64Hex = string.Empty;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastMessage = $"Failed to read '{item.FullPath}': {ex.Message}";
                crc64Hex = "ERROR";
            }

            var entry = new FileIndexEntry
            {
                Id = idToUse,
                EntryKind = item.EntryKind,
                RelativeDir = item.RelativeDir,
                FileName = item.FileName,
                Crc64Hex = crc64Hex,
                SizeBytes = sizeBytes,
                LastWriteTimeUtc = lastWriteUtc
            };

            await writer.AppendRowAsync(entry).ConfigureAwait(false);
            processed++;
            existingRows.Keys.Add(item.RelativeKey);
            lastProcessedRelativeKey = item.RelativeKey;
            rowsSinceCheckpoint++;
            var shouldCheckpointByRows = rowsSinceCheckpoint >= checkpointEveryFiles;
            var shouldCheckpointByTime = checkpointInterval == TimeSpan.Zero
                || DateTime.UtcNow - lastCheckpointUtc >= checkpointInterval;

            if (shouldCheckpointByRows || shouldCheckpointByTime)
            {
                await writer.FlushCheckpointAsync(new IndexCheckpoint(
                        fullSourceRoot,
                        fullIndexPath,
                        startedUtc,
                        item.RelativeKey,
                        processed))
                    .ConfigureAwait(false);

                rowsSinceCheckpoint = 0;
                lastCheckpointUtc = DateTime.UtcNow;
            }

            if (ioCooldownMs > 0)
            {
                try
                {
                    await Task.Delay(ioCooldownMs, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (rowsSinceCheckpoint > 0 && !string.IsNullOrWhiteSpace(lastProcessedRelativeKey))
                    {
                        await writer.FlushCheckpointAsync(new IndexCheckpoint(
                                fullSourceRoot,
                                fullIndexPath,
                                startedUtc,
                                lastProcessedRelativeKey,
                                processed))
                            .ConfigureAwait(false);
                    }

                    throw;
                }
            }

            progress?.Invoke(processed, total, lastMessage, item.FullPath);
        }

        await writer.FinalizeAsync(fullIndexPath).ConfigureAwait(false);
        return total;
    }

    private static string NormalizePath(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            return string.Empty;
        }

        return relativePath.Replace('\\', '/');
    }

    private static List<ScanItem> EnumerateScanItems(
        string fullSourceRoot,
        HashSet<string> ignorePaths,
        PathExclusionMatcher exclusionMatcher,
        CancellationToken ct,
        Action<int, int, string?, string?>? progress)
    {
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        var directorySet = new HashSet<string>(comparer);
        var discoveredDirectories = new List<string>();
        var items = new List<ScanItem>();
        progress?.Invoke(0, -1, null, fullSourceRoot);

        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(fullSourceRoot);

        while (pendingDirectories.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var currentDirectory = pendingDirectories.Pop();

            foreach (var directoryPath in Directory.EnumerateDirectories(currentDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();

                var fullDirectoryPath = Path.GetFullPath(directoryPath);
                var relativeDirectoryPath = NormalizePath(Path.GetRelativePath(fullSourceRoot, fullDirectoryPath));
                var directoryName = Path.GetFileName(fullDirectoryPath);

                if (string.IsNullOrEmpty(relativeDirectoryPath)
                    || exclusionMatcher.ShouldExcludeDirectory(directoryName, relativeDirectoryPath))
                {
                    continue;
                }

                pendingDirectories.Push(fullDirectoryPath);
                discoveredDirectories.Add(fullDirectoryPath);
            }

            foreach (var filePath in Directory.EnumerateFiles(currentDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();

                var fullPath = Path.GetFullPath(filePath);
                if (ignorePaths.Contains(fullPath))
                {
                    continue;
                }

                var relativePath = NormalizePath(Path.GetRelativePath(fullSourceRoot, fullPath));
                var relativeDir = NormalizePath(Path.GetDirectoryName(relativePath) ?? string.Empty);
                var fileName = Path.GetFileName(fullPath);
                if (exclusionMatcher.ShouldExcludeFile(fileName, relativePath))
                {
                    continue;
                }

                var relativeKey = string.IsNullOrEmpty(relativeDir)
                    ? fileName
                    : $"{relativeDir}/{fileName}";

                items.Add(new ScanItem(fullPath, relativeKey, relativeDir, fileName, IndexEntryKind.File));
                progress?.Invoke(items.Count, -1, null, fullPath);

                if (!string.IsNullOrEmpty(relativeDir))
                {
                    directorySet.Add(relativeDir);
                }
            }
        }

        foreach (var directoryPath in discoveredDirectories)
        {
            ct.ThrowIfCancellationRequested();

            var relativePath = NormalizePath(Path.GetRelativePath(fullSourceRoot, directoryPath));
            if (string.IsNullOrEmpty(relativePath))
            {
                continue;
            }

            if (Directory.EnumerateFileSystemEntries(directoryPath).Any())
            {
                continue;
            }

            if (directorySet.Contains(relativePath))
            {
                continue;
            }

            var relativeDir = NormalizePath(Path.GetDirectoryName(relativePath) ?? string.Empty);
            var emptyDirectoryName = Path.GetFileName(directoryPath);
            items.Add(new ScanItem(directoryPath, relativePath, relativeDir, emptyDirectoryName, IndexEntryKind.Directory));
            progress?.Invoke(items.Count, -1, null, directoryPath);
        }

        items.Sort((left, right) => comparer.Compare(left.RelativeKey, right.RelativeKey));
        return items;
    }

    private static async Task<ExistingRowsState> LoadExistingRowsStateAsync(
        string workPath,
        IEqualityComparer<string> comparer,
        CancellationToken ct)
    {
        var keys = new HashSet<string>(comparer);
        long maxId = -1;

        var rows = await CsvIndexReader.ReadAsync(workPath, ct).ConfigureAwait(false);

        foreach (var pair in rows)
        {
            keys.Add(pair.Key);
            if (pair.Value.Id > maxId)
            {
                maxId = pair.Value.Id;
            }
        }

        return new ExistingRowsState(keys, maxId);
    }

    private static async Task<Dictionary<string, IndexRow>> LoadExistingIndexAsync(
        string fullIndexPath,
        IEqualityComparer<string> comparer,
        CancellationToken ct)
    {
        if (!File.Exists(fullIndexPath))
        {
            return new Dictionary<string, IndexRow>(comparer);
        }

        try
        {
            var rows = await CsvIndexReader.ReadAsync(fullIndexPath, ct).ConfigureAwait(false);
            var map = new Dictionary<string, IndexRow>(comparer);
            foreach (var pair in rows)
            {
                map[pair.Key] = pair.Value;
            }

            return map;
        }
        catch
        {
            return new Dictionary<string, IndexRow>(comparer);
        }
    }

    private static bool IsWorkFileCompatible(
        string workPath,
        int processedCount,
        string expectedHeader)
    {
        try
        {
            if (!File.Exists(workPath))
            {
                return false;
            }

            using var reader = new StreamReader(workPath);

            var header = reader.ReadLine();
            if (header is null || header != expectedHeader)
            {
                return false;
            }

            // Count data rows (excluding header)
            var dataRows = 0;
            while (reader.ReadLine() is not null)
            {
                dataRows++;
            }

            // Need header + processedCount rows => dataRows >= processedCount
            return dataRows >= processedCount;
        }
        catch
        {
            return false;
        }
    }

    private static ResumeState? TryLoadResumeState(
        string checkpointPath,
        string workPath,
        string fullSourceRoot,
        string fullIndexPath,
        StringComparison comparison,
        IReadOnlyList<ScanItem> items)
    {
        if (!File.Exists(checkpointPath) || !File.Exists(workPath))
        {
            return null;
        }

        if (!TryReadCheckpoint(checkpointPath, out var checkpoint))
        {
            return null;
        }

        if (!string.Equals(checkpoint.RootPath, fullSourceRoot, comparison)
            || !string.Equals(checkpoint.IndexPath, fullIndexPath, comparison))
        {
            return null;
        }

        if (checkpoint.ProcessedCount <= 0 || checkpoint.ProcessedCount > items.Count)
        {
            return null;
        }

        if (checkpoint.ProcessedCount > int.MaxValue)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(checkpoint.LastRelativePath))
        {
            return null;
        }

        if (!IsWorkFileCompatible(workPath, (int)checkpoint.ProcessedCount, CsvIndexWriter.Header))
        {
            return null;
        }

        var lastIndex = -1;
        for (var i = 0; i < items.Count; i++)
        {
            if (string.Equals(items[i].RelativeKey, checkpoint.LastRelativePath, comparison))
            {
                lastIndex = i;
                break;
            }
        }

        if (lastIndex < 0)
        {
            return null;
        }

        // Note: We don't check checkpoint.ProcessedCount == lastIndex + 1
        // because the position of LastRelativePath may shift when files are
        // added/removed between runs. We only verify that LastRelativePath exists
        // in the current items list and that the .work file is compatible.

        return new ResumeState(
            checkpoint.StartedUtc,
            checkpoint.LastRelativePath,
            (int)checkpoint.ProcessedCount,
            lastIndex + 1);
    }

    private static bool TryReadCheckpoint(string checkpointPath, out IndexCheckpoint checkpoint)
    {
        checkpoint = default;
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var line in File.ReadLines(checkpointPath))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var index = line.IndexOf('=', StringComparison.Ordinal);
                if (index <= 0)
                {
                    continue;
                }

                var key = line[..index].Trim();
                var value = line[(index + 1)..].Trim();
                if (key.Length == 0)
                {
                    continue;
                }

                values[key] = value;
            }

            if (!values.TryGetValue("Version", out var versionText) || versionText != "1")
            {
                return false;
            }

            if (!values.TryGetValue("RootPath", out var rootPath))
            {
                return false;
            }

            if (!values.TryGetValue("IndexPath", out var indexPath))
            {
                return false;
            }

            if (!values.TryGetValue("StartedUtc", out var startedText))
            {
                return false;
            }

            if (!DateTime.TryParse(
                    startedText,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var startedUtc))
            {
                return false;
            }

            if (!values.TryGetValue("LastRelativePath", out var lastRelativePath))
            {
                return false;
            }

            if (!values.TryGetValue("ProcessedCount", out var processedText))
            {
                return false;
            }

            if (!long.TryParse(
                    processedText,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var processedCount))
            {
                return false;
            }

            checkpoint = new IndexCheckpoint(
                rootPath,
                indexPath,
                startedUtc,
                lastRelativePath,
                processedCount);
            return true;
        }
        catch (Exception)
        {
            checkpoint = default;
            return false;
        }
    }

    private static void TryDeleteIfExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup for stale resume files.
        }
    }

    private readonly record struct ScanItem(
        string FullPath,
        string RelativeKey,
        string RelativeDir,
        string FileName,
        IndexEntryKind EntryKind);

    private readonly record struct ExistingRowsState(
        HashSet<string> Keys,
        long MaxId)
    {
        public static ExistingRowsState Empty(IEqualityComparer<string> comparer)
        {
            return new ExistingRowsState(new HashSet<string>(comparer), -1);
        }
    }

    private readonly record struct ResumeState(
        DateTime StartedUtc,
        string LastRelativePath,
        int ProcessedCount,
        int NextIndex);

    private readonly record struct IndexCheckpoint(
        string RootPath,
        string IndexPath,
        DateTime StartedUtc,
        string LastRelativePath,
        long ProcessedCount)
    {
        public string ToContent()
        {
            var builder = new StringBuilder();
            builder.AppendLine("Version=1");
            builder.AppendLine($"RootPath={RootPath}");
            builder.AppendLine($"IndexPath={IndexPath}");
            builder.AppendLine($"StartedUtc={StartedUtc:O}");
            builder.AppendLine($"LastRelativePath={LastRelativePath}");
            builder.AppendLine($"ProcessedCount={ProcessedCount.ToString(CultureInfo.InvariantCulture)}");
            return builder.ToString();
        }
    }

    private sealed class IndexRebuildWriter : IDisposable
    {
        private readonly string _workPath;
        private readonly string _checkpointPath;
        private readonly FileStream _stream;
        private readonly StreamWriter _writer;
        private bool _disposed;
        private int _rowsSinceFlush;

        public const int FlushEvery = 32;

        public IndexRebuildWriter(string workPath, string checkpointPath, bool append)
        {
            _workPath = workPath;
            _checkpointPath = checkpointPath;

            _stream = new FileStream(
                workPath,
                append ? FileMode.Append : FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            _writer = new StreamWriter(_stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        public async Task WriteHeaderAsync()
        {
            await _writer.WriteLineAsync(CsvIndexWriter.Header).ConfigureAwait(false);
            await _writer.FlushAsync().ConfigureAwait(false);
            FlushStream();
        }

        public async Task AppendRowAsync(FileIndexEntry entry)
        {
            await _writer.WriteLineAsync(CsvIndexWriter.FormatLine(entry)).ConfigureAwait(false);
            _rowsSinceFlush++;
            if (_rowsSinceFlush >= FlushEvery)
            {
                await _writer.FlushAsync().ConfigureAwait(false);
                FlushStream();
                _rowsSinceFlush = 0;
            }
        }

        public async Task FlushCheckpointAsync(IndexCheckpoint checkpoint)
        {
            // Flush all buffered data before writing checkpoint
            await _writer.FlushAsync().ConfigureAwait(false);
            FlushStream();
            _rowsSinceFlush = 0;

            var tempPath = $"{_checkpointPath}.tmp";
            File.WriteAllText(tempPath, checkpoint.ToContent(), new UTF8Encoding(false));
            File.Move(tempPath, _checkpointPath, overwrite: true);
        }

        public async Task FinalizeAsync(string finalPath)
        {
            await _writer.FlushAsync().ConfigureAwait(false);
            FlushStream();
            Close();

            ReplaceFile(_workPath, finalPath);
            TryDeleteIfExists(_checkpointPath);
        }

        public void Dispose()
        {
            Close();
        }

        private void Close()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                _writer.Flush();
            }
            catch
            {
            }

            try
            {
                FlushStream();
            }
            catch
            {
            }

            _writer.Dispose();
            _stream.Dispose();
        }

        private void FlushStream()
        {
            _stream.Flush(flushToDisk: true);
        }

        private static void ReplaceFile(string sourcePath, string destinationPath)
        {
            AtomicFileOperations.ReplaceFile(sourcePath, destinationPath);
        }
    }
}
