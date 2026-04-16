using Xunit;

namespace BareSync.Tests;

public sealed class UiRoutingTpBatch004Tests
{
    [Fact]
    public async Task TP_BATCH_004_BatchPreflightFailsRoutesTo_S2_12a()
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
                    "1",
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

                var nonExecutableJson = BatchTestData.CreateNonExecutableBatch(
                    "33333333-3333-3333-3333-333333333333",
                    "Missing context");
                await File.WriteAllTextAsync(Path.Combine(batchRoot, "missing-context.json"), nonExecutableJson);
            });

        Assert.Contains("S2.12a", result.ScreenTrace);
        Assert.Contains("** Batch / Preflight (FAILED) **", result.Stdout);
        Assert.Contains("Step 1: Missing field: MirrorRoot", result.Stdout);
    }
}
