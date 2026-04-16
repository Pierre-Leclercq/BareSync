using System.Globalization;
using System.Text;
using BareSync.Domain;

namespace BareSync.Infra;

internal readonly struct IndexRow
{
    public IndexRow(
        long id,
        string relativePath,
        string crc64Hex,
        long sizeBytes,
        DateTime lastWriteTimeUtc,
        IndexEntryKind entryKind = IndexEntryKind.File)
    {
        Id = id;
        RelativePath = relativePath;
        Crc64Hex = crc64Hex;
        SizeBytes = sizeBytes;
        LastWriteTimeUtc = lastWriteTimeUtc;
        EntryKind = entryKind;
    }

    public long Id { get; }

    public string RelativePath { get; }

    public string Crc64Hex { get; }

    public long SizeBytes { get; }

    public DateTime LastWriteTimeUtc { get; }

    public IndexEntryKind EntryKind { get; }
}

internal static class CsvIndexReader
{
    private static readonly string[] ExpectedHeader =
    {
        "Id",
        "RelativeDir",
        "FileName",
        "Crc64Hex",
        "SizeBytes",
        "LastWriteTimeUtc",
        "EntryKind"
    };

    public static async Task<Dictionary<string, IndexRow>> ReadAsync(
        string csvFullPath,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        await using var stream = new FileStream(
            csvFullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var buffer = new char[1];
        var pending = new PendingChar();
        var recordNumber = 0;
        var header = await ReadRecordAsync(reader, buffer, pending, ++recordNumber, ct).ConfigureAwait(false);
        if (header is null)
        {
            throw new FormatException("CSV is empty.");
        }

        if (header.Count < ExpectedHeader.Length - 1)
        {
            throw new FormatException("CSV header has too few columns.");
        }

        header[0] = header[0].TrimStart('\uFEFF');
        for (var i = 0; i < ExpectedHeader.Length - 1; i++)
        {
            if (!string.Equals(header[i], ExpectedHeader[i], StringComparison.Ordinal))
            {
                throw new FormatException($"CSV header invalid at column {i + 1}.");
            }
        }

        if (header.Count >= ExpectedHeader.Length
            && !string.Equals(header[ExpectedHeader.Length - 1], ExpectedHeader[ExpectedHeader.Length - 1], StringComparison.Ordinal))
        {
            throw new FormatException($"CSV header invalid at column {ExpectedHeader.Length}.");
        }

        var rows = new Dictionary<string, IndexRow>(StringComparer.Ordinal);

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var fields = await ReadRecordAsync(reader, buffer, pending, ++recordNumber, ct)
                .ConfigureAwait(false);
            if (fields is null)
            {
                break;
            }

            if (fields.Count < ExpectedHeader.Length - 1)
            {
                throw new FormatException($"CSV record {recordNumber} has too few columns.");
            }

            if (!long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                throw new FormatException($"CSV record {recordNumber} has invalid Id.");
            }

            var relativeDir = fields[1];
            var fileName = fields[2];
            var relativePath = string.IsNullOrEmpty(relativeDir)
                ? fileName
                : $"{relativeDir}/{fileName}";

            var crc64Hex = fields[3];

            if (!long.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sizeBytes))
            {
                throw new FormatException($"CSV record {recordNumber} has invalid SizeBytes.");
            }

            if (!DateTimeOffset.TryParseExact(
                    fields[5],
                    "O",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var lastWrite))
            {
                throw new FormatException($"CSV record {recordNumber} has invalid LastWriteTimeUtc.");
            }

            var entryKind = IndexEntryKind.File;
            if (fields.Count >= ExpectedHeader.Length)
            {
                if (!Enum.TryParse<IndexEntryKind>(fields[6], ignoreCase: true, out entryKind))
                {
                    throw new FormatException($"CSV record {recordNumber} has invalid EntryKind.");
                }
            }

            rows[relativePath] = new IndexRow(
                id,
                relativePath,
                crc64Hex,
                sizeBytes,
                lastWrite.UtcDateTime,
                entryKind);
        }

        return rows;
    }

    private static async Task<List<string>?> ReadRecordAsync(
        StreamReader reader,
        char[] buffer,
        PendingChar pending,
        int recordNumber,
        CancellationToken ct)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var hasData = false;

        while (true)
        {
            var value = await ReadCharAsync(reader, buffer, pending, ct).ConfigureAwait(false);
            if (value == -1)
            {
                if (inQuotes)
                {
                    throw new FormatException($"CSV record {recordNumber} ends inside a quoted field.");
                }

                if (!hasData && field.Length == 0 && fields.Count == 0)
                {
                    return null;
                }

                fields.Add(field.ToString());
                return fields;
            }

            hasData = true;
            var ch = (char)value;

            if (inQuotes)
            {
                if (ch == '"')
                {
                    var next = await ReadCharAsync(reader, buffer, pending, ct).ConfigureAwait(false);
                    if (next == '"')
                    {
                        field.Append('"');
                        continue;
                    }

                    inQuotes = false;
                    if (next == -1)
                    {
                        fields.Add(field.ToString());
                        return fields;
                    }

                    var nextChar = (char)next;
                    if (nextChar == ',')
                    {
                        fields.Add(field.ToString());
                        field.Clear();
                        continue;
                    }

                    if (nextChar == '\r' || nextChar == '\n')
                    {
                        if (nextChar == '\r')
                        {
                            await ConsumeLineFeedAsync(reader, buffer, pending, ct).ConfigureAwait(false);
                        }

                        fields.Add(field.ToString());
                        return fields;
                    }

                    throw new FormatException(
                        $"CSV record {recordNumber} has invalid character after closing quote.");
                }

                field.Append(ch);
                continue;
            }

            if (ch == '"')
            {
                if (field.Length > 0)
                {
                    throw new FormatException(
                        $"CSV record {recordNumber} has an unexpected quote in an unquoted field.");
                }

                inQuotes = true;
                continue;
            }

            if (ch == ',')
            {
                fields.Add(field.ToString());
                field.Clear();
                continue;
            }

            if (ch == '\r' || ch == '\n')
            {
                if (ch == '\r')
                {
                    await ConsumeLineFeedAsync(reader, buffer, pending, ct).ConfigureAwait(false);
                }

                fields.Add(field.ToString());
                return fields;
            }

            field.Append(ch);
        }
    }

    private static async Task<int> ReadCharAsync(
        StreamReader reader,
        char[] buffer,
        PendingChar pending,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (pending.Value.HasValue)
        {
            var value = pending.Value.Value;
            pending.Value = null;
            return value;
        }

        var read = await reader.ReadAsync(buffer.AsMemory(0, 1), ct).ConfigureAwait(false);
        return read == 0 ? -1 : buffer[0];
    }

    private static async Task ConsumeLineFeedAsync(
        StreamReader reader,
        char[] buffer,
        PendingChar pending,
        CancellationToken ct)
    {
        var next = await ReadCharAsync(reader, buffer, pending, ct).ConfigureAwait(false);
        if (next != '\n' && next != -1)
        {
            pending.Value = next;
        }
    }

    private sealed class PendingChar
    {
        public int? Value { get; set; }
    }
}
