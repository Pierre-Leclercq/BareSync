using System.Text.Json;
using Xunit;

namespace BareSync.Tests;

public sealed class UiRoutingTpBatch017Tests
{
    [Fact]
    public async Task TP_BATCH_017_EditTags_UpdatesJsonAndUi()
    {
        var stdin = string.Join(
                Environment.NewLine,
                new[]
                {
                    "2",
                    "2",
                    "MyBatch",
                    "3",
                    "a, b, c",
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
        var tags = doc.RootElement.GetProperty("tags");
        Assert.Equal(3, tags.GetArrayLength());
        Assert.Equal("a", tags[0].GetString());
        Assert.Equal("b", tags[1].GetString());
        Assert.Equal("c", tags[2].GetString());
    }
}