using BareSync.App.Common;

namespace BareSync.Tests;

public sealed class PowerInhibitorTests
{
    [Fact]
    public void PowerInhibitor_NonWindows_IsNoOpAndDoesNotThrow()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var exception = Record.Exception(() =>
        {
            using var lease = PowerInhibitor.AcquireSleepInhibitLease();
        });

        Assert.Null(exception);
    }
}