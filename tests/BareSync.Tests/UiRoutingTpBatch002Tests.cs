using Xunit;

namespace BareSync.Tests;

public sealed class UiRoutingTpBatch002Tests
{
    [Fact]
    public async Task TP_BATCH_002_BatchLibraryToleratesInvalidUnit()
    {
        var stdin = string.Join(
                Environment.NewLine,
                new[]
                {
                    "2",
                    "1",
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

                var validJson = BatchTestData.CreateValidBatch("11111111-1111-1111-1111-111111111111", "Alpha");
                var invalidJson = BatchTestData.CreateInvalidBatch();

                await File.WriteAllTextAsync(Path.Combine(batchRoot, "alpha.json"), validJson);
                await File.WriteAllTextAsync(Path.Combine(batchRoot, "invalid.json"), invalidJson);
            });

        Assert.Contains("status=Valid", result.Stdout);
        Assert.Contains("status=Invalid", result.Stdout);
    }
}