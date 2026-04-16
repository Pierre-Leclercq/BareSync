using System.Globalization;
using System.Text;
using BareSync.Domain;

namespace BareSync.Infra;

internal static class CsvIndexWriter
{
    private const char Separator = ',';
    internal const string Header = "Id,RelativeDir,FileName,Crc64Hex,SizeBytes,LastWriteTimeUtc,EntryKind";

    public static async Task WriteAsync(
        string sourceRoot,
        string outputFileName,
        FileIndex index,
        CancellationToken ct)
    {
        var outputPath = Path.Combine(sourceRoot, outputFileName);
        await WriteAsync(index, outputPath, ct).ConfigureAwait(false);
    }

    public static async Task WriteAsync(
        FileIndex index,
        string fullCsvPath,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var parent = Path.GetDirectoryName(fullCsvPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        await using var stream = new FileStream(
            fullCsvPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var writer = new StreamWriter(stream, encoding);

        await writer.WriteLineAsync(Header).ConfigureAwait(false);

        foreach (var entry in index.Entries.OrderBy(pair => pair.Key).Select(pair => pair.Value))
        {
            ct.ThrowIfCancellationRequested();

            var line = FormatLine(entry);

            await writer.WriteLineAsync(line).ConfigureAwait(false);
        }
    }

    internal static string FormatLine(FileIndexEntry entry)
    {
        return string.Join(Separator, new[]
        {
            Escape(entry.Id.ToString(CultureInfo.InvariantCulture)),
            Escape(entry.RelativeDir),
            Escape(entry.FileName),
            Escape(entry.Crc64Hex),
            Escape(entry.SizeBytes.ToString(CultureInfo.InvariantCulture)),
            Escape(entry.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture)),
            Escape(entry.EntryKind.ToString())
        });
    }

    private static string Escape(string value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        var mustQuote = value.Contains(Separator)
            || value.Contains(';')
            || value.Contains('"')
            || value.Contains('\r')
            || value.Contains('\n');

        if (!mustQuote)
        {
            return value;
        }

        var escaped = value.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }
}
