using BareSync.Domain;
using BareSync.Infra;
using BareSync.UI;

namespace BareSync.App.BatchMode;

/// <summary>
/// Orchestrates all batch mode screens and navigation.
/// </summary>
internal sealed class BatchModeController
{
    private readonly BatchStorageLoader _loader;
    private readonly string _appDataRoot;
    private readonly AppConfig _config;
    private MenuStatus? _batchStatus;

    public BatchModeController(BatchStorageLoader loader, string appDataRoot, AppConfig config)
    {
        _loader = loader;
        _appDataRoot = appDataRoot;
        _config = config;
    }

    /// <summary>
    /// Main entry point for batch mode.
    /// </summary>
    public void Run()
    {
        var running = true;
        while (running)
        {
            var selection = ShowHomeMenu();
            switch (selection)
            {
                case 0:
                    running = false;
                    break;
                case 1:
                    var selected = ShowBatchList();
                    if (selected is not null)
                    {
                        ShowBatchDetails(selected);
                    }
                    break;
                case 2:
                    var createdId = CreateNewBatch();
                    if (!string.IsNullOrWhiteSpace(createdId))
                    {
                        var created = _loader.LoadAll(_appDataRoot)
                            .FirstOrDefault(e => string.Equals(e.Id, createdId, StringComparison.OrdinalIgnoreCase));
                        if (created is not null)
                        {
                            ShowBatchDetails(created);
                        }
                    }
                    break;
                case 3:
                    ExecuteBatchQuick();
                    break;
                case 4:
                    PurgeBatchIndexes();
                    break;
            }
        }
    }

    private int ShowHomeMenu()
    {
        UiInteraction.Clear();
        Bare.Primitive.UI.UiConsole.WriteLine("** Batch mode **");
        if (_batchStatus is not null && !string.IsNullOrWhiteSpace(_batchStatus.StatusLine))
        {
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine($"Last status: {_batchStatus.StatusLine}");
            if (!string.IsNullOrWhiteSpace(_batchStatus.LogPath))
            {
                Bare.Primitive.UI.UiConsole.WriteLine($"Log: {_batchStatus.LogPath}");
            }

            if (!string.IsNullOrWhiteSpace(_batchStatus.ReportPath))
            {
                Bare.Primitive.UI.UiConsole.WriteLine($"Report: {_batchStatus.ReportPath}");
            }
        }

        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.WriteLine("** Menu **");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.WriteLine("1. List batches");
        Bare.Primitive.UI.UiConsole.WriteLine("2. Create new batch");
        Bare.Primitive.UI.UiConsole.WriteLine("3. Execute batch");
        Bare.Primitive.UI.UiConsole.WriteLine("4. Purge Batch indexes");
        Bare.Primitive.UI.UiConsole.WriteLine("0. Back");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.Write("Select an option: ");
        return UiInteraction.ReadMenuDigit(0, 4);
    }

    private BatchStorageDescriptor? ShowBatchList()
    {
        var listScreen = new Screens.BatchListScreen();
        return listScreen.Show(_loader, _appDataRoot, _batchStatus);
    }

    private string? CreateNewBatch()
    {
        UiInteraction.Clear();
        Bare.Primitive.UI.UiConsole.WriteLine("** Batch / Create **");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.Write("Enter batch name (empty or ESC to cancel):");
        Bare.Primitive.UI.UiConsole.WriteLine();
        var name = UiInteraction.ReadLineWithEscape();
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var trimmedName = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            return null;
        }

        var id = Guid.NewGuid().ToString("D");
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        var batch = new BatchV0
        {
            SchemaVersion = 0,
            Id = id,
            Name = trimmedName,
            CreatedUtc = timestamp,
            UpdatedUtc = timestamp,
            ContextSnapshot = new System.Text.Json.Nodes.JsonObject(),
            Steps = new List<BatchStepV0>()
        };

        var writer = new BatchStorageWriter();
        if (!writer.SaveAtomic(_appDataRoot, batch, out var error))
        {
            Bare.Primitive.UI.UiConsole.WriteLine($"Failed to save batch: {error}");
            return null;
        }
        return id;
    }

    private void ShowBatchDetails(BatchStorageDescriptor descriptor)
    {
        var current = descriptor;
        var running = true;

        while (running)
        {
            var detailsScreen = new Screens.BatchDetailsScreen();
            var context = new BatchScreenContext(current, _loader, _appDataRoot, _config);
            var result = detailsScreen.Show(context, _batchStatus);

            switch (result.Action)
            {
                case Screens.BatchDetailsAction.Back:
                    running = false;
                    break;
                case Screens.BatchDetailsAction.Preflight:
                    current = ShowPreflight(current);
                    break;
                case Screens.BatchDetailsAction.EditSteps:
                    current = ShowStepsEditor(current);
                    break;
                case Screens.BatchDetailsAction.EditContext:
                    current = ShowContextEditor(current);
                    break;
            }

            if (running)
            {
                current = _loader.LoadAll(_appDataRoot)
                    .FirstOrDefault(e => string.Equals(e.Id, current.Id, StringComparison.OrdinalIgnoreCase))
                    ?? current;
            }
        }
    }

    private BatchStorageDescriptor ShowPreflight(BatchStorageDescriptor descriptor)
    {
        var current = descriptor;
        while (true)
        {
            var preflightScreen = new Screens.PreflightScreen();
            var context = new BatchScreenContext(current, _loader, _appDataRoot, _config);
            var result = preflightScreen.Show(context, _batchStatus);

            switch (result.Action)
            {
                case Screens.PreflightAction.Back:
                    return result.Descriptor;
                case Screens.PreflightAction.EditContext:
                    current = ShowContextEditor(result.Descriptor);
                    continue;
                case Screens.PreflightAction.EditSteps:
                    current = ShowStepsEditor(result.Descriptor);
                    continue;
                case Screens.PreflightAction.Run:
                    var started = ExecuteBatch(result.Descriptor, result.PreflightData);
                    if (!started)
                    {
                        current = result.Descriptor;
                        continue;
                    }

                    return result.Descriptor;
            }
        }
    }

    private BatchStorageDescriptor ShowStepsEditor(BatchStorageDescriptor descriptor)
    {
        var stepsScreen = new Screens.StepsEditorScreen();
        var context = new BatchScreenContext(descriptor, _loader, _appDataRoot, _config);
        return stepsScreen.Show(context);
    }

    private BatchStorageDescriptor ShowContextEditor(BatchStorageDescriptor descriptor)
    {
        var contextScreen = new Screens.ContextEditorScreen();
        var context = new BatchScreenContext(descriptor, _loader, _appDataRoot, _config);
        return contextScreen.Show(context);
    }

    private bool ExecuteBatch(BatchStorageDescriptor descriptor, BatchPreflightResult preflight)
    {
        Dictionary<string, string>? secrets = null;
        if (preflight.RequiresSecret)
        {
            var secretScreen = new Screens.SecretPromptScreen();
            secrets = secretScreen.Show(descriptor, _config);
            if (secrets is null)
            {
                _batchStatus = new MenuStatus
                {
                    StatusLine = "Canceled"
                };
                return false;
            }
        }
        secrets ??= new Dictionary<string, string>();

        var execScreen = new Screens.ExecutionScreen();
        var execResult = execScreen.Execute(descriptor, preflight, secrets, _config);

        if (execResult is null)
        {
            _batchStatus = new MenuStatus
            {
                StatusLine = "Fail"
            };
            return false;
        }

        var overall = BatchRunStatus.GetOverallStatus(execResult);
        _batchStatus = new MenuStatus
        {
            StatusLine = BatchRunStatus.ToLabel(overall),
            SuccessOrWarningFlag = overall is BatchRunStepStatus.Success or BatchRunStepStatus.Warning,
            LogPath = execResult.LogPath ?? string.Empty,
            ReportPath = execResult.ReportPath ?? string.Empty
        };

        var summaryScreen = new Screens.RunSummaryScreen();
        var summaryAction = summaryScreen.Show(execResult, _batchStatus);

        if (summaryAction == Screens.RunSummaryAction.ViewArtifacts)
        {
            var artifactsScreen = new Screens.ArtifactsScreen();
            artifactsScreen.Show(execResult, _batchStatus);
        }

        return true;
    }

    private void ExecuteBatchQuick()
    {
        var selectionScreen = new Screens.BatchExecuteSelectionScreen(_loader, _appDataRoot);
        var descriptor = selectionScreen.Show();
        if (descriptor is null)
        {
            return;
        }

        var summaryScreen = new Screens.BatchExecuteSummaryScreen(_appDataRoot, _config);
        var shouldRun = summaryScreen.Show(descriptor);
        if (!shouldRun)
        {
            return;
        }

        _ = ShowPreflight(descriptor);
    }

    private void PurgeBatchIndexes()
    {
        var purgeScreen = new Screens.PurgeBatchIndexesScreen();
        var status = purgeScreen.ShowAndPurge(_loader, _appDataRoot);
        if (status is not null)
        {
            _batchStatus = status;
        }
    }
}
