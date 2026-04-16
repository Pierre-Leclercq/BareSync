using System.Text;
using BareSync.App;
using BareSync.Domain;
using BareSync.Infra;
using Xunit;

namespace BareSync.Tests;

public sealed class SyncEdgeCaseTests
{
    private const string CsvHeader = CsvIndexWriter.Header;

    [Fact]
    public async Task RunAsync_DestOnlyEntriesDoNotTriggerCopy()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.RootPath, "src");
        var destRoot = Path.Combine(temp.RootPath, "dest");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(destRoot);

        var sourcePath = Path.Combine(sourceRoot, "a.txt");
        var destPath = Path.Combine(destRoot, "a.txt");
        var orphanPath = Path.Combine(destRoot, "orphan.txt");
        await File.WriteAllTextAsync(sourcePath, "alpha");
        await File.WriteAllTextAsync(destPath, "alpha");
        await File.WriteAllTextAsync(orphanPath, "orphan");

        var fixedTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(sourcePath, fixedTime);
        File.SetLastWriteTimeUtc(destPath, fixedTime);
        File.SetLastWriteTimeUtc(orphanPath, fixedTime);

        var sourceIndexPath = Path.Combine(sourceRoot, "baresync_source_index.csv");
        var destIndexPath = Path.Combine(destRoot, "baresync_dest_index.csv");
        await BuildAndWriteIndexAsync(sourceRoot, sourceIndexPath);
        await BuildAndWriteIndexAsync(destRoot, destIndexPath);

        var sourceRows = await CsvIndexReader.ReadAsync(sourceIndexPath, CancellationToken.None);
        var destRows = await CsvIndexReader.ReadAsync(destIndexPath, CancellationToken.None);
        var decisionLogPath = Path.Combine(temp.RootPath, "decisions.log");
        using (var decisionLogger = new SimpleFileLogger(decisionLogPath, flushEveryLines: 1))
        {
            var decisions = SyncOneWay.BuildDecisions(sourceRows, destRows, decisionLogger, destRoot);
            var decision = decisions.Single(entry => entry.RelativePath == "a.txt");

            Assert.Equal(SyncDecisionType.Skip, decision.Type);
        }

        var config = new AppConfig
        {
            SourceRoot = sourceRoot,
            MirrorRoot = destRoot,
            SourceIndexCsvPath = sourceIndexPath,
            DestIndexCsvPath = destIndexPath
        };

        var summary = await SyncOneWay.RunAsync(config, CancellationToken.None);

        Assert.Equal(0, summary.ErrorCount);
        Assert.Equal(1, summary.DestOnlyCount);
        Assert.Equal(0, summary.WarnCount);
        Assert.Equal(0, summary.CopyAttemptedCount);
        var status = SyncOneWay.GetFinalStatus(summary.ErrorCount, summary.DestOnlyCount, summary.WarnCount);
        Assert.Equal(SyncFinalStatus.Warning, status);

        var reportContent = await File.ReadAllTextAsync(summary.ReportFilePath);
        Assert.Contains(
            "WARNING is due to destination-only entries detected (these files are not copied by design).",
            reportContent,
            StringComparison.Ordinal);

        var logContent = await File.ReadAllTextAsync(summary.LogFilePath);
        Assert.Contains(
            "Final status explanation: WARNING is due to destination-only entries detected (these files are not copied by design).",
            logContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_MirrorEnabled_DeletesDestOnlyEntries()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.RootPath, "src");
        var destRoot = Path.Combine(temp.RootPath, "dest");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(destRoot);

        var sourcePath = Path.Combine(sourceRoot, "a.txt");
        var destPath = Path.Combine(destRoot, "a.txt");
        var orphanPath = Path.Combine(destRoot, "orphan.txt");
        await File.WriteAllTextAsync(sourcePath, "alpha");
        await File.WriteAllTextAsync(destPath, "alpha");
        await File.WriteAllTextAsync(orphanPath, "orphan");

        var fixedTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(sourcePath, fixedTime);
        File.SetLastWriteTimeUtc(destPath, fixedTime);
        File.SetLastWriteTimeUtc(orphanPath, fixedTime);

        var sourceIndexPath = Path.Combine(sourceRoot, "baresync_source_index.csv");
        var destIndexPath = Path.Combine(destRoot, "baresync_dest_index.csv");
        await BuildAndWriteIndexAsync(sourceRoot, sourceIndexPath);
        await BuildAndWriteIndexAsync(destRoot, destIndexPath);

        var config = new AppConfig
        {
            SourceRoot = sourceRoot,
            MirrorRoot = destRoot,
            Mirror = true,
            SourceIndexCsvPath = sourceIndexPath,
            DestIndexCsvPath = destIndexPath
        };

        var summary = await SyncOneWay.RunAsync(config, CancellationToken.None);

        Assert.Equal(0, summary.ErrorCount);
        Assert.Equal(0, summary.DestOnlyCount);
        Assert.Equal(0, summary.WarnCount);
        Assert.Equal(1, summary.DeleteAttemptedCount);
        Assert.Equal(1, summary.DeletedCount);
        Assert.False(File.Exists(orphanPath));

        var status = SyncOneWay.GetFinalStatus(summary.ErrorCount, summary.DestOnlyCount, summary.WarnCount);
        Assert.Equal(SyncFinalStatus.Success, status);

        var logContent = await File.ReadAllTextAsync(summary.LogFilePath);
        Assert.Contains("Deleted (destination-only): orphan.txt", logContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_MirrorEnabled_DeletesEmptyParentDirectoriesRecursively()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.RootPath, "src");
        var destRoot = Path.Combine(temp.RootPath, "dest");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(destRoot);

        var sharedSourceDir = Path.Combine(sourceRoot, "shared");
        Directory.CreateDirectory(sharedSourceDir);

        var leafDir = Path.Combine(destRoot, "orphan", "a", "b");
        Directory.CreateDirectory(leafDir);

        var sourceIndexPath = Path.Combine(sourceRoot, "baresync_source_index.csv");
        var destIndexPath = Path.Combine(destRoot, "baresync_dest_index.csv");
        await BuildAndWriteIndexAsync(sourceRoot, sourceIndexPath);
        await BuildAndWriteIndexAsync(destRoot, destIndexPath);

        var config = new AppConfig
        {
            SourceRoot = sourceRoot,
            MirrorRoot = destRoot,
            Mirror = true,
            SourceIndexCsvPath = sourceIndexPath,
            DestIndexCsvPath = destIndexPath
        };

        var summary = await SyncOneWay.RunAsync(config, CancellationToken.None);

        Assert.Equal(0, summary.ErrorCount);
        Assert.Equal(1, summary.DeleteAttemptedCount);
        Assert.Equal(3, summary.DeletedCount);
        Assert.False(Directory.Exists(Path.Combine(destRoot, "orphan", "a", "b")));
        Assert.False(Directory.Exists(Path.Combine(destRoot, "orphan", "a")));
        Assert.False(Directory.Exists(Path.Combine(destRoot, "orphan")));

        var rows = await CsvIndexReader.ReadAsync(destIndexPath, CancellationToken.None);
        Assert.DoesNotContain("orphan", rows.Keys);
        Assert.DoesNotContain("orphan/a", rows.Keys);
        Assert.DoesNotContain("orphan/a/b", rows.Keys);
    }

    [Fact]
    public async Task RunAsync_MirrorEnabled_StopsRecursiveDeleteWhenParentExistsInSource()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.RootPath, "src");
        var destRoot = Path.Combine(temp.RootPath, "dest");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(destRoot);

        Directory.CreateDirectory(Path.Combine(sourceRoot, "keep"));
        Directory.CreateDirectory(Path.Combine(destRoot, "keep", "orphan"));

        var sourceIndexPath = Path.Combine(sourceRoot, "baresync_source_index.csv");
        var destIndexPath = Path.Combine(destRoot, "baresync_dest_index.csv");
        await BuildAndWriteIndexAsync(sourceRoot, sourceIndexPath);
        await BuildAndWriteIndexAsync(destRoot, destIndexPath);

        var config = new AppConfig
        {
            SourceRoot = sourceRoot,
            MirrorRoot = destRoot,
            Mirror = true,
            SourceIndexCsvPath = sourceIndexPath,
            DestIndexCsvPath = destIndexPath
        };

        var summary = await SyncOneWay.RunAsync(config, CancellationToken.None);

        Assert.Equal(0, summary.ErrorCount);
        Assert.Equal(1, summary.DeleteAttemptedCount);
        Assert.Equal(1, summary.DeletedCount);
        Assert.False(Directory.Exists(Path.Combine(destRoot, "keep", "orphan")));
        Assert.True(Directory.Exists(Path.Combine(destRoot, "keep")));
    }

    [Fact]
    public async Task RunAsync_MirrorEnabled_StopsRecursiveDeleteWhenParentIsNotEmpty()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.RootPath, "src");
        var destRoot = Path.Combine(temp.RootPath, "dest");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(destRoot);

        Directory.CreateDirectory(Path.Combine(destRoot, "orphan", "leaf"));

        var sourceIndexPath = Path.Combine(sourceRoot, "baresync_source_index.csv");
        var destIndexPath = Path.Combine(destRoot, "baresync_dest_index.csv");
        await BuildAndWriteIndexAsync(sourceRoot, sourceIndexPath);
        await BuildAndWriteIndexAsync(destRoot, destIndexPath);

        // Entry created after destination index build: not managed by sync decisions,
        // keeps parent directory non-empty during recursive cleanup.
        var runtimeFilePath = Path.Combine(destRoot, "orphan", "runtime.tmp");
        await File.WriteAllTextAsync(runtimeFilePath, "runtime");

        var config = new AppConfig
        {
            SourceRoot = sourceRoot,
            MirrorRoot = destRoot,
            Mirror = true,
            SourceIndexCsvPath = sourceIndexPath,
            DestIndexCsvPath = destIndexPath
        };

        var summary = await SyncOneWay.RunAsync(config, CancellationToken.None);

        Assert.Equal(0, summary.ErrorCount);
        Assert.Equal(1, summary.DeleteAttemptedCount); // orphan/leaf directory (empty directory indexed)
        Assert.Equal(1, summary.DeletedCount);
        Assert.Equal(0, summary.WarnCount);
        Assert.True(File.Exists(runtimeFilePath));
        Assert.False(Directory.Exists(Path.Combine(destRoot, "orphan", "leaf")));
        Assert.True(Directory.Exists(Path.Combine(destRoot, "orphan")));
    }

    [Fact]
    public async Task RunAsync_CopiesEmptySourceDirectoryToDestination()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.RootPath, "src");
        var destRoot = Path.Combine(temp.RootPath, "dest");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(destRoot);

        var emptyDir = Path.Combine(sourceRoot, "empty");
        Directory.CreateDirectory(emptyDir);

        var sourceIndexPath = Path.Combine(sourceRoot, "baresync_source_index.csv");
        var destIndexPath = Path.Combine(destRoot, "baresync_dest_index.csv");
        await BuildAndWriteIndexAsync(sourceRoot, sourceIndexPath);
        await BuildAndWriteIndexAsync(destRoot, destIndexPath);

        var config = new AppConfig
        {
            SourceRoot = sourceRoot,
            MirrorRoot = destRoot,
            SourceIndexCsvPath = sourceIndexPath,
            DestIndexCsvPath = destIndexPath
        };

        var summary = await SyncOneWay.RunAsync(config, CancellationToken.None);

        Assert.Equal(0, summary.ErrorCount);
        Assert.True(Directory.Exists(Path.Combine(destRoot, "empty")));

        var rows = await CsvIndexReader.ReadAsync(destIndexPath, CancellationToken.None);
        Assert.True(rows.TryGetValue("empty", out var entry));
        Assert.Equal(IndexEntryKind.Directory, entry.EntryKind);
    }

    [Fact]
    public async Task RunAsync_MirrorDisabled_DoesNotDeleteDestOnlyEmptyDirectory()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.RootPath, "src");
        var destRoot = Path.Combine(temp.RootPath, "dest");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(destRoot);
        Directory.CreateDirectory(Path.Combine(destRoot, "orphan-empty"));

        var sourceIndexPath = Path.Combine(sourceRoot, "baresync_source_index.csv");
        var destIndexPath = Path.Combine(destRoot, "baresync_dest_index.csv");
        await BuildAndWriteIndexAsync(sourceRoot, sourceIndexPath);
        await BuildAndWriteIndexAsync(destRoot, destIndexPath);

        var config = new AppConfig
        {
            SourceRoot = sourceRoot,
            MirrorRoot = destRoot,
            SourceIndexCsvPath = sourceIndexPath,
            DestIndexCsvPath = destIndexPath,
            Mirror = false
        };

        var summary = await SyncOneWay.RunAsync(config, CancellationToken.None);

        Assert.Equal(0, summary.ErrorCount);
        Assert.Equal(1, summary.DestOnlyCount);
        Assert.True(Directory.Exists(Path.Combine(destRoot, "orphan-empty")));
    }

    [Fact]
    public async Task RunAsync_CopiesWhenCrcMatchesButTimestampDiffers()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.RootPath, "src");
        var destRoot = Path.Combine(temp.RootPath, "dest");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(destRoot);

        var sourcePath = Path.Combine(sourceRoot, "alpha.txt");
        var destPath = Path.Combine(destRoot, "alpha.txt");
        await File.WriteAllTextAsync(sourcePath, "same");
        await File.WriteAllTextAsync(destPath, "same");

        var sourceTime = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var destTime = sourceTime.AddMinutes(10);
        File.SetLastWriteTimeUtc(sourcePath, sourceTime);
        File.SetLastWriteTimeUtc(destPath, destTime);

        var sourceIndexPath = Path.Combine(sourceRoot, "baresync_source_index.csv");
        var destIndexPath = Path.Combine(destRoot, "baresync_dest_index.csv");
        await BuildAndWriteIndexAsync(sourceRoot, sourceIndexPath);
        await BuildAndWriteIndexAsync(destRoot, destIndexPath);

        var config = new AppConfig
        {
            SourceRoot = sourceRoot,
            MirrorRoot = destRoot,
            SourceIndexCsvPath = sourceIndexPath,
            DestIndexCsvPath = destIndexPath
        };

        var summary = await SyncOneWay.RunAsync(config, CancellationToken.None);

        Assert.Equal(1, summary.CopyAttemptedCount);
        Assert.Equal(1, summary.CopiedCount);
        Assert.Equal(0, summary.WarnCount);
        Assert.Equal(0, summary.ErrorCount);
        Assert.Equal(0, summary.DestOnlyCount);
        var status = SyncOneWay.GetFinalStatus(summary.ErrorCount, summary.DestOnlyCount, summary.WarnCount);
        Assert.Equal(SyncFinalStatus.Success, status);

        var postDestInfo = new FileInfo(destPath);
        Assert.Equal(sourceTime, postDestInfo.LastWriteTimeUtc);
    }

    [Fact]
    public async Task RunAsync_OverwritesWhenCrcDiffers()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.RootPath, "src");
        var destRoot = Path.Combine(temp.RootPath, "dest");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(destRoot);

        var sourcePath = Path.Combine(sourceRoot, "alpha.txt");
        var destPath = Path.Combine(destRoot, "alpha.txt");
        await File.WriteAllTextAsync(sourcePath, "NEW");
        await File.WriteAllTextAsync(destPath, "OLD");

        var sourceIndexPath = Path.Combine(sourceRoot, "baresync_source_index.csv");
        var destIndexPath = Path.Combine(destRoot, "baresync_dest_index.csv");
        await BuildAndWriteIndexAsync(sourceRoot, sourceIndexPath);
        await BuildAndWriteIndexAsync(destRoot, destIndexPath);

        var config = new AppConfig
        {
            SourceRoot = sourceRoot,
            MirrorRoot = destRoot,
            SourceIndexCsvPath = sourceIndexPath,
            DestIndexCsvPath = destIndexPath
        };

        var summary = await SyncOneWay.RunAsync(config, CancellationToken.None);

        Assert.Equal(1, summary.CopyAttemptedCount);
        Assert.Equal(1, summary.CopiedCount);
        Assert.Equal(0, summary.ErrorCount);
        Assert.Equal(0, summary.WarnCount);
        Assert.Equal(0, summary.DestOnlyCount);
        var status = SyncOneWay.GetFinalStatus(summary.ErrorCount, summary.DestOnlyCount, summary.WarnCount);
        Assert.Equal(SyncFinalStatus.Success, status);

        var destContent = await File.ReadAllTextAsync(destPath);
        Assert.Equal("NEW", destContent);
    }

    [Fact]
    public async Task RunAsync_ReportContainsFinalStatusLine()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.RootPath, "src");
        var destRoot = Path.Combine(temp.RootPath, "dest");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(destRoot);

        var sourcePath = Path.Combine(sourceRoot, "alpha.txt");
        await File.WriteAllTextAsync(sourcePath, "alpha");

        var sourceIndexPath = Path.Combine(sourceRoot, "baresync_source_index.csv");
        var destIndexPath = Path.Combine(destRoot, "baresync_dest_index.csv");
        await BuildAndWriteIndexAsync(sourceRoot, sourceIndexPath);

        var config = new AppConfig
        {
            SourceRoot = sourceRoot,
            MirrorRoot = destRoot,
            SourceIndexCsvPath = sourceIndexPath,
            DestIndexCsvPath = destIndexPath
        };

        var summary = await SyncOneWay.RunAsync(config, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(summary.LogFilePath));
        var logDir = Path.GetDirectoryName(summary.LogFilePath);
        Assert.False(string.IsNullOrWhiteSpace(logDir));

        var reportsDir = GetReportsDirectory(logDir!);
        var reportFile = FindReportForLog(reportsDir, summary.LogFilePath);
        Assert.False(string.IsNullOrWhiteSpace(reportFile));

        var content = await File.ReadAllTextAsync(reportFile!);
        var hasStatus = content.Contains($"{Environment.NewLine}SUCCESS{Environment.NewLine}", StringComparison.Ordinal)
            || content.Contains($"{Environment.NewLine}WARNING{Environment.NewLine}", StringComparison.Ordinal)
            || content.Contains($"{Environment.NewLine}FAIL{Environment.NewLine}", StringComparison.Ordinal);

        Assert.True(hasStatus);
    }

    [Fact]
    public async Task CsvIndexReader_ParsesQuotedFieldsWithNewlines()
    {
        using var temp = new TempDirectory();
        var csvPath = Path.Combine(temp.RootPath, "index.csv");

        var fileName = "odd,\"name\nfinal.txt";
        var entry = new FileIndexEntry
        {
            Id = 1,
            RelativeDir = "Sub",
            FileName = fileName,
            Crc64Hex = "ABCDEF",
            SizeBytes = 123,
            LastWriteTimeUtc = new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero)
        };

        var line = CsvIndexWriter.FormatLine(entry);
        var content = new StringBuilder();
        content.AppendLine(CsvHeader);
        content.AppendLine(line);

        await File.WriteAllTextAsync(csvPath, content.ToString(), new UTF8Encoding(false));

        var rows = await CsvIndexReader.ReadAsync(csvPath, CancellationToken.None);

        var expectedKey = $"Sub/{fileName}";
        Assert.True(rows.TryGetValue(expectedKey, out var row));
        Assert.Equal(entry.Crc64Hex, row.Crc64Hex);
        Assert.Equal(entry.SizeBytes, row.SizeBytes);
        Assert.Equal(entry.LastWriteTimeUtc.UtcDateTime, row.LastWriteTimeUtc);
    }

    [Fact]
    public void SimpleFileLogger_FlushEveryLinesWritesAllLines()
    {
        using var temp = new TempDirectory();
        var logPath = Path.Combine(temp.RootPath, "log.txt");

        using var logger = new SimpleFileLogger(logPath, flushEveryLines: 2);
        logger.Info("line 1");
        logger.Info("line 2");
        logger.Info("line 3");
        logger.Info("line 4");
        logger.Info("line 5");
        logger.Flush();

        var lines = File.ReadAllLines(logPath);
        Assert.Equal(5, lines.Length);
        Assert.Contains(lines, line => line.Contains("line 5", StringComparison.Ordinal));
    }

    private static async Task BuildAndWriteIndexAsync(string root, string indexPath)
    {
        var index = await FileScanner.BuildIndexAsync(root, indexPath, CancellationToken.None);
        await CsvIndexWriter.WriteAsync(index, indexPath, CancellationToken.None);
    }

    private static string GetReportsDirectory(string logDirectory)
    {
        var baseDir = Directory.GetParent(logDirectory)?.FullName ?? AppContext.BaseDirectory;
        return Path.Combine(baseDir, "Reports");
    }

    private static string? FindReportForLog(string reportsDirectory, string logFilePath)
    {
        foreach (var reportFile in Directory.GetFiles(reportsDirectory, "baresync_report_*.txt"))
        {
            var content = File.ReadAllText(reportFile, new UTF8Encoding(false));
            if (content.Contains($"LogFilePath: {logFilePath}", StringComparison.Ordinal))
            {
                return reportFile;
            }
        }

        return null;
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
