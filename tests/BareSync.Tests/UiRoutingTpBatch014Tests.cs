using System.Text.Json;
using Xunit;

namespace BareSync.Tests;

public sealed class UiRoutingTpBatch014Tests
{
    [Fact]
    public async Task TP_BATCH_014_ReorderSteps_SwapsOrder()
    {
        var appDir = Path.GetFullPath(AppContext.BaseDirectory);
        var firstSource = Path.Combine(appDir, "first_source.csv");
        var firstDest = Path.Combine(appDir, "first_dest.csv");
        var secondSource = Path.Combine(appDir, "second_source.csv");
        var secondDest = Path.Combine(appDir, "second_dest.csv");

        var stdin = string.Join(
                Environment.NewLine,
                new[]
                {
                    "2",
                    "2",
                    "MyBatch",
                    "4",
                    "1",
                    "4",
                    "2", "S",
                    "3", "S",
                    "4", "S", "first_source.csv",
                    "5", "S", "first_dest.csv",
                    "0",
                    "1",
                    "4",
                    "2", "S",
                    "3", "S",
                    "4", "S", "second_source.csv",
                    "5", "S", "second_dest.csv",
                    "0",
                    "2", // Action 2: Edit step
                    "2", // Editor for Step 2
                    "9", // Move Up
                    "0", // Back from editor because Move Up returns Result! Wait, MoveUp closes editor? Yes!
                    "0", // Back from Batch Details
                    "0"  // Back from Home
                }) + Environment.NewLine;

        string? batchJson = null;
        var result = await UiRoutingHarness.RunBareSyncAsync(
            stdin,
            timeout: TimeSpan.FromSeconds(10),
            capture: dir =>
            {
                var batchRoot = Path.Combine(dir, "batches");
                var jsonFiles = Directory.Exists(batchRoot)
                    ? Directory.GetFiles(batchRoot, "*.json")
                    : Array.Empty<string>();
                if (jsonFiles.Length >= 1)
                {
                    batchJson = File.ReadAllText(jsonFiles[0]);
                }
                return Task.CompletedTask;
            });

        Assert.Contains("Steps: 2", result.Stdout);
        Assert.False(string.IsNullOrWhiteSpace(batchJson));

        using var doc = JsonDocument.Parse(batchJson!);
        var steps = doc.RootElement.GetProperty("steps");
        Assert.Equal(2, steps.GetArrayLength());

        var first = steps[0].GetProperty("contextOverrides");
        var second = steps[1].GetProperty("contextOverrides");

        Assert.Equal(secondSource, first.GetProperty("sourceIndexCsvPath").GetString());
        Assert.Equal(firstSource, second.GetProperty("sourceIndexCsvPath").GetString());
    }
}
