using BareSync.Domain;
using BareSync.Infra;
using BareSync.UI;

namespace BareSync.App.BatchMode.Screens;

/// <summary>
/// S2.1c - Batch Execute Summary screen.
/// Shows batch summary with Run/Back options.
/// </summary>
internal sealed class BatchExecuteSummaryScreen
{
    private readonly string _appDataRoot;
    private readonly AppConfig _config;

    public BatchExecuteSummaryScreen(string appDataRoot, AppConfig config)
    {
        _appDataRoot = appDataRoot;
        _config = config;
    }

    /// <summary>
    /// Shows the batch summary and returns true if user chose to run.
    /// </summary>
    public bool Show(BatchStorageDescriptor descriptor)
    {
        var batch = LoadBatchV0(descriptor.Path);
        if (batch is null)
        {
            UiInteraction.Clear();
            Bare.Primitive.UI.UiConsole.WriteLine("** Batch / Execute **");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine("Error: Could not load batch.");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine("Press any key to go back...");
            Bare.Primitive.UI.UiConsole.ReadKey(true);
            return false;
        }

        var running = true;
        var pageIndex = 0;
        const int pageSize = 9;

        while (running)
        {
            var steps = batch.Steps;
            var totalPages = Math.Max(1, (int)Math.Ceiling(steps.Count / (double)pageSize));
            if (pageIndex >= totalPages) pageIndex = totalPages - 1;

            var pageSteps = steps
                .Skip(pageIndex * pageSize)
                .Take(pageSize)
                .ToList();

            UiInteraction.Clear();
            Bare.Primitive.UI.UiConsole.WriteLine("** Batch / Execute **");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine($"Batch: {batch.Name}");
            Bare.Primitive.UI.UiConsole.WriteLine($"Id:    {GetShortId(batch.Id)}");
            Bare.Primitive.UI.UiConsole.WriteLine($"Steps: {steps.Count}");
            Bare.Primitive.UI.UiConsole.WriteLine();

            if (steps.Count == 0)
            {
                Bare.Primitive.UI.UiConsole.WriteLine("(no steps defined)");
            }
            else
            {
                Bare.Primitive.UI.UiConsole.WriteLine($"Page {pageIndex + 1}/{totalPages}");
                Bare.Primitive.UI.UiConsole.WriteLine();

                for (int i = 0; i < pageSteps.Count; i++)
                {
                    var step = pageSteps[i];
                    var globalIndex = (pageIndex * pageSize) + i + 1;
                    Bare.Primitive.UI.UiConsole.WriteLine($"{globalIndex,2}) {step.OperationType}");
                    Bare.Primitive.UI.UiConsole.WriteLine();
                }
            }

            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine("** Menu **");
            Bare.Primitive.UI.UiConsole.WriteLine();

            var maxOption = 0;
            if (steps.Count > 0)
            {
                Bare.Primitive.UI.UiConsole.WriteLine("1. Run");
                maxOption = 1;
            }
            if (totalPages > 1)
            {
                Bare.Primitive.UI.UiConsole.WriteLine("2. Next page");
                Bare.Primitive.UI.UiConsole.WriteLine("3. Previous page");
                maxOption = 3;
            }
            Bare.Primitive.UI.UiConsole.WriteLine("0. Back");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.Write("Select an option: ");

            var selection = UiInteraction.ReadMenuDigit(0, maxOption);

            switch (selection)
            {
                case 0:
                    return false;
                case 1:
                    if (steps.Count > 0)
                    {
                        return true; // User chose to run
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

        return false;
    }

    private static string GetShortId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "?";
        return id.Length <= 8 ? id : id.Substring(0, 8);
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
                CreatedUtc = root["createdUtc"]?.GetValue<string>() ?? string.Empty,
                UpdatedUtc = root["updatedUtc"]?.GetValue<string>() ?? string.Empty,
                ContextSnapshot = root["contextSnapshot"] as System.Text.Json.Nodes.JsonObject ?? new System.Text.Json.Nodes.JsonObject(),
                Steps = new List<BatchStepV0>()
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
                        OperationParams = new StepOperationParamsV0
                        {
                            Values = values,
                            Extensions = opParams?["extensions"] as System.Text.Json.Nodes.JsonObject
                        },
                        ContextOverrides = stepObject["contextOverrides"] as System.Text.Json.Nodes.JsonObject ?? new System.Text.Json.Nodes.JsonObject()
                    });
                }
            }
            return batch;
        }
        catch { return null; }
    }
}
