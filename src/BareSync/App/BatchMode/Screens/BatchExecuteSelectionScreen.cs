using BareSync.Domain;
using BareSync.Infra;
using BareSync.UI;

namespace BareSync.App.BatchMode.Screens;

/// <summary>
/// S2.1b - Batch Execute Selection screen.
/// Allows quick selection of a batch for execution without editing.
/// </summary>
internal sealed class BatchExecuteSelectionScreen
{
    private readonly BatchStorageLoader _loader;
    private readonly string _appDataRoot;

    public BatchExecuteSelectionScreen(BatchStorageLoader loader, string appDataRoot)
    {
        _loader = loader;
        _appDataRoot = appDataRoot;
    }

    /// <summary>
    /// Shows the batch selection list and returns the selected batch descriptor.
    /// Returns null if user cancels (Back/ESC).
    /// </summary>
    public BatchStorageDescriptor? Show()
    {
        var running = true;
        var pageIndex = 0;
        const int pageSize = 9;

        while (running)
        {
            var batches = _loader.LoadAll(_appDataRoot)
                .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(b => b.Id)
                .ToList();

            if (batches.Count == 0)
            {
                UiInteraction.Clear();
                Bare.Primitive.UI.UiConsole.WriteLine("** Batch / Execute **");
                Bare.Primitive.UI.UiConsole.WriteLine();
                Bare.Primitive.UI.UiConsole.WriteLine("No batches available.");
                Bare.Primitive.UI.UiConsole.WriteLine();
                Bare.Primitive.UI.UiConsole.WriteLine("Press any key to go back...");
                Bare.Primitive.UI.UiConsole.ReadKey(true);
                return null;
            }

            var totalPages = (int)Math.Ceiling(batches.Count / (double)pageSize);
            if (pageIndex >= totalPages) pageIndex = totalPages - 1;

            var pageBatches = batches
                .Skip(pageIndex * pageSize)
                .Take(pageSize)
                .ToList();

            UiInteraction.Clear();
            Bare.Primitive.UI.UiConsole.WriteLine("** Batch / Execute **");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine("Select a batch to execute:");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine($"Page {pageIndex + 1}/{totalPages}");
            Bare.Primitive.UI.UiConsole.WriteLine();

            for (int i = 0; i < pageBatches.Count; i++)
            {
                var batch = pageBatches[i];
                var shortId = batch.Id.Length <= 8 ? batch.Id : batch.Id.Substring(0, 8);
                var batchV0 = LoadBatchV0(batch.Path);
                var stepCount = batchV0?.Steps?.Count ?? 0;
                Bare.Primitive.UI.UiConsole.WriteLine($"{i + 1}) {batch.Name} [{shortId}] steps={stepCount}");
            }

            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine("** Menu **");
            Bare.Primitive.UI.UiConsole.WriteLine();

            var maxOption = 0;
            if (pageBatches.Count > 0)
            {
                Bare.Primitive.UI.UiConsole.WriteLine("1. Select batch");
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
                    return null;
                case 1:
                    if (pageBatches.Count > 0)
                    {
                        var selected = PromptSelectBatchNumber(pageBatches.Count, pageIndex, pageSize, batches.Count);
                        if (selected.HasValue)
                        {
                            var globalIndex = (pageIndex * pageSize) + (selected.Value - 1);
                            if (globalIndex < batches.Count)
                            {
                                return batches[globalIndex];
                            }
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

        return null;
    }

    private static int? PromptSelectBatchNumber(int pageCount, int pageIndex, int pageSize, int totalCount)
    {
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.Write($"Select batch number (1..{pageCount}, 0 to cancel): ");

        while (true)
        {
            var input = Bare.Primitive.UI.UiConsole.ReadLine()?.Trim();
            
            if (string.IsNullOrWhiteSpace(input))
            {
                Bare.Primitive.UI.UiConsole.Write("Invalid input. Select batch number (1..{pageCount}, 0 to cancel): ");
                continue;
            }

            if (input == "0")
            {
                return null;
            }

            if (int.TryParse(input, out var number))
            {
                if (number >= 1 && number <= pageCount)
                {
                    return number;
                }
            }

            Bare.Primitive.UI.UiConsole.Write($"Invalid selection. Enter 1..{pageCount} or 0: ");
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