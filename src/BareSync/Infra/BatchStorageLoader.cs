using System.Text.Json;
using System.Text.Json.Nodes;
using BareSync.Domain;

namespace BareSync.Infra;

internal sealed class BatchStorageLoader
{
    private const string MissingRequiredFieldReason = "Invalid: missing required field";
    private const string InvalidFieldTypeReason = "Invalid: invalid field type";

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true
    };

    public IReadOnlyList<BatchStorageDescriptor> LoadAll(string appDataRoot)
    {
        var root = ResolveBatchStoreRoot(appDataRoot);
        Directory.CreateDirectory(root);

        var entries = new List<BatchStorageDescriptor>();
        foreach (var path in Directory.EnumerateFiles(root, "*.json", SearchOption.TopDirectoryOnly))
        {
            if (IsIgnoredAuxiliaryFile(path))
            {
                continue;
            }

            entries.Add(LoadDescriptor(path));
        }

        return entries
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => Path.GetFileName(entry.Path), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string ResolveBatchStoreRoot(string appDataRoot)
    {
        return Path.Combine(appDataRoot, "batches");
    }

    private static bool IsIgnoredAuxiliaryFile(string path)
    {
        var fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return true;
        }

        return fileName.Contains(".tmp.", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase);
    }

    private static BatchStorageDescriptor LoadDescriptor(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return BuildInvalid(path, "Invalid: unreadable or malformed JSON");
            }

            using var document = JsonDocument.Parse(json, DocumentOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return BuildInvalid(path, "Invalid: missing required field");
            }

            var root = document.RootElement;

            if (!TryGetInt(root, "schemaVersion", out var schemaVersion))
            {
                return BuildInvalid(path, "Invalid: missing required field");
            }

            if (!root.TryGetProperty("id", out var idElement)
                || !root.TryGetProperty("name", out var nameElement)
                || !root.TryGetProperty("createdUtc", out var createdElement)
                || !root.TryGetProperty("updatedUtc", out var updatedElement)
                || !root.TryGetProperty("contextSnapshot", out var contextSnapshot)
                || !root.TryGetProperty("steps", out var steps))
            {
                return BuildInvalid(path, MissingRequiredFieldReason);
            }

            if (idElement.ValueKind != JsonValueKind.String
                || nameElement.ValueKind != JsonValueKind.String
                || createdElement.ValueKind != JsonValueKind.String
                || updatedElement.ValueKind != JsonValueKind.String
                || contextSnapshot.ValueKind != JsonValueKind.Object
                || steps.ValueKind != JsonValueKind.Array)
            {
                return BuildInvalid(path, InvalidFieldTypeReason);
            }

            var id = idElement.GetString() ?? string.Empty;
            var name = nameElement.GetString() ?? string.Empty;
            var createdUtc = createdElement.GetString() ?? string.Empty;
            var updatedUtc = updatedElement.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
            {
                return BuildInvalid(path, InvalidFieldTypeReason);
            }

            var batch = LoadBatch(document.RootElement);
            var readiness = BatchExecutionReadinessEvaluator.EvaluateBatchExecutionReadiness(batch);
            return readiness.SchemaValidity switch
            {
                BatchSchemaValidity.Invalid => BuildInvalid(path, readiness.Errors.FirstOrDefault() ?? MissingRequiredFieldReason),
                BatchSchemaValidity.Incompatible => BuildIncompatible(path, schemaVersion),
                _ => readiness.ExecutionReadiness == BatchExecutionReadinessStatus.NonExecutable
                    ? new BatchStorageDescriptor(id, name, BatchStorageStatus.NonExecutable, readiness.Errors.FirstOrDefault() ?? "NonExecutable: preflight failed", path)
                    : new BatchStorageDescriptor(id, name, BatchStorageStatus.Valid, "Valid", path)
            };
        }
        catch (JsonException)
        {
            return BuildInvalid(path, "Invalid: unreadable or malformed JSON");
        }
        catch (IOException)
        {
            return BuildInvalid(path, "Invalid: unreadable or malformed JSON");
        }
    }

    private static BatchStorageDescriptor BuildInvalid(string path, string reason)
    {
        return new BatchStorageDescriptor("<invalid>", Path.GetFileName(path), BatchStorageStatus.Invalid, reason, path);
    }

    private static BatchStorageDescriptor BuildIncompatible(string path, int schemaVersion)
    {
        return new BatchStorageDescriptor(
            "<incompatible>",
            Path.GetFileName(path),
            BatchStorageStatus.Incompatible,
            $"Incompatible: unsupported schemaVersion={schemaVersion}",
            path);
    }

    private static bool TryGetInt(JsonElement element, string property, out int value)
    {
        value = 0;
        if (!element.TryGetProperty(property, out var prop))
        {
            return false;
        }

        if (prop.ValueKind != JsonValueKind.Number || !prop.TryGetInt32(out value))
        {
            return false;
        }

        return true;
    }

    private static BatchV0 LoadBatch(JsonElement root)
    {
        var batch = new BatchV0
        {
            SchemaVersion = root.TryGetProperty("schemaVersion", out var schema) && schema.ValueKind == JsonValueKind.Number
                ? schema.GetInt32()
                : -1,
            Id = root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                ? id.GetString() ?? string.Empty
                : string.Empty,
            Name = root.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
                ? name.GetString() ?? string.Empty
                : string.Empty,
            Description = root.TryGetProperty("description", out var desc) && desc.ValueKind == JsonValueKind.String
                ? desc.GetString()
                : null,
            Tags = root.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array
                ? tags.EnumerateArray().Select(tag => tag.GetString() ?? string.Empty).ToList()
                : null,
            CreatedUtc = root.TryGetProperty("createdUtc", out var created) && created.ValueKind == JsonValueKind.String
                ? created.GetString() ?? string.Empty
                : string.Empty,
            UpdatedUtc = root.TryGetProperty("updatedUtc", out var updated) && updated.ValueKind == JsonValueKind.String
                ? updated.GetString() ?? string.Empty
                : string.Empty,
            ContextSnapshot = root.TryGetProperty("contextSnapshot", out var context) && context.ValueKind == JsonValueKind.Object
                ? JsonNode.Parse(context.GetRawText()) as JsonObject ?? new JsonObject()
                : new JsonObject(),
            Extensions = root.TryGetProperty("extensions", out var extensions) && extensions.ValueKind == JsonValueKind.Object
                ? JsonNode.Parse(extensions.GetRawText()) as JsonObject
                : null,
            Steps = new List<BatchStepV0>()
        };

        if (root.TryGetProperty("steps", out var steps) && steps.ValueKind == JsonValueKind.Array)
        {
            foreach (var step in steps.EnumerateArray())
            {
                if (step.ValueKind != JsonValueKind.Object)
                {
                    batch.Steps.Add(new BatchStepV0
                    {
                        OperationType = string.Empty,
                        OperationParams = new StepOperationParamsV0 { Values = new JsonObject() },
                        ContextOverrides = new JsonObject()
                    });
                    continue;
                }

                var operationParams = step.TryGetProperty("operationParams", out var opParams)
                    && opParams.ValueKind == JsonValueKind.Object
                    ? opParams
                    : default;
                var values = operationParams.ValueKind == JsonValueKind.Object
                    && operationParams.TryGetProperty("values", out var valuesElement)
                    && valuesElement.ValueKind == JsonValueKind.Object
                    ? JsonNode.Parse(valuesElement.GetRawText()) as JsonObject ?? new JsonObject()
                    : new JsonObject();
                var contextOverrides = step.TryGetProperty("contextOverrides", out var overrides)
                    && overrides.ValueKind == JsonValueKind.Object
                    ? JsonNode.Parse(overrides.GetRawText()) as JsonObject ?? new JsonObject()
                    : new JsonObject();

                batch.Steps.Add(new BatchStepV0
                {
                    StepId = step.TryGetProperty("stepId", out var stepId) && stepId.ValueKind == JsonValueKind.String
                        ? stepId.GetString()
                        : null,
                    OperationType = step.TryGetProperty("operationType", out var opType) && opType.ValueKind == JsonValueKind.String
                        ? opType.GetString() ?? string.Empty
                        : string.Empty,
                    OperationParams = new StepOperationParamsV0
                    {
                        Values = values,
                        Extensions = operationParams.ValueKind == JsonValueKind.Object
                            && operationParams.TryGetProperty("extensions", out var opExt)
                            && opExt.ValueKind == JsonValueKind.Object
                            ? JsonNode.Parse(opExt.GetRawText()) as JsonObject
                            : null
                    },
                    ContextOverrides = contextOverrides,
                    Extensions = step.TryGetProperty("extensions", out var stepExt) && stepExt.ValueKind == JsonValueKind.Object
                        ? JsonNode.Parse(stepExt.GetRawText()) as JsonObject
                        : null
                });
            }
        }

        return batch;
    }

}
