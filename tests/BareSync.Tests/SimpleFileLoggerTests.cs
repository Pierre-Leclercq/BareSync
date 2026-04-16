using System.Text;
using BareSync.Infra;
using Xunit;

namespace BareSync.Tests;

public sealed class SimpleFileLoggerTests
{
    [Fact]
    public void SimpleFileLogger_FlushesWhenBufferFull()
    {
        using var temp = new TempDirectory();
        var logPath = Path.Combine(temp.RootPath, "log.txt");

        using var logger = new SimpleFileLogger(logPath, flushEveryLines: 2);
        logger.Info("first message");
        logger.Warn("second message");

        Assert.True(File.Exists(logPath));
        var content = File.ReadAllText(logPath, Encoding.UTF8);
        Assert.Contains("[INFO] first message", content);
        Assert.Contains("[WARN] second message", content);
    }

    [Fact]
    public void SimpleFileLogger_DisposeFlushesRemainingLines()
    {
        using var temp = new TempDirectory();
        var logPath = Path.Combine(temp.RootPath, "log.txt");

        var logger = new SimpleFileLogger(logPath, flushEveryLines: 10);
        logger.Info("final message");
        logger.Dispose();

        Assert.True(File.Exists(logPath));
        var content = File.ReadAllText(logPath, Encoding.UTF8);
        Assert.Contains("[INFO] final message", content);
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
