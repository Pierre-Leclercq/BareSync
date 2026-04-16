using System.Text.Json;
using Xunit;

namespace BareSync.Tests;

public sealed class UiRoutingTpBatch010Tests
{
    [Fact]
    public async Task TP_BATCH_010_AddStepCancelled_DoesNotPersist()
    {
        var stdin = string.Join(
                Environment.NewLine,
                new[]
                {
                    "2",
                    "2",
                    "MyBatch",
                    "4",
                    "1",
                    "0",
                    "0",
                    "0",
                    "0"
                }) + Environment.NewLine;

        string[]? jsonFiles = null;
        string? batchJson = null;
        var result = await UiRoutingHarness.RunBareSyncAsync(
            stdin,
            timeout: TimeSpan.FromSeconds(10),
            capture: appDir =>
            {
                var batchRoot = Path.Combine(appDir, "batches");
                jsonFiles = Directory.Exists(batchRoot)
                    ? Directory.GetFiles(batchRoot, "*.json")
                    : Array.Empty<string>();
                if (jsonFiles.Length == 1)
                {
                    batchJson = File.ReadAllText(jsonFiles[0]);
                }
                return Task.CompletedTask;
            });

        Assert.Contains("** Batch / Details **", result.Stdout);
        Assert.NotNull(jsonFiles);
        Assert.Single(jsonFiles!);

        Assert.False(string.IsNullOrWhiteSpace(batchJson));
        using var doc = JsonDocument.Parse(batchJson!);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("steps", out var steps));
        Assert.Equal(0, steps.GetArrayLength());
    }
}
