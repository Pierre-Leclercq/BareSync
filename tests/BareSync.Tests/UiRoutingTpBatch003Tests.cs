using Xunit;

namespace BareSync.Tests;

public sealed class UiRoutingTpBatch003Tests
{
    [Fact]
    public async Task TP_BATCH_003_BatchIncompatibleSchemaShowsValidityDetails()
    {
        var stdin = string.Join(
                Environment.NewLine,
                new[]
                {
                    "2",
                    "1",
                    "1",
                    "1",
                    "6",
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

                var incompatibleJson = BatchTestData.CreateIncompatibleBatch(
                    "22222222-2222-2222-2222-222222222222",
                    "Future");
                await File.WriteAllTextAsync(Path.Combine(batchRoot, "future.json"), incompatibleJson);
            });

        Assert.Contains("S2.16", result.ScreenTrace);
        Assert.Contains("** Batch / Validity details **", result.Stdout);
        Assert.Contains("Status: Incompatible", result.Stdout);
        Assert.Contains("Reason: Incompatible: unsupported schemaVersion=1", result.Stdout);
    }
}