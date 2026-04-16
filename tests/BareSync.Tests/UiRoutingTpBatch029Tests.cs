using Xunit;
using BareSync.Domain;
using System.Text.Json.Nodes;

namespace BareSync.Tests;

/// <summary>
/// TP_BATCH_029: S2.15 Run Summary - Unit tests
/// </summary>
public sealed class UiRoutingTpBatch029Tests
{
    [Fact]
    public void BatchExecutionResult_SuccessState_IsCorrect()
    {
        var stepResults = new List<BatchStepResult>
        {
            new BatchStepResult(1, "OneWaySyncApply", true, "Completed", TimeSpan.FromSeconds(2), Array.Empty<string>())
        };

        var result = new BatchExecutionResult(
            true,
            "test-id",
            "Test Batch",
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow,
            stepResults);

        Assert.True(result.Success);
        Assert.Equal("Test Batch", result.BatchName);
        Assert.Single(result.StepResults);
    }

    [Fact]
    public void BatchExecutionResult_FailureState_IsCorrect()
    {
        var stepResults = new List<BatchStepResult>
        {
            new BatchStepResult(1, "OneWaySyncApply", false, "Failed", TimeSpan.FromSeconds(1), Array.Empty<string>())
        };

        var result = new BatchExecutionResult(
            false,
            "test-id",
            "Test Batch",
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow,
            stepResults);

        Assert.False(result.Success);
        Assert.Equal("Test Batch", result.BatchName);
    }

    [Fact]
    public void BatchExecutionResult_EmptySteps_IsSuccess()
    {
        var result = new BatchExecutionResult(
            true,
            "empty-id",
            "Empty Batch",
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow,
            Array.Empty<BatchStepResult>());

        Assert.True(result.Success);
        Assert.Empty(result.StepResults);
    }

    [Fact]
    public void BatchStepResult_WithArtifacts_ContainsArtifacts()
    {
        var artifacts = new[] { "log.txt", "report.csv", "data.zip" };
        
        var result = new BatchStepResult(
            1,
            "OneWaySyncApply",
            true,
            "Completed",
            TimeSpan.FromSeconds(5),
            artifacts);

        Assert.Equal(3, result.Artifacts.Count);
        Assert.Contains("log.txt", result.Artifacts);
        Assert.Contains("report.csv", result.Artifacts);
        Assert.Contains("data.zip", result.Artifacts);
    }

    [Fact]
    public void BatchExecutionResult_WithLogAndReport_HasPaths()
    {
        var stepResults = new List<BatchStepResult>();
        
        var result = new BatchExecutionResult(
            true,
            "test-id",
            "Test Batch",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            stepResults,
            "C:\\Logs\\batch.log",
            "C:\\Reports\\batch-report.csv");

        Assert.Equal("C:\\Logs\\batch.log", result.LogPath);
        Assert.Equal("C:\\Reports\\batch-report.csv", result.ReportPath);
    }
}
