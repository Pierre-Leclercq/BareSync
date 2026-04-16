using BareSync.Infra;
using Xunit;

namespace BareSync.Tests;

public sealed class SingleInstanceLockTests
{
    [Fact]
    public void TryAcquire_PreventsSecondInstanceUntilDisposed()
    {
        using var temp = new TempDirectory();
        var lockPath = Path.Combine(temp.RootPath, "baresync.lock");

        var acquired = SingleInstanceLock.TryAcquire(lockPath, out var first, out _);
        Assert.True(acquired);
        Assert.NotNull(first);

        var secondAcquired = SingleInstanceLock.TryAcquire(lockPath, out var second, out _);
        Assert.False(secondAcquired);
        Assert.Null(second);

        first!.Dispose();

        var thirdAcquired = SingleInstanceLock.TryAcquire(lockPath, out var third, out _);
        Assert.True(thirdAcquired);
        third!.Dispose();
    }

    [Fact]
    public void TryAcquire_RecoversFromStaleLockWithDeadPid()
    {
        using var temp = new TempDirectory();
        var lockPath = Path.Combine(temp.RootPath, "baresync.lock");

        File.WriteAllText(
            lockPath,
            $"999999{Environment.NewLine}{DateTimeOffset.UtcNow.AddMinutes(-10):O}{Environment.NewLine}BareSync{Environment.NewLine}");

        var acquired = SingleInstanceLock.TryAcquire(lockPath, out var instanceLock, out var error);

        Assert.True(acquired);
        Assert.NotNull(instanceLock);
        Assert.Null(error);

        instanceLock!.Dispose();
    }

    [Fact]
    public void TryAcquire_RecoversFromInvalidLockContent()
    {
        using var temp = new TempDirectory();
        var lockPath = Path.Combine(temp.RootPath, "baresync.lock");

        File.WriteAllText(lockPath, "not-a-valid-lock");

        var acquired = SingleInstanceLock.TryAcquire(lockPath, out var instanceLock, out var error);

        Assert.True(acquired);
        Assert.NotNull(instanceLock);
        Assert.Null(error);

        instanceLock!.Dispose();
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
