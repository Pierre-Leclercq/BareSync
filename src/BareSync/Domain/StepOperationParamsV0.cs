using System.Text.Json.Nodes;

namespace BareSync.Domain;

internal sealed class StepOperationParamsV0
{
    public JsonObject Values { get; set; } = new();
    public JsonObject? Extensions { get; set; }
}
