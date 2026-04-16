using System.Text.Json;
using BareSync.Domain;
using Xunit;

namespace BareSync.Tests;

public sealed class UiRoutingTpBatch025Tests
{
    [Fact]
    public async Task TP_BATCH_025_StepsList_ShowsOverridesAndMenu()
    {
        var stdin = string.Join(
                Environment.NewLine,
                new[]
                {
                    "2",
                    "1",
                    "1",
                    "1",
                    "4",
                    "0",
                    "0",
                    "0",
                    "0"
                }) + Environment.NewLine;

        var result = await UiRoutingHarness.RunBareSyncAsync(
            stdin,
            timeout: TimeSpan.FromSeconds(10),
            setup: async appDir =>
            {
                var batchRoot = Path.Combine(appDir, "batches");
                Directory.CreateDirectory(batchRoot);

                var payload = new
                {
                    schemaVersion = 0,
                    id = "88888888-8888-8888-8888-888888888888",
                    name = "Batch steps",
                    createdUtc = "2026-01-20T10:00:00Z",
                    updatedUtc = "2026-01-20T10:00:00Z",
                    contextSnapshot = new
                    {
                        sourceRoot = "D:/Data/Source",
                        mirrorRoot = "D:/Data/Mirror",
                        sourceIndexCsvPath = "D:/Data/Index/source.csv",
                        destIndexCsvPath = "D:/Data/Index/dest.csv"
                    },
                    steps = new object[]
                    {
                        new
                        {
                            operationType = BatchOperationCatalog.OperationTypeOneWaySyncApply,
                            operationParams = new { values = new { } },
                            contextOverrides = new
                            {
                                sourceRoot = "D:/Override/Source",
                                mirrorRoot = "D:/Override/Mirror"
                            }
                        },
                        new
                        {
                            operationType = BatchOperationCatalog.OperationTypeRefreshIndexesFull,
                            operationParams = new { values = new { } },
                            contextOverrides = new { }
                        }
                    }
                };

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(Path.Combine(batchRoot, "steps.json"), json);
            });

        Assert.Contains("** Batch / Steps **", result.Stdout);
        Assert.Contains("Page 1/1", result.Stdout);
        Assert.Contains("1) OneWaySyncApply  overrides: {SourceRoot,MirrorRoot}", result.Stdout);
        Assert.Contains("2) RefreshIndexesFull  overrides: <none>", result.Stdout);
        Assert.Contains("1. Add step", result.Stdout);
    }
}
