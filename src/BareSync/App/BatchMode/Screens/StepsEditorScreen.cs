using BareSync.Domain;
using BareSync.Infra;
using BareSync.UI;
using System.Text.Json.Nodes;

namespace BareSync.App.BatchMode.Screens;

/// <summary>
/// S2.6 - Batch Steps Editor - Manage batch steps with alphanumeric commands and pagination.
/// </summary>
internal sealed class StepsEditorScreen
{
    private const int PageSize = 9;

    // Sub-screens
    private readonly StepTypePickerScreen _typePicker = new();
    private readonly StepEditorScreen _stepEditor = new();

    public BatchStorageDescriptor Show(BatchScreenContext context)
    {
        var current = context.Descriptor;
        var batch = LoadBatchV0(current.Path) ?? new BatchV0 { Steps = new List<BatchStepV0>() };
        var pageIndex = 0;
        var running = true;

        while (running)
        {
            var steps = batch.Steps ?? new List<BatchStepV0>();
            var totalPages = Math.Max(1, (int)Math.Ceiling(steps.Count / (double)PageSize));
            if (pageIndex >= totalPages) pageIndex = totalPages - 1;
            if (pageIndex < 0) pageIndex = 0;

            var pageSteps = steps.Skip(pageIndex * PageSize).Take(PageSize).ToList();

            UiInteraction.Clear();
            Bare.Primitive.UI.UiConsole.WriteLine("** BareSync **");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine("** Batch / Steps **");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine($"Page {pageIndex + 1}/{totalPages}");
            Bare.Primitive.UI.UiConsole.WriteLine();

            if (steps.Count == 0)
            {
                Bare.Primitive.UI.UiConsole.WriteLine("(no steps)");
            }
            else
            {
                for (var i = 0; i < pageSteps.Count; i++)
                {
                    var globalIndex = pageIndex * PageSize + i + 1;
                    var step = pageSteps[i];
                    var overrides = DescribeOverrides(step.ContextOverrides as JsonObject);
                    Bare.Primitive.UI.UiConsole.WriteLine($"{globalIndex}) {step.OperationType}  overrides: {overrides}");
                }
            }

            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine("** Step Actions **");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine("1. Add step");
            Bare.Primitive.UI.UiConsole.WriteLine("2. Edit step");
            Bare.Primitive.UI.UiConsole.WriteLine("3. Append steps from existing batch");
            Bare.Primitive.UI.UiConsole.WriteLine("4. Move/Reorder step");
            Bare.Primitive.UI.UiConsole.WriteLine("5. Remove step");
            if (totalPages > 1)
            {
                Bare.Primitive.UI.UiConsole.WriteLine("8. Next page");
                Bare.Primitive.UI.UiConsole.WriteLine("9. Previous page");
            }
            Bare.Primitive.UI.UiConsole.WriteLine("0. Back");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.Write("Choice: ");

            var input = UiInteraction.ReadLineWithEscape()?.Trim() ?? "0";

            if (input == "0")
            {
                running = false;
                continue;
            }

            if (int.TryParse(input, out var choice))
            {
                if (choice == 1) // Add step
                {
                    var directOpType = _typePicker.Show();
                    if (directOpType != null)
                    {
                        var newStep = CreateNewStep(directOpType);
                        batch.Steps ??= new List<BatchStepV0>();
                        batch.Steps.Add(newStep);
                        
                        var editResult = _stepEditor.Show(newStep, batch, context.AppDataRoot, pathPromptService: context.PathPromptService);
                        
                        if (editResult.Saved && editResult.Step != null)
                        {
                            var stepIndex = batch.Steps.Count - 1;
                            batch.Steps[stepIndex] = editResult.Step;
                            ApplyStepEditorAction(batch.Steps, stepIndex, editResult.Action);
                            
                            var savedAfterAdd = SaveBatch(batch, context);
                            if (savedAfterAdd != null)
                            {
                                current = savedAfterAdd;
                                batch = LoadBatchV0(current.Path) ?? batch;
                            }
                        }
                        else
                        {
                            var savedAfterAdd = SaveBatch(batch, context);
                            if (savedAfterAdd != null)
                            {
                                current = savedAfterAdd;
                                batch = LoadBatchV0(current.Path) ?? batch;
                            }
                        }
                    }
                }
                else if (choice == 2) // Edit step
                {
                    if (steps.Count == 0)
                    {
                        Bare.Primitive.UI.UiConsole.WriteLine("No steps to edit.");
                        UiInteraction.SkipNextClear();
                        continue;
                    }
                    Bare.Primitive.UI.UiConsole.Write("Enter global step number to edit (ESC to cancel): ");
                    var editInput = UiInteraction.ReadLineWithEscape();
                    if (editInput != null && int.TryParse(editInput.Trim(), out var targetStepIndex) && targetStepIndex >= 1 && targetStepIndex <= steps.Count)
                    {
                        var realIndex = targetStepIndex - 1;
                        var stepToEdit = steps[realIndex];
                        var result = _stepEditor.Show(stepToEdit, batch, context.AppDataRoot, pathPromptService: context.PathPromptService);
                        
                        if (result.Saved && result.Step != null)
                        {
                            steps[realIndex] = result.Step;
                            ApplyStepEditorAction(steps, realIndex, result.Action);
                            
                            var saved = SaveBatch(batch, context);
                            if (saved != null)
                            {
                                current = saved;
                                batch = LoadBatchV0(current.Path) ?? batch;
                            }
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(editInput))
                    {
                        Bare.Primitive.UI.UiConsole.WriteLine("Invalid step number.");
                        UiInteraction.SkipNextClear();
                    }
                }
                else if (choice == 3) // Append steps from existing batch
                {
                    var allBatches = context.Loader.LoadAll(context.AppDataRoot)
                        .Where(b => b.Status == BatchStorageStatus.Valid && !string.Equals(b.Id, current.Id, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    
                    if (allBatches.Count == 0)
                    {
                        Bare.Primitive.UI.UiConsole.WriteLine("No other valid batches found.");
                        UiInteraction.SkipNextClear();
                        continue;
                    }

                    Bare.Primitive.UI.UiConsole.WriteLine();
                    Bare.Primitive.UI.UiConsole.WriteLine("** Select a batch **");
                    for (int i = 0; i < allBatches.Count; i++)
                    {
                        Bare.Primitive.UI.UiConsole.WriteLine($"{i + 1}. {allBatches[i].Name} ({allBatches[i].Id})");
                    }
                    Bare.Primitive.UI.UiConsole.WriteLine("0. Cancel");
                    Bare.Primitive.UI.UiConsole.Write("Choice: ");
                    var batchChoice = UiInteraction.ReadMenuDigit(0, allBatches.Count);
                    if (batchChoice > 0)
                    {
                        var selectedDesc = allBatches[batchChoice - 1];
                        var selectedBatch = LoadBatchV0(selectedDesc.Path);
                        if (selectedBatch?.Steps != null && selectedBatch.Steps.Count > 0)
                        {
                            batch.Steps ??= new List<BatchStepV0>();
                            int appendedCount = 0;
                            foreach (var stepToCopy in selectedBatch.Steps)
                            {
                                var clonedParams = stepToCopy.OperationParams.Values != null
                                    ? System.Text.Json.Nodes.JsonNode.Parse(stepToCopy.OperationParams.Values.ToJsonString()) as System.Text.Json.Nodes.JsonObject ?? new System.Text.Json.Nodes.JsonObject()
                                    : new System.Text.Json.Nodes.JsonObject();
                                
                                var clonedOverrides = stepToCopy.ContextOverrides != null
                                    ? System.Text.Json.Nodes.JsonNode.Parse(stepToCopy.ContextOverrides.ToJsonString()) as System.Text.Json.Nodes.JsonObject ?? new System.Text.Json.Nodes.JsonObject()
                                    : new System.Text.Json.Nodes.JsonObject();

                                var clonedExtensions = stepToCopy.OperationParams.Extensions != null
                                    ? System.Text.Json.Nodes.JsonNode.Parse(stepToCopy.OperationParams.Extensions.ToJsonString()) as System.Text.Json.Nodes.JsonObject
                                    : null;

                                var clonedStepExtensions = stepToCopy.Extensions != null
                                    ? System.Text.Json.Nodes.JsonNode.Parse(stepToCopy.Extensions.ToJsonString()) as System.Text.Json.Nodes.JsonObject
                                    : null;

                                batch.Steps.Add(new BatchStepV0
                                {
                                    StepId = Guid.NewGuid().ToString("D"),
                                    OperationType = stepToCopy.OperationType,
                                    OperationParams = new StepOperationParamsV0 { Values = clonedParams, Extensions = clonedExtensions },
                                    ContextOverrides = clonedOverrides,
                                    Extensions = clonedStepExtensions
                                });
                                appendedCount++;
                            }

                            var saved = SaveBatch(batch, context);
                            if (saved != null)
                            {
                                current = saved;
                                batch = LoadBatchV0(current.Path) ?? batch;
                                Bare.Primitive.UI.UiConsole.WriteLine($"Successfully appended {appendedCount} step(s).");
                            }
                        }
                        else
                        {
                            Bare.Primitive.UI.UiConsole.WriteLine("The selected batch has no steps.");
                        }
                        UiInteraction.SkipNextClear();
                    }
                }
                else if (choice == 4) // Move step
                {
                    if (steps.Count < 2)
                    {
                        Bare.Primitive.UI.UiConsole.WriteLine("Not enough steps to reorder.");
                        UiInteraction.SkipNextClear();
                        continue;
                    }
                    Bare.Primitive.UI.UiConsole.Write("Enter global step number to move (ESC to cancel): ");
                    var moveInput = UiInteraction.ReadLineWithEscape();
                    if (moveInput != null && int.TryParse(moveInput.Trim(), out var moveIndex) && moveIndex >= 1 && moveIndex <= steps.Count)
                    {
                        var realIndex = moveIndex - 1;
                        Bare.Primitive.UI.UiConsole.Write("Enter new position (1 to " + steps.Count + "): ");
                        var posInput = UiInteraction.ReadLineWithEscape();
                        if (posInput != null && int.TryParse(posInput.Trim(), out var newPos) && newPos >= 1 && newPos <= steps.Count)
                        {
                            var targetIndex = newPos - 1;
                            if (targetIndex != realIndex)
                            {
                                var stepToMove = steps[realIndex];
                                steps.RemoveAt(realIndex);
                                steps.Insert(targetIndex, stepToMove);
                                
                                var saved = SaveBatch(batch, context);
                                if (saved != null)
                                {
                                    current = saved;
                                    batch = LoadBatchV0(current.Path) ?? batch;
                                }
                            }
                        }
                        else if (!string.IsNullOrWhiteSpace(posInput))
                        {
                            Bare.Primitive.UI.UiConsole.WriteLine("Invalid position.");
                            UiInteraction.SkipNextClear();
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(moveInput))
                    {
                        Bare.Primitive.UI.UiConsole.WriteLine("Invalid step number.");
                        UiInteraction.SkipNextClear();
                    }
                }
                else if (choice == 5) // Remove step
                {
                    if (steps.Count == 0)
                    {
                        Bare.Primitive.UI.UiConsole.WriteLine("No steps to remove.");
                        UiInteraction.SkipNextClear();
                        continue;
                    }
                    Bare.Primitive.UI.UiConsole.Write("Enter global step number to remove (ESC to cancel): ");
                    var removeInput = UiInteraction.ReadLineWithEscape();
                    if (removeInput != null && int.TryParse(removeInput.Trim(), out var removeIndex) && removeIndex >= 1 && removeIndex <= steps.Count)
                    {
                        var realIndex = removeIndex - 1;
                        UiInteraction.Clear();
                        Bare.Primitive.UI.UiConsole.WriteLine("** BareSync **");
                        Bare.Primitive.UI.UiConsole.WriteLine();
                        Bare.Primitive.UI.UiConsole.WriteLine($"Remove step {steps[realIndex].OperationType}?");
                        Bare.Primitive.UI.UiConsole.WriteLine();
                        Bare.Primitive.UI.UiConsole.Write("Proceed? (y/n): ");
                        var confirmRaw = UiInteraction.ReadLineWithEscape();
                        var confirm = confirmRaw?.Trim().ToUpperInvariant() ?? "N";
                        if (confirm == "Y")
                        {
                            steps.RemoveAt(realIndex);
                            var saved = SaveBatch(batch, context);
                            if (saved != null)
                            {
                                current = saved;
                                batch = LoadBatchV0(current.Path) ?? batch;
                            }
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(removeInput))
                    {
                        Bare.Primitive.UI.UiConsole.WriteLine("Invalid step number.");
                        UiInteraction.SkipNextClear();
                    }
                }
                else if (choice == 8 && totalPages > 1) // Next page
                {
                    pageIndex = (pageIndex + 1) % totalPages;
                }
                else if (choice == 9 && totalPages > 1) // Previous page
                {
                    pageIndex = pageIndex == 0 ? totalPages - 1 : pageIndex - 1;
                }
                else
                {
                    Bare.Primitive.UI.UiConsole.WriteLine("Invalid choice.");
                    UiInteraction.SkipNextClear();
                }
            }
            else if (!string.IsNullOrEmpty(input))
            {
                Bare.Primitive.UI.UiConsole.WriteLine("Invalid choice.");
                UiInteraction.SkipNextClear();
            }
        }

        return current;
    }

    private void ApplyStepEditorAction(List<BatchStepV0> steps, int index, StepEditorAction action)
    {
        if (action == StepEditorAction.None) return;

        if (action == StepEditorAction.Remove)
        {
            steps.RemoveAt(index);
        }
        else if (action == StepEditorAction.MoveUp && index > 0)
        {
            var temp = steps[index];
            steps[index] = steps[index - 1];
            steps[index - 1] = temp;
        }
        else if (action == StepEditorAction.MoveDown && index < steps.Count - 1)
        {
            var temp = steps[index];
            steps[index] = steps[index + 1];
            steps[index + 1] = temp;
        }
    }

    private static BatchStepV0 CreateNewStep(string operationType)
    {
        return new BatchStepV0
        {
            StepId = Guid.NewGuid().ToString("D"),
            OperationType = operationType,
            OperationParams = new StepOperationParamsV0 { Values = new JsonObject() },
            ContextOverrides = new JsonObject()
        };
    }

    private static BatchStorageDescriptor? SaveBatch(BatchV0 batch, BatchScreenContext context)
    {
        batch.UpdatedUtc = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        
        var writer = new BatchStorageWriter();
        if (!writer.SaveAtomic(context.AppDataRoot, batch, out var error))
        {
            Bare.Primitive.UI.UiConsole.WriteLine($"Failed to save batch: {error}");
            UiInteraction.SkipNextClear();
            return null;
        }
        
        return context.Loader.LoadAll(context.AppDataRoot)
            .FirstOrDefault(e => string.Equals(e.Id, batch.Id, StringComparison.OrdinalIgnoreCase));
    }

    private static string DescribeOverrides(JsonObject? contextOverrides)
    {
        if (contextOverrides is null || contextOverrides.Count == 0) return "<none>";

        var fields = new List<string>();
        foreach (var field in new[] {
            BatchContextFields.SourceRoot, BatchContextFields.MirrorRoot,
            BatchContextFields.SourceIndexCsvPath, BatchContextFields.DestIndexCsvPath,
            BatchContextFields.EncryptedOutputRoot, BatchContextFields.RestoreRoot
        })
        {
            if (contextOverrides.TryGetPropertyValue(field, out var value)
                && value is not null
                && value.GetValue<string>() is { } text
                && !string.IsNullOrWhiteSpace(text))
            {
                fields.Add(char.ToUpperInvariant(field[0]) + field.Substring(1));
            }
        }

        return fields.Count == 0 ? "<none>" : "{" + string.Join(",", fields) + "}";
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
}
