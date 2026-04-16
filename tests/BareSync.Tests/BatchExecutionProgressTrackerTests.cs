using Bare.Primitive.UI;
using BareSync.App.BatchMode.Screens;

namespace BareSync.Tests;

public sealed class BatchExecutionProgressTrackerTests
{
    [Fact]
    public void OnStepStarting_ThenKnownTotalProgress_PrintsExecutingLabelWithoutInitialPreparingHeader()
    {
        var tracker = new BatchExecutionProgressTracker(
            batchLabel: "Batch",
            stepCount: 2,
            keyInput: new ScriptedUiKeyInput(Array.Empty<ConsoleKeyInfo>()));

        var console = new MockConsoleAdapter();
        using var _ = new TestConsoleScope(console);

        tracker.OnStepStarting(1, "OneWaySyncApply");
        tracker.OnStepProgress(1, "OneWaySyncApply", processed: 1, total: 4, currentItem: "item");

        var output = console.OutputText;
        Assert.Contains("Step 1/2: [OneWaySyncApply]", output);
        Assert.DoesNotContain("Preparing step [OneWaySyncApply]...", output);
        Assert.Contains("Executing step [OneWaySyncApply]... (item)", output);
        Assert.Contains("Progress: 1/4 (25%)", output);
    }

    [Fact]
    public void OnStepProgress_WhenTotalUnknown_PrintsPreparingCounterWithoutExecutingLabel()
    {
        var tracker = new BatchExecutionProgressTracker(
            batchLabel: "Batch",
            stepCount: 2,
            keyInput: new ScriptedUiKeyInput(Array.Empty<ConsoleKeyInfo>()));

        var console = new MockConsoleAdapter();
        using var _ = new TestConsoleScope(console);

        tracker.OnStepStarting(1, "OneWaySyncApply");
        tracker.OnStepProgress(1, "OneWaySyncApply", processed: 3, total: -1, currentItem: null);

        var output = console.OutputText;
        Assert.Contains("Preparing step [OneWaySyncApply]... (Discovering items)", output);
        Assert.Contains("Preparing: 3 item(s) discovered", output);
        Assert.DoesNotContain("Executing step [OneWaySyncApply]...", output);
    }

    [Fact]
    public void OnStepProgress_WhenPhaseLabelChanges_ReprintsExecutingHeaderWithNewLabel()
    {
        var tracker = new BatchExecutionProgressTracker(
            batchLabel: "Batch",
            stepCount: 1,
            keyInput: new ScriptedUiKeyInput(Array.Empty<ConsoleKeyInfo>()));

        var console = new MockConsoleAdapter();
        using var _ = new TestConsoleScope(console);

        tracker.OnStepStarting(1, "OneWaySyncApply");
        tracker.OnStepProgress(1, "OneWaySyncApply", processed: 1, total: 10, currentItem: "Applying sync decisions...");
        tracker.OnStepProgress(1, "OneWaySyncApply", processed: 2, total: 10, currentItem: "Refreshing destination index...");

        var output = console.OutputText;
        Assert.Contains("Executing step [OneWaySyncApply]... (Applying sync decisions...)", output);
        Assert.Contains("Executing step [OneWaySyncApply]... (Refreshing destination index...)", output);
    }

    [Fact]
    public void OnStepProgress_WhenEscapePressed_SetsCancellationRequested()
    {
        var tracker = new BatchExecutionProgressTracker(
            batchLabel: "Batch",
            stepCount: 2,
            keyInput: new ScriptedUiKeyInput(new[]
            {
                new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, false, false, false)
            }));

        tracker.OnStepProgress(1, "OneWaySyncApply", processed: 1, total: 2, currentItem: "item");

        Assert.True(tracker.IsCancellationRequested);
    }

    [Fact]
    public void OnStepProgress_WhenNoEscape_DoesNotRequestCancellation()
    {
        var tracker = new BatchExecutionProgressTracker(
            batchLabel: "Batch",
            stepCount: 2,
            keyInput: new ScriptedUiKeyInput(new[]
            {
                new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false)
            }));

        tracker.OnStepProgress(1, "OneWaySyncApply", processed: 1, total: 2, currentItem: "item");

        Assert.False(tracker.IsCancellationRequested);
    }

    [Fact]
    public void OnStepStarting_AfterInlinePreparingProgress_TerminatesPreviousLineBeforeNextStep()
    {
        var tracker = new BatchExecutionProgressTracker(
            batchLabel: "Batch",
            stepCount: 2,
            keyInput: new ScriptedUiKeyInput(Array.Empty<ConsoleKeyInfo>()));

        var console = new MockConsoleAdapter();
        using var _ = new TestConsoleScope(console);

        tracker.OnStepStarting(1, "RefreshIndexesSmart");
        tracker.OnStepProgress(1, "RefreshIndexesSmart", processed: 42, total: -1, currentItem: null);
        tracker.OnStepStarting(2, "RefreshIndexesSmart");

        var output = console.OutputText;
        const string preparingMarker = "Preparing: 42 item(s) discovered";
        var preparingIndex = output.IndexOf(preparingMarker, StringComparison.Ordinal);
        Assert.True(preparingIndex >= 0);

        var afterPreparingIndex = preparingIndex + preparingMarker.Length;
        Assert.True(afterPreparingIndex < output.Length);
        Assert.True(output[afterPreparingIndex] is '\r' or '\n');

        var firstHeaderIndex = output.IndexOf("** Batch / Execution **", StringComparison.Ordinal);
        Assert.True(firstHeaderIndex >= 0);
        var secondHeaderIndex = output.IndexOf("** Batch / Execution **", firstHeaderIndex + 1, StringComparison.Ordinal);
        Assert.True(secondHeaderIndex > preparingIndex);
    }

    [Fact]
    public void OnStepProgress_WhenProcessedCounterResets_ReprintsPreparingHeaderBeforeNewCounter()
    {
        var tracker = new BatchExecutionProgressTracker(
            batchLabel: "Batch",
            stepCount: 1,
            keyInput: new ScriptedUiKeyInput(Array.Empty<ConsoleKeyInfo>()));

        var console = new MockConsoleAdapter();
        using var _ = new TestConsoleScope(console);

        tracker.OnStepStarting(1, "RefreshIndexesSmart");
        tracker.OnStepProgress(1, "RefreshIndexesSmart", processed: 12, total: -1, currentItem: null);
        tracker.OnStepProgress(1, "RefreshIndexesSmart", processed: 3, total: -1, currentItem: null);

        var output = console.OutputText;
        var firstPreparingIndex = output.IndexOf("Preparing step [RefreshIndexesSmart]...", StringComparison.Ordinal);
        Assert.True(firstPreparingIndex >= 0);

        var secondPreparingIndex = output.IndexOf(
            "Preparing step [RefreshIndexesSmart]...",
            firstPreparingIndex + 1,
            StringComparison.Ordinal);
        Assert.True(secondPreparingIndex > firstPreparingIndex);

        Assert.Contains("Preparing: 12 item(s) discovered", output);
        Assert.Contains("Preparing: 3 item(s) discovered", output);
    }
}