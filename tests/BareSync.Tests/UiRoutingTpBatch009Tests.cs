using System.Text.Json;
using BareSync.Domain;
using Xunit;

namespace BareSync.Tests;

public sealed class UiRoutingTpBatch009Tests
{
    [Fact]
    public async Task TP_BATCH_009_AddStep_SavesStepAndDisplaysDetails()
    {
        // Use dynamic digit for OneWaySyncApply to be resilient to menu reordering
        var applyDigit = TestMenuDigits.DigitForOperationString(BatchOperationCatalog.OperationTypeOneWaySyncApply);
        
        var stdin = string.Join(
                Environment.NewLine,
                new[]
                {
                    "2",
                    "2",
                    "MyBatch",
                    "4", // 4 = Edit steps
                    "1", // 1 = Add step
                    applyDigit,
                    "2", "S", // Selects current appDir for Source Root
                    "3", "S", // Selects current appDir for Mirror Root
                    "4", "S", "SRC.csv",
                    "5", "S", "DST.csv",
                    "0", // Back from StepEditor
                    "0", // Back from StepsEditor
                    "0", // Back from Details
                    "0"  // Back from Home
                }) + Environment.NewLine;

        string[]? jsonFiles = null;
        string? batchJson = null;
        string? appDirActual = null;
        
        var result = await UiRoutingHarness.RunBareSyncAsync(
            stdin,
            timeout: TimeSpan.FromSeconds(10),
            capture: appDir =>
            {
                appDirActual = appDir;
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
        Assert.Contains("Steps: 1", result.Stdout);
        Assert.Contains($"Step 1: {BatchOperationCatalog.OperationTypeOneWaySyncApply}", result.Stdout);

        Assert.NotNull(jsonFiles);
        Assert.Single(jsonFiles!);
        Assert.False(string.IsNullOrWhiteSpace(batchJson));

        using var doc = JsonDocument.Parse(batchJson!);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("steps", out var steps));
        Assert.Equal(JsonValueKind.Array, steps.ValueKind);
        Assert.Equal(1, steps.GetArrayLength());

        var step = steps[0];
        Assert.Equal(BatchOperationCatalog.OperationTypeOneWaySyncApply, step.GetProperty("operationType").GetString());
        var opParams = step.GetProperty("operationParams");
        Assert.True(opParams.TryGetProperty("values", out var values));
        Assert.Equal(JsonValueKind.Object, values.ValueKind);
        var overrides = step.GetProperty("contextOverrides");
        
        var expectedRoot = NormalizePath(Environment.CurrentDirectory);
        Assert.Equal(expectedRoot, NormalizePath(overrides.GetProperty("sourceRoot").GetString()));
        Assert.Equal(expectedRoot, NormalizePath(overrides.GetProperty("mirrorRoot").GetString()));
        
        Assert.EndsWith(
            "SRC.csv",
            overrides.GetProperty("sourceIndexCsvPath").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(
            "DST.csv",
            overrides.GetProperty("destIndexCsvPath").GetString(),
            StringComparison.OrdinalIgnoreCase);
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
