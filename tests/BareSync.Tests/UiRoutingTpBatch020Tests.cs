using System.Text.Json;
using Xunit;

namespace BareSync.Tests;

public sealed class UiRoutingTpBatch020Tests
{
    [Fact]
    public async Task TP_BATCH_020_CopySnapshot_No_DoesNotPersist()
    {
        var stdin = string.Join(
                Environment.NewLine,
                new[]
                {
                    "2",
                    "2",
                    "MyBatch",
                    "5",
                    "2",
                    "n",
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

        Assert.Contains("** Batch / Context (defaults) **", result.Stdout);
        Assert.Contains("Mirror = '<empty>'", result.Stdout);
        Assert.False(string.IsNullOrWhiteSpace(batchJson));

        using var doc = JsonDocument.Parse(batchJson!);
        var contextSnapshot = doc.RootElement.GetProperty("contextSnapshot");
        Assert.Equal(JsonValueKind.Object, contextSnapshot.ValueKind);
        Assert.Empty(contextSnapshot.EnumerateObject());
    }
}
