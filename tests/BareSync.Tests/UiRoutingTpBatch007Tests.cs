using System.Text.Json;
using Xunit;

namespace BareSync.Tests;

public sealed class UiRoutingTpBatch007Tests
{
    [Fact]
    public async Task TP_BATCH_007_CreateBatchSavesFileAndShowsDetails()
    {
        var stdin = string.Join(
                Environment.NewLine,
                new[]
                {
                    "2",
                    "2",
                    "MyBatch",
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

        Assert.Contains("S2.1", result.ScreenTrace);
        Assert.Contains("S2.1a", result.ScreenTrace);
        Assert.Contains("S2.3", result.ScreenTrace);

        Assert.NotNull(jsonFiles);
        Assert.Single(jsonFiles!);

        Assert.False(string.IsNullOrWhiteSpace(batchJson));
        using var doc = JsonDocument.Parse(batchJson!);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("schemaVersion", out _));
        Assert.True(root.TryGetProperty("id", out _));
        Assert.True(root.TryGetProperty("name", out var nameElement));
        Assert.True(root.TryGetProperty("createdUtc", out _));
        Assert.True(root.TryGetProperty("updatedUtc", out _));
        Assert.True(root.TryGetProperty("contextSnapshot", out _));
        Assert.True(root.TryGetProperty("steps", out _));

        Assert.Equal("MyBatch", nameElement.GetString());
        Assert.Contains("** Batch / Details **", result.Stdout);
        Assert.Contains("Name: MyBatch", result.Stdout);
        Assert.Equal(0, result.ExitCode);
    }
}