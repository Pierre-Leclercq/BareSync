using BareSync.Domain;
using BareSync.UI;

namespace BareSync.App.BatchMode.Screens;

/// <summary>
/// S2.15 Run Summary - Display execution results.
/// </summary>
internal sealed class RunSummaryScreen
{
    private const int PageSize = 9;

    public RunSummaryAction Show(BatchExecutionResult result, MenuStatus? lastStatus = null)
    {
        var pageIndex = 0;
        var running = true;
        var overallStatus = BatchRunStatus.GetOverallStatus(result);

        while (running)
        {
            var steps = result.StepResults;
            var totalPages = Math.Max(1, (int)Math.Ceiling(steps.Count / (double)PageSize));
            if (pageIndex >= totalPages) pageIndex = totalPages - 1;

            var pageSteps = steps.Skip(pageIndex * PageSize).Take(PageSize).ToList();

            UiInteraction.Clear();
            Bare.Primitive.UI.UiConsole.WriteLine("** Batch / Summary **");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine($"Batch: {result.BatchName} [{GetShortId(result.BatchId)}]");
            Bare.Primitive.UI.UiConsole.WriteLine($"Status: {BatchRunStatus.ToLabel(overallStatus)}");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine($"Page {pageIndex + 1}/{totalPages}");
            Bare.Primitive.UI.UiConsole.WriteLine();

            if (steps.Count == 0)
            {
                Bare.Primitive.UI.UiConsole.WriteLine("(no steps executed)");
            }
            else
            {
                for (var i = 0; i < pageSteps.Count; i++)
                {
                    var step = pageSteps[i];
                    var statusLabel = BatchRunStatus.ToLabel(BatchRunStatus.GetStepStatus(step));
                    Bare.Primitive.UI.UiConsole.WriteLine($"{step.StepIndex}) {step.OperationType} - {statusLabel} - {step.StatusMessage}");
                }
            }

            var artifacts = pageSteps.SelectMany(step => step.Artifacts).Where(path => !string.IsNullOrWhiteSpace(path)).ToList();
            if (artifacts.Count > 0)
            {
                Bare.Primitive.UI.UiConsole.WriteLine();
                Bare.Primitive.UI.UiConsole.WriteLine("Artifacts:");
                foreach (var artifact in artifacts.Take(5))
                {
                    Bare.Primitive.UI.UiConsole.WriteLine($"  - {artifact}");
                }

                if (artifacts.Count > 5)
                {
                    Bare.Primitive.UI.UiConsole.WriteLine($"  - ... {artifacts.Count - 5} more");
                }
            }

            Bare.Primitive.UI.UiConsole.WriteLine();
            if (lastStatus is not null && !string.IsNullOrWhiteSpace(lastStatus.StatusLine))
            {
                Bare.Primitive.UI.UiConsole.WriteLine($"Last status: {lastStatus.StatusLine}");
                Bare.Primitive.UI.UiConsole.WriteLine();
            }

            Bare.Primitive.UI.UiConsole.WriteLine("** Menu **");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine("1. View artifacts");
            Bare.Primitive.UI.UiConsole.WriteLine("2. Back to batch");
            Bare.Primitive.UI.UiConsole.WriteLine("3. Next page");
            Bare.Primitive.UI.UiConsole.WriteLine("4. Previous page");
            Bare.Primitive.UI.UiConsole.WriteLine("0. Back");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.Write("Select an option: ");

            var selection = UiInteraction.ReadMenuDigit(0, 4);
            switch (selection)
            {
                case 0:
                case 2:
                    running = false;
                    return RunSummaryAction.Back;
                case 1:
                    return RunSummaryAction.ViewArtifacts;
                case 3:
                    if (totalPages > 1)
                    {
                        pageIndex = (pageIndex + 1) % totalPages;
                    }
                    break;
                case 4:
                    if (totalPages > 1)
                    {
                        pageIndex = pageIndex == 0 ? totalPages - 1 : pageIndex - 1;
                    }
                    break;
            }
        }

        return RunSummaryAction.Back;
    }

    private static string GetShortId(string id) =>
        string.IsNullOrWhiteSpace(id) ? "?" : (id.Length <= 8 ? id : id.Substring(0, 8));
}

/// <summary>
/// Action from Run Summary screen.
/// </summary>
internal enum RunSummaryAction
{
    Back,
    ViewArtifacts
}
