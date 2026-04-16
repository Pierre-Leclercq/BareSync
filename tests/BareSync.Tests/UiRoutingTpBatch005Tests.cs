using Xunit;

namespace BareSync.Tests;

public sealed class UiRoutingTpBatch005Tests
{
    [Fact]
    public async Task TP_BATCH_005_BatchPreflightOkShows_S2_12()
    {
        var stdin = string.Join(
                Environment.NewLine,
                new[]
                {
                    "2",
                    "1",
                    "1",
                    "1",
                    "7",
                    "2",
                    "0",
                    "0"
                }) + Environment.NewLine;

        var result = await UiRoutingHarness.RunBareSyncAsync(
            stdin,
            timeout: TimeSpan.FromSeconds(10),
            setup: async appDir =>
            {
                var batchRoot = Path.Combine(appDir, "batches");
                Directory.CreateDirectory(batchRoot);

                var validJson = BatchTestData.CreateValidConfirmationBatch(
                    "44444444-4444-4444-4444-444444444444",
                    "Ready batch");
                await File.WriteAllTextAsync(Path.Combine(batchRoot, "ready.json"), validJson);
            });

        Assert.Contains("S2.12", result.ScreenTrace);
        Assert.Contains("** Batch / Preflight **", result.Stdout);
        Assert.Contains("requiresConfirmation=", result.Stdout);
        Assert.Contains("requiresSecret=", result.Stdout);
    }
}
