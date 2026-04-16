using BareSync.Domain;
using BareSync.Infra;
using BareSync.UI;

namespace BareSync.App.BatchMode.Screens;

/// <summary>
/// S2.12 / S2.12a Batch Preflight - Show execution plan or errors.
/// </summary>
internal sealed class PreflightScreen
{
    private const int PageSize = 9;

    public PreflightResult Show(BatchScreenContext context, MenuStatus? lastStatus = null)
    {
        var pageIndex = 0;
        var running = true;
        var result = RunBatchPreflight(context.Descriptor.Path);

        // Bypass preflight visual summary if everything is ready to run automatically
        if (result.Success && !result.RequiresConfirmation && !result.RequiresSecret)
        {
            return new PreflightResult(context.Descriptor, PreflightAction.Run, result);
        }

        while (running)
        {
            if (!result.Success)
            {
                // S2.12a - Failed preflight
                var action = ShowFailedPreflight(result, context, lastStatus, ref pageIndex, ref running);
                if (action != PreflightAction.Continue)
                    return new PreflightResult(context.Descriptor, action, result);
            }
            else
            {
                // S2.12 - OK preflight
                var action = ShowOkPreflight(result, context, lastStatus, ref pageIndex, ref running);
                if (action != PreflightAction.Continue)
                    return new PreflightResult(context.Descriptor, action, result);
            }
        }

        return new PreflightResult(context.Descriptor, PreflightAction.Back, result);
    }

    private PreflightAction ShowFailedPreflight(
        BatchPreflightResult result,
        BatchScreenContext context,
        MenuStatus? lastStatus,
        ref int pageIndex,
        ref bool running)
    {
        var errors = result.Errors;
        var totalPages = Math.Max(1, (int)Math.Ceiling(errors.Count / (double)PageSize));
        if (pageIndex >= totalPages) pageIndex = totalPages - 1;

        var pageErrors = errors.Skip(pageIndex * PageSize).Take(PageSize).ToList();

        UiInteraction.Clear();
        Bare.Primitive.UI.UiConsole.WriteLine("** Batch / Preflight (FAILED) **");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.WriteLine($"Page {pageIndex + 1}/{totalPages}");
        Bare.Primitive.UI.UiConsole.WriteLine();
        foreach (var line in pageErrors)
        {
            Bare.Primitive.UI.UiConsole.WriteLine(line);
        }
        Bare.Primitive.UI.UiConsole.WriteLine();
        if (lastStatus is not null && !string.IsNullOrWhiteSpace(lastStatus.StatusLine))
        {
            Bare.Primitive.UI.UiConsole.WriteLine($"Last status: {lastStatus.StatusLine}");
            Bare.Primitive.UI.UiConsole.WriteLine();
        }

        Bare.Primitive.UI.UiConsole.WriteLine("** Menu **");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.WriteLine("1. Back to batch details");
        Bare.Primitive.UI.UiConsole.WriteLine("2. Edit batch context");
        Bare.Primitive.UI.UiConsole.WriteLine("3. Edit steps");
        var maxOption = 3;
        if (totalPages > 1)
        {
            Bare.Primitive.UI.UiConsole.WriteLine("4. Next page");
            Bare.Primitive.UI.UiConsole.WriteLine("5. Previous page");
            maxOption = 5;
        }
        Bare.Primitive.UI.UiConsole.WriteLine("0. Back");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.Write("Select an option: ");

        var selection = UiInteraction.ReadMenuDigit(0, maxOption);
        switch (selection)
        {
            case 0:
            case 1:
                running = false;
                return PreflightAction.Back;
            case 2:
                running = false;
                return PreflightAction.EditContext;
            case 3:
                running = false;
                return PreflightAction.EditSteps;
            case 4:
                if (totalPages > 1) pageIndex = (pageIndex + 1) % totalPages;
                break;
            case 5:
                if (totalPages > 1) pageIndex = pageIndex == 0 ? totalPages - 1 : pageIndex - 1;
                break;
        }
        return PreflightAction.Continue;
    }

    private PreflightAction ShowOkPreflight(
        BatchPreflightResult result,
        BatchScreenContext context,
        MenuStatus? lastStatus,
        ref int pageIndex,
        ref bool running)
    {
        var steps = result.Steps;
        var totalPages = Math.Max(1, (int)Math.Ceiling(steps.Count / (double)PageSize));
        if (pageIndex >= totalPages) pageIndex = totalPages - 1;

        var pageSteps = steps.Skip(pageIndex * PageSize).Take(PageSize).ToList();

        UiInteraction.Clear();
        Bare.Primitive.UI.UiConsole.WriteLine("** Batch / Preflight **");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.WriteLine($"Batch: {result.BatchName} [{GetShortId(result.BatchId)}]");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.WriteLine($"requiresConfirmation={ToYesNo(result.RequiresConfirmation)}");
        Bare.Primitive.UI.UiConsole.WriteLine($"requiresSecret={ToYesNo(result.RequiresSecret)}");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.WriteLine($"Page {pageIndex + 1}/{totalPages}");
        Bare.Primitive.UI.UiConsole.WriteLine();
        foreach (var step in pageSteps)
        {
            Bare.Primitive.UI.UiConsole.WriteLine($"Step {step.StepIndex}: {step.OperationType}  params: {step.ParamSummary}  requiresConfirmation={ToYesNo(step.RequiresConfirmation)}  requiresSecret={ToYesNo(step.RequiresSecret)}");
        }
        Bare.Primitive.UI.UiConsole.WriteLine();
        if (lastStatus is not null && !string.IsNullOrWhiteSpace(lastStatus.StatusLine))
        {
            Bare.Primitive.UI.UiConsole.WriteLine($"Last status: {lastStatus.StatusLine}");
            Bare.Primitive.UI.UiConsole.WriteLine();
        }

        Bare.Primitive.UI.UiConsole.WriteLine("** Menu **");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.WriteLine(result.RequiresConfirmation ? "1. Confirm & run" : "1. Run");
        Bare.Primitive.UI.UiConsole.WriteLine("2. Back to batch");
        var maxOption = 2;
        if (totalPages > 1)
        {
            Bare.Primitive.UI.UiConsole.WriteLine("3. Next page");
            Bare.Primitive.UI.UiConsole.WriteLine("4. Previous page");
            maxOption = 4;
        }
        Bare.Primitive.UI.UiConsole.WriteLine("0. Back");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.Write("Select an option: ");

        var selection = UiInteraction.ReadMenuDigit(0, maxOption);
        switch (selection)
        {
            case 0:
            case 2:
                running = false;
                return PreflightAction.Back;
            case 1:
                if (result.RequiresConfirmation && !ConfirmProceed())
                {
                    return PreflightAction.Continue;
                }

                running = false;
                return PreflightAction.Run;
            case 3:
                if (totalPages > 1) pageIndex = (pageIndex + 1) % totalPages;
                break;
            case 4:
                if (totalPages > 1) pageIndex = pageIndex == 0 ? totalPages - 1 : pageIndex - 1;
                break;
        }
        return PreflightAction.Continue;
    }

    private static BatchPreflightResult RunBatchPreflight(string batchPath)
    {
        try
        {
            var batch = LoadBatchV0(batchPath);
            if (batch is null)
            {
                return new BatchPreflightResult(
                    false, false, false,
                    new[] { "Invalid batch." },
                    Array.Empty<BatchPreflightStepSummary>(),
                    "<unknown>", string.Empty);
            }

            var readiness = BatchExecutionReadinessEvaluator.EvaluateBatchExecutionReadiness(batch);
            if (readiness.SchemaValidity == BatchSchemaValidity.Invalid)
            {
                return new BatchPreflightResult(
                    false, false, false,
                    new[] { "Invalid batch." },
                    Array.Empty<BatchPreflightStepSummary>(),
                    batch.Name, batch.Id);
            }

            if (readiness.SchemaValidity == BatchSchemaValidity.Incompatible)
            {
                return new BatchPreflightResult(
                    false, false, false,
                    new[] { "Incompatible batch." },
                    Array.Empty<BatchPreflightStepSummary>(),
                    batch.Name, batch.Id);
            }

            if (readiness.ExecutionReadiness == BatchExecutionReadinessStatus.NonExecutable)
            {
                return new BatchPreflightResult(
                    false,
                    readiness.RequiresConfirmation,
                    readiness.RequiresSecret,
                    readiness.Errors,
                    readiness.Steps,
                    batch.Name, batch.Id);
            }

            return new BatchPreflightResult(
                true,
                readiness.RequiresConfirmation,
                readiness.RequiresSecret,
                Array.Empty<string>(),
                readiness.Steps,
                batch.Name, batch.Id);
        }
        catch
        {
            return new BatchPreflightResult(
                false, false, false,
                new[] { "Invalid batch." },
                Array.Empty<BatchPreflightStepSummary>(),
                "<unknown>", string.Empty);
        }
    }

    private static BatchV0? LoadBatchV0(string batchPath)
    {
        try
        {
            var json = File.ReadAllText(batchPath);
            var root = System.Text.Json.Nodes.JsonNode.Parse(json) as System.Text.Json.Nodes.JsonObject;
            if (root is null) return null;

            var batch = new BatchV0
            {
                SchemaVersion = root["schemaVersion"]?.GetValue<int>() ?? 0,
                Id = root["id"]?.GetValue<string>() ?? string.Empty,
                Name = root["name"]?.GetValue<string>() ?? string.Empty,
                Description = root["description"]?.GetValue<string?>(),
                Tags = (root["tags"] as System.Text.Json.Nodes.JsonArray)?.Select(n => n?.GetValue<string>() ?? string.Empty).ToList(),
                CreatedUtc = root["createdUtc"]?.GetValue<string>() ?? string.Empty,
                UpdatedUtc = root["updatedUtc"]?.GetValue<string>() ?? string.Empty,
                ContextSnapshot = root["contextSnapshot"] as System.Text.Json.Nodes.JsonObject ?? new System.Text.Json.Nodes.JsonObject()
            };

            if (root["steps"] is System.Text.Json.Nodes.JsonArray stepsArray)
            {
                foreach (var node in stepsArray)
                {
                    if (node is not System.Text.Json.Nodes.JsonObject stepObject) continue;
                    var opParams = stepObject["operationParams"] as System.Text.Json.Nodes.JsonObject;
                    var values = opParams?["values"] as System.Text.Json.Nodes.JsonObject ?? new System.Text.Json.Nodes.JsonObject();
                    batch.Steps.Add(new BatchStepV0
                    {
                        StepId = stepObject["stepId"]?.GetValue<string>(),
                        OperationType = stepObject["operationType"]?.GetValue<string>() ?? string.Empty,
                        OperationParams = new StepOperationParamsV0 { Values = values, Extensions = opParams?["extensions"] as System.Text.Json.Nodes.JsonObject },
                        ContextOverrides = stepObject["contextOverrides"] as System.Text.Json.Nodes.JsonObject ?? new System.Text.Json.Nodes.JsonObject()
                    });
                }
            }
            return batch;
        }
        catch { return null; }
    }

    private static string GetShortId(string id) =>
        string.IsNullOrWhiteSpace(id) ? "?" : (id.Length <= 8 ? id : id.Substring(0, 8));

    private static string ToYesNo(bool value) => value ? "yes" : "no";

    private static bool ConfirmProceed()
    {
        while (true)
        {
            Bare.Primitive.UI.UiConsole.Write("Proceed? (y/n): ");
            if (Bare.Primitive.UI.UiConsole.IsInputRedirected)
            {
                var input = Bare.Primitive.UI.UiConsole.ReadLine();
                if (string.IsNullOrWhiteSpace(input))
                {
                    continue;
                }

                var key = input.Trim()[0];
                if (key == 'y' || key == 'Y')
                {
                    return true;
                }

                if (key == 'n' || key == 'N')
                {
                    return false;
                }

                continue;
            }

            var keyInfo = Bare.Primitive.UI.UiConsole.ReadKey(intercept: true);
            Bare.Primitive.UI.UiConsole.WriteLine();
            if (keyInfo.KeyChar == 'y' || keyInfo.KeyChar == 'Y')
            {
                return true;
            }

            if (keyInfo.KeyChar == 'n' || keyInfo.KeyChar == 'N')
            {
                return false;
            }
        }
    }
}

/// <summary>
/// Result from PreflightScreen indicating the action to take.
/// </summary>
internal enum PreflightAction
{
    Continue,    // Keep showing preflight
    Back,        // Return to batch details
    EditContext, // Navigate to context editor
    EditSteps,   // Navigate to steps editor
    Run          // Proceed with execution
}

/// <summary>
/// Result containing the descriptor, action, and preflight data.
/// </summary>
internal sealed record PreflightResult(
    BatchStorageDescriptor Descriptor,
    PreflightAction Action,
    BatchPreflightResult PreflightData);
