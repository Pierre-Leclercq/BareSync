namespace BareSync.Domain;

internal sealed record BatchStorageDescriptor(
    string Id,
    string Name,
    BatchStorageStatus Status,
    string Reason,
    string Path);