using System.Text;
using BareSync.App;
using BareSync.Domain;
using BareSync.Infra;
using Xunit;

namespace BareSync.Tests;

public sealed class DryRunTests
{
    [Fact]
    public async Task DryRun_NoDestinationWrites()
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

        var sentinelPath = Path.Combine(destRoot, "keep.txt");
        await File.WriteAllTextAsync(sentinelPath, "keep");

        var sourceIndexPath = Path.Combine(sourceRoot, "baresync_source_index.csv");
        var index = await FileScanner.BuildIndexAsync(sourceRoot, sourceIndexPath, CancellationToken.None);
        await CsvIndexWriter.WriteAsync(index, sourceIndexPath, CancellationToken.None);

        var destIndexPath = Path.Combine(destRoot, "baresync_dest_index.csv");
        Assert.False(File.Exists(destIndexPath));
        Assert.False(Directory.Exists(Path.Combine(destRoot, "Sub")));

        var config = new AppConfig
        {
            SourceRoot = sourceRoot,
            MirrorRoot = destRoot,
            SourceIndexCsvPath = sourceIndexPath,
            DestIndexCsvPath = destIndexPath
        };

        var summary = await SyncOneWay.RunAsync(config, CancellationToken.None, dryRun: true);

        Assert.False(File.Exists(Path.Combine(destRoot, "alpha.txt")));
        Assert.False(File.Exists(Path.Combine(destRoot, "Sub", "bravo.txt")));
        Assert.False(Directory.Exists(Path.Combine(destRoot, "Sub")));
        Assert.False(File.Exists(destIndexPath));
        Assert.True(File.Exists(sentinelPath));
        Assert.Equal("keep", await File.ReadAllTextAsync(sentinelPath));

        Assert.False(string.IsNullOrWhiteSpace(summary.LogFilePath));
        Assert.True(File.Exists(summary.LogFilePath));

        var logDir = Path.GetDirectoryName(summary.LogFilePath);
        Assert.False(string.IsNullOrWhiteSpace(logDir));
        Assert.True(Directory.Exists(logDir!));

        var reportsDir = GetReportsDirectory(logDir!);
        Assert.True(Directory.Exists(reportsDir));

        var reportFile = FindReportForLog(reportsDir, summary.LogFilePath);
        Assert.False(string.IsNullOrWhiteSpace(reportFile));

        var reportContent = await File.ReadAllTextAsync(reportFile!, new UTF8Encoding(false));
        Assert.Contains("Dry run: true", reportContent);

        var logContent = await File.ReadAllTextAsync(summary.LogFilePath, new UTF8Encoding(false));
        Assert.Contains("DRY RUN: Would copy", logContent);
    }

    [Fact]
    public async Task DryRun_DetectsMissingFilesWhenDestIndexIsStale()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.RootPath, "src");
        var destRoot = Path.Combine(temp.RootPath, "dest");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(destRoot);

        var alphaPath = Path.Combine(sourceRoot, "alpha.txt");
        await File.WriteAllTextAsync(alphaPath, "alpha");

        var destAlphaPath = Path.Combine(destRoot, "alpha.txt");
        await File.WriteAllTextAsync(destAlphaPath, "alpha-old");

        var sourceIndexPath = Path.Combine(sourceRoot, "baresync_source_index.csv");
        var destIndexPath = Path.Combine(destRoot, "baresync_dest_index.csv");
        var sourceIndex = await FileScanner.BuildIndexAsync(sourceRoot, sourceIndexPath, CancellationToken.None);
        await CsvIndexWriter.WriteAsync(sourceIndex, sourceIndexPath, CancellationToken.None);

        var destIndex = await FileScanner.BuildIndexAsync(destRoot, destIndexPath, CancellationToken.None);
        await CsvIndexWriter.WriteAsync(destIndex, destIndexPath, CancellationToken.None);

        File.Delete(destAlphaPath);

        var config = new AppConfig
        {
            SourceRoot = sourceRoot,
            MirrorRoot = destRoot,
            SourceIndexCsvPath = sourceIndexPath,
            DestIndexCsvPath = destIndexPath
        };

        var summary = await SyncOneWay.RunAsync(config, CancellationToken.None, dryRun: true);

        Assert.Equal(1, summary.CopyAttemptedCount);
        Assert.Equal(0, summary.CopiedCount);
        Assert.Equal(0, summary.ErrorCount);
        Assert.False(File.Exists(destAlphaPath));

        var logContent = await File.ReadAllTextAsync(summary.LogFilePath, new UTF8Encoding(false));
        Assert.Contains("DRY RUN: Would copy", logContent);
        Assert.Contains("alpha.txt", logContent);
    }

    [Fact]
    public async Task DryRun_MirrorEnabled_ReportsWouldDeleteWithoutDeleting()
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
        var sourceIndex = await FileScanner.BuildIndexAsync(sourceRoot, sourceIndexPath, CancellationToken.None);
        await CsvIndexWriter.WriteAsync(sourceIndex, sourceIndexPath, CancellationToken.None);
        var destIndex = await FileScanner.BuildIndexAsync(destRoot, destIndexPath, CancellationToken.None);
        await CsvIndexWriter.WriteAsync(destIndex, destIndexPath, CancellationToken.None);

        var config = new AppConfig
        {
            SourceRoot = sourceRoot,
            MirrorRoot = destRoot,
            Mirror = true,
            SourceIndexCsvPath = sourceIndexPath,
            DestIndexCsvPath = destIndexPath
        };

        var summary = await SyncOneWay.RunAsync(config, CancellationToken.None, dryRun: true);

        Assert.Equal(1, summary.DeleteAttemptedCount);
        Assert.Equal(0, summary.DeletedCount);
        Assert.Equal(1, summary.WouldDeleteCount);
        Assert.True(File.Exists(orphanPath));

        var logContent = await File.ReadAllTextAsync(summary.LogFilePath, new UTF8Encoding(false));
        Assert.Contains("DRY RUN: Would delete (destination-only): orphan.txt", logContent);

        var reportContent = await File.ReadAllTextAsync(summary.ReportFilePath, new UTF8Encoding(false));
        Assert.Contains("Would delete: 1", reportContent);
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
