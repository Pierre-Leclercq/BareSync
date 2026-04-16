using BareSync.Domain;
using BareSync.Infra;
using BareSync.UI;
using System.Text.Json.Nodes;

namespace BareSync.App.BatchMode.Screens;

/// <summary>
/// S2.5 Batch Context Editor - Edit batch context snapshot fields.
/// </summary>
internal sealed class ContextEditorScreen
{
    public BatchStorageDescriptor Show(BatchScreenContext context)
    {
        var current = context.Descriptor;
        var batch = LoadBatchV0(current.Path) ?? new BatchV0 { ContextSnapshot = new JsonObject() };
        var running = true;
        
        while (running)
        {
            var contextSnapshot = batch.ContextSnapshot ?? new JsonObject();

            UiInteraction.Clear();
            Bare.Primitive.UI.UiConsole.WriteLine("** Batch / Context (defaults) **");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine($"SourceRoot = '{FormatOptionalValue(GetContextValue(contextSnapshot, BatchContextFields.SourceRoot))}'");
            Bare.Primitive.UI.UiConsole.WriteLine($"MirrorRoot = '{FormatOptionalValue(GetContextValue(contextSnapshot, BatchContextFields.MirrorRoot))}'");
            Bare.Primitive.UI.UiConsole.WriteLine($"Mirror = '{FormatOptionalValue(GetContextValue(contextSnapshot, BatchContextFields.Mirror))}'");
            Bare.Primitive.UI.UiConsole.WriteLine($"SourceIndexCsvPath = '{FormatOptionalValue(GetContextValue(contextSnapshot, BatchContextFields.SourceIndexCsvPath))}'");
            Bare.Primitive.UI.UiConsole.WriteLine($"DestIndexCsvPath = '{FormatOptionalValue(GetContextValue(contextSnapshot, BatchContextFields.DestIndexCsvPath))}'");
            Bare.Primitive.UI.UiConsole.WriteLine($"EncryptedOutputRoot = '{FormatOptionalValue(GetContextValue(contextSnapshot, BatchContextFields.EncryptedOutputRoot))}'");
            Bare.Primitive.UI.UiConsole.WriteLine($"RestoreRoot = '{FormatOptionalValue(GetContextValue(contextSnapshot, BatchContextFields.RestoreRoot))}'");

            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine("** Menu **");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine("1. Edit field");
            Bare.Primitive.UI.UiConsole.WriteLine("2. Copy snapshot from interactive settings");
            Bare.Primitive.UI.UiConsole.WriteLine("0. Back");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.Write("Select an option: ");

            var selection = UiInteraction.ReadMenuDigit(0, 2);
            switch (selection)
            {
                case 0:
                    running = false;
                    break;
                case 1:
                    var updated = RunEditBatchContextField(batch, context);
                    if (updated is not null)
                    {
                        current = updated;
                        batch = LoadBatchV0(current.Path) ?? batch;
                    }
                    break;
                case 2:
                    updated = RunCopyBatchContextSnapshot(batch, context);
                    if (updated is not null)
                    {
                        current = updated;
                        batch = LoadBatchV0(current.Path) ?? batch;
                    }
                    break;
            }
        }

        return current;
    }

    private static BatchStorageDescriptor? RunEditBatchContextField(BatchV0 batch, BatchScreenContext context)
    {
        while (true)
        {
            Bare.Primitive.UI.UiConsole.WriteLine("Select field number (1..7, 0/ESC to cancel):");
            Bare.Primitive.UI.UiConsole.WriteLine("1. SourceRoot");
            Bare.Primitive.UI.UiConsole.WriteLine("2. MirrorRoot");
            Bare.Primitive.UI.UiConsole.WriteLine("3. Mirror");
            Bare.Primitive.UI.UiConsole.WriteLine("4. SourceIndexCsvPath");
            Bare.Primitive.UI.UiConsole.WriteLine("5. DestIndexCsvPath");
            Bare.Primitive.UI.UiConsole.WriteLine("6. EncryptedOutputRoot");
            Bare.Primitive.UI.UiConsole.WriteLine("7. RestoreRoot");
            Bare.Primitive.UI.UiConsole.Write("Selection: ");

            var input = UiInteraction.ReadLineWithEscape();
            if (input is null) return null;
            if (!int.TryParse(input.Trim(), out var value)) continue;
            if (value == 0) return null;

            var field = value switch
            {
                1 => BatchContextFields.SourceRoot,
                2 => BatchContextFields.MirrorRoot,
                3 => BatchContextFields.Mirror,
                4 => BatchContextFields.SourceIndexCsvPath,
                5 => BatchContextFields.DestIndexCsvPath,
                6 => BatchContextFields.EncryptedOutputRoot,
                7 => BatchContextFields.RestoreRoot,
                _ => null
            };

            if (field is null) continue;

            var currentValue = GetContextValue(batch.ContextSnapshot ?? new JsonObject(), field);
            string? newValue = null;
            var pathService = context.PathPromptService;

            switch (field)
            {
                case BatchContextFields.SourceRoot:
                    newValue = pathService.PickDirectory("Select Source Root", currentValue);
                    break;
                case BatchContextFields.MirrorRoot:
                    newValue = pathService.PickDirectory("Select Mirror Root", currentValue);
                    break;
                case BatchContextFields.Mirror:
                    newValue = PromptMirrorValue(currentValue);
                    if (newValue is null)
                    {
                        return null;
                    }
                    break;
                case BatchContextFields.SourceIndexCsvPath:
                    var sourceRoot = GetContextValue(batch.ContextSnapshot ?? new JsonObject(), BatchContextFields.SourceRoot) ?? string.Empty;
                    var sourceDefaultFileName = BuildGuidSuffixedDefaultIndexFileName(AppConfig.DefaultSourceIndexCsvFileName);
                    newValue = pathService.PickDefaultSourceIndexCsvPath("Select Source Index CSV", sourceRoot, currentValue, sourceDefaultFileName, preferDefaultFileName: true);
                    break;
                case BatchContextFields.DestIndexCsvPath:
                    var mirrorRoot = GetContextValue(batch.ContextSnapshot ?? new JsonObject(), BatchContextFields.MirrorRoot) ?? string.Empty;
                    var destDefaultFileName = BuildGuidSuffixedDefaultIndexFileName(AppConfig.DefaultDestIndexCsvFileName);
                    newValue = pathService.PickDefaultDestIndexCsvPath("Select Dest Index CSV", mirrorRoot, currentValue, destDefaultFileName, preferDefaultFileName: true);
                    break;
                case BatchContextFields.EncryptedOutputRoot:
                    newValue = pathService.PickDirectory("Select Encrypted Output Root", currentValue);
                    break;
                case BatchContextFields.RestoreRoot:
                    newValue = pathService.PickDirectory("Select Restore Root", currentValue);
                    break;
            }

            batch.ContextSnapshot ??= new JsonObject();
            if (string.IsNullOrWhiteSpace(newValue))
                batch.ContextSnapshot.Remove(field);
            else
                batch.ContextSnapshot[field] = newValue.Trim();

            batch.UpdatedUtc = UtcNowTimestamp();
            return SaveBatchAndReload(batch, context.Loader, context.AppDataRoot);
        }
    }

    private static BatchStorageDescriptor? RunCopyBatchContextSnapshot(BatchV0 batch, BatchScreenContext context)
    {
        UiInteraction.Clear();
        Bare.Primitive.UI.UiConsole.WriteLine("Copy interactive settings into batch context (snapshot).");
        Bare.Primitive.UI.UiConsole.WriteLine("This may overwrite existing batch values.");
        Bare.Primitive.UI.UiConsole.WriteLine();
        
        if (!ConfirmYesNo("Proceed? (Y/N) ")) return null;

        batch.ContextSnapshot = BuildContextSnapshotFromConfig(context.Config, batch.Id, context.AppDataRoot);
        batch.UpdatedUtc = UtcNowTimestamp();
        return SaveBatchAndReload(batch, context.Loader, context.AppDataRoot);
    }

    private static JsonObject BuildContextSnapshotFromConfig(AppConfig config, string batchId, string appDataRoot)
    {
        var snapshot = new JsonObject();
        AddContextValue(snapshot, BatchContextFields.SourceRoot, config.SourceRoot);
        AddContextValue(snapshot, BatchContextFields.MirrorRoot, config.MirrorRoot);
        AddContextValue(snapshot, BatchContextFields.Mirror, config.Mirror.ToString().ToLowerInvariant());
        
        // Create the batches folder if it doesn't exist
        var batchFolder = Path.Combine(appDataRoot, "batches");
        Directory.CreateDirectory(batchFolder);
        
        // Build CSV index paths with batch ID in the batches folder
        var sourceIndexFileName = $"{Path.GetFileNameWithoutExtension(AppConfig.DefaultSourceIndexCsvFileName)}_{batchId}.csv";
        var destIndexFileName = $"{Path.GetFileNameWithoutExtension(AppConfig.DefaultDestIndexCsvFileName)}_{batchId}.csv";
        
        var sourceIndexPath = Path.Combine(batchFolder, sourceIndexFileName);
        var destIndexPath = Path.Combine(batchFolder, destIndexFileName);
        
        AddContextValue(snapshot, BatchContextFields.SourceIndexCsvPath, sourceIndexPath);
        AddContextValue(snapshot, BatchContextFields.DestIndexCsvPath, destIndexPath);
        
        AddContextValue(snapshot, BatchContextFields.EncryptedOutputRoot, config.EncryptedOutputRoot);
        AddContextValue(snapshot, BatchContextFields.RestoreRoot, config.RestoreRoot);
        return snapshot;
    }

    internal static string BuildGuidSuffixedDefaultIndexFileName(string baseFileName)
    {
        var normalizedBaseName = string.IsNullOrWhiteSpace(baseFileName)
            ? "baresync_index.csv"
            : baseFileName.Trim();

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(normalizedBaseName);
        var extension = Path.GetExtension(normalizedBaseName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".csv";
        }

        return $"{nameWithoutExtension}_{Guid.NewGuid():N}{extension}";
    }

    private static void AddContextValue(JsonObject snapshot, string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            snapshot[field] = value.Trim();
    }

    private static string? GetContextValue(JsonObject snapshot, string field)
    {
        if (!snapshot.TryGetPropertyValue(field, out var value) || value is null)
        {
            return null;
        }

        if (value is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<string>(out var text))
            {
                return text;
            }

            if (jsonValue.TryGetValue<bool>(out var boolValue))
            {
                return boolValue ? "true" : "false";
            }

            if (jsonValue.TryGetValue<int>(out var intValue))
            {
                return intValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        return value.ToJsonString();
    }

    private static string FormatOptionalValue(string? value) => string.IsNullOrWhiteSpace(value) ? "<empty>" : value;

    private static bool ConfirmYesNo(string prompt)
    {
        while (true)
        {
            Bare.Primitive.UI.UiConsole.Write(prompt);
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
            if (keyInfo.KeyChar == 'y' || keyInfo.KeyChar == 'Y') return true;
            if (keyInfo.KeyChar == 'n' || keyInfo.KeyChar == 'N') return false;
        }
    }

    private static string? PromptMirrorValue(string? currentValue)
    {
        while (true)
        {
            Bare.Primitive.UI.UiConsole.Write($"Mirror (true/false, 1/0, empty=clear, ESC=cancel) [current: {FormatOptionalValue(currentValue)}]: ");
            var input = UiInteraction.ReadLineWithEscape();
            if (input is null)
            {
                return null;
            }

            var trimmed = input.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return string.Empty;
            }

            if (TryParseBooleanInput(trimmed, out var boolValue))
            {
                return boolValue ? "true" : "false";
            }

            Bare.Primitive.UI.UiConsole.WriteLine("Invalid value. Use true/false or 1/0.");
        }
    }

    private static bool TryParseBooleanInput(string text, out bool value)
    {
        if (bool.TryParse(text, out value))
        {
            return true;
        }

        if (string.Equals(text, "1", StringComparison.Ordinal))
        {
            value = true;
            return true;
        }

        if (string.Equals(text, "0", StringComparison.Ordinal))
        {
            value = false;
            return true;
        }

        value = false;
        return false;
    }

        // ReadLineWithEscape has been centralized in UiInteraction

    private static string UtcNowTimestamp()
    {
        return DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
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
            return batch;
        }
        catch { return null; }
    }

    private static BatchStorageDescriptor? SaveBatchAndReload(BatchV0 batch, BatchStorageLoader loader, string appDataRoot)
    {
        var writer = new BatchStorageWriter();
        if (!writer.SaveAtomic(appDataRoot, batch, out var error))
        {
            Bare.Primitive.UI.UiConsole.WriteLine($"Failed to save batch: {error}");
            UiInteraction.SkipNextClear();
            return null;
        }
        return loader.LoadAll(appDataRoot).FirstOrDefault(e => string.Equals(e.Id, batch.Id, StringComparison.OrdinalIgnoreCase));
    }
}
