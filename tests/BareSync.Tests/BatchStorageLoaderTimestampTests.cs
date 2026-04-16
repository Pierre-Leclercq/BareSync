using System.Text.Json;
using BareSync.Domain;
using BareSync.Infra;
using Xunit;

namespace BareSync.Tests;

public sealed class BatchStorageLoaderTimestampTests
{
    [Fact]
    public void LoadAll_AcceptsUtcTimestampsWithMilliseconds()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "BareSyncTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var batchRoot = Path.Combine(tempRoot, "batches");
            Directory.CreateDirectory(batchRoot);

            var validPayload = new
            {
                schemaVersion = 0,
                id = Guid.NewGuid().ToString("D"),
                name = "Alpha",
                createdUtc = "2026-01-20T10:00:00.123Z",
                updatedUtc = "2026-01-20T10:00:00.1234567Z",
                contextSnapshot = new
                {
                    sourceRoot = "D:/Data/Source",
                    mirrorRoot = "D:/Data/Mirror",
                    sourceIndexCsvPath = "D:/Data/Index/source.csv",
                    destIndexCsvPath = "D:/Data/Index/dest.csv"
                },
                steps = new[]
                {
                    new
                    {
                        operationType = BatchOperationCatalog.OperationTypeOneWaySyncDryRun,
                        operationParams = new { values = new { } },
                        contextOverrides = new { }
                    }
                }
            };

            var nonExecutablePayload = new
            {
                schemaVersion = 0,
                id = Guid.NewGuid().ToString("D"),
                name = "Beta",
                createdUtc = "2026-01-20T10:00:00.050Z",
                updatedUtc = "2026-01-20T10:00:00.050Z",
                contextSnapshot = new
                {
                    sourceRoot = "D:/Data/Source",
                    mirrorRoot = "D:/Data/Mirror",
                    sourceIndexCsvPath = "D:/Data/Index/source.csv",
                    destIndexCsvPath = "D:/Data/Index/dest.csv"
                },
                steps = new[]
                {
                    new
                    {
                        operationType = BatchOperationCatalog.OperationTypeOneWaySyncApply,
                        operationParams = new { values = new { } },
                        contextOverrides = new { }
                    }
                }
            };

            File.WriteAllText(
                Path.Combine(batchRoot, "alpha.json"),
                JsonSerializer.Serialize(validPayload, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(
                Path.Combine(batchRoot, "beta.json"),
                JsonSerializer.Serialize(nonExecutablePayload, new JsonSerializerOptions { WriteIndented = true }));

            var loader = new BatchStorageLoader();
            var entries = loader.LoadAll(tempRoot);

            var valid = entries.Single(entry => entry.Name == "Alpha");
            var alsoValid = entries.Single(entry => entry.Name == "Beta");

            Assert.Equal(BatchStorageStatus.Valid, valid.Status);
            Assert.Equal(BatchStorageStatus.Valid, alsoValid.Status);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
