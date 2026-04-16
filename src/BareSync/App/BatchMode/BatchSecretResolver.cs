using System.Text.Json.Nodes;
using BareSync.Domain;

namespace BareSync.App.BatchMode;

internal sealed record BatchSecretSlotRequirement(string SlotKey, string Scope);

internal static class BatchSecretResolver
{
    public static IReadOnlyList<BatchSecretSlotRequirement> GetRequiredSecretSlots(BatchV0 batch, AppConfig config)
    {
        var requirements = new List<BatchSecretSlotRequirement>();
        var seenSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var batchContext = batch.ContextSnapshot ?? new JsonObject();

        foreach (var step in batch.Steps)
        {
            if (!BatchOperationCatalog.RequiresSecret(step.OperationType))
            {
                continue;
            }

            var overrides = step.ContextOverrides ?? new JsonObject();
            var scope = ResolveEffectiveEncryptedOutputRoot(batchContext, overrides, config.EncryptedOutputRoot);
            var slot = BatchSecretSlot.GetSecretSlot(step.OperationType, scope);
            if (string.IsNullOrWhiteSpace(slot) || !seenSlots.Add(slot))
            {
                continue;
            }

            requirements.Add(new BatchSecretSlotRequirement(
                slot,
                string.IsNullOrWhiteSpace(scope) ? "<not set>" : scope));
        }

        return requirements;
    }

    private static string ResolveEffectiveEncryptedOutputRoot(
        JsonObject batchContext,
        JsonObject stepOverrides,
        string configFallback)
    {
        if (stepOverrides.TryGetPropertyValue(BatchContextFields.EncryptedOutputRoot, out var stepValue)
            && stepValue is not null)
        {
            var text = stepValue.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        if (batchContext.TryGetPropertyValue(BatchContextFields.EncryptedOutputRoot, out var batchValue)
            && batchValue is not null)
        {
            var text = batchValue.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return configFallback;
    }
}
