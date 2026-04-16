using System.Text.Json;
using BareSync.Domain;

namespace BareSync.Tests;

internal static class BatchTestData
{
    public static string CreateValidBatch(string id, string name)
    {
        var payload = new
        {
            schemaVersion = 0,
            id,
            name,
            createdUtc = "2026-01-20T10:00:00Z",
            updatedUtc = "2026-01-20T10:00:00Z",
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

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string CreateValidConfirmationBatch(string id, string name)
    {
        var payload = new
        {
            schemaVersion = 0,
            id,
            name,
            createdUtc = "2026-01-20T10:00:00Z",
            updatedUtc = "2026-01-20T10:00:00Z",
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

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string CreateInvalidBatch()
    {
        var payload = new
        {
            schemaVersion = 0,
            id = string.Empty,
            name = 42,
            createdUtc = "2026-01-20T10:00:00Z",
            updatedUtc = "2026-01-20T10:00:00Z",
            contextSnapshot = new { },
            steps = "not-an-array"
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string CreateIncompatibleBatch(string id, string name)
    {
        var payload = new
        {
            schemaVersion = 1,
            id,
            name,
            createdUtc = "2026-01-20T10:00:00Z",
            updatedUtc = "2026-01-20T10:00:00Z",
            contextSnapshot = new { },
            steps = new object[] { }
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string CreateNonExecutableBatch(string id, string name)
    {
        var payload = new
        {
            schemaVersion = 0,
            id,
            name,
            createdUtc = "2026-01-20T10:00:00Z",
            updatedUtc = "2026-01-20T10:00:00Z",
            contextSnapshot = new
            {
                sourceRoot = "D:/Data/Source"
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

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }
}
