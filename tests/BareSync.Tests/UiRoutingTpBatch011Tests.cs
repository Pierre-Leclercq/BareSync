using System.Text.Json;
using Xunit;

namespace BareSync.Tests;

public sealed class UiRoutingTpBatch011Tests
{
    [Fact]
    public async Task TP_BATCH_011_AddStep_UsesPickerSelectionsForPaths()
    {
        var appDir = Path.GetFullPath(AppContext.BaseDirectory);
        var sourceRoot = appDir;
        var mirrorRoot = appDir;
        var sourceIndex = Path.Combine(appDir, "custom_source.csv");
        var destIndex = Path.Combine(appDir, "custom_dest.csv");

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
                    "2",
                    "S",
                    "3",
                    "S",
                    "4",
                    "S",
                    "custom_source.csv",
                    "5",
                    "S",
                    "custom_dest.csv",
                    "0",
                    "0",
                    "0",
                    "0"
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
                if (jsonFiles.Length == 1)
                {
                    batchJson = File.ReadAllText(jsonFiles[0]);
                }
                return Task.CompletedTask;
            });

        Assert.Contains("** Batch / Details **", result.Stdout);
        Assert.False(string.IsNullOrWhiteSpace(batchJson));
        using var doc = JsonDocument.Parse(batchJson!);
        var step = doc.RootElement.GetProperty("steps")[0];
        var overrides = step.GetProperty("contextOverrides");

        var expectedRoot = NormalizePath(sourceRoot);
        Assert.Equal(expectedRoot, NormalizePath(overrides.GetProperty("sourceRoot").GetString()));
        Assert.Equal(expectedRoot, NormalizePath(overrides.GetProperty("mirrorRoot").GetString()));
        Assert.Equal(
            NormalizePath(sourceIndex),
            NormalizePath(overrides.GetProperty("sourceIndexCsvPath").GetString()));
        Assert.Equal(
            NormalizePath(destIndex),
            NormalizePath(overrides.GetProperty("destIndexCsvPath").GetString()));
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
