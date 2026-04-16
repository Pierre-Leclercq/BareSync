using System.Text.Json.Nodes;
using BareSync.Domain;
using BareSync.Infra;
using Xunit;

namespace BareSync.Tests;

public sealed class BatchExecutionReadinessTests
{
    [Fact]
    public void EvaluateBatchExecutionReadiness_SchemaValidMissingContext_IsNonExecutable()
    {
        var batch = new BatchV0
        {
            SchemaVersion = 0,
            Id = "batch-1",
            Name = "Missing context",
            CreatedUtc = "2026-01-20T10:00:00Z",
            UpdatedUtc = "2026-01-20T10:00:00Z",
            ContextSnapshot = new JsonObject
            {
                [BatchContextFields.SourceRoot] = "D:/Data/Source",
                [BatchContextFields.SourceIndexCsvPath] = "D:/Data/Index/source.csv",
                [BatchContextFields.DestIndexCsvPath] = "D:/Data/Index/dest.csv"
            },
            Steps = new List<BatchStepV0>
            {
                new()
                {
                    OperationType = BatchOperationCatalog.OperationTypeOneWaySyncApply,
                    OperationParams = new StepOperationParamsV0 { Values = new JsonObject() },
                    ContextOverrides = new JsonObject()
                }
            }
        };

        var readiness = BatchExecutionReadinessEvaluator.EvaluateBatchExecutionReadiness(batch);

        Assert.Equal(BatchSchemaValidity.Valid, readiness.SchemaValidity);
        Assert.Equal(BatchExecutionReadinessStatus.NonExecutable, readiness.ExecutionReadiness);
        Assert.Contains("Step 1: Missing field: MirrorRoot", readiness.Errors);
        Assert.Single(readiness.Steps);
    }

    [Fact]
    public void EvaluateBatchExecutionReadiness_WhenDriveLabelNotFound_IsNonExecutable()
    {
        const string sourceRoot = @"SourceVolume\Data";
        const string mirrorRoot = @"MirrorVolume\Mirror";

        var batch = new BatchV0
        {
            SchemaVersion = 0,
            Id = "batch-notfound",
            Name = "Drive label not found",
            CreatedUtc = "2026-01-20T10:00:00Z",
            UpdatedUtc = "2026-01-20T10:00:00Z",
            ContextSnapshot = new JsonObject
            {
                [BatchContextFields.SourceRoot] = sourceRoot,
                [BatchContextFields.MirrorRoot] = mirrorRoot,
                [BatchContextFields.SourceIndexCsvPath] = @"SourceVolume\Data\source.csv",
                [BatchContextFields.DestIndexCsvPath] = @"MirrorVolume\Mirror\dest.csv"
            },
            Steps = new List<BatchStepV0>
            {
                new()
                {
                    OperationType = BatchOperationCatalog.OperationTypeOneWaySyncApply,
                    OperationParams = new StepOperationParamsV0 { Values = new JsonObject() },
                    ContextOverrides = new JsonObject()
                }
            }
        };

        var readiness = BatchExecutionReadinessEvaluator.EvaluateBatchExecutionReadiness(
            batch,
            path => string.Equals(path, sourceRoot, StringComparison.OrdinalIgnoreCase)
                ? DriveLabelResolutionResult.NotFound(path)
                : DriveLabelResolutionResult.NotApplicable(path));

        Assert.Equal(BatchSchemaValidity.Valid, readiness.SchemaValidity);
        Assert.Equal(BatchExecutionReadinessStatus.NonExecutable, readiness.ExecutionReadiness);
        Assert.Contains("Step 1: Drive name not found: SourceRoot", readiness.Errors);
    }

    [Fact]
    public void EvaluateBatchExecutionReadiness_WhenDriveLabelAmbiguous_IsNonExecutable()
    {
        const string sourceRoot = @"SourceVolume\Data";
        const string mirrorRoot = @"MirrorVolume\Mirror";

        var batch = new BatchV0
        {
            SchemaVersion = 0,
            Id = "batch-ambiguous",
            Name = "Drive label ambiguous",
            CreatedUtc = "2026-01-20T10:00:00Z",
            UpdatedUtc = "2026-01-20T10:00:00Z",
            ContextSnapshot = new JsonObject
            {
                [BatchContextFields.SourceRoot] = sourceRoot,
                [BatchContextFields.MirrorRoot] = mirrorRoot,
                [BatchContextFields.SourceIndexCsvPath] = @"SourceVolume\Data\source.csv",
                [BatchContextFields.DestIndexCsvPath] = @"MirrorVolume\Mirror\dest.csv"
            },
            Steps = new List<BatchStepV0>
            {
                new()
                {
                    OperationType = BatchOperationCatalog.OperationTypeOneWaySyncApply,
                    OperationParams = new StepOperationParamsV0 { Values = new JsonObject() },
                    ContextOverrides = new JsonObject()
                }
            }
        };

        var readiness = BatchExecutionReadinessEvaluator.EvaluateBatchExecutionReadiness(
            batch,
            path => string.Equals(path, sourceRoot, StringComparison.OrdinalIgnoreCase)
                ? DriveLabelResolutionResult.Ambiguous(path, new[] { @"D:\", @"E:\" })
                : DriveLabelResolutionResult.NotApplicable(path));

        Assert.Equal(BatchSchemaValidity.Valid, readiness.SchemaValidity);
        Assert.Equal(BatchExecutionReadinessStatus.NonExecutable, readiness.ExecutionReadiness);
        Assert.Contains(
            "Step 1: Ambiguous drive name: SourceRoot (candidates: D:\\, E:\\)",
            readiness.Errors);
    }

    [Fact]
    public void EvaluateBatchExecutionReadiness_RefreshEncryptedFolder_MissingSourceContext_IsNonExecutable()
    {
        var batch = new BatchV0
        {
            SchemaVersion = 0,
            Id = "batch-refresh-encrypted-missing-source",
            Name = "Refresh encrypted missing source context",
            CreatedUtc = "2026-01-20T10:00:00Z",
            UpdatedUtc = "2026-01-20T10:00:00Z",
            ContextSnapshot = new JsonObject
            {
                [BatchContextFields.EncryptedOutputRoot] = "D:/Vault/A"
            },
            Steps = new List<BatchStepV0>
            {
                new()
                {
                    OperationType = BatchOperationCatalog.OperationTypeRefreshEncryptedFolder,
                    OperationParams = new StepOperationParamsV0 { Values = new JsonObject() },
                    ContextOverrides = new JsonObject()
                }
            }
        };

        var readiness = BatchExecutionReadinessEvaluator.EvaluateBatchExecutionReadiness(batch);

        Assert.Equal(BatchSchemaValidity.Valid, readiness.SchemaValidity);
        Assert.Equal(BatchExecutionReadinessStatus.NonExecutable, readiness.ExecutionReadiness);
        Assert.Contains("Step 1: Missing field: SourceRoot", readiness.Errors);
    }
}
