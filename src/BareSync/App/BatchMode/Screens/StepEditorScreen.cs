using BareSync.Domain;
using BareSync.Infra;
using BareSync.UI;
using System.Text.Json.Nodes;

namespace BareSync.App.BatchMode.Screens;

internal enum StepEditorAction
{
    None,
    MoveUp,
    MoveDown,
    Remove
}

/// <summary>
/// S2.8 - Step Editor (parameters + overrides)
/// Edit operation parameters, context overrides, and sequence actions for a step.
/// </summary>
internal sealed class StepEditorScreen
{
    private static readonly string[] AllContextFields =
    [
        BatchContextFields.SourceRoot,
        BatchContextFields.MirrorRoot,
        BatchContextFields.SourceIndexCsvPath,
        BatchContextFields.DestIndexCsvPath,
        BatchContextFields.EncryptedOutputRoot,
        BatchContextFields.RestoreRoot
    ];

    /// <summary>
    /// Result of editing a step.
    /// </summary>
    public sealed record StepEditorResult(
        bool Saved,
        BatchStepV0? Step,
        bool HasChanges,
        StepEditorAction Action = StepEditorAction.None);

    /// <summary>
    /// Shows the step editor for the given step.
    /// </summary>
    public StepEditorResult Show(BatchStepV0 step, BatchV0 batch, string appDataRoot, Func<int, int, int>? inputProvider = null, IPathPromptService? pathPromptService = null, Func<char>? keyProvider = null)
    {
        var workingStep = CloneStep(step);
        var hasChanges = false;

        while (true)
        {
            UiInteraction.Clear();
            Bare.Primitive.UI.UiConsole.WriteLine("** BareSync **");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine("** Step / Edit **");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine($"Type: {workingStep.OperationType}");
            Bare.Primitive.UI.UiConsole.WriteLine();
            
            // Display operation params
            Bare.Primitive.UI.UiConsole.WriteLine("Operation params:");
            DisplayOperationParams(workingStep);
            
            Bare.Primitive.UI.UiConsole.WriteLine();
            
            // Display context overrides
            Bare.Primitive.UI.UiConsole.WriteLine("Context overrides (replace defaults for this step only):");
            DisplayOverrides(workingStep.ContextOverrides as JsonObject, batch.ContextSnapshot as JsonObject ?? new JsonObject());
            
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine("** Menu **");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine("1. Edit operation parameters");
            for (int i = 0; i < AllContextFields.Length; i++)
            {
                Bare.Primitive.UI.UiConsole.WriteLine($"{i + 2}. Edit {AllContextFields[i]} override");
            }
            int optClear = AllContextFields.Length + 2;
            int optMoveUp = optClear + 1;
            int optMoveDown = optMoveUp + 1;
            int optRemove = optMoveDown + 1;
            int optCopyFromStep = optRemove + 1;

            Bare.Primitive.UI.UiConsole.WriteLine($"{optClear}. Clear an override or all");
            Bare.Primitive.UI.UiConsole.WriteLine($"{optMoveUp}. Move step up");
            Bare.Primitive.UI.UiConsole.WriteLine($"{optMoveDown}. Move step down");
            Bare.Primitive.UI.UiConsole.WriteLine($"{optRemove}. Remove step");
            Bare.Primitive.UI.UiConsole.WriteLine($"{optCopyFromStep}. Copy context overrides from another step");
            Bare.Primitive.UI.UiConsole.WriteLine("0. Back (auto-save)");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.Write("Select an option: ");

            var selection = ReadSelection(inputProvider, 0, optCopyFromStep);

            if (selection == 0)
            {
                if (hasChanges)
                {
                    if (!ValidateStep(workingStep))
                    {
                        continue;
                    }
                    return new StepEditorResult(true, workingStep, true, StepEditorAction.None);
                }
                return new StepEditorResult(false, null, false, StepEditorAction.None);
            }
            else if (selection == 1)
            {
                EditOperationParameters(workingStep);
                hasChanges = true;
            }
            else if (selection >= 2 && selection < optClear)
            {
                var fieldIndex = selection - 2;
                var currentOverrides = workingStep.ContextOverrides as JsonObject ?? new JsonObject();
                hasChanges |= EditField(currentOverrides, AllContextFields[fieldIndex], batch.ContextSnapshot as JsonObject ?? new JsonObject(), pathPromptService);
                workingStep.ContextOverrides = currentOverrides;
            }
            else if (selection == optClear)
            {
                var currentOverrides = workingStep.ContextOverrides as JsonObject ?? new JsonObject();
                hasChanges |= HandleClearOverridesMenu(currentOverrides, inputProvider, keyProvider);
                workingStep.ContextOverrides = currentOverrides;
            }
            else if (selection == optMoveUp)
            {
                return new StepEditorResult(true, workingStep, true, StepEditorAction.MoveUp);
            }
            else if (selection == optMoveDown)
            {
                return new StepEditorResult(true, workingStep, true, StepEditorAction.MoveDown);
            }
            else if (selection == optRemove)
            {
                UiInteraction.Clear();
                Bare.Primitive.UI.UiConsole.WriteLine("** BareSync **");
                Bare.Primitive.UI.UiConsole.WriteLine();
                Bare.Primitive.UI.UiConsole.WriteLine($"Remove step {workingStep.OperationType} ?");
                Bare.Primitive.UI.UiConsole.WriteLine();
                
                if (ConfirmYesNo("Proceed? (y/n): ", keyProvider))
                {
                    return new StepEditorResult(true, workingStep, true, StepEditorAction.Remove);
                }
            }
            else if (selection == optCopyFromStep)
            {
                hasChanges |= CopyContextOverridesFromAnotherStep(step, workingStep, batch, inputProvider);
            }
        }
    }

    private bool CopyContextOverridesFromAnotherStep(BatchStepV0 currentStepInBatch, BatchStepV0 workingStep, BatchV0 batch, Func<int, int, int>? inputProvider)
    {
        var allSteps = batch.Steps ?? new List<BatchStepV0>();
        var candidates = allSteps
            .Where(s => !ReferenceEquals(s, currentStepInBatch)
                && (string.IsNullOrWhiteSpace(currentStepInBatch.StepId)
                    || !string.Equals(s.StepId, currentStepInBatch.StepId, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        UiInteraction.Clear();
        Bare.Primitive.UI.UiConsole.WriteLine("** BareSync **");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.WriteLine("** Copy context overrides **");
        Bare.Primitive.UI.UiConsole.WriteLine();

        if (candidates.Count == 0)
        {
            Bare.Primitive.UI.UiConsole.WriteLine("No other step available to copy from.");
            UiInteraction.SkipNextClear();
            return false;
        }

        for (var i = 0; i < candidates.Count; i++)
        {
            var source = candidates[i];
            var overrides = source.ContextOverrides as JsonObject;
            Bare.Primitive.UI.UiConsole.WriteLine($"{i + 1}. {source.OperationType}  overrides: {DescribeOverrides(overrides)}");
        }

        Bare.Primitive.UI.UiConsole.WriteLine("0. Cancel");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.Write("Select source step: ");

        var selection = ReadSelection(inputProvider, 0, candidates.Count);
        if (selection == 0)
        {
            return false;
        }

        var sourceStep = candidates[selection - 1];
        var cloned = sourceStep.ContextOverrides?.DeepClone() as JsonObject ?? new JsonObject();
        workingStep.ContextOverrides = cloned;
        Bare.Primitive.UI.UiConsole.WriteLine("Context overrides copied.");
        UiInteraction.SkipNextClear();
        return true;
    }

    private bool HandleClearOverridesMenu(JsonObject overrides, Func<int, int, int>? inputProvider, Func<char>? keyProvider)
    {
        while(true)
        {
            UiInteraction.Clear();
            Bare.Primitive.UI.UiConsole.WriteLine("** BareSync **");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine("** Clear overrides **");
            Bare.Primitive.UI.UiConsole.WriteLine();
            
            var fieldsWithOverrides = AllContextFields
                .Where(f => overrides.TryGetPropertyValue(f, out _))
                .ToList();
            
            if (fieldsWithOverrides.Count == 0)
            {
                Bare.Primitive.UI.UiConsole.WriteLine("No overrides to clear.");
                UiInteraction.SkipNextClear();
                return false;
            }
            
            for (var i = 0; i < fieldsWithOverrides.Count; i++)
            {
                Bare.Primitive.UI.UiConsole.WriteLine($"{i + 1}. Clear {fieldsWithOverrides[i]}");
            }
            int optAll = fieldsWithOverrides.Count + 1;
            Bare.Primitive.UI.UiConsole.WriteLine($"{optAll}. Clear ALL overrides");
            Bare.Primitive.UI.UiConsole.WriteLine("0. Back");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.Write("Select field to clear: ");

            var selection = ReadSelection(inputProvider, 0, optAll);
            if (selection == 0) return false;
            
            if (selection == optAll)
            {
                Bare.Primitive.UI.UiConsole.WriteLine();
                if (ConfirmYesNo("Clear all overrides? (y/n): ", keyProvider))
                {
                    overrides.Clear();
                    Bare.Primitive.UI.UiConsole.WriteLine("All overrides cleared.");
                    UiInteraction.SkipNextClear();
                    return true;
                }
            }
            else if (selection >= 1 && selection <= fieldsWithOverrides.Count)
            {
                var field = fieldsWithOverrides[selection - 1];
                var removed = overrides.Remove(field);
                Bare.Primitive.UI.UiConsole.WriteLine($"Override for {field} cleared.");
                UiInteraction.SkipNextClear();
                return removed;
            }
        }
    }

    private bool EditField(JsonObject overrides, string field, JsonObject batchContextSnapshot, IPathPromptService? pathPromptService)
    {
        UiInteraction.Clear();
        Bare.Primitive.UI.UiConsole.WriteLine("** BareSync **");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.WriteLine($"** Edit {field} **");
        Bare.Primitive.UI.UiConsole.WriteLine();
        
        var currentValue = overrides.TryGetPropertyValue(field, out var val) 
            ? val?.GetValue<string>() 
            : null;
        
        Bare.Primitive.UI.UiConsole.WriteLine($"Current override: {currentValue ?? "<not set>"}");
        Bare.Primitive.UI.UiConsole.WriteLine();
        
        string? input = null;

        if (pathPromptService != null)
        {
            switch (field)
            {
                case BatchContextFields.SourceRoot:
                    input = pathPromptService.PickDirectory("Select Source Root Override", currentValue);
                    break;
                case BatchContextFields.MirrorRoot:
                    input = pathPromptService.PickDirectory("Select Mirror Root Override", currentValue);
                    break;
                case BatchContextFields.SourceIndexCsvPath:
                    var sourceRoot = overrides.TryGetPropertyValue(BatchContextFields.SourceRoot, out var srNode) ? srNode?.GetValue<string>() : null;
                    if (string.IsNullOrWhiteSpace(sourceRoot))
                        sourceRoot = batchContextSnapshot.TryGetPropertyValue(BatchContextFields.SourceRoot, out var bsNode) ? bsNode?.GetValue<string>() : null;
                    sourceRoot ??= string.Empty;
                    input = pathPromptService.PickDefaultSourceIndexCsvPath("Select Source Index CSV Override", sourceRoot, currentValue, ContextEditorScreen.BuildGuidSuffixedDefaultIndexFileName(AppConfig.DefaultSourceIndexCsvFileName));
                    break;
                case BatchContextFields.DestIndexCsvPath:
                    var mirrorRoot = overrides.TryGetPropertyValue(BatchContextFields.MirrorRoot, out var mrNode) ? mrNode?.GetValue<string>() : null;
                    if (string.IsNullOrWhiteSpace(mirrorRoot))
                        mirrorRoot = batchContextSnapshot.TryGetPropertyValue(BatchContextFields.MirrorRoot, out var bmNode) ? bmNode?.GetValue<string>() : null;
                    mirrorRoot ??= string.Empty;
                    input = pathPromptService.PickDefaultDestIndexCsvPath("Select Dest Index CSV Override", mirrorRoot, currentValue, ContextEditorScreen.BuildGuidSuffixedDefaultIndexFileName(AppConfig.DefaultDestIndexCsvFileName));
                    break;
                case BatchContextFields.EncryptedOutputRoot:
                    input = pathPromptService.PickDirectory("Select Encrypted Output Root Override", currentValue);
                    break;
                case BatchContextFields.RestoreRoot:
                    input = pathPromptService.PickDirectory("Select Restore Root Override", currentValue);
                    break;
                default:
                    Bare.Primitive.UI.UiConsole.Write("Enter new value (empty to clear, ESC to cancel): ");
                    input = UiInteraction.ReadLineWithEscape();
                    break;
            }
            
            // If pathPromptService was used and returned null (Escape pressed), cancel.
            if (input == null && field != "Unknown") // Fallback check to avoid canceling if not a path field
            {
                Bare.Primitive.UI.UiConsole.WriteLine("Cancelled.");
                UiInteraction.SkipNextClear();
                return false;
            }
        }
        else
        {
            Bare.Primitive.UI.UiConsole.Write("Enter new value (empty to clear, ESC to cancel): ");
            input = UiInteraction.ReadLineWithEscape();
            if (input == null)
            {
                Bare.Primitive.UI.UiConsole.WriteLine("Cancelled.");
                UiInteraction.SkipNextClear();
                return false;
            }
        }
        
        if (string.IsNullOrWhiteSpace(input))
        {
            var removed = overrides.Remove(field);
            Bare.Primitive.UI.UiConsole.WriteLine("Override cleared. Will inherit from batch context.");
            UiInteraction.SkipNextClear();
            return removed;
        }
        overrides[field] = input.Trim();
        Bare.Primitive.UI.UiConsole.WriteLine($"Override set: {field}='{input.Trim()}'");
        UiInteraction.SkipNextClear();
        return true;
    }

    private void DisplayOperationParams(BatchStepV0 step)
    {
        if (!BatchOperationCatalog.IsKnownOperationType(step.OperationType))
        {
            Bare.Primitive.UI.UiConsole.WriteLine("  (unknown operation type)");
            return;
        }

        var paramsObj = step.OperationParams?.Values;
        if (paramsObj == null || paramsObj.Count == 0)
        {
            Bare.Primitive.UI.UiConsole.WriteLine("  (none)");
            return;
        }

        foreach (var prop in paramsObj)
        {
            Bare.Primitive.UI.UiConsole.WriteLine($"  {prop.Key}={prop.Value?.ToString() ?? "<null>"}");
        }
    }

    private void DisplayOverrides(JsonObject? overrides, JsonObject batchContextSnapshot)
    {
        overrides ??= new JsonObject();
        foreach (var field in AllContextFields)
        {
            var overrideValue = overrides.TryGetPropertyValue(field, out var val) 
                ? val?.GetValue<string>() 
                : null;
            
            var batchValue = batchContextSnapshot.TryGetPropertyValue(field, out var bVal)
                ? bVal?.GetValue<string>()
                : null;
            
            var displayValue = overrideValue ?? batchValue ?? "<not set>";
            var source = overrideValue != null ? "[override]" : batchValue != null ? "[batch]" : "[not set]";
            
            Bare.Primitive.UI.UiConsole.WriteLine($"  {field}='{displayValue}' {source}");
        }
    }

    private void EditOperationParameters(BatchStepV0 step)
    {
        if (!BatchOperationCatalog.IsKnownOperationType(step.OperationType))
        {
            Bare.Primitive.UI.UiConsole.WriteLine("Unknown operation type.");
            UiInteraction.SkipNextClear();
            return;
        }

        // For now, most operations have fixed parameters determined by type
        // This screen would be extended when operations have configurable parameters
        
        UiInteraction.Clear();
        Bare.Primitive.UI.UiConsole.WriteLine("** BareSync **");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.WriteLine("** Step / Operation parameters **");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.WriteLine($"Type: {step.OperationType}");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.WriteLine("Parameters:");
        Bare.Primitive.UI.UiConsole.WriteLine("  (none)");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.WriteLine("Operation parameters are fixed by the operation type.");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.WriteLine("0. Back");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.Write("Select an option: ");
        
        _ = UiInteraction.ReadMenuDigit(0, 0);
    }

    private bool ValidateStep(BatchStepV0 step)
    {
        if (!BatchOperationCatalog.IsKnownOperationType(step.OperationType))
        {
            Bare.Primitive.UI.UiConsole.WriteLine("Invalid operation type.");
            UiInteraction.SkipNextClear();
            return false;
        }
        return true;
    }

    private BatchStepV0 CloneStep(BatchStepV0 original)
    {
        var cloned = new BatchStepV0
        {
            StepId = original.StepId ?? Guid.NewGuid().ToString("D"),
            OperationType = original.OperationType,
            OperationParams = new StepOperationParamsV0
            {
                Values = original.OperationParams?.Values?.DeepClone() as JsonObject ?? new JsonObject(),
                Extensions = original.OperationParams?.Extensions?.DeepClone() as JsonObject
            },
            ContextOverrides = original.ContextOverrides?.DeepClone() as JsonObject ?? new JsonObject(),
            Extensions = original.Extensions?.DeepClone() as JsonObject
        };
        return cloned;
    }

    private int ReadSelection(Func<int, int, int>? inputProvider, int min, int max)
    {
        if (inputProvider != null) return inputProvider(min, max);
        while (true)
        {
            var line = UiInteraction.ReadLineWithEscape();
            if (line is null)
            {
                // ESC => cancel/back
                return min;
            }

            if (int.TryParse(line, out var sel) && sel >= min && sel <= max)
            {
                return sel;
            }
        }
    }

    private static bool ConfirmYesNo(string prompt, Func<char>? keyProvider = null)
    {
        Bare.Primitive.UI.UiConsole.Write(prompt);
        char c;
        if (keyProvider != null)
        {
            c = keyProvider();
            Bare.Primitive.UI.UiConsole.Write(c.ToString());
        }
        else
        {
            c = Bare.Primitive.UI.UiConsole.ReadKey(intercept: true).KeyChar;
        }
        Bare.Primitive.UI.UiConsole.WriteLine(string.Empty);
        return c == 'y' || c == 'Y';
    }

    private static string DescribeOverrides(JsonObject? contextOverrides)
    {
        if (contextOverrides is null || contextOverrides.Count == 0) return "<none>";

        var fields = new List<string>();
        foreach (var field in AllContextFields)
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
}
