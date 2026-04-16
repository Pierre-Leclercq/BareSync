using Xunit;

namespace BareSync.Tests;

public sealed class UiRoutingTpBatch022Tests
{
    [Fact]
    public async Task TP_BATCH_022_PreflightFailed_MenuRoutesToContextEditor()
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
                    "0",
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
                    "55555555-5555-5555-5555-555555555555",
                    "Missing context");
                await File.WriteAllTextAsync(Path.Combine(batchRoot, "missing-context.json"), nonExecutableJson);
            });

        Assert.Contains("** Batch / Preflight (FAILED) **", result.Stdout);
        Assert.Contains("** Batch / Context (defaults) **", result.Stdout);
    }
}
