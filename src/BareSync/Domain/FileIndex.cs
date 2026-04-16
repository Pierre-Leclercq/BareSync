namespace BareSync.Domain;

internal sealed class FileIndex
{
    public Dictionary<long, FileIndexEntry> Entries { get; } = new();

    public int Count => Entries.Count;

    public void Add(FileIndexEntry entry)
    {
        Entries[entry.Id] = entry;
    }
}
