using Xunit;
using BareSync.App.BatchMode;
using BareSync.App.BatchMode.Screens;
using BareSync.Domain;
using BareSync.Infra;
using System;
using System.Text.Json.Nodes;

namespace BareSync.Tests;

/// <summary>
/// TP_BATCH_030-035 - Step Editor Screens (S2.6-S2.17) - Unit tests with injectable input providers
/// </summary>
public sealed class UiRoutingTpBatch030Tests : IDisposable
{
    private const int StepEditorContextFieldCount = 6;
    private const int StepEditorClearOption = StepEditorContextFieldCount + 2;
    private const int StepEditorMoveUpOption = StepEditorClearOption + 1;
    private const int StepEditorMoveDownOption = StepEditorMoveUpOption + 1;
    private const int StepEditorRemoveOption = StepEditorMoveDownOption + 1;
    private const int StepEditorCopyFromStepOption = StepEditorRemoveOption + 1;

    private readonly string _appDataRoot;
    private readonly BatchStorageLoader _loader;
    private readonly AppConfig _config;
    private readonly BatchStorageWriter _writer;

    public UiRoutingTpBatch030Tests()
    {
        _appDataRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_appDataRoot);
        Directory.CreateDirectory(Path.Combine(_appDataRoot, "batches"));
        _loader = new BatchStorageLoader();
        _config = new AppConfig();
        _writer = new BatchStorageWriter();
    }

    public void Dispose()
    {
        try { Directory.Delete(_appDataRoot, true); } catch { }
    }

    private BatchV0 CreateTestBatch(string name = "Test Batch")
    {
        var batch = new BatchV0
        {
            SchemaVersion = 0,
            Id = Guid.NewGuid().ToString("D"),
            Name = name,
            CreatedUtc = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss'Z'"),
            UpdatedUtc = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss'Z'"),
            ContextSnapshot = new JsonObject(),
            Steps = new List<BatchStepV0>()
        };
        _writer.SaveAtomic(_appDataRoot, batch, out _);
        return batch;
    }

    [Fact]
    public void TP_BATCH_030_StepTypePicker_SelectOneWaySyncApply()
    {
        // Arrange - Get the correct menu digit for OneWaySyncApply
        var screen = new StepTypePickerScreen();
        var applyDigit = TestMenuDigits.DigitForOperation(BatchOperationCatalog.OperationTypeOneWaySyncApply);
        
        // Act - Use the dynamic digit for OneWaySyncApply
        var result = screen.Show(applyDigit);
        
        // Assert - Should return OneWaySyncApply operation type
        Assert.Equal(BatchOperationCatalog.OperationTypeOneWaySyncApply, result);
    }

    [Fact]
    public void TP_BATCH_031_StepTypePicker_CancelReturnsNull()
    {
        // Arrange
        var screen = new StepTypePickerScreen();
        
        // Act - Select 0 (cancel)
        var result = screen.Show(0);
        
        // Assert - Should return null for cancel
        Assert.Null(result);
    }

    [Fact]
    public void TP_BATCH_032_StepEditor_DisplayOverridesAndBack()
    {
        // Arrange - Create batch with step that has overrides
        var batch = CreateTestBatch("TP_BATCH_032");
        batch.Steps.Add(new BatchStepV0
        {
            StepId = "step-001",
            OperationType = "OneWaySyncApply",
            OperationParams = new StepOperationParamsV0 { Values = new JsonObject() },
            ContextOverrides = new JsonObject { ["sourceRoot"] = "C:\\Test\\Source" }
        });
        _writer.SaveAtomic(_appDataRoot, batch, out _);
        
        var screen = new StepEditorScreen();
        var step = batch.Steps[0];
        
        // Act - Simulate user pressing 0 (Back) immediately
        var result = screen.Show(step, batch, _appDataRoot, (min, max) => 0);
        
        // Assert - No changes, so nothing to save
        Assert.NotNull(result);
        Assert.False(result.Saved);
        Assert.Null(result.Step);
        Assert.False(result.HasChanges);
    }

    [Fact]
    public void TP_BATCH_033_StepEditor_MoveStepDownReturnsAction()
    {
        // Arrange - Create batch with step
        var batch = CreateTestBatch("TP_BATCH_033");
        batch.Steps.Add(new BatchStepV0
        {
            StepId = "step-001",
            OperationType = "OneWaySyncDryRun",
            OperationParams = new StepOperationParamsV0 { Values = new JsonObject() },
            ContextOverrides = new JsonObject()
        });
        _writer.SaveAtomic(_appDataRoot, batch, out _);
        
        var screen = new StepEditorScreen();
        
        // Act - Select Move Down
        var result = screen.Show(batch.Steps[0], batch, _appDataRoot, (min, max) => StepEditorMoveDownOption);
        
        // Assert
        Assert.NotNull(result);
        Assert.True(result.Saved);
        Assert.Equal(StepEditorAction.MoveDown, result.Action);
    }

    [Fact]
    public void TP_BATCH_034_StepEditor_RemoveReturnsAction()
    {
        // Arrange
        var batch = CreateTestBatch("TP_BATCH_034");
        batch.Steps.Add(new BatchStepV0
        {
            StepId = "step-001",
            OperationType = "OneWaySyncApply",
            OperationParams = new StepOperationParamsV0 { Values = new JsonObject() },
            ContextOverrides = new JsonObject()
        });
        _writer.SaveAtomic(_appDataRoot, batch, out _);
        
        var screen = new StepEditorScreen();
        
        // Act - Select Remove, then confirm 'y'
        var result = screen.Show(batch.Steps[0], batch, _appDataRoot, (min, max) => StepEditorRemoveOption, null, () => 'y');
        
        // Assert
        Assert.NotNull(result);
        Assert.True(result.Saved);
        Assert.Equal(StepEditorAction.Remove, result.Action);
    }

    [Fact]
    public void TP_BATCH_035_StepEditor_ClearAllOverrides()
    {
        // Arrange
        var batch = CreateTestBatch("TP_BATCH_035");
        batch.Steps.Add(new BatchStepV0
        {
            StepId = "step-001",
            OperationType = "OneWaySyncApply",
            OperationParams = new StepOperationParamsV0 { Values = new JsonObject() },
            ContextOverrides = new JsonObject { ["sourceRoot"] = "C:\\Test" }
        });
        
        var screen = new StepEditorScreen();
        
        // Act - Select Clear Overrides, then All (option 2 since there is 1 field), then 'y' to confirm, then Back (0) to save
        int callCount = 0;
        var result = screen.Show(batch.Steps[0], batch, _appDataRoot, (min, max) =>
        {
            callCount++;
            if (callCount == 1) return StepEditorClearOption; // Menu -> Clear Overrides
            if (callCount == 2) return 2; // Clear Menu -> Clear All
            return 0; // Return to save after confirm
        }, null, () => 'y');
        
        // Assert
        Assert.NotNull(result);
        Assert.True(result.Saved);
        var overrides = result.Step?.ContextOverrides as JsonObject;
        Assert.True(overrides == null || overrides.Count == 0);
    }

    [Fact]
    public void TP_BATCH_036_StepEditor_CopyOverridesFromAnotherStep()
    {
        // Arrange
        var batch = CreateTestBatch("TP_BATCH_036");
        batch.Steps.Add(new BatchStepV0
        {
            StepId = "step-source",
            OperationType = "OneWaySyncApply",
            OperationParams = new StepOperationParamsV0 { Values = new JsonObject() },
            ContextOverrides = new JsonObject
            {
                [BatchContextFields.SourceRoot] = "C:\\Data\\Source",
                [BatchContextFields.MirrorRoot] = "D:\\Data\\Mirror"
            }
        });
        batch.Steps.Add(new BatchStepV0
        {
            StepId = "step-target",
            OperationType = "OneWaySyncDryRun",
            OperationParams = new StepOperationParamsV0 { Values = new JsonObject() },
            ContextOverrides = new JsonObject()
        });

        var screen = new StepEditorScreen();

        // Copy overrides from another step, 1 = select source step, 0 = back (save)
        int callCount = 0;
        var result = screen.Show(batch.Steps[1], batch, _appDataRoot, (min, max) =>
        {
            callCount++;
            if (callCount == 1) return StepEditorCopyFromStepOption;
            if (callCount == 2) return 1;
            return 0;
        });

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Saved);
        Assert.NotNull(result.Step);

        var copied = result.Step!.ContextOverrides as JsonObject;
        Assert.NotNull(copied);
        Assert.Equal("C:\\Data\\Source", copied![BatchContextFields.SourceRoot]?.GetValue<string>());
        Assert.Equal("D:\\Data\\Mirror", copied[BatchContextFields.MirrorRoot]?.GetValue<string>());
    }
}
