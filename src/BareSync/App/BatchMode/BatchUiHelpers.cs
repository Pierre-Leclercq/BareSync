using System.Text.Json;
using System.Text.Json.Nodes;
using BareSync.Domain;
using BareSync.Infra;
using BareSync.UI;

namespace BareSync.App.BatchMode;

/// <summary>
/// Helper methods for batch UI operations and formatting.
/// </summary>
internal static class BatchUiHelpers
{
    /// <summary>
    /// Returns a short display version of an ID (first 8 chars or full if shorter).
    /// </summary>
    public static string GetShortId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return "?";
        }

        return id.Length <= 8 ? id : id.Substring(0, 8);
    }

    /// <summary>
    /// Formats an optional string value for display.
    /// </summary>
    public static string FormatOptionalValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<empty>" : value;
    }

    /// <summary>
    /// Formats a list of tags for display.
    /// </summary>
    public static string FormatTags(IReadOnlyList<string>? tags)
    {
        if (tags is null || tags.Count == 0)
        {
            return "<empty>";
        }

        return string.Join(", ", tags);
    }

    /// <summary>
    /// Describes context overrides for display in step listings.
    /// </summary>
    public static string DescribeOverrides(JsonObject? contextOverrides)
    {
        if (contextOverrides is null || contextOverrides.Count == 0)
        {
            return "<none>";
        }

        var fields = new List<string>();
        foreach (var field in new[]
        {
            BatchContextFields.SourceRoot,
            BatchContextFields.MirrorRoot,
            BatchContextFields.SourceIndexCsvPath,
            BatchContextFields.DestIndexCsvPath,
            BatchContextFields.EncryptedOutputRoot,
            BatchContextFields.RestoreRoot
        })
        {
            if (contextOverrides.TryGetPropertyValue(field, out var value)
                && value is not null
                && value.GetValue<string>() is { } text
                && !string.IsNullOrWhiteSpace(text))
            {
                fields.Add(ToDisplayFieldName(field));
            }
        }

        if (fields.Count == 0)
        {
            return "<none>";
        }

        return "{" + string.Join(",", fields) + "}";
    }

    /// <summary>
    /// Converts a field name to display format (PascalCase).
    /// </summary>
    public static string ToDisplayFieldName(string field)
    {
        return string.IsNullOrWhiteSpace(field)
            ? field
            : char.ToUpperInvariant(field[0]) + field.Substring(1);
    }

    /// <summary>
    /// Loads a BatchV0 from a file path.
    /// </summary>
    public static BatchV0? LoadBatchV0(string batchPath)
    {
        try
        {
            var json = File.ReadAllText(batchPath);
            var root = JsonNode.Parse(json) as JsonObject;
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
                Tags = (root["tags"] as JsonArray)?.Select(node => node?.GetValue<string>() ?? string.Empty).ToList(),
                CreatedUtc = root["createdUtc"]?.GetValue<string>() ?? string.Empty,
                UpdatedUtc = root["updatedUtc"]?.GetValue<string>() ?? string.Empty,
                ContextSnapshot = root["contextSnapshot"] as JsonObject ?? new JsonObject(),
                Extensions = root["extensions"] as JsonObject
            };

            if (root["steps"] is JsonArray stepsArray)
            {
                foreach (var node in stepsArray)
                {
                    if (node is not JsonObject stepObject)
                    {
                        continue;
                    }

                    var opParams = stepObject["operationParams"] as JsonObject;
                    var values = opParams?["values"] as JsonObject ?? new JsonObject();
                    batch.Steps.Add(new BatchStepV0
                    {
                        StepId = stepObject["stepId"]?.GetValue<string>(),
                        OperationType = stepObject["operationType"]?.GetValue<string>() ?? string.Empty,
                        OperationParams = new StepOperationParamsV0
                        {
                            Values = values,
                            Extensions = opParams?["extensions"] as JsonObject
                        },
                        ContextOverrides = stepObject["contextOverrides"] as JsonObject ?? new JsonObject(),
                        Extensions = stepObject["extensions"] as JsonObject
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

    /// <summary>
    /// Saves a batch and reloads its descriptor.
    /// </summary>
    public static BatchStorageDescriptor? SaveBatchAndReload(
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
}