using Xunit;

namespace BareSync.Tests;

public sealed class UiRoutingTpBatch006Tests
{
    [Fact]
    public async Task TP_BATCH_006_CreateBatchCancelled_ReturnsToHome()
    {
        var stdin = string.Join(
                Environment.NewLine,
                new[]
                {
                    "2",
                    "2",
                    "",
                    "0",
                    "0"
                }) + Environment.NewLine;

        string[]? jsonFiles = null;
        var result = await UiRoutingHarness.RunBareSyncAsync(
            stdin,
            timeout: TimeSpan.FromSeconds(10),
            capture: appDir =>
            {
                var batchRoot = Path.Combine(appDir, "batches");
                jsonFiles = Directory.Exists(batchRoot)
                    ? Directory.GetFiles(batchRoot, "*.json")
                    : Array.Empty<string>();
                return Task.CompletedTask;
            });

        Assert.Contains("S2.1", result.ScreenTrace);
        Assert.Contains("S2.1a", result.ScreenTrace);

        Assert.NotNull(jsonFiles);
        Assert.Empty(jsonFiles!);
        Assert.Equal(0, result.ExitCode);
    }
}