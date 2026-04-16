using System.Text.Json;
using System.Text.Json.Nodes;
using BareSync.Domain;
using BareSync.Infra;
using Xunit;

namespace BareSync.Tests;

public sealed class BatchSchemaStrictTests
{
    [Fact]
    public void BatchStorageWriter_WritesRequiredV0Fields()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "BareSyncTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var batch = new BatchV0
            {
                SchemaVersion = 0,
                Id = Guid.NewGuid().ToString("D"),
                Name = "Strict batch",
                CreatedUtc = "2026-01-20T10:00:00Z",
                UpdatedUtc = "2026-01-20T10:00:00Z",
                ContextSnapshot = new JsonObject(),
                Steps = new List<BatchStepV0>
                {
                    new BatchStepV0
                    {
                        OperationType = BatchOperationCatalog.OperationTypeOneWaySyncDryRun,
                        OperationParams = new StepOperationParamsV0
                        {
                            Values = new JsonObject()
                        },
                        ContextOverrides = new JsonObject()
                    }
                }
            };

            var writer = new BatchStorageWriter();
            Assert.True(writer.SaveAtomic(tempRoot, batch, out var error), error);

            var batchRoot = Path.Combine(tempRoot, "batches");
            var jsonPath = Directory.GetFiles(batchRoot, "*.json").Single();

            using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
            var root = doc.RootElement;

            Assert.True(root.TryGetProperty("schemaVersion", out _));
            Assert.True(root.TryGetProperty("id", out _));
            Assert.True(root.TryGetProperty("name", out _));
            Assert.True(root.TryGetProperty("createdUtc", out var createdUtc));
            Assert.True(root.TryGetProperty("updatedUtc", out var updatedUtc));
            Assert.True(root.TryGetProperty("contextSnapshot", out var contextSnapshot));
            Assert.True(root.TryGetProperty("steps", out var steps));

            Assert.Equal(JsonValueKind.Object, contextSnapshot.ValueKind);
            Assert.Equal(JsonValueKind.Array, steps.ValueKind);
            Assert.EndsWith("Z", createdUtc.GetString());
            Assert.EndsWith("Z", updatedUtc.GetString());

            var step = steps[0];
            Assert.True(step.TryGetProperty("operationType", out _));
            Assert.True(step.TryGetProperty("operationParams", out var opParams));
            Assert.True(step.TryGetProperty("contextOverrides", out var contextOverrides));
            Assert.Equal(JsonValueKind.Object, contextOverrides.ValueKind);
            Assert.True(opParams.TryGetProperty("values", out var values));
            Assert.Equal(JsonValueKind.Object, values.ValueKind);
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
