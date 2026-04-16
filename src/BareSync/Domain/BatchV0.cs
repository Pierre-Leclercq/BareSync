using System.Text.Json.Nodes;

namespace BareSync.Domain;

internal sealed class BatchV0
{
    public int SchemaVersion { get; set; }
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<string>? Tags { get; set; }
    public string CreatedUtc { get; set; } = string.Empty;
    public string UpdatedUtc { get; set; } = string.Empty;
    public JsonObject ContextSnapshot { get; set; } = new();
    public JsonObject? Extensions { get; set; }
    public List<BatchStepV0> Steps { get; set; } = new();
}
