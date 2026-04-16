namespace BareSync.Domain;

internal sealed class ConfigValidationError
{
    public ConfigValidationError(string field, string message)
    {
        Field = field;
        Message = message;
    }

    public string Field { get; }

    public string Message { get; }
}
