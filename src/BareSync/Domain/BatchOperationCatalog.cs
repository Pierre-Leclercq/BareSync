namespace BareSync.Domain;

internal static class BatchOperationCatalog
{
    public const string OperationTypeRefreshIndexesFull = "RefreshIndexesFull";
    public const string OperationTypeRefreshIndexesSmart = "RefreshIndexesSmart";
    public const string OperationTypeOneWaySyncDryRun = "OneWaySyncDryRun";
    public const string OperationTypeOneWaySyncApply = "OneWaySyncApply";
    public const string OperationTypeCreateEncryptedFolder = "CreateEncryptedFolder";
    public const string OperationTypeRefreshEncryptedFolder = "RefreshEncryptedFolder";
    public const string OperationTypeRestoreEncryptedFiles = "RestoreEncryptedFiles";

    public static bool IsKnownOperationType(string opType)
    {
        return opType is OperationTypeRefreshIndexesFull
            or OperationTypeRefreshIndexesSmart
            or OperationTypeOneWaySyncDryRun
            or OperationTypeOneWaySyncApply
            or OperationTypeCreateEncryptedFolder
            or OperationTypeRefreshEncryptedFolder
            or OperationTypeRestoreEncryptedFiles;
    }

    public static bool RequiresConfirmation(string opType)
    {
        return opType is OperationTypeOneWaySyncApply
            or OperationTypeCreateEncryptedFolder
            or OperationTypeRefreshEncryptedFolder
            or OperationTypeRestoreEncryptedFiles;
    }

    public static bool RequiresSecret(string opType)
    {
        return opType is OperationTypeCreateEncryptedFolder
            or OperationTypeRefreshEncryptedFolder
            or OperationTypeRestoreEncryptedFiles;
    }

    public static IReadOnlyList<string> GetRequiredContextFields(string opType)
    {
        return opType switch
        {
            OperationTypeRefreshIndexesFull => RequiredForIndexRefresh,
            OperationTypeRefreshIndexesSmart => RequiredForIndexRefresh,
            OperationTypeOneWaySyncDryRun => RequiredForSync,
            OperationTypeOneWaySyncApply => RequiredForSync,
            OperationTypeCreateEncryptedFolder => RequiredForCreateEncrypted,
            OperationTypeRefreshEncryptedFolder => RequiredForRefreshEncrypted,
            OperationTypeRestoreEncryptedFiles => RequiredForRestoreEncrypted,
            _ => Array.Empty<string>()
        };
    }

    private static readonly string[] RequiredForIndexRefresh =
    {
        BatchContextFields.SourceRoot,
        BatchContextFields.MirrorRoot,
        BatchContextFields.SourceIndexCsvPath,
        BatchContextFields.DestIndexCsvPath
    };

    private static readonly string[] RequiredForSync =
    {
        BatchContextFields.SourceRoot,
        BatchContextFields.MirrorRoot,
        BatchContextFields.SourceIndexCsvPath,
        BatchContextFields.DestIndexCsvPath
    };

    private static readonly string[] RequiredForCreateEncrypted =
    {
        BatchContextFields.SourceRoot,
        BatchContextFields.SourceIndexCsvPath,
        BatchContextFields.EncryptedOutputRoot
    };

    private static readonly string[] RequiredForRefreshEncrypted =
    {
        BatchContextFields.SourceRoot,
        BatchContextFields.SourceIndexCsvPath,
        BatchContextFields.EncryptedOutputRoot
    };

    private static readonly string[] RequiredForRestoreEncrypted =
    {
        BatchContextFields.EncryptedOutputRoot,
        BatchContextFields.RestoreRoot
    };
}
