using System.Globalization;
using System.Text.Json.Nodes;
using BareSync.Infra;

namespace BareSync.Domain;

internal static class BatchExecutionReadinessEvaluator
{
    private static readonly string[] TimestampFormats =
    {
        "yyyy-MM-ddTHH:mm:ss'Z'",
        "yyyy-MM-ddTHH:mm:ss.FFFFFFF'Z'"
    };

    public static BatchExecutionReadiness EvaluateBatchExecutionReadiness(BatchV0? batch)
    {
        return EvaluateBatchExecutionReadiness(batch, ConfigService.ResolveDriveLabelPath);
    }

    internal static BatchExecutionReadiness EvaluateBatchExecutionReadiness(
        BatchV0? batch,
        Func<string, DriveLabelResolutionResult> resolvePath)
    {
        if (batch is null)
        {
            return BuildInvalid("Invalid: missing required field");
        }

        if (batch.SchemaVersion != (int)BatchSchemaVersion.V0)
        {
            return BuildIncompatible(batch.SchemaVersion);
        }

        if (string.IsNullOrWhiteSpace(batch.Id)
            || string.IsNullOrWhiteSpace(batch.Name)
            || string.IsNullOrWhiteSpace(batch.CreatedUtc)
            || string.IsNullOrWhiteSpace(batch.UpdatedUtc)
            || batch.ContextSnapshot is null
            || batch.Steps is null)
        {
            return BuildInvalid("Invalid: missing required field");
        }

        if (!IsValidUtcTimestamp(batch.CreatedUtc) || !IsValidUtcTimestamp(batch.UpdatedUtc))
        {
            return BuildInvalid("Invalid: invalid field type");
        }

        var errors = new List<string>();
        var requiresConfirmation = false;
        var requiresSecret = false;
        var stepSummaries = new List<BatchPreflightStepSummary>();

        if (batch.Steps.Count == 0)
        {
            errors.Add("No steps defined.");
        }

        for (var index = 0; index < batch.Steps.Count; index++)
        {
            var stepIndex = index + 1;
            var step = batch.Steps[index];
            if (step is null)
            {
                errors.Add($"Step {stepIndex}: Invalid step.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(step.OperationType))
            {
                errors.Add($"Step {stepIndex}: Missing field: operationType");
                continue;
            }

            if (!BatchOperationCatalog.IsKnownOperationType(step.OperationType))
            {
                errors.Add($"Step {stepIndex}: Invalid operationType");
                continue;
            }

            var overrides = step.ContextOverrides ?? new JsonObject();
            var effectiveContext = BuildEffectiveContext(batch.ContextSnapshot, overrides);
            var overrideFields = GetOverrideFields(overrides);
            foreach (var requiredField in BatchOperationCatalog.GetRequiredContextFields(step.OperationType))
            {
                var value = effectiveContext.TryGetValue(requiredField, out var resolved)
                    ? resolved
                    : null;
                if (string.IsNullOrWhiteSpace(value))
                {
                    var label = char.ToUpperInvariant(requiredField[0]) + requiredField.Substring(1);
                    errors.Add($"Step {stepIndex}: Missing field: {label}");
                    break;
                }

                if (!IsPathContextField(requiredField))
                {
                    continue;
                }

                var resolution = resolvePath(value!);
                if (resolution.Status == DriveLabelResolutionStatus.Resolved)
                {
                    effectiveContext[requiredField] = resolution.ResolvedPath;
                    continue;
                }

                if (resolution.Status == DriveLabelResolutionStatus.NotFound)
                {
                    var label = char.ToUpperInvariant(requiredField[0]) + requiredField.Substring(1);
                    errors.Add($"Step {stepIndex}: Drive name not found: {label}");
                    break;
                }

                if (resolution.Status == DriveLabelResolutionStatus.Ambiguous)
                {
                    var label = char.ToUpperInvariant(requiredField[0]) + requiredField.Substring(1);
                    var candidates = string.Join(", ", resolution.CandidateRoots);
                    errors.Add($"Step {stepIndex}: Ambiguous drive name: {label} (candidates: {candidates})");
                    break;
                }
            }

            var stepRequiresConfirmation = BatchOperationCatalog.RequiresConfirmation(step.OperationType);
            if (stepRequiresConfirmation)
            {
                requiresConfirmation = true;
            }

            var stepRequiresSecret = BatchOperationCatalog.RequiresSecret(step.OperationType);
            if (stepRequiresSecret)
            {
                requiresSecret = true;
            }

            stepSummaries.Add(new BatchPreflightStepSummary(
                stepIndex,
                step.OperationType,
                BuildParamSummary(step.OperationType, effectiveContext, overrideFields),
                stepRequiresConfirmation,
                stepRequiresSecret));
        }

        var readiness = errors.Count == 0
            ? BatchExecutionReadinessStatus.Ready
            : BatchExecutionReadinessStatus.NonExecutable;

        var orderedErrors = errors
            .Select((message, index) => new { message, index })
            .OrderBy(entry => entry.message.StartsWith("Step ", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(entry => entry.index)
            .Select(entry => entry.message)
            .ToList();

        return new BatchExecutionReadiness(
            BatchSchemaValidity.Valid,
            readiness,
            orderedErrors,
            requiresConfirmation,
            requiresSecret,
            stepSummaries);
    }

    private static bool IsPathContextField(string field)
    {
        return field is BatchContextFields.SourceRoot
            or BatchContextFields.MirrorRoot
            or BatchContextFields.SourceIndexCsvPath
            or BatchContextFields.DestIndexCsvPath
            or BatchContextFields.EncryptedOutputRoot
            or BatchContextFields.RestoreRoot;
    }

    private static BatchExecutionReadiness BuildInvalid(string reason)
    {
        return new BatchExecutionReadiness(
            BatchSchemaValidity.Invalid,
            BatchExecutionReadinessStatus.NonExecutable,
            new List<string> { reason },
            false,
            false,
            Array.Empty<BatchPreflightStepSummary>());
    }

    private static BatchExecutionReadiness BuildIncompatible(int schemaVersion)
    {
        return new BatchExecutionReadiness(
            BatchSchemaValidity.Incompatible,
            BatchExecutionReadinessStatus.NonExecutable,
            new List<string> { $"Incompatible: unsupported schemaVersion={schemaVersion}" },
            false,
            false,
            Array.Empty<BatchPreflightStepSummary>());
    }

    private static bool IsValidUtcTimestamp(string value)
    {
        return DateTimeOffset.TryParseExact(
            value,
            TimestampFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out _);
    }

    private static Dictionary<string, string?> BuildEffectiveContext(
        JsonObject contextSnapshot,
        JsonObject contextOverrides)
    {
        var context = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [BatchContextFields.SourceRoot] = GetEffectiveValue(contextSnapshot, contextOverrides, BatchContextFields.SourceRoot),
            [BatchContextFields.MirrorRoot] = GetEffectiveValue(contextSnapshot, contextOverrides, BatchContextFields.MirrorRoot),
            [BatchContextFields.SourceIndexCsvPath] = GetEffectiveValue(contextSnapshot, contextOverrides, BatchContextFields.SourceIndexCsvPath),
            [BatchContextFields.DestIndexCsvPath] = GetEffectiveValue(contextSnapshot, contextOverrides, BatchContextFields.DestIndexCsvPath),
            [BatchContextFields.EncryptedOutputRoot] = GetEffectiveValue(contextSnapshot, contextOverrides, BatchContextFields.EncryptedOutputRoot),
            [BatchContextFields.RestoreRoot] = GetEffectiveValue(contextSnapshot, contextOverrides, BatchContextFields.RestoreRoot)
        };

        return context;
    }

    private static string? GetEffectiveValue(JsonObject contextSnapshot, JsonObject contextOverrides, string field)
    {
        var overrideValue = GetContextString(contextOverrides, field);
        if (!string.IsNullOrWhiteSpace(overrideValue))
        {
            return overrideValue;
        }

        return GetContextString(contextSnapshot, field);
    }

    private static string? GetContextString(JsonObject element, string field)
    {
        return element.TryGetPropertyValue(field, out var value)
            ? value?.GetValue<string>()
            : null;
    }

    private static IReadOnlyList<string> GetOverrideFields(JsonObject contextOverrides)
    {
        if (contextOverrides.Count == 0)
        {
            return Array.Empty<string>();
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
                && !string.IsNullOrWhiteSpace(value.GetValue<string>()))
            {
                fields.Add(ToDisplayFieldName(field));
            }
        }

        return fields;
    }

    private static string BuildParamSummary(
        string operationType,
        IReadOnlyDictionary<string, string?> effectiveContext,
        IReadOnlyList<string> overrideFields)
    {
        var parts = new List<string>();
        switch (operationType)
        {
            case BatchOperationCatalog.OperationTypeRefreshIndexesFull:
                parts.Add("mode=full");
                AppendContextFields(parts, effectiveContext,
                    BatchContextFields.SourceRoot,
                    BatchContextFields.MirrorRoot,
                    BatchContextFields.SourceIndexCsvPath,
                    BatchContextFields.DestIndexCsvPath);
                break;
            case BatchOperationCatalog.OperationTypeRefreshIndexesSmart:
                parts.Add("mode=incremental");
                AppendContextFields(parts, effectiveContext,
                    BatchContextFields.SourceRoot,
                    BatchContextFields.MirrorRoot,
                    BatchContextFields.SourceIndexCsvPath,
                    BatchContextFields.DestIndexCsvPath);
                break;
            case BatchOperationCatalog.OperationTypeOneWaySyncDryRun:
                parts.Add("dryRun=true");
                AppendContextFields(parts, effectiveContext,
                    BatchContextFields.SourceRoot,
                    BatchContextFields.MirrorRoot,
                    BatchContextFields.SourceIndexCsvPath,
                    BatchContextFields.DestIndexCsvPath);
                parts.Add("note=no file writes");
                break;
            case BatchOperationCatalog.OperationTypeOneWaySyncApply:
                parts.Add("dryRun=false");
                AppendContextFields(parts, effectiveContext,
                    BatchContextFields.SourceRoot,
                    BatchContextFields.MirrorRoot,
                    BatchContextFields.SourceIndexCsvPath,
                    BatchContextFields.DestIndexCsvPath);
                parts.Add("note=may overwrite destination files");
                break;
            case BatchOperationCatalog.OperationTypeCreateEncryptedFolder:
                AppendContextFields(parts, effectiveContext,
                    BatchContextFields.SourceRoot,
                    BatchContextFields.SourceIndexCsvPath,
                    BatchContextFields.EncryptedOutputRoot);
                parts.Add("note=secret required");
                break;
            case BatchOperationCatalog.OperationTypeRefreshEncryptedFolder:
                AppendContextFields(parts, effectiveContext,
                    BatchContextFields.SourceRoot,
                    BatchContextFields.SourceIndexCsvPath,
                    BatchContextFields.EncryptedOutputRoot);
                parts.Add("note=secret required");
                break;
            case BatchOperationCatalog.OperationTypeRestoreEncryptedFiles:
                AppendContextFields(parts, effectiveContext,
                    BatchContextFields.EncryptedOutputRoot,
                    BatchContextFields.RestoreRoot);
                parts.Add("note=secret required");
                break;
        }

        if (overrideFields.Count > 0)
        {
            parts.Add($"overrides={string.Join(',', overrideFields)}");
        }

        return string.Join("; ", parts);
    }

    private static void AppendContextFields(
        ICollection<string> parts,
        IReadOnlyDictionary<string, string?> effectiveContext,
        params string[] fields)
    {
        foreach (var field in fields)
        {
            var value = effectiveContext.TryGetValue(field, out var resolved)
                ? resolved
                : null;
            parts.Add($"{ToDisplayFieldName(field)}={FormatPreflightValue(value)}");
        }
    }

    private static string FormatPreflightValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<not set>" : value;
    }

    private static string ToDisplayFieldName(string field)
    {
        return string.IsNullOrWhiteSpace(field)
            ? field
            : char.ToUpperInvariant(field[0]) + field.Substring(1);
    }
}
