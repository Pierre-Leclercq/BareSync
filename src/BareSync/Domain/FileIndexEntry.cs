namespace BareSync.Domain;

internal sealed class FileIndexEntry
{
    public long Id { get; set; }

    public IndexEntryKind EntryKind { get; set; } = IndexEntryKind.File;

    public string RelativeDir { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string Crc64Hex { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public DateTimeOffset LastWriteTimeUtc { get; set; }
}
