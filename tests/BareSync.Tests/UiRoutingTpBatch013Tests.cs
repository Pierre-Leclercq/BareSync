using System.Text.Json;
using Xunit;

namespace BareSync.Tests;

public sealed class UiRoutingTpBatch013Tests
{
    [Fact]
    public async Task TP_BATCH_013_RemoveStep_MakesNonExecutable()
    {
        var stdin = string.Join(
                Environment.NewLine,
                new[]
                {
                    "2",
                    "2",
                    "MyBatch",
                    "1",
                    "1",
                    "S",
                    "S",
                    "S",
                    "",
                    "S",
                    "",
                    "y",
                    "0",
                    "1",
                    "3",
                    "1",
                    "y",
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

        Assert.Contains("Steps: 0", result.Stdout);
        Assert.Contains("Status: NonExecutable", result.Stdout);
        Assert.False(string.IsNullOrWhiteSpace(batchJson));

        using var doc = JsonDocument.Parse(batchJson!);
        var steps = doc.RootElement.GetProperty("steps");
        Assert.Equal(0, steps.GetArrayLength());
    }

    [Fact]
    public async Task TP_BATCH_013_RemoveStepConfirmEscape_KeepsStep()
    {
        const string Esc = "\u001b";

        var stdin = string.Join(
                Environment.NewLine,
                new[]
                {
                    "2", // Main -> Batch mode
                    "2", // Create batch
                    "MyBatch",
                    "4", // Details -> Steps editor
                    "1", // Step actions -> Add step
                    "4", // One-way sync (stable in current routing tests)
                    "2", "S",
                    "3", "S",
                    "4", "S", "SRC.csv",
                    "5", "S", "DST.csv",
                    "0", // Back from step editor
                    "5", // Step actions -> Remove step
                    "1", // Remove step #1
                    Esc, // Confirm prompt: ESC => cancel removal
                    "0", // Back from steps
                    "0", // Back from details
                    "0"  // Back from batch mode / exit
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

        Assert.Contains("Proceed? (y/n):", result.Stdout);
        Assert.Contains("Steps: 1", result.Stdout);
        Assert.False(string.IsNullOrWhiteSpace(batchJson));

        using var doc = JsonDocument.Parse(batchJson!);
        var steps = doc.RootElement.GetProperty("steps");
        Assert.Equal(1, steps.GetArrayLength());
    }
}
