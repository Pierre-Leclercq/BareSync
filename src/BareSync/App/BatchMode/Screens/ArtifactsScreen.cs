using BareSync.Domain;
using BareSync.UI;
using System.Text;

namespace BareSync.App.BatchMode.Screens;

/// <summary>
/// S2.15a/b Artifacts Viewer - Display and open execution artifacts.
/// </summary>
internal sealed class ArtifactsScreen
{
    private const int PageSize = 9;

    public void Show(BatchExecutionResult result, MenuStatus? lastStatus = null)
    {
        var allSteps = result.StepResults.ToList();

        var pageIndex = 0;
        var running = true;

        while (running)
        {
            var totalPages = Math.Max(1, (int)Math.Ceiling(allSteps.Count / (double)PageSize));
            if (pageIndex >= totalPages) pageIndex = totalPages - 1;

            var pageSteps = allSteps
                .Skip(pageIndex * PageSize)
                .Take(PageSize)
                .ToList();

            UiInteraction.Clear();
            Bare.Primitive.UI.UiConsole.WriteLine("** Batch / Artifacts **");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine($"Batch: {result.BatchName} [{GetShortId(result.BatchId)}]");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine($"Page {pageIndex + 1}/{totalPages}");
            Bare.Primitive.UI.UiConsole.WriteLine();

            if (allSteps.Count == 0)
            {
                Bare.Primitive.UI.UiConsole.WriteLine("(no artifacts available)");
            }
            else
            {
                for (var i = 0; i < pageSteps.Count; i++)
                {
                    var step = pageSteps[i];
                    Bare.Primitive.UI.UiConsole.WriteLine($"{step.StepIndex}) {step.OperationType} - {step.Artifacts.Count}");
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
            Bare.Primitive.UI.UiConsole.WriteLine("1. View step artifacts");
            Bare.Primitive.UI.UiConsole.WriteLine("2. Next page");
            Bare.Primitive.UI.UiConsole.WriteLine("3. Previous page");
            Bare.Primitive.UI.UiConsole.WriteLine("0. Back");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.Write("Select an option: ");

            var selection = UiInteraction.ReadMenuDigit(0, 3);
            switch (selection)
            {
                case 0:
                    running = false;
                    break;
                case 1:
                    if (allSteps.Count > 0)
                    {
                        var selectedStep = PromptStepNumber(allSteps.Count);
                        if (selectedStep > 0 && selectedStep <= allSteps.Count)
                        {
                            ShowStepArtifacts(allSteps[selectedStep - 1]);
                        }
                    }
                    break;
                case 2:
                    if (totalPages > 1)
                    {
                        pageIndex = (pageIndex + 1) % totalPages;
                    }
                    break;
                case 3:
                    if (totalPages > 1)
                    {
                        pageIndex = pageIndex == 0 ? totalPages - 1 : pageIndex - 1;
                    }
                    break;
            }
        }
    }

    private static int PromptStepNumber(int totalSteps)
    {
        UiInteraction.Clear();
        Bare.Primitive.UI.UiConsole.WriteLine("** BareSync **");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.WriteLine($"Enter step number (1..{totalSteps}, 0 to cancel):");

        while (true)
        {
            Bare.Primitive.UI.UiConsole.Write("> ");
            var input = UiInteraction.ReadLineWithEscape();
            if (input is null)
            {
                return 0;
            }

            if (string.IsNullOrWhiteSpace(input))
            {
                continue;
            }

            if (int.TryParse(input.Trim(), out var value))
            {
                if (value == 0)
                {
                    return 0;
                }

                if (value >= 1 && value <= totalSteps)
                {
                    return value;
                }
            }
        }
    }

    private static void ShowStepArtifacts(BatchStepResult step)
    {
        var running = true;
        while (running)
        {
            UiInteraction.Clear();
            Bare.Primitive.UI.UiConsole.WriteLine($"** Artifacts / Step {step.StepIndex} **");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine(step.OperationType);
            Bare.Primitive.UI.UiConsole.WriteLine();

            if (step.Artifacts.Count == 0)
            {
                Bare.Primitive.UI.UiConsole.WriteLine("(no artifacts)");
            }
            else
            {
                for (var index = 0; index < step.Artifacts.Count; index++)
                {
                    Bare.Primitive.UI.UiConsole.WriteLine($"{index + 1}) {step.Artifacts[index]}");
                }
            }

            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine("** Menu **");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine("0. Back");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.Write("Select an option: ");
            _ = UiInteraction.ReadMenuDigit(0, 0);
            running = false;
        }
    }


    private static string GetShortId(string id) =>
        string.IsNullOrWhiteSpace(id) ? "?" : (id.Length <= 8 ? id : id.Substring(0, 8));
}
