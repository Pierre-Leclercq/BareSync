using Xunit;

namespace BareSync.Tests;

public sealed class UiRoutingTpBatch024Tests
{
    [Fact]
    public async Task TP_BATCH_024_PreflightRun_ExecutesBatch()
    {
        var stdin = string.Join(
                Environment.NewLine,
                new[]
                {
                    "2",
                    "1",
                    "1",
                    "1",
                    "7", // Details -> Run
                    "1", // Preflight -> Confirm & run
                    "y", // Proceed? (y/n)
                    "0", // Back from run summary
                    "0", // Back from details
                    "0"  // Exit
                }) + Environment.NewLine;

        var result = await UiRoutingHarness.RunBareSyncAsync(
            stdin,
            timeout: TimeSpan.FromSeconds(10),
            setup: async appDir =>
            {
                var batchRoot = Path.Combine(appDir, "batches");
                Directory.CreateDirectory(batchRoot);

                var validJson = BatchTestData.CreateValidConfirmationBatch(
                    "77777777-7777-7777-7777-777777777777",
                    "Ready batch");
                await File.WriteAllTextAsync(Path.Combine(batchRoot, "ready.json"), validJson);
            });

        Assert.Contains("** Batch / Preflight **", result.Stdout);
        // After clicking Run, should see execution progress screen
        Assert.Contains("** Batch / Execution **", result.Stdout);
        // Should show batch info during execution
        Assert.Contains("Ready batch", result.Stdout);
    }
}
