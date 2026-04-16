using System.Text.Json;
using BareSync.Domain;
using Xunit;

namespace BareSync.Tests;

public sealed class UiRoutingTpBatch012Tests
{
    [Fact]
    public async Task TP_BATCH_012_EditStep_UpdatesPaths()
    {
        var dryRunDigit = TestMenuDigits.DigitForOperationString(BatchOperationCatalog.OperationTypeOneWaySyncDryRun);

        var stdin = string.Join(
                Environment.NewLine,
                new[]
                {
                    "2",
                    "2",
                    "MyBatch",
                    "4",
                    "1",
                    dryRunDigit,
                    "2", "S", // sourceRoot = appDir
                    "3", "S", // mirrorRoot = appDir
                    "4", "S", "source.csv",
                    "5", "S", "dest.csv",
                    "0", // Back from StepEditor
                    "2", // Edit Step action
                    "1", // Target step 1
                    "4", "S", "edited_source.csv",
                    "5", "S", "edited_dest.csv",
                    "0", // Back from StepEditor
                    "0", // Back from StepsEditor
                    "0", // Back from Details
                    "0"  // Back from Home
                }) + Environment.NewLine;

        string? batchJson = null;
        string? appDir = null;
        var result = await UiRoutingHarness.RunBareSyncAsync(
            stdin,
            timeout: TimeSpan.FromSeconds(10),
            capture: dir =>
            {
                appDir = dir;
                var batchRoot = Path.Combine(dir, "batches");
                var jsonFiles = Directory.Exists(batchRoot)
                    ? Directory.GetFiles(batchRoot, "*.json")
                    : Array.Empty<string>();
                if (jsonFiles.Length == 1)
                {
                    batchJson = File.ReadAllText(jsonFiles[0]);
                }
                return Task.CompletedTask;
            });

        Assert.Contains("Steps: 1", result.Stdout);
        Assert.Contains("Status: Valid", result.Stdout);
        Assert.False(string.IsNullOrWhiteSpace(batchJson));

        using var doc = JsonDocument.Parse(batchJson!);
        var step = doc.RootElement.GetProperty("steps")[0];
        var overrides = step.GetProperty("contextOverrides");
        
        var expectedRoot = NormalizePath(Environment.CurrentDirectory);
        Assert.Equal(expectedRoot, NormalizePath(overrides.GetProperty("sourceRoot").GetString()));
        Assert.Equal(expectedRoot, NormalizePath(overrides.GetProperty("mirrorRoot").GetString()));

        var sourceIndexPath = overrides.GetProperty("sourceIndexCsvPath").GetString();
        var destIndexPath = overrides.GetProperty("destIndexCsvPath").GetString();
        Assert.EndsWith("edited_source.csv", sourceIndexPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("edited_dest.csv", destIndexPath, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var trimmed = path.Trim();
        return Path.TrimEndingDirectorySeparator(trimmed)
            .Replace('\\', '/')
            .ToLowerInvariant();
    }
}
