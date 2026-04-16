using Xunit;

namespace BareSync.Tests;

public sealed class UiRoutingTpBatch026Tests
{
    [Fact]
    public async Task TP_BATCH_026_StepsList_DoesNotShowSave()
    {
        var stdin = string.Join(
                Environment.NewLine,
                new[]
                {
                    "2",
                    "1",
                    "1",
                    "1",
                    "4",
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

                var validJson = BatchTestData.CreateValidBatch(
                    "99999999-9999-9999-9999-999999999999",
                    "Ready batch");
                await File.WriteAllTextAsync(Path.Combine(batchRoot, "ready.json"), validJson);
            });

        Assert.Contains("** Batch / Steps **", result.Stdout);
        Assert.DoesNotContain("5. Save", result.Stdout);
    }
}
