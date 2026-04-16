using Xunit;

namespace BareSync.Tests;

public sealed class UiRoutingTpBatch001Tests
{
    [Fact]
    public async Task TP_BATCH_001_BatchListEmptyLibrary()
    {
        var stdin = string.Join(
                Environment.NewLine,
                new[]
                {
                    "2",
                    "1",
                    "1",
                    "0",
                    "0",
                    "0"
                }) + Environment.NewLine;

        var result = await UiRoutingHarness.RunBareSyncAsync(
            stdin,
            timeout: TimeSpan.FromSeconds(10),
            setup: _ => Task.CompletedTask);

        Assert.Contains("S2.2", result.ScreenTrace);
        Assert.Contains("** Batch / List **", result.Stdout);
        Assert.Contains("(no batches)", result.Stdout);
    }
}