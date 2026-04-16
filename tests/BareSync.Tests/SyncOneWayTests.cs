using BareSync.App;
using BareSync.Domain;
using BareSync.Infra;
using Xunit;

namespace BareSync.Tests;

public sealed class SyncOneWayTests
{
    private const string CsvHeader = CsvIndexWriter.Header;

    [Fact]
    public void BuildDecisions_ReturnsCopyMissingWhenDestMissing()
    {
        using var temp = new TempDirectory();
        var src = CreateIndex(CreateRow("file.txt", "AAA", 1, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        var dest = new Dictionary<string, IndexRow>(StringComparer.Ordinal);
        var logPath = Path.Combine(temp.RootPath, "decisions.log");

        var decisions = BuildDecisions(src, dest, logPath);

        var decision = Assert.Single(decisions);
        Assert.Equal("file.txt", decision.RelativePath);
        Assert.Equal(SyncDecisionType.Copy, decision.Type);
        Assert.Equal(SyncDecisionReason.MissingInDest, decision.Reason);
    }

    [Fact]
    public void BuildDecisions_ReturnsCopyChangedWhenCrcDiffers()
    {
        using var temp = new TempDirectory();
        var src = CreateIndex(CreateRow("file.txt", "AAA", 1, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        var dest = CreateIndex(CreateRow("file.txt", "BBB", 1, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        var logPath = Path.Combine(temp.RootPath, "decisions.log");
        var mirrorRoot = Path.Combine(temp.RootPath, "dest");
        Directory.CreateDirectory(mirrorRoot);
        File.WriteAllText(Path.Combine(mirrorRoot, "file.txt"), "existing");

        var decisions = BuildDecisions(src, dest, logPath, mirrorRoot);

        var decision = Assert.Single(decisions);
        Assert.Equal(SyncDecisionType.Copy, decision.Type);
        Assert.Equal(SyncDecisionReason.CrcChanged, decision.Reason);
    }

    [Fact]
    public void BuildDecisions_ReturnsSkipWhenCrcAndTimestampMatch()
    {
        using var temp = new TempDirectory();
        var timestamp = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var src = CreateIndex(CreateRow("file.txt", "AAA", 1, timestamp));
        var dest = CreateIndex(CreateRow("file.txt", "AAA", 1, timestamp));
        var logPath = Path.Combine(temp.RootPath, "decisions.log");
        var mirrorRoot = Path.Combine(temp.RootPath, "dest");
        Directory.CreateDirectory(mirrorRoot);
        File.WriteAllText(Path.Combine(mirrorRoot, "file.txt"), "existing");

        var decisions = BuildDecisions(src, dest, logPath, mirrorRoot);

        var decision = Assert.Single(decisions);
        Assert.Equal(SyncDecisionType.Skip, decision.Type);
        Assert.Equal(SyncDecisionReason.CrcSame, decision.Reason);
    }

    [Fact]
    public void BuildDecisions_CopiesWhenTimestampMismatchEvenIfCrcSame()
    {
        using var temp = new TempDirectory();
        var src = CreateIndex(CreateRow("file.txt", "AAA", 1, new DateTime(2024, 1, 1, 0, 0, 1, DateTimeKind.Utc)));
        var dest = CreateIndex(CreateRow("file.txt", "AAA", 1, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        var logPath = Path.Combine(temp.RootPath, "decisions.log");
        var mirrorRoot = Path.Combine(temp.RootPath, "dest");
        Directory.CreateDirectory(mirrorRoot);
        File.WriteAllText(Path.Combine(mirrorRoot, "file.txt"), "existing");

        var decisions = BuildDecisions(src, dest, logPath, mirrorRoot);
        var logContent = File.ReadAllText(logPath);

        var decision = Assert.Single(decisions);
        Assert.Equal(SyncDecisionType.Copy, decision.Type);
        Assert.Equal(SyncDecisionReason.TimestampMismatch, decision.Reason);
        Assert.Contains("CRC same but timestamp differs, scheduling copy: file.txt", logContent);
    }

    [Fact]
    public void BuildDecisions_LogsDestOnlyEntries()
    {
        using var temp = new TempDirectory();
        var src = new Dictionary<string, IndexRow>(StringComparer.Ordinal);
        var dest = CreateIndex(CreateRow("orphan.txt", "AAA", 1, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        var logPath = Path.Combine(temp.RootPath, "decisions.log");

        var decisions = BuildDecisions(src, dest, logPath);
        var logContent = File.ReadAllText(logPath);

        var decision = Assert.Single(decisions);
        Assert.Equal("orphan.txt", decision.RelativePath);
        Assert.Equal(SyncDecisionType.DestOnly, decision.Type);
        Assert.Equal(SyncDecisionReason.DestOnly, decision.Reason);
        Assert.Contains("Destination-only entries: 1", logContent);
        Assert.Contains("Destination-only: orphan.txt", logContent);
    }

    [Fact]
    public void BuildDecisions_WithMirrorEnabled_SchedulesDestOnlyDelete()
    {
        using var temp = new TempDirectory();
        var src = new Dictionary<string, IndexRow>(StringComparer.Ordinal);
        var dest = CreateIndex(CreateRow("orphan.txt", "AAA", 1, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        var logPath = Path.Combine(temp.RootPath, "decisions.log");

        var decisions = BuildDecisions(src, dest, logPath, mirrorRoot: "", mirror: true);
        var logContent = File.ReadAllText(logPath);

        var decision = Assert.Single(decisions);
        Assert.Equal("orphan.txt", decision.RelativePath);
        Assert.Equal(SyncDecisionType.Delete, decision.Type);
        Assert.Equal(SyncDecisionReason.DestOnlyDelete, decision.Reason);
        Assert.Contains("Destination-only entries scheduled for deletion: 1", logContent);
        Assert.Contains("Destination-only (delete): orphan.txt", logContent);
    }

    [Fact]
    public void BuildDecisions_SourceEmptyDirectoryMissingInDest_SchedulesCreateDirectory()
    {
        using var temp = new TempDirectory();
        var src = new Dictionary<string, IndexRow>(StringComparer.Ordinal)
        {
            ["empty"] = new IndexRow(1, "empty", string.Empty, 0, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), IndexEntryKind.Directory)
        };
        var dest = new Dictionary<string, IndexRow>(StringComparer.Ordinal);
        var logPath = Path.Combine(temp.RootPath, "decisions.log");

        var decisions = BuildDecisions(src, dest, logPath, mirrorRoot: temp.RootPath);

        var decision = Assert.Single(decisions);
        Assert.Equal("empty", decision.RelativePath);
        Assert.Equal(SyncDecisionType.CreateDirectory, decision.Type);
        Assert.Equal(SyncDecisionReason.MissingDirectoryInDest, decision.Reason);
    }

    [Fact]
    public void BuildDecisions_WithMirrorEnabled_SchedulesDestOnlyEmptyDirectoryDelete()
    {
        using var temp = new TempDirectory();
        var src = new Dictionary<string, IndexRow>(StringComparer.Ordinal);
        var dest = new Dictionary<string, IndexRow>(StringComparer.Ordinal)
        {
            ["empty"] = new IndexRow(1, "empty", string.Empty, 0, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), IndexEntryKind.Directory)
        };
        var logPath = Path.Combine(temp.RootPath, "decisions.log");

        var decisions = BuildDecisions(src, dest, logPath, mirrorRoot: temp.RootPath, mirror: true);

        var decision = Assert.Single(decisions);
        Assert.Equal("empty", decision.RelativePath);
        Assert.Equal(SyncDecisionType.DeleteDirectory, decision.Type);
        Assert.Equal(SyncDecisionReason.DestOnlyDelete, decision.Reason);
    }

    [Fact]
    public void BuildDecisions_CountsExistenceChecksOnlyWhenCrcMatches()
    {
        using var temp = new TempDirectory();
        var timestamp = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var src = CreateIndex(
            CreateRow("keep.txt", "AAA", 1, timestamp),
            CreateRow("missing.txt", "AAA", 1, timestamp),
            CreateRow("changed.txt", "AAA", 1, timestamp));
        var dest = CreateIndex(
            CreateRow("keep.txt", "AAA", 1, timestamp),
            CreateRow("missing.txt", "AAA", 1, timestamp),
            CreateRow("changed.txt", "BBB", 1, timestamp));

        var mirrorRoot = Path.Combine(temp.RootPath, "dest");
        Directory.CreateDirectory(mirrorRoot);
        File.WriteAllText(Path.Combine(mirrorRoot, "keep.txt"), "keep");
        File.WriteAllText(Path.Combine(mirrorRoot, "changed.txt"), "changed");

        var logPath = Path.Combine(temp.RootPath, "decisions.log");

        var decisions = BuildDecisions(src, dest, logPath, mirrorRoot);
        var logContent = File.ReadAllText(logPath);

        Assert.Equal(3, decisions.Count);
        Assert.Contains(decisions, d => d.RelativePath == "missing.txt" && d.Type == SyncDecisionType.Copy && d.Reason == SyncDecisionReason.MissingInDest);
        Assert.Contains(decisions, d => d.RelativePath == "keep.txt" && d.Type == SyncDecisionType.Skip && d.Reason == SyncDecisionReason.CrcSame);
        Assert.Contains(decisions, d => d.RelativePath == "changed.txt" && d.Type == SyncDecisionType.Copy && d.Reason == SyncDecisionReason.CrcChanged);
        Assert.Contains("Destination existence checks: 2", logContent);
    }

    [Fact]
    public void BuildDecisions_SkipsSourceEntriesMarkedAsReadError()
    {
        using var temp = new TempDirectory();
        var timestamp = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var src = CreateIndex(CreateRow("volatile.xlsx", "ERROR", 0, timestamp));
        var dest = CreateIndex(CreateRow("volatile.xlsx", "ABC123", 10, timestamp));
        var logPath = Path.Combine(temp.RootPath, "decisions.log");

        var decisions = BuildDecisions(src, dest, logPath, mirrorRoot: temp.RootPath);
        var logContent = File.ReadAllText(logPath);

        var decision = Assert.Single(decisions);
        Assert.Equal("volatile.xlsx", decision.RelativePath);
        Assert.Equal(SyncDecisionType.Skip, decision.Type);
        Assert.Equal(SyncDecisionReason.SourceReadError, decision.Reason);
        Assert.Contains("Source entries skipped because unreadable during source index refresh: 1", logContent);
        Assert.Contains("Source unreadable (skip): volatile.xlsx", logContent);
    }

    [Fact]
    public async Task ApplyDecisionsAsync_SourceReadErrorDecision_ProducesWarningWithoutCopyAttempt()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.RootPath, "src");
        var destRoot = Path.Combine(temp.RootPath, "dest");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(destRoot);

        var decisions = new List<SyncDecision>
        {
            new("volatile.xlsx", SyncDecisionType.Skip, SyncDecisionReason.SourceReadError)
        };

        var sourceDirectoryPaths = new HashSet<string>(StringComparer.Ordinal);
        var logPath = Path.Combine(temp.RootPath, "apply.log");
        using var logger = new SimpleFileLogger(logPath, flushEveryLines: 1);

        var summary = await SyncOneWay.ApplyDecisionsAsync(
            decisions,
            sourceRoot,
            destRoot,
            sourceDirectoryPaths,
            dryRun: false,
            logger,
            CancellationToken.None);

        var logContent = File.ReadAllText(logPath);
        Assert.Equal(1, summary.SourceCount);
        Assert.Equal(0, summary.CopyAttemptedCount);
        Assert.Equal(0, summary.CopiedCount);
        Assert.Equal(0, summary.ErrorCount);
        Assert.Equal(1, summary.WarnCount);
        Assert.Contains("Skipping copy because source entry is unreadable in source index (CRC=ERROR): volatile.xlsx", logContent);
    }

    [Fact]
    public async Task ApplyDecisionsAsync_CopyDecisionWithMissingSource_LogsWarningAndSkipsError()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.RootPath, "src");
        var destRoot = Path.Combine(temp.RootPath, "dest");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(destRoot);

        var decisions = new List<SyncDecision>
        {
            new("gone.xlsx", SyncDecisionType.Copy, SyncDecisionReason.MissingInDest)
        };

        var sourceDirectoryPaths = new HashSet<string>(StringComparer.Ordinal);
        var logPath = Path.Combine(temp.RootPath, "apply.log");
        using var logger = new SimpleFileLogger(logPath, flushEveryLines: 1);

        var summary = await SyncOneWay.ApplyDecisionsAsync(
            decisions,
            sourceRoot,
            destRoot,
            sourceDirectoryPaths,
            dryRun: false,
            logger,
            CancellationToken.None);

        var logContent = File.ReadAllText(logPath);
        Assert.Equal(1, summary.CopyAttemptedCount);
        Assert.Equal(0, summary.CopiedCount);
        Assert.Equal(0, summary.ErrorCount);
        Assert.Equal(1, summary.WarnCount);
        Assert.False(File.Exists(Path.Combine(destRoot, "gone.xlsx")));
        Assert.Contains("Skipping copy because source file is absent on disk at copy time: gone.xlsx", logContent);
    }

    [Fact]
    public void GetFinalStatus_ReturnsFailWhenErrorsExist()
    {
        var status = SyncOneWay.GetFinalStatus(errorCount: 1, destOnlyCount: 0, warnCount: 0);

        Assert.Equal(SyncFinalStatus.Fail, status);
    }

    [Fact]
    public void GetFinalStatus_ReturnsWarningWhenDestOnlyOrWarnings()
    {
        var status = SyncOneWay.GetFinalStatus(errorCount: 0, destOnlyCount: 1, warnCount: 0);
        var statusWithWarn = SyncOneWay.GetFinalStatus(errorCount: 0, destOnlyCount: 0, warnCount: 2);

        Assert.Equal(SyncFinalStatus.Warning, status);
        Assert.Equal(SyncFinalStatus.Warning, statusWithWarn);
    }

    [Fact]
    public void GetFinalStatus_ReturnsSuccessWhenNoErrorsOrWarnings()
    {
        var status = SyncOneWay.GetFinalStatus(errorCount: 0, destOnlyCount: 0, warnCount: 0);

        Assert.Equal(SyncFinalStatus.Success, status);
    }

    [Fact]
    public void CalculateSuccessRate_Returns100WhenNoCopiesAttempted()
    {
        var rate = SyncOneWay.CalculateSuccessRate(copiedCount: 0, copyAttemptedCount: 0);

        Assert.Equal(100, rate);
    }

    [Fact]
    public void GetStatusExplanation_ReturnsDestOnlyExplanationWhenNoErrorsAndNoWarnings()
    {
        var explanation = SyncOneWay.GetStatusExplanation(errorCount: 0, destOnlyCount: 3, warnCount: 0);

        Assert.Equal(
            "WARNING is due to destination-only entries detected (these files are not copied by design).",
            explanation);
    }

    [Fact]
    public void GetStatusExplanation_ReturnsEmptyWhenNotDestOnlyCase()
    {
        var withErrors = SyncOneWay.GetStatusExplanation(errorCount: 1, destOnlyCount: 3, warnCount: 0);
        var withWarnings = SyncOneWay.GetStatusExplanation(errorCount: 0, destOnlyCount: 3, warnCount: 1);
        var success = SyncOneWay.GetStatusExplanation(errorCount: 0, destOnlyCount: 0, warnCount: 0);

        Assert.Equal(string.Empty, withErrors);
        Assert.Equal(string.Empty, withWarnings);
        Assert.Equal(string.Empty, success);
    }

    [Fact]
    public async Task RunAsync_CopiesMissingFilesAndWritesDestIndex()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.RootPath, "src");
        var destRoot = Path.Combine(temp.RootPath, "dest");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(destRoot);

        var alphaPath = Path.Combine(sourceRoot, "alpha.txt");
        var subDir = Path.Combine(sourceRoot, "Sub");
        Directory.CreateDirectory(subDir);
        var bravoPath = Path.Combine(subDir, "bravo.txt");
        await File.WriteAllTextAsync(alphaPath, "alpha");
        await File.WriteAllTextAsync(bravoPath, "bravo");

        var sourceIndexPath = Path.Combine(sourceRoot, "baresync_source_index.csv");
        var index = await FileScanner.BuildIndexAsync(sourceRoot, sourceIndexPath, CancellationToken.None);
        await CsvIndexWriter.WriteAsync(index, sourceIndexPath, CancellationToken.None);

        var destIndexPath = Path.Combine(destRoot, "baresync_dest_index.csv");
        var config = new AppConfig
        {
            SourceRoot = sourceRoot,
            MirrorRoot = destRoot,
            SourceIndexCsvPath = sourceIndexPath,
            DestIndexCsvPath = destIndexPath
        };

        var summary = await SyncOneWay.RunAsync(config, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(destRoot, "alpha.txt")));
        Assert.True(File.Exists(Path.Combine(destRoot, "Sub", "bravo.txt")));
        Assert.Equal(2, summary.CopyAttemptedCount);
        Assert.Equal(2, summary.CopiedCount);
        Assert.Equal(0, summary.ErrorCount);
        Assert.True(File.Exists(destIndexPath));
        var lines = File.ReadAllLines(destIndexPath);
        Assert.True(lines.Length >= 3);
        Assert.Equal(CsvHeader, lines[0]);
        Assert.Contains(lines, line => line.Contains("alpha.txt", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("bravo.txt", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_RebuildsSourceIndex_WhenMissing()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.RootPath, "src");
        var destRoot = Path.Combine(temp.RootPath, "dest");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(destRoot);

        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "alpha.txt"), "alpha");

        var sourceIndexPath = Path.Combine(sourceRoot, "baresync_source_index.csv");
        var destIndexPath = Path.Combine(destRoot, "baresync_dest_index.csv");

        var config = new AppConfig
        {
            SourceRoot = sourceRoot,
            MirrorRoot = destRoot,
            SourceIndexCsvPath = sourceIndexPath,
            DestIndexCsvPath = destIndexPath
        };

        var summary = await SyncOneWay.RunAsync(config, CancellationToken.None);

        Assert.Equal(0, summary.ErrorCount);
        Assert.True(File.Exists(sourceIndexPath));
        Assert.True(File.Exists(Path.Combine(destRoot, "alpha.txt")));

        var sourceRows = await CsvIndexReader.ReadAsync(sourceIndexPath, CancellationToken.None);
        Assert.Contains("alpha.txt", sourceRows.Keys);

        var logContent = await File.ReadAllTextAsync(summary.LogFilePath);
        Assert.Contains("Source index missing; rebuilding source index...", logContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateDestIndexAfterSyncAsync_ReplacesExistingIndexAndLeavesNoWorkFile()
    {
        using var temp = new TempDirectory();
        var destRoot = Path.Combine(temp.RootPath, "dest");
        Directory.CreateDirectory(destRoot);

        var destIndexPath = Path.Combine(destRoot, "baresync_dest_index.csv");
        var initialIndex = new FileIndex();
        initialIndex.Add(new FileIndexEntry
        {
            Id = 42,
            RelativeDir = string.Empty,
            FileName = "alpha.txt",
            Crc64Hex = "OLDCRC",
            SizeBytes = 1,
            LastWriteTimeUtc = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)
        });
        await CsvIndexWriter.WriteAsync(initialIndex, destIndexPath, CancellationToken.None);

        var touched = new[]
        {
            new SyncedDestEntry(
                DestRelativePath: "alpha.txt",
                SizeBytes: 10,
                LastWriteTimeUtc: new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                Crc64Hex: "NEWALPHA",
                EntryKind: IndexEntryKind.File),
            new SyncedDestEntry(
                DestRelativePath: "Sub/beta.txt",
                SizeBytes: 20,
                LastWriteTimeUtc: new DateTime(2024, 2, 2, 0, 0, 0, DateTimeKind.Utc),
                Crc64Hex: "NEWBETA",
                EntryKind: IndexEntryKind.File)
        };

        var deleted = new[] { "ghost.txt" };
        var config = new AppConfig
        {
            DestIndexCsvPath = destIndexPath
        };

        var count = await SyncOneWay.UpdateDestIndexAfterSyncAsync(
            config,
            touched,
            deleted,
            CancellationToken.None);

        Assert.Equal(2, count);
        Assert.False(File.Exists($"{destIndexPath}.work"));

        var rows = await CsvIndexReader.ReadAsync(destIndexPath, CancellationToken.None);
        Assert.Equal(2, rows.Count);
        Assert.Equal(42, rows["alpha.txt"].Id);
        Assert.Equal("NEWALPHA", rows["alpha.txt"].Crc64Hex);
        Assert.Equal("NEWBETA", rows["Sub/beta.txt"].Crc64Hex);
        Assert.False(rows.ContainsKey("ghost.txt"));
    }

    [Fact]
    public async Task CopyFileWithCrcAsync_OverwritesExistingReadOnlyDestination()
    {
        using var temp = new TempDirectory();
        var sourcePath = Path.Combine(temp.RootPath, "src.txt");
        var destPath = Path.Combine(temp.RootPath, "dest.txt");

        await File.WriteAllTextAsync(sourcePath, "new-content");
        await File.WriteAllTextAsync(destPath, "old-content");
        File.SetAttributes(destPath, File.GetAttributes(destPath) | FileAttributes.ReadOnly);

        var crc = await SyncOneWay.CopyFileWithCrcAsync(sourcePath, destPath, CancellationToken.None);

        var destContent = await File.ReadAllTextAsync(destPath);
        Assert.Equal("new-content", destContent);
        Assert.False((File.GetAttributes(destPath) & FileAttributes.ReadOnly) != 0);
        Assert.False(string.IsNullOrWhiteSpace(crc));
    }

    [Fact]
    public async Task CopyFileWithCrcAsync_RetriesWhenDestinationTemporarilyLocked()
    {
        using var temp = new TempDirectory();
        var sourcePath = Path.Combine(temp.RootPath, "src.txt");
        var destPath = Path.Combine(temp.RootPath, "dest.txt");

        await File.WriteAllTextAsync(sourcePath, "retry-content");
        await File.WriteAllTextAsync(destPath, "initial-content");

        using var lockHandle = new FileStream(
            destPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        var copyTask = SyncOneWay.CopyFileWithCrcAsync(sourcePath, destPath, CancellationToken.None);
        await Task.Delay(250);
        lockHandle.Dispose();

        var crc = await copyTask;
        var destContent = await File.ReadAllTextAsync(destPath);
        Assert.Equal("retry-content", destContent);
        Assert.False(string.IsNullOrWhiteSpace(crc));
    }

    private static IndexRow CreateRow(string path, string crc, long sizeBytes, DateTime lastWriteTimeUtc)
    {
        return new IndexRow(IdPool.Next(), path, crc, sizeBytes, lastWriteTimeUtc);
    }

    private static class IdPool
    {
        private static long _next;

        public static long Next()
        {
            return Interlocked.Increment(ref _next) - 1;
        }
    }

    private static Dictionary<string, IndexRow> CreateIndex(params IndexRow[] rows)
    {
        var index = new Dictionary<string, IndexRow>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            index[row.RelativePath] = row;
        }

        return index;
    }

    private static List<SyncDecision> BuildDecisions(
        IReadOnlyDictionary<string, IndexRow> sourceIndex,
        IReadOnlyDictionary<string, IndexRow> destinationIndex,
        string logPath,
        string mirrorRoot = "",
        bool mirror = false)
    {
        using var logger = new SimpleFileLogger(logPath, flushEveryLines: 1);
        return SyncOneWay.BuildDecisions(sourceIndex, destinationIndex, logger, mirrorRoot, mirror);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            RootPath = Path.Combine(
                Path.GetTempPath(),
                "BareSyncTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public void Dispose()
        {
            if (!Directory.Exists(RootPath))
            {
                return;
            }

            try
            {
                Directory.Delete(RootPath, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for temp directories.
            }
        }
    }
}
