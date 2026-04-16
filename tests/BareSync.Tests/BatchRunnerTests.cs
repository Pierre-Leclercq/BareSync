using Xunit;
using BareSync.Domain;
using BareSync.Infra;
using System.Text.Json.Nodes;
using System.Reflection;

namespace BareSync.Tests;

public sealed class BatchRunnerTests
{
    [Fact]
    public void BuildConfigForStep_ContextMirrorStringTrue_OverridesBaseConfig()
    {
        using var temp = new TempDirectory();
        var baseConfig = CreateMinimalConfig(temp);
        baseConfig.Mirror = false;

        var context = new JsonObject
        {
            ["mirror"] = "true"
        };

        var stepConfig = InvokeBuildConfigForStep(baseConfig, context);

        Assert.True(stepConfig.Mirror);
    }

    [Fact]
    public void BuildConfigForStep_ContextMirrorStringZero_OverridesBaseConfig()
    {
        using var temp = new TempDirectory();
        var baseConfig = CreateMinimalConfig(temp);
        baseConfig.Mirror = true;

        var context = new JsonObject
        {
            ["mirror"] = "0"
        };

        var stepConfig = InvokeBuildConfigForStep(baseConfig, context);

        Assert.False(stepConfig.Mirror);
    }

    [Fact]
    public void BuildConfigForStep_ContextMirrorInvalid_FallsBackToBaseConfig()
    {
        using var temp = new TempDirectory();
        var baseConfig = CreateMinimalConfig(temp);
        baseConfig.Mirror = true;

        var context = new JsonObject
        {
            ["mirror"] = "not-a-bool"
        };

        var stepConfig = InvokeBuildConfigForStep(baseConfig, context);

        Assert.True(stepConfig.Mirror);
    }

    [Fact]
    public void BatchStepResult_Record_HasExpectedProperties()
    {
        var result = new BatchStepResult(
            StepIndex: 1,
            OperationType: "OneWaySyncApply",
            Success: true,
            StatusMessage: "Completed",
            Duration: TimeSpan.FromSeconds(5),
            Artifacts: new[] { "log.txt", "report.csv" });

        Assert.Equal(1, result.StepIndex);
        Assert.Equal("OneWaySyncApply", result.OperationType);
        Assert.True(result.Success);
        Assert.Equal("Completed", result.StatusMessage);
        Assert.Equal(TimeSpan.FromSeconds(5), result.Duration);
        Assert.Equal(2, result.Artifacts.Count);
    }

    [Fact]
    public void BatchExecutionResult_Record_HasExpectedProperties()
    {
        var stepResults = new List<BatchStepResult>
        {
            new BatchStepResult(1, "OneWaySyncApply", true, "Done", TimeSpan.FromSeconds(2), Array.Empty<string>())
        };

        var result = new BatchExecutionResult(
            Success: true,
            BatchId: "test-id",
            BatchName: "Test Batch",
            StartedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            CompletedAt: DateTimeOffset.UtcNow,
            StepResults: stepResults,
            LogPath: "batch.log",
            ReportPath: "batch-report.csv");

        Assert.True(result.Success);
        Assert.Equal("test-id", result.BatchId);
        Assert.Equal("Test Batch", result.BatchName);
        Assert.Single(result.StepResults);
        Assert.Equal("batch.log", result.LogPath);
        Assert.Equal("batch-report.csv", result.ReportPath);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyBatch_ReturnsEmptySuccessResult()
    {
        var batch = CreateValidBatch("test-empty");
        batch.Steps.Clear();

        var readiness = BatchExecutionReadinessEvaluator.EvaluateBatchExecutionReadiness(batch);
        var progress = new TestBatchExecutionProgress();
        using var temp = new TempDirectory();
        var config = CreateMinimalConfig(temp);

        var result = await BatchRunner.ExecuteAsync(batch, readiness, config, progress, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("test-empty", result.BatchId);
        Assert.Empty(result.StepResults);
    }

    [Fact]
    public async Task ExecuteAsync_SingleStep_ExecutesStepAndReturnsResult()
    {
        var batch = CreateValidBatch("test-single");
        batch.Steps.Add(new BatchStepV0
        {
            OperationType = "OneWaySyncApply",
            OperationParams = new StepOperationParamsV0 { Values = new JsonObject() },
            ContextOverrides = new JsonObject()
        });

        var readiness = BatchExecutionReadinessEvaluator.EvaluateBatchExecutionReadiness(batch);
        var progress = new TestBatchExecutionProgress();
        using var temp = new TempDirectory();
        var config = CreateMinimalConfig(temp);

        var result = await BatchRunner.ExecuteAsync(batch, readiness, config, progress, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Single(result.StepResults);
        Assert.Equal(1, result.StepResults[0].StepIndex);
        Assert.Equal("OneWaySyncApply", result.StepResults[0].OperationType);
    }

    [Fact]
    public async Task ExecuteAsync_MultipleSteps_ExecutesAllSequentially()
    {
        var batch = CreateValidBatch("test-multi");
        batch.Steps.Add(new BatchStepV0
        {
            OperationType = BatchOperationCatalog.OperationTypeRefreshIndexesFull,
            OperationParams = new StepOperationParamsV0 { Values = new JsonObject() },
            ContextOverrides = new JsonObject()
        });
        batch.Steps.Add(new BatchStepV0
        {
            OperationType = BatchOperationCatalog.OperationTypeOneWaySyncApply,
            OperationParams = new StepOperationParamsV0 { Values = new JsonObject() },
            ContextOverrides = new JsonObject()
        });

        var readiness = BatchExecutionReadinessEvaluator.EvaluateBatchExecutionReadiness(batch);
        var progress = new TestBatchExecutionProgress();
        using var temp = new TempDirectory();
        var config = CreateMinimalConfig(temp);

        var result = await BatchRunner.ExecuteAsync(batch, readiness, config, progress, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, result.StepResults.Count);
        Assert.Equal(1, result.StepResults[0].StepIndex);
        Assert.Equal(2, result.StepResults[1].StepIndex);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsProgressEvents()
    {
        var batch = CreateValidBatch("test-progress");
        batch.Steps.Add(new BatchStepV0
        {
            OperationType = "OneWaySyncApply",
            OperationParams = new StepOperationParamsV0 { Values = new JsonObject() },
            ContextOverrides = new JsonObject()
        });

        var readiness = BatchExecutionReadinessEvaluator.EvaluateBatchExecutionReadiness(batch);
        var progress = new TestBatchExecutionProgress();
        using var temp = new TempDirectory();
        var config = CreateMinimalConfig(temp);

        await BatchRunner.ExecuteAsync(batch, readiness, config, progress, CancellationToken.None);

        Assert.True(progress.StepStartingCalled);
        Assert.True(progress.StepCompletedCalled);
        Assert.Equal(1, progress.StartingStepIndex);
        Assert.Equal("OneWaySyncApply", progress.StartingOperationType);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationRequested_StopsExecution()
    {
        var batch = CreateValidBatch("test-cancel");
        batch.Steps.Add(new BatchStepV0
        {
            OperationType = "OneWaySyncApply",
            OperationParams = new StepOperationParamsV0 { Values = new JsonObject() },
            ContextOverrides = new JsonObject()
        });

        var readiness = BatchExecutionReadinessEvaluator.EvaluateBatchExecutionReadiness(batch);
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var progress = new TestBatchExecutionProgress();
        using var temp = new TempDirectory();
        var config = CreateMinimalConfig(temp);

        var result = await BatchRunner.ExecuteAsync(batch, readiness, config, progress, cts.Token);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownOperationType_ReturnsFailure()
    {
        var batch = CreateValidBatch("test-unknown");
        batch.Steps.Add(new BatchStepV0
        {
            OperationType = "UnknownOperation",
            OperationParams = new StepOperationParamsV0 { Values = new JsonObject() },
            ContextOverrides = new JsonObject()
        });

        var readiness = BatchExecutionReadinessEvaluator.EvaluateBatchExecutionReadiness(batch);
        var progress = new TestBatchExecutionProgress();
        using var temp = new TempDirectory();
        var config = CreateMinimalConfig(temp);

        var result = await BatchRunner.ExecuteAsync(batch, readiness, config, progress, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Single(result.StepResults);
        Assert.False(result.StepResults[0].Success);
        Assert.Contains("Unknown operation type", result.StepResults[0].StatusMessage);
    }

    [Fact]
    public async Task ExecuteAsync_FailedStep_StopsAndMarksRemainingAsNotRun()
    {
        var batch = CreateValidBatch("test-stop-policy");
        batch.Steps.Add(new BatchStepV0
        {
            OperationType = "UnknownOperation",
            OperationParams = new StepOperationParamsV0 { Values = new JsonObject() },
            ContextOverrides = new JsonObject()
        });
        batch.Steps.Add(new BatchStepV0
        {
            OperationType = BatchOperationCatalog.OperationTypeOneWaySyncDryRun,
            OperationParams = new StepOperationParamsV0 { Values = new JsonObject() },
            ContextOverrides = new JsonObject()
        });

        var readiness = BatchExecutionReadinessEvaluator.EvaluateBatchExecutionReadiness(batch);
        var progress = new TestBatchExecutionProgress();
        using var temp = new TempDirectory();
        var config = CreateMinimalConfig(temp);

        var result = await BatchRunner.ExecuteAsync(batch, readiness, config, progress, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(2, result.StepResults.Count);
        Assert.Contains("Unknown operation type", result.StepResults[0].StatusMessage);
        Assert.StartsWith("NotRun", result.StepResults[1].StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_RefreshIndexesSmart_PrunesMissingEntriesFromSourceAndDestinationIndexes()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.RootPath, "source");
        var mirrorRoot = Path.Combine(temp.RootPath, "mirror");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(mirrorRoot);

        var sourceIndexPath = Path.Combine(temp.RootPath, "source_index.csv");
        var destIndexPath = Path.Combine(temp.RootPath, "dest_index.csv");

        var sourceLiveFile = Path.Combine(sourceRoot, "keep-source.txt");
        var mirrorLiveFile = Path.Combine(mirrorRoot, "keep-dest.txt");
        await File.WriteAllTextAsync(sourceLiveFile, "source-live");
        await File.WriteAllTextAsync(mirrorLiveFile, "dest-live");

        await File.WriteAllTextAsync(
            sourceIndexPath,
            string.Join(
                Environment.NewLine,
                new[]
                {
                    CsvIndexWriter.Header,
                    "0,,keep-source.txt,AAAAAAAAAAAAAAAA,11,2026-01-01T00:00:00.0000000Z,File",
                    "1,,ghost-source.txt,BBBBBBBBBBBBBBBB,22,2026-01-01T00:00:00.0000000Z,File",
                    string.Empty
                }));

        await File.WriteAllTextAsync(
            destIndexPath,
            string.Join(
                Environment.NewLine,
                new[]
                {
                    CsvIndexWriter.Header,
                    "0,,keep-dest.txt,CCCCCCCCCCCCCCCC,33,2026-01-01T00:00:00.0000000Z,File",
                    "1,,ghost-dest.txt,DDDDDDDDDDDDDDDD,44,2026-01-01T00:00:00.0000000Z,File",
                    string.Empty
                }));

        await File.WriteAllTextAsync($"{sourceIndexPath}.work", "stale-work");
        await File.WriteAllTextAsync($"{sourceIndexPath}.checkpoint", "stale-checkpoint");
        await File.WriteAllTextAsync($"{destIndexPath}.work", "stale-work");
        await File.WriteAllTextAsync($"{destIndexPath}.checkpoint", "stale-checkpoint");

        var batch = CreateValidBatch("test-refresh-smart-prune");
        batch.Steps.Add(new BatchStepV0
        {
            OperationType = BatchOperationCatalog.OperationTypeRefreshIndexesSmart,
            OperationParams = new StepOperationParamsV0 { Values = new JsonObject() },
            ContextOverrides = new JsonObject()
        });

        var readiness = BatchExecutionReadinessEvaluator.EvaluateBatchExecutionReadiness(batch);
        var progress = new TestBatchExecutionProgress();
        var config = new AppConfig
        {
            SourceRoot = sourceRoot,
            MirrorRoot = mirrorRoot,
            SourceIndexCsvPath = sourceIndexPath,
            DestIndexCsvPath = destIndexPath
        };

        var result = await BatchRunner.ExecuteAsync(batch, readiness, config, progress, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Single(result.StepResults);
        Assert.Contains("Pruned stale index entries", result.StepResults[0].StatusMessage);

        var refreshedSourceRows = await CsvIndexReader.ReadAsync(sourceIndexPath, CancellationToken.None);
        var refreshedDestRows = await CsvIndexReader.ReadAsync(destIndexPath, CancellationToken.None);

        Assert.Contains("keep-source.txt", refreshedSourceRows.Keys);
        Assert.DoesNotContain("ghost-source.txt", refreshedSourceRows.Keys);

        Assert.Contains("keep-dest.txt", refreshedDestRows.Keys);
        Assert.DoesNotContain("ghost-dest.txt", refreshedDestRows.Keys);

        Assert.False(File.Exists($"{sourceIndexPath}.work"));
        Assert.False(File.Exists($"{sourceIndexPath}.checkpoint"));
        Assert.False(File.Exists($"{destIndexPath}.work"));
        Assert.False(File.Exists($"{destIndexPath}.checkpoint"));

        Assert.True(File.Exists(sourceLiveFile));
        Assert.True(File.Exists(mirrorLiveFile));
    }

    [Fact]
    public async Task ExecuteAsync_RefreshIndexesSmart_DoesNotAppendDiskOnlyFiles()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.RootPath, "source");
        var mirrorRoot = Path.Combine(temp.RootPath, "mirror");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(mirrorRoot);

        var sourceIndexPath = Path.Combine(temp.RootPath, "source_index.csv");
        var destIndexPath = Path.Combine(temp.RootPath, "dest_index.csv");

        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "keep-source.txt"), "source-live");
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "disk-only-source.txt"), "disk-only");
        await File.WriteAllTextAsync(Path.Combine(mirrorRoot, "keep-dest.txt"), "dest-live");
        await File.WriteAllTextAsync(Path.Combine(mirrorRoot, "disk-only-dest.txt"), "disk-only");

        await File.WriteAllTextAsync(
            sourceIndexPath,
            string.Join(
                Environment.NewLine,
                new[]
                {
                    CsvIndexWriter.Header,
                    "0,,keep-source.txt,AAAAAAAAAAAAAAAA,11,2026-01-01T00:00:00.0000000Z,File",
                    string.Empty
                }));

        await File.WriteAllTextAsync(
            destIndexPath,
            string.Join(
                Environment.NewLine,
                new[]
                {
                    CsvIndexWriter.Header,
                    "0,,keep-dest.txt,CCCCCCCCCCCCCCCC,33,2026-01-01T00:00:00.0000000Z,File",
                    string.Empty
                }));

        var batch = CreateValidBatch("test-refresh-smart-no-append");
        batch.Steps.Add(new BatchStepV0
        {
            OperationType = BatchOperationCatalog.OperationTypeRefreshIndexesSmart,
            OperationParams = new StepOperationParamsV0 { Values = new JsonObject() },
            ContextOverrides = new JsonObject()
        });

        var readiness = BatchExecutionReadinessEvaluator.EvaluateBatchExecutionReadiness(batch);
        var progress = new TestBatchExecutionProgress();
        var config = new AppConfig
        {
            SourceRoot = sourceRoot,
            MirrorRoot = mirrorRoot,
            SourceIndexCsvPath = sourceIndexPath,
            DestIndexCsvPath = destIndexPath
        };

        var result = await BatchRunner.ExecuteAsync(batch, readiness, config, progress, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Single(result.StepResults);

        var refreshedSourceRows = await CsvIndexReader.ReadAsync(sourceIndexPath, CancellationToken.None);
        var refreshedDestRows = await CsvIndexReader.ReadAsync(destIndexPath, CancellationToken.None);

        Assert.Contains("keep-source.txt", refreshedSourceRows.Keys);
        Assert.DoesNotContain("disk-only-source.txt", refreshedSourceRows.Keys);

        Assert.Contains("keep-dest.txt", refreshedDestRows.Keys);
        Assert.DoesNotContain("disk-only-dest.txt", refreshedDestRows.Keys);
    }

    [Fact]
    public async Task ExecuteAsync_RefreshIndexesSmart_MissingSourceIndex_RebuildsAndSucceeds()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.RootPath, "source");
        var mirrorRoot = Path.Combine(temp.RootPath, "mirror");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(mirrorRoot);

        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "keep-source.txt"), "source-live");
        await File.WriteAllTextAsync(Path.Combine(mirrorRoot, "keep-dest.txt"), "dest-live");

        var sourceIndexPath = Path.Combine(temp.RootPath, "missing_source_index.csv");
        var destIndexPath = Path.Combine(temp.RootPath, "dest_index.csv");
        await File.WriteAllTextAsync(
            destIndexPath,
            string.Join(
                Environment.NewLine,
                new[]
                {
                    CsvIndexWriter.Header,
                    "0,,keep-dest.txt,CCCCCCCCCCCCCCCC,33,2026-01-01T00:00:00.0000000Z,File",
                    string.Empty
                }));

        var batch = CreateValidBatch("test-refresh-smart-missing-index");
        batch.Steps.Add(new BatchStepV0
        {
            OperationType = BatchOperationCatalog.OperationTypeRefreshIndexesSmart,
            OperationParams = new StepOperationParamsV0 { Values = new JsonObject() },
            ContextOverrides = new JsonObject()
        });

        var readiness = BatchExecutionReadinessEvaluator.EvaluateBatchExecutionReadiness(batch);
        var progress = new TestBatchExecutionProgress();
        var config = new AppConfig
        {
            SourceRoot = sourceRoot,
            MirrorRoot = mirrorRoot,
            SourceIndexCsvPath = sourceIndexPath,
            DestIndexCsvPath = destIndexPath
        };

        var result = await BatchRunner.ExecuteAsync(batch, readiness, config, progress, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Single(result.StepResults);
        Assert.True(result.StepResults[0].Success);
        Assert.Contains("Smart batch index verification completed", result.StepResults[0].StatusMessage, StringComparison.OrdinalIgnoreCase);

        Assert.True(File.Exists(sourceIndexPath));
        var refreshedSourceRows = await CsvIndexReader.ReadAsync(sourceIndexPath, CancellationToken.None);
        Assert.Contains("keep-source.txt", refreshedSourceRows.Keys);
    }

    [Fact]
    public void GetSecretSlotForOperation_CreateEncryptedFolder_ReturnsScopedSlot()
    {
        var operationType = "CreateEncryptedFolder";
        var scope = "D:/Vault/A";
        
        var slot = GetSecretSlotForOperation(operationType, scope);
        
        Assert.Equal("EncryptionPassword|D:/Vault/A", slot);
    }

    [Fact]
    public void GetSecretSlotForOperation_RefreshEncryptedFolder_ReturnsScopedSlot()
    {
        var operationType = "RefreshEncryptedFolder";
        var scope = "D:/Vault/B";
        
        var slot = GetSecretSlotForOperation(operationType, scope);
        
        Assert.Equal("EncryptionPassword|D:/Vault/B", slot);
    }

    [Fact]
    public void GetSecretSlotForOperation_RestoreEncryptedFiles_ReturnsScopedSlot()
    {
        var operationType = "RestoreEncryptedFiles";
        var scope = "D:/Vault/C";
        
        var slot = GetSecretSlotForOperation(operationType, scope);
        
        Assert.Equal("EncryptionPassword|D:/Vault/C", slot);
    }

    [Fact]
    public void GetSecretSlotForOperation_OneWaySync_ReturnsEmpty()
    {
        var operationType = "OneWaySyncApply";
        var scope = "D:/Vault/Unused";
        
        var slot = GetSecretSlotForOperation(operationType, scope);
        
        Assert.Empty(slot);
    }

    [Fact]
    public void GetSecretSlotForOperation_EmptyScope_UsesNotSetMarker()
    {
        var slot = GetSecretSlotForOperation("CreateEncryptedFolder", string.Empty);
        Assert.Equal("EncryptionPassword|<not set>", slot);
    }

    // Helper method mirroring the logic in BatchSecretSlot
    private static string GetSecretSlotForOperation(string operationType, string encryptedOutputRoot)
    {
        var requiresSecret = operationType is "CreateEncryptedFolder"
            or "RefreshEncryptedFolder"
            or "RestoreEncryptedFiles";
        if (!requiresSecret)
        {
            return string.Empty;
        }

        var scope = string.IsNullOrWhiteSpace(encryptedOutputRoot)
            ? "<not set>"
            : encryptedOutputRoot.Trim();
        return $"EncryptionPassword|{scope}";
    }

    private static BatchV0 CreateValidBatch(string id)
    {
        return new BatchV0
        {
            SchemaVersion = 0,
            Id = id,
            Name = $"Test Batch {id}",
            CreatedUtc = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            UpdatedUtc = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ContextSnapshot = new JsonObject(),
            Steps = new List<BatchStepV0>()
        };
    }

    private static AppConfig CreateMinimalConfig(TempDirectory temp)
    {
        var sourceRoot = Path.Combine(temp.RootPath, "source");
        var mirrorRoot = Path.Combine(temp.RootPath, "mirror");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(mirrorRoot);

        var sourceIndex = Path.Combine(temp.RootPath, "source_index.csv");
        var destIndex = Path.Combine(temp.RootPath, "dest_index.csv");
        WriteEmptyCsvIndex(sourceIndex);
        WriteEmptyCsvIndex(destIndex);

        return new AppConfig
        {
            SourceRoot = sourceRoot,
            MirrorRoot = mirrorRoot,
            SourceIndexCsvPath = sourceIndex,
            DestIndexCsvPath = destIndex
        };
    }

    private static void WriteEmptyCsvIndex(string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.WriteAllText(path, CsvIndexWriter.Header + Environment.NewLine);
    }

    private static AppConfig InvokeBuildConfigForStep(AppConfig baseConfig, JsonObject effectiveContext)
    {
        var method = typeof(BatchRunner).GetMethod(
            "BuildConfigForStep",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var result = method!.Invoke(null, new object[] { baseConfig, effectiveContext });
        Assert.IsType<AppConfig>(result);
        return (AppConfig)result!;
    }

    private sealed class TestBatchExecutionProgress : IBatchExecutionProgress
    {
        public bool IsCancellationRequested => false;
        public bool StepStartingCalled { get; private set; }
        public bool StepCompletedCalled { get; private set; }
        public int StartingStepIndex { get; private set; }
        public string StartingOperationType { get; private set; } = string.Empty;

        public void OnStepStarting(int stepIndex, string operationType)
        {
            StepStartingCalled = true;
            StartingStepIndex = stepIndex;
            StartingOperationType = operationType;
        }

        public void OnStepCompleted(int stepIndex, string operationType, bool success, string statusMessage)
        {
            StepCompletedCalled = true;
        }

        public void OnStepProgress(int stepIndex, string operationType, int processed, int total, string? currentItem)
        {
            // Not tested in these tests
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            RootPath = Path.Combine(
                Path.GetTempPath(),
                "BareSyncTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(RootPath))
                {
                    Directory.Delete(RootPath, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
