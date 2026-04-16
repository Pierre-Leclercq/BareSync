using Xunit;

namespace BareSync.Tests;

public sealed class UiRoutingTpBatch019Tests
{
    [Fact]
    public async Task TP_BATCH_019_EditContext_RoutesToContextScreen()
    {
        var stdin = string.Join(
                Environment.NewLine,
                new[]
                {
                    "2",
                    "2",
                    "MyBatch",
                    "5",
                    "1",
                    "0",
                    "0",
                    "0"
                }) + Environment.NewLine;

        var result = await UiRoutingHarness.RunBareSyncAsync(
            stdin,
            timeout: TimeSpan.FromSeconds(10));

        Assert.Contains("** Batch / Context (defaults) **", result.Stdout);
        Assert.Contains("Select field number (1..7, 0/ESC to cancel):", result.Stdout);
    }
}
