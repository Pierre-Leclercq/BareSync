using BareSync.App.BatchMode;
using BareSync.Domain;
using BareSync.UI;
using Bare.Primitive.UI;

namespace BareSync.App.BatchMode.Screens;

/// <summary>
/// S2.14 Run Progress - Execute batch with progress display.
/// </summary>
internal sealed class ExecutionScreen
{
    public BatchExecutionResult? Execute(
        BatchStorageDescriptor descriptor,
        BatchPreflightResult preflight,
        Dictionary<string, string> secrets,
        AppConfig config)
    {
        var batch = BatchUiHelpers.LoadBatchV0(descriptor.Path);
        if (batch is null)
        {
            Bare.Primitive.UI.UiConsole.WriteLine("Failed to load batch for execution.");
            UiInteraction.SkipNextClear();
            return null;
        }

        var readiness = BatchExecutionReadinessEvaluator.EvaluateBatchExecutionReadiness(batch);
        var batchLabel = $"{batch.Name} [{GetShortId(batch.Id)}]";
        var progress = new BatchExecutionProgressTracker(batchLabel, batch.Steps.Count);
        
        UiInteraction.Clear();
        Bare.Primitive.UI.UiConsole.WriteLine("** Batch / Execution **");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.WriteLine($"Batch: {batchLabel}");
        Bare.Primitive.UI.UiConsole.WriteLine($"Steps: {batch.Steps.Count}");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.WriteLine("Press ESC to cancel");
        Bare.Primitive.UI.UiConsole.WriteLine();

        try
        {
            var cts = new CancellationTokenSource();
            // Use BatchRunner for actual execution
            var result = BatchRunner.ExecuteAsync(batch, readiness, config, progress, secrets, cts.Token).Result;
            return result;
        }
        catch (OperationCanceledException)
        {
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine("Execution cancelled by user.");
            UiInteraction.SkipNextClear();
            return new BatchExecutionResult(
                false,
                batch.Id,
                batch.Name,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                new List<BatchStepResult>());
        }
        catch (Exception ex)
        {
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine($"Execution failed: {ex.Message}");
            UiInteraction.SkipNextClear();
            return new BatchExecutionResult(
                false,
                batch.Id,
                batch.Name,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                new List<BatchStepResult>());
        }
    }

    private static string GetShortId(string id) =>
        string.IsNullOrWhiteSpace(id) ? "?" : (id.Length <= 8 ? id : id.Substring(0, 8));
}

/// <summary>
/// Simple progress tracker for batch execution.
/// </summary>
internal sealed class BatchExecutionProgressTracker : IBatchExecutionProgress
{
    private readonly string _batchLabel;
    private readonly int _stepCount;
    private readonly IUiKeyInput _keyInput;
    private int _lastInlineProgressLength;
    private int _lastProcessed = -1;
    private string? _activePhaseKey;
    private bool _cancellationRequested;
    public bool IsCancellationRequested => _cancellationRequested;

    public BatchExecutionProgressTracker(
        string batchLabel,
        int stepCount,
        IUiKeyInput? keyInput = null)
    {
        _batchLabel = batchLabel;
        _stepCount = stepCount;
        _keyInput = keyInput ?? new ConsoleUiKeyInput();
    }

    public void OnStepStarting(int stepIndex, string operationType)
    {
        TerminateInlineProgressLine();
        RefreshBetweenSteps(stepIndex);
        _lastInlineProgressLength = 0;
        _lastProcessed = -1;
        _activePhaseKey = null;
        RenderHeader();
        Bare.Primitive.UI.UiConsole.WriteLine($"Step {stepIndex}/{_stepCount}: [{operationType}]");
        Bare.Primitive.UI.UiConsole.WriteLine();
        CheckForCancel();
    }

    public void OnStepCompleted(int stepIndex, string operationType, bool success, string statusMessage)
    {
        TerminateInlineProgressLine();

        var symbol = success ? "OK" : "FAIL";
        Bare.Primitive.UI.UiConsole.WriteLine($"Step {stepIndex}: [{symbol}] {statusMessage}");
        _lastInlineProgressLength = 0;
        _lastProcessed = -1;
        _activePhaseKey = null;
        
        // Show additional error details for failed steps
        if (!success && statusMessage.Length > 80)
        {
            Bare.Primitive.UI.UiConsole.WriteLine($"       Details: {statusMessage[..77]}...");
        }
        else if (!success)
        {
            Bare.Primitive.UI.UiConsole.WriteLine($"       Details: {statusMessage}");
        }
    }

    public void OnStepProgress(int stepIndex, string operationType, int processed, int total, string? currentItem)
    {
        if (_lastProcessed >= 0 && processed < _lastProcessed)
        {
            TerminateInlineProgressLine();
            _activePhaseKey = null;
        }

        var phaseLabel = BuildPhaseLabel(total, currentItem);
        var phaseKey = BuildPhaseKey(total, phaseLabel);
        if (!string.Equals(_activePhaseKey, phaseKey, StringComparison.Ordinal))
        {
            TerminateInlineProgressLine();
            var phasePrefix = total > 0 ? "Executing" : "Preparing";
            Bare.Primitive.UI.UiConsole.WriteLine($"{phasePrefix} step [{operationType}]... ({phaseLabel})");
            _activePhaseKey = phaseKey;
        }

        var progressLine = BuildProgressLine(processed, total);

        if (Bare.Primitive.UI.UiConsole.IsOutputRedirected)
        {
            Bare.Primitive.UI.UiConsole.WriteLine(progressLine);
            _lastInlineProgressLength = 0;
        }
        else
        {
            var paddedLine = InlineProgressText.PadForRewrite(progressLine, _lastInlineProgressLength);
            Bare.Primitive.UI.UiConsole.Write($"\r{paddedLine}");
            _lastInlineProgressLength = paddedLine.Length;
        }

        _lastProcessed = processed;

        CheckForCancel();
    }

    private static string BuildProgressLine(int processed, int total)
    {
        if (total > 0)
        {
            var boundedTotal = Math.Max(total, 1);
            var percent = Math.Clamp((int)Math.Round((processed * 100.0) / boundedTotal), 0, 100);
            return $"  Progress: {processed}/{total} ({percent}%)";
        }

        return $"  Preparing: {processed} item(s) discovered";
    }

    private static string BuildPhaseLabel(int total, string? currentItem)
    {
        if (!string.IsNullOrWhiteSpace(currentItem))
        {
            return currentItem.Trim();
        }

        return total > 0 ? "Processing items" : "Discovering items";
    }

    private static string BuildPhaseKey(int total, string phaseLabel)
    {
        var prefix = total > 0 ? "E" : "P";
        return $"{prefix}:{phaseLabel}";
    }

    private void TerminateInlineProgressLine()
    {
        if (_lastInlineProgressLength <= 0)
        {
            return;
        }

        Bare.Primitive.UI.UiConsole.WriteLine();
        _lastInlineProgressLength = 0;
    }

    private static void RefreshBetweenSteps(int stepIndex)
    {
        UiInteraction.Clear();

        // When output is redirected, Clear() is intentionally a no-op.
        // Add an explicit visual break to avoid perceived overlap between steps.
        if (stepIndex > 1 && Bare.Primitive.UI.UiConsole.IsOutputRedirected)
        {
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine("----- Next step -----");
            Bare.Primitive.UI.UiConsole.WriteLine();
        }
    }

    private void CheckForCancel()
    {
        if (_keyInput.TryReadKey(out var key, intercept: true)
            && key.Key == ConsoleKey.Escape)
        {
            _cancellationRequested = true;
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine("Cancelling...");
        }
    }

    private void RenderHeader()
    {
        Bare.Primitive.UI.UiConsole.WriteLine("** Batch / Execution **");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.WriteLine($"Batch: {_batchLabel}");
        Bare.Primitive.UI.UiConsole.WriteLine($"Steps: {_stepCount}");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.WriteLine("Press ESC to cancel");
        Bare.Primitive.UI.UiConsole.WriteLine();
    }
}
