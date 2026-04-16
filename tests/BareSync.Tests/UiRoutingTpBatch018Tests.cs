using System.Text.Json;
using Xunit;

namespace BareSync.Tests;

public sealed class UiRoutingTpBatch018Tests
{
    [Fact]
    public async Task TP_BATCH_018_CancelRename_DoesNotPersist()
    {
        var stdin = string.Join(
                Environment.NewLine,
                new[]
                {
                    "2",
                    "2",
                    "MyBatch",
                    "1",
                    "",
                    "0",
                    "0",
                    "0"
                }) + Environment.NewLine;

        string? batchJson = null;
        var result = await UiRoutingHarness.RunBareSyncAsync(
            stdin,
            timeout: TimeSpan.FromSeconds(10),
            capture: appDir =>
            {
                var batchRoot = Path.Combine(appDir, "batches");
                var jsonFiles = Directory.Exists(batchRoot)
                    ? Directory.GetFiles(batchRoot, "*.json")
                    : Array.Empty<string>();
                if (jsonFiles.Length == 1)
                {
                    batchJson = File.ReadAllText(jsonFiles[0]);
                }
                return Task.CompletedTask;
            });

        Assert.False(string.IsNullOrWhiteSpace(batchJson));

        using var doc = JsonDocument.Parse(batchJson!);
        Assert.Equal("MyBatch", doc.RootElement.GetProperty("name").GetString());
    }
}