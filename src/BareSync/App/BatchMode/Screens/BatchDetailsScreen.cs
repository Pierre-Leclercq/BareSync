using BareSync.Domain;
using BareSync.App.Common;
using BareSync.Infra;
using BareSync.UI;
using System.Text;
using System.Text.Json.Nodes;

namespace BareSync.App.BatchMode.Screens;

/// <summary>
/// Action requested from the batch details screen.
/// </summary>
internal enum BatchDetailsAction
{
    Back,
    Preflight,
    EditSteps,
    EditContext
}

/// <summary>
/// Result from the batch details screen including the action to take.
/// </summary>
internal sealed record BatchDetailsResult(
    BatchStorageDescriptor Descriptor,
    BatchDetailsAction Action);

/// <summary>
/// S2.3 Batch Details screen - Displays batch information and provides editing options.
/// </summary>
internal sealed class BatchDetailsScreen
{
    /// <summary>
    /// Shows the batch details screen and handles identity editing (name, description, tags).
    /// Returns the result with action indicating next step.
    /// </summary>
    public BatchDetailsResult Show(BatchScreenContext context, MenuStatus? lastStatus = null)
    {
        var current = context.Descriptor;
        var running = true;
        
        while (running)
        {
            var batch = LoadBatchV0(current.Path);
            var steps = batch?.Steps ?? new List<BatchStepV0>();
            var displayName = batch?.Name ?? current.Name;

            UiInteraction.Clear();
            Bare.Primitive.UI.UiConsole.WriteLine("** Batch / Details **");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine($"Name: {displayName}");
            Bare.Primitive.UI.UiConsole.WriteLine($"Description: {FormatOptionalValue(batch?.Description)}");
            Bare.Primitive.UI.UiConsole.WriteLine($"Tags: {FormatTags(batch?.Tags)}");
            Bare.Primitive.UI.UiConsole.WriteLine($"Id: {current.Id}");
            Bare.Primitive.UI.UiConsole.WriteLine($"Steps: {steps.Count}");
            Bare.Primitive.UI.UiConsole.WriteLine($"Status: {current.Status}");
            
            if (steps.Count > 0)
            {
                Bare.Primitive.UI.UiConsole.WriteLine();
                for (var index = 0; index < steps.Count; index++)
                {
                    Bare.Primitive.UI.UiConsole.WriteLine($"Step {index + 1}: {steps[index].OperationType}");
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
            Bare.Primitive.UI.UiConsole.WriteLine("1. Rename batch");
            Bare.Primitive.UI.UiConsole.WriteLine("2. Edit description");
            Bare.Primitive.UI.UiConsole.WriteLine("3. Edit tags");
            Bare.Primitive.UI.UiConsole.WriteLine("4. Edit steps");
            Bare.Primitive.UI.UiConsole.WriteLine("5. Edit context");
            Bare.Primitive.UI.UiConsole.WriteLine("6. Validity details");
            Bare.Primitive.UI.UiConsole.WriteLine("7. Run");
            Bare.Primitive.UI.UiConsole.WriteLine("8. Manage encryption secrets");
            Bare.Primitive.UI.UiConsole.WriteLine("0. Back");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.Write("Select an option: ");

            var selection = UiInteraction.ReadMenuDigit(0, 8);
            switch (selection)
            {
                case 0:
                    running = false;
                    break;
                case 1:
                    current = RunRenameBatch(current, context.Loader, context.AppDataRoot, batch);
                    break;
                case 2:
                    current = RunEditBatchDescription(current, context.Loader, context.AppDataRoot, batch);
                    break;
                case 3:
                    current = RunEditBatchTags(current, context.Loader, context.AppDataRoot, batch);
                    break;
                case 4:
                    // Handled by caller - return to signal
                    return new BatchDetailsResult(current, BatchDetailsAction.EditSteps);
                case 5:
                    // Handled by caller - return to signal
                    return new BatchDetailsResult(current, BatchDetailsAction.EditContext);
                case 6:
                    ShowValidityDetails(current);
                    break;
                case 7:
                    // Handled by caller - return to signal
                    return new BatchDetailsResult(current, BatchDetailsAction.Preflight);
                case 8:
                    current = RunManageEncryptionSecrets(current, context.Loader, context.AppDataRoot, context.Config, batch);
                    break;
            }

            // Reload to get any external changes
            if (selection is >= 1 and <= 3 or 6 or 8)
            {
                current = context.Loader.LoadAll(context.AppDataRoot)
                    .FirstOrDefault(entry => string.Equals(entry.Id, current.Id, StringComparison.OrdinalIgnoreCase))
                    ?? current;
            }
        }

        return new BatchDetailsResult(current, BatchDetailsAction.Back);
    }

    // Identity editors
    private static BatchStorageDescriptor RunRenameBatch(
        BatchStorageDescriptor descriptor,
        BatchStorageLoader loader,
        string appDataRoot,
        BatchV0? batch)
    {
        if (batch is null)
        {
            Bare.Primitive.UI.UiConsole.WriteLine("Invalid batch.");
            UiInteraction.SkipNextClear();
            return descriptor;
        }

        while (true)
        {
            Bare.Primitive.UI.UiConsole.Write("Enter new batch name (empty = cancel, ESC = cancel): ");
            var input = UiInteraction.ReadLineWithEscape();
            if (input is null || string.IsNullOrWhiteSpace(input))
            {
                return descriptor;
            }

            var trimmed = input.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                Bare.Primitive.UI.UiConsole.WriteLine("Invalid batch name.");
                continue;
            }

            if (trimmed.Any(char.IsControl))
            {
                Bare.Primitive.UI.UiConsole.WriteLine("Invalid batch name.");
                continue;
            }

            batch.Name = trimmed;
            batch.UpdatedUtc = UtcNowTimestamp();
            return SaveBatchAndReload(batch, loader, appDataRoot) ?? descriptor;
        }
    }

    private static BatchStorageDescriptor RunEditBatchDescription(
        BatchStorageDescriptor descriptor,
        BatchStorageLoader loader,
        string appDataRoot,
        BatchV0? batch)
    {
        if (batch is null)
        {
            Bare.Primitive.UI.UiConsole.WriteLine("Invalid batch.");
            UiInteraction.SkipNextClear();
            return descriptor;
        }

        Bare.Primitive.UI.UiConsole.Write("Enter description (empty = clear, Q/ESC = cancel): ");
        var input = UiInteraction.ReadLineWithEscape();
        if (input is null || string.Equals(input, "Q", StringComparison.OrdinalIgnoreCase))
        {
            return descriptor;
        }

        if (string.IsNullOrWhiteSpace(input))
        {
            batch.Description = null;
        }
        else
        {
            batch.Description = input.Trim();
        }

        batch.UpdatedUtc = UtcNowTimestamp();
        return SaveBatchAndReload(batch, loader, appDataRoot) ?? descriptor;
    }

    private static BatchStorageDescriptor RunEditBatchTags(
        BatchStorageDescriptor descriptor,
        BatchStorageLoader loader,
        string appDataRoot,
        BatchV0? batch)
    {
        if (batch is null)
        {
            Bare.Primitive.UI.UiConsole.WriteLine("Invalid batch.");
            UiInteraction.SkipNextClear();
            return descriptor;
        }

        Bare.Primitive.UI.UiConsole.Write("Enter tags separated by commas (empty = clear, Q/ESC = cancel): ");
        var input = UiInteraction.ReadLineWithEscape();
        if (input is null || string.Equals(input, "Q", StringComparison.OrdinalIgnoreCase))
        {
            return descriptor;
        }

        if (string.IsNullOrWhiteSpace(input))
        {
            batch.Tags = null;
        }
        else
        {
            var tags = input
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(tag => tag.Trim())
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .ToList();
            batch.Tags = tags.Count == 0 ? null : tags;
        }

        batch.UpdatedUtc = UtcNowTimestamp();
        return SaveBatchAndReload(batch, loader, appDataRoot) ?? descriptor;
    }

    private static BatchStorageDescriptor RunManageEncryptionSecrets(
        BatchStorageDescriptor descriptor,
        BatchStorageLoader loader,
        string appDataRoot,
        AppConfig config,
        BatchV0? batch)
    {
        if (batch is null)
        {
            Bare.Primitive.UI.UiConsole.WriteLine("Invalid batch.");
            UiInteraction.SkipNextClear();
            return descriptor;
        }

        var requiredSlots = BatchSecretResolver.GetRequiredSecretSlots(batch, config);
        if (requiredSlots.Count == 0)
        {
            UiInteraction.Clear();
            Bare.Primitive.UI.UiConsole.WriteLine("** Batch / Manage encryption secrets **");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine("No secret is required by current batch steps.");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine("Press any key to return...");
            if (!Bare.Primitive.UI.UiConsole.IsInputRedirected)
            {
                _ = Bare.Primitive.UI.UiConsole.ReadKey(intercept: true);
            }
            else
            {
                _ = Bare.Primitive.UI.UiConsole.ReadLine();
            }

            return descriptor;
        }

        if (!SecretStoreProvider.IsAvailable)
        {
            UiInteraction.Clear();
            Bare.Primitive.UI.UiConsole.WriteLine("** Batch / Manage encryption secrets **");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine("Vault indisponible: impossible de stocker les secrets.");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine("Press any key to return...");
            if (!Bare.Primitive.UI.UiConsole.IsInputRedirected)
            {
                _ = Bare.Primitive.UI.UiConsole.ReadKey(intercept: true);
            }
            else
            {
                _ = Bare.Primitive.UI.UiConsole.ReadLine();
            }

            return descriptor;
        }

        while (true)
        {
            UiInteraction.Clear();
            Bare.Primitive.UI.UiConsole.WriteLine("** Batch / Manage encryption secrets **");
            Bare.Primitive.UI.UiConsole.WriteLine();
            for (var i = 0; i < requiredSlots.Count; i++)
            {
                var requirement = requiredSlots[i];
                var hasSecret = SecretStoreProvider.TryLoadSecret(requirement.SlotKey, out _);
                Bare.Primitive.UI.UiConsole.WriteLine($"{i + 1}. Scope={requirement.Scope} | Vault={(hasSecret ? "present" : "missing")}");
            }

            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine("0. Back");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.Write("Select scope to set/replace secret: ");
            var selection = UiInteraction.ReadMenuDigit(0, requiredSlots.Count);
            if (selection == 0)
            {
                return descriptor;
            }

            var selected = requiredSlots[selection - 1];
            Bare.Primitive.UI.UiConsole.Write($"Enter password for scope {selected.Scope} (will not be echoed): ");
            var password = ReadPassword();
            if (string.IsNullOrWhiteSpace(password))
            {
                Bare.Primitive.UI.UiConsole.WriteLine("Empty password ignored.");
                UiInteraction.SkipNextClear();
                continue;
            }

            if (!SecretStoreProvider.TrySaveSecret(selected.SlotKey, password))
            {
                Bare.Primitive.UI.UiConsole.WriteLine("Failed to store secret in vault.");
            }
            else
            {
                Bare.Primitive.UI.UiConsole.WriteLine("Secret saved in vault.");
            }

            UiInteraction.SkipNextClear();
        }
    }

    // Helper methods
    private static void ShowValidityDetails(BatchStorageDescriptor descriptor)
    {
        UiInteraction.Clear();
        Bare.Primitive.UI.UiConsole.WriteLine("** Batch / Validity details **");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.WriteLine($"Status: {descriptor.Status}");
        Bare.Primitive.UI.UiConsole.WriteLine($"Reason: {descriptor.Reason}");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.WriteLine("** Menu **");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.WriteLine("0. Back");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.Write("Select an option: ");
        _ = UiInteraction.ReadMenuDigit(0, 0);
    }

    private static BatchV0? LoadBatchV0(string batchPath)
    {
        try
        {
            var json = File.ReadAllText(batchPath);
            var root = System.Text.Json.Nodes.JsonNode.Parse(json) as System.Text.Json.Nodes.JsonObject;
            if (root is null)
            {
                return null;
            }

            var batch = new BatchV0
            {
                SchemaVersion = root["schemaVersion"]?.GetValue<int>() ?? 0,
                Id = root["id"]?.GetValue<string>() ?? string.Empty,
                Name = root["name"]?.GetValue<string>() ?? string.Empty,
                Description = root["description"]?.GetValue<string?>(),
                Tags = (root["tags"] as System.Text.Json.Nodes.JsonArray)?.Select(node => node?.GetValue<string>() ?? string.Empty).ToList(),
                CreatedUtc = root["createdUtc"]?.GetValue<string>() ?? string.Empty,
                UpdatedUtc = root["updatedUtc"]?.GetValue<string>() ?? string.Empty,
                ContextSnapshot = root["contextSnapshot"] as System.Text.Json.Nodes.JsonObject ?? new System.Text.Json.Nodes.JsonObject(),
                Extensions = root["extensions"] as System.Text.Json.Nodes.JsonObject
            };

            if (root["steps"] is System.Text.Json.Nodes.JsonArray stepsArray)
            {
                foreach (var node in stepsArray)
                {
                    if (node is not System.Text.Json.Nodes.JsonObject stepObject)
                    {
                        continue;
                    }

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
                        ContextOverrides = stepObject["contextOverrides"] as System.Text.Json.Nodes.JsonObject ?? new System.Text.Json.Nodes.JsonObject(),
                        Extensions = stepObject["extensions"] as System.Text.Json.Nodes.JsonObject
                    });
                }
            }

            return batch;
        }
        catch
        {
            return null;
        }
    }

    private static BatchStorageDescriptor? SaveBatchAndReload(
        BatchV0 batch,
        BatchStorageLoader loader,
        string appDataRoot)
    {
        var writer = new BatchStorageWriter();
        if (!writer.SaveAtomic(appDataRoot, batch, out var error))
        {
            Bare.Primitive.UI.UiConsole.WriteLine($"Failed to save batch: {error}");
            UiInteraction.SkipNextClear();
            return null;
        }

        return loader.LoadAll(appDataRoot)
            .FirstOrDefault(entry => string.Equals(entry.Id, batch.Id, StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatOptionalValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<empty>" : value;
    }

    private static string FormatTags(IReadOnlyList<string>? tags)
    {
        if (tags is null || tags.Count == 0)
        {
            return "<empty>";
        }

        return string.Join(", ", tags);
    }

        // ReadLineWithEscape has been centralized in UiInteraction

    private static string ReadPassword()
    {
        if (Bare.Primitive.UI.UiConsole.IsInputRedirected)
        {
            return Bare.Primitive.UI.UiConsole.ReadLine() ?? string.Empty;
        }

        var builder = new StringBuilder();
        while (true)
        {
            var key = Bare.Primitive.UI.UiConsole.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Bare.Primitive.UI.UiConsole.WriteLine();
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (builder.Length > 0)
                {
                    builder.Length--;
                    Bare.Primitive.UI.UiConsole.Write("\b \b");
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                builder.Append(key.KeyChar);
                Bare.Primitive.UI.UiConsole.Write('*');
            }
        }

        return builder.ToString();
    }

    private static string UtcNowTimestamp()
    {
        return DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
    }
}
