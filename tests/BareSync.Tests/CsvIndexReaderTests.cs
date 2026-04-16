using System.Text;
using BareSync.Domain;
using BareSync.Infra;
using Xunit;

namespace BareSync.Tests;

public sealed class CsvIndexReaderTests
{
    [Fact]
    public async Task ReadAsync_ParsesQuotedFields()
    {
        using var temp = new TempDirectory();
        var csvPath = Path.Combine(temp.RootPath, "index.csv");

        var entryA = new FileIndexEntry
        {
            Id = 1,
            RelativeDir = "Sub",
            FileName = "bravo,final.txt",
            Crc64Hex = "ABCDEF",
            SizeBytes = 42,
            LastWriteTimeUtc = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero)
        };

        var entryB = new FileIndexEntry
        {
            Id = 2,
            RelativeDir = "Sub",
            FileName = "quote\"file.txt",
            Crc64Hex = "123456",
            SizeBytes = 7,
            LastWriteTimeUtc = new DateTimeOffset(2024, 2, 3, 4, 5, 6, TimeSpan.Zero)
        };

        var lines = new[]
        {
            CsvIndexWriter.Header,
            CsvIndexWriter.FormatLine(entryA),
            CsvIndexWriter.FormatLine(entryB)
        };

        await File.WriteAllLinesAsync(csvPath, lines, new UTF8Encoding(false));

        var rows = await CsvIndexReader.ReadAsync(csvPath, CancellationToken.None);

        Assert.Equal(2, rows.Count);
        Assert.True(rows.TryGetValue("Sub/bravo,final.txt", out var rowA));
        Assert.Equal(entryA.Id, rowA.Id);
        Assert.Equal(entryA.Crc64Hex, rowA.Crc64Hex);
        Assert.Equal(entryA.SizeBytes, rowA.SizeBytes);
        Assert.Equal(entryA.LastWriteTimeUtc.UtcDateTime, rowA.LastWriteTimeUtc);

        Assert.True(rows.TryGetValue("Sub/quote\"file.txt", out var rowB));
        Assert.Equal(entryB.Id, rowB.Id);
        Assert.Equal(entryB.Crc64Hex, rowB.Crc64Hex);
        Assert.Equal(entryB.SizeBytes, rowB.SizeBytes);
        Assert.Equal(entryB.LastWriteTimeUtc.UtcDateTime, rowB.LastWriteTimeUtc);
    }

    [Fact]
    public async Task ReadAsync_ThrowsOnInvalidHeader()
    {
        using var temp = new TempDirectory();
        var csvPath = Path.Combine(temp.RootPath, "index.csv");

        var lines = new[]
        {
            "Bad,Header",
            "1,2,3"
        };

        await File.WriteAllLinesAsync(csvPath, lines, new UTF8Encoding(false));

        await Assert.ThrowsAsync<FormatException>(
            async () => await CsvIndexReader.ReadAsync(csvPath, CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync_SupportsLegacyHeaderWithoutEntryKind()
    {
        using var temp = new TempDirectory();
        var csvPath = Path.Combine(temp.RootPath, "index.csv");

        var lines = new[]
        {
            "Id,RelativeDir,FileName,Crc64Hex,SizeBytes,LastWriteTimeUtc",
            "1,,legacy.txt,ABCDEF,10,2024-01-01T00:00:00.0000000Z"
        };

        await File.WriteAllLinesAsync(csvPath, lines, new UTF8Encoding(false));

        var rows = await CsvIndexReader.ReadAsync(csvPath, CancellationToken.None);
        var row = Assert.Single(rows).Value;
        Assert.Equal(IndexEntryKind.File, row.EntryKind);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            RootPath = Path.Combine(
                Path.GetTempPath(),
                "BareSyncTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public void Dispose()
        {
            if (!Directory.Exists(RootPath))
            {
                return;
            }

            try
            {
                Directory.Delete(RootPath, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for temp directories.
            }
        }
    }
}
