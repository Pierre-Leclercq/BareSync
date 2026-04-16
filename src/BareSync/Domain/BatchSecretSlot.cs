namespace BareSync.Domain;

internal static class BatchSecretSlot
{
    private const string EncryptionPasswordRole = "EncryptionPassword";

    public static string GetSecretSlot(string operationType, string encryptedOutputRoot)
    {
        if (!BatchOperationCatalog.RequiresSecret(operationType))
        {
            return string.Empty;
        }

        var scope = string.IsNullOrWhiteSpace(encryptedOutputRoot)
            ? "<not set>"
            : encryptedOutputRoot.Trim();
        return $"{EncryptionPasswordRole}|{scope}";
    }
}
