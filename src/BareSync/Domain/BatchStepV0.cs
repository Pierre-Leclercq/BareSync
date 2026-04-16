using System.Text.Json.Nodes;

namespace BareSync.Domain;

internal sealed class BatchStepV0
{
    public string? StepId { get; set; }
    public string OperationType { get; set; } = string.Empty;
    public StepOperationParamsV0 OperationParams { get; set; } = new();
    public JsonObject ContextOverrides { get; set; } = new();
    public JsonObject? Extensions { get; set; }
}
