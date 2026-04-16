using BareSync.App;
using Xunit;

namespace BareSync.Tests;

public sealed class UniqueFileNameTests
{
    [Fact]
    public void GetRunFilePaths_StartsCounterAtOne()
    {
        using var temp = new TempDirectory();
        var logsDir = Path.Combine(temp.RootPath, "log");
        var reportsDir = Path.Combine(temp.RootPath, "Reports");
        Directory.CreateDirectory(logsDir);
        Directory.CreateDirectory(reportsDir);
        var runStartedUtc = new DateTime(2026, 1, 15, 17, 44, 15, DateTimeKind.Utc);

        var runPaths = SyncOneWay.GetRunFilePaths(logsDir, reportsDir, runStartedUtc);

        Assert.Equal("20260115_174415", runPaths.RunId);
        Assert.Equal(
            Path.Combine(logsDir, "baresync_sync_20260115_174415.log"),
            runPaths.LogPath);
        Assert.Equal(
            Path.Combine(reportsDir, "baresync_report_20260115_174415.txt"),
            runPaths.ReportPath);
        Assert.True(File.Exists(runPaths.LogPath));
        Assert.True(File.Exists(runPaths.ReportPath));
    }

    [Fact]
    public void GetRunFilePaths_SkipsExistingCounters()
    {
        using var temp = new TempDirectory();
        var logsDir = Path.Combine(temp.RootPath, "log");
        var reportsDir = Path.Combine(temp.RootPath, "Reports");
        Directory.CreateDirectory(logsDir);
        Directory.CreateDirectory(reportsDir);
        var runStartedUtc = new DateTime(2026, 1, 15, 17, 44, 15, DateTimeKind.Utc);

        File.WriteAllText(
            Path.Combine(logsDir, "baresync_sync_20260115_174415.log"),
            "existing log");
        File.WriteAllText(
            Path.Combine(reportsDir, "baresync_report_20260115_174415_2.txt"),
            "existing report");

        var runPaths = SyncOneWay.GetRunFilePaths(logsDir, reportsDir, runStartedUtc);

        Assert.Equal("20260115_174415_3", runPaths.RunId);
        Assert.Equal(
            Path.Combine(logsDir, "baresync_sync_20260115_174415_3.log"),
            runPaths.LogPath);
        Assert.Equal(
            Path.Combine(reportsDir, "baresync_report_20260115_174415_3.txt"),
            runPaths.ReportPath);
        Assert.True(File.Exists(runPaths.LogPath));
        Assert.True(File.Exists(runPaths.ReportPath));
        Assert.True(File.Exists(Path.Combine(logsDir, "baresync_sync_20260115_174415.log")));
        Assert.True(File.Exists(Path.Combine(reportsDir, "baresync_report_20260115_174415_2.txt")));
        Assert.False(File.Exists(Path.Combine(logsDir, "baresync_sync_20260115_174415_2.log")));
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
