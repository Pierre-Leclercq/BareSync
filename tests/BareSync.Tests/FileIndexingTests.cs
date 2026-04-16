using System.Globalization;
using BareSync.Domain;
using BareSync.Infra;
using Xunit;

namespace BareSync.Tests;

public sealed class FileIndexingTests
{
    [Fact]
    public async Task BuildIndexAsync_NormalizesRelativeDir()
    {
        using var temp = new TempDirectory();
        var root = temp.RootPath;
        var nested = Path.Combine(root, "Folder", "Sub");
        Directory.CreateDirectory(nested);
        var filePath = Path.Combine(nested, "file.txt");
        await File.WriteAllTextAsync(filePath, "data");
        var rootFile = Path.Combine(root, "root.txt");
        await File.WriteAllTextAsync(rootFile, "root");

        var index = await FileScanner.BuildIndexAsync(root, ignoreFullPath: null, CancellationToken.None);

        Assert.Equal(2, index.Entries.Count);
        var nestedEntry = index.Entries.Values.Single(entry => entry.FileName == "file.txt");
        var rootEntry = index.Entries.Values.Single(entry => entry.FileName == "root.txt");
        var expected = Normalize(Path.Combine("Folder", "Sub"));
        Assert.Equal(expected, nestedEntry.RelativeDir);
        Assert.Equal(string.Empty, rootEntry.RelativeDir);
    }

    [Fact]
    public async Task BuildIndexAsync_AssignsStableIds()
    {
        using var temp = new TempDirectory();
        var root = temp.RootPath;
        var paths = new[]
        {
            Path.Combine(root, "b.txt"),
            Path.Combine(root, "A.txt"),
            Path.Combine(root, "c.txt")
        };

        foreach (var path in paths)
        {
            await File.WriteAllTextAsync(path, "data");
        }

        var index = await FileScanner.BuildIndexAsync(root, ignoreFullPath: null, CancellationToken.None);

        var expectedOrder = paths
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => Normalize(Path.GetRelativePath(root, path)))
            .ToList();

        var actualOrder = index.Entries
            .OrderBy(pair => pair.Key)
            .Select(pair => CombineRelative(pair.Value.RelativeDir, pair.Value.FileName))
            .ToList();

        Assert.Equal(new long[] { 0, 1, 2 }, index.Entries.Keys.OrderBy(key => key));
        Assert.Equal(expectedOrder, actualOrder);
    }

    [Fact]
    public async Task BuildIndexAsync_IgnoresConfiguredIndexFile()
    {
        using var temp = new TempDirectory();
        var root = temp.RootPath;
        var alphaPath = Path.Combine(root, "alpha.txt");
        var ignorePath = Path.Combine(root, "baresync_source_index.csv");
        await File.WriteAllTextAsync(alphaPath, "alpha");
        await File.WriteAllTextAsync(ignorePath, "csv");

        var index = await FileScanner.BuildIndexAsync(root, ignorePath, CancellationToken.None);

        Assert.Single(index.Entries);
        var entry = index.Entries.Values.Single();
        Assert.Equal("alpha.txt", entry.FileName);
    }

    [Fact]
    public async Task BuildIndexAsync_ExcludesDefaultWindowsSystemFiles()
    {
        using var temp = new TempDirectory();
        var root = temp.RootPath;
        await File.WriteAllTextAsync(Path.Combine(root, "alpha.txt"), "alpha");
        await File.WriteAllTextAsync(Path.Combine(root, "Thumbs.db"), "thumb");
        await File.WriteAllTextAsync(Path.Combine(root, "desktop.ini"), "ini");
        await File.WriteAllTextAsync(Path.Combine(root, "ehthumbs.db"), "eh");

        var index = await FileScanner.BuildIndexAsync(root, ignoreFullPath: null, CancellationToken.None);

        var files = index.Entries.Values
            .Where(entry => entry.EntryKind == IndexEntryKind.File)
            .Select(entry => CombineRelative(entry.RelativeDir, entry.FileName))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(new[] { "alpha.txt" }, files);
    }

    [Fact]
    public async Task BuildIndexAsync_ExcludesDefaultWindowsSystemDirectories()
    {
        using var temp = new TempDirectory();
        var root = temp.RootPath;

        var keepDir = Path.Combine(root, "keep");
        Directory.CreateDirectory(keepDir);
        await File.WriteAllTextAsync(Path.Combine(keepDir, "data.txt"), "data");

        var sviDir = Path.Combine(root, "System Volume Information");
        Directory.CreateDirectory(sviDir);
        await File.WriteAllTextAsync(Path.Combine(sviDir, "secret.dat"), "secret");

        var recycleDir = Path.Combine(root, "$RECYCLE.BIN");
        Directory.CreateDirectory(recycleDir);
        await File.WriteAllTextAsync(Path.Combine(recycleDir, "ghost.txt"), "ghost");

        var index = await FileScanner.BuildIndexAsync(root, ignoreFullPath: null, CancellationToken.None);

        var files = index.Entries.Values
            .Where(entry => entry.EntryKind == IndexEntryKind.File)
            .Select(entry => CombineRelative(entry.RelativeDir, entry.FileName))
            .ToArray();

        Assert.Contains("keep/data.txt", files);
        Assert.DoesNotContain(files, path => path.StartsWith("System Volume Information/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(files, path => path.StartsWith("$RECYCLE.BIN/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildIndexAsync_ExcludesUsingCustomPathGlobs()
    {
        using var temp = new TempDirectory();
        var root = temp.RootPath;
        var nested = Path.Combine(root, "sub");
        Directory.CreateDirectory(nested);

        await File.WriteAllTextAsync(Path.Combine(root, "keep.txt"), "keep");
        await File.WriteAllTextAsync(Path.Combine(root, "root.tmp"), "tmp");
        await File.WriteAllTextAsync(Path.Combine(nested, "nested.tmp"), "tmp");

        var config = new AppConfig
        {
            ExcludePathGlobs = ["*.tmp", "**/*.tmp"]
        };

        var index = await FileScanner.BuildIndexAsync(root, ignoreFullPath: null, config, CancellationToken.None);

        var files = index.Entries.Values
            .Where(entry => entry.EntryKind == IndexEntryKind.File)
            .Select(entry => CombineRelative(entry.RelativeDir, entry.FileName))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(new[] { "keep.txt" }, files);
    }

    [Fact]
    public void CsvIndexWriter_EscapesFields()
    {
        var timestamp = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);

        var entry = new FileIndexEntry
        {
            Id = 0,
            RelativeDir = "A,B/C\"D\nE",
            FileName = "report,final\"v2.csv",
            Crc64Hex = "ABCDEF0123456789",
            SizeBytes = 123,
            LastWriteTimeUtc = timestamp
        };

        var expectedTimestamp = timestamp.ToString("O", CultureInfo.InvariantCulture);
        var expectedLine = string.Join(",", new[]
        {
            "0",
            "\"A,B/C\"\"D\nE\"",
            "\"report,final\"\"v2.csv\"",
            "ABCDEF0123456789",
            "123",
            expectedTimestamp,
            entry.EntryKind.ToString()
        });

        var line = CsvIndexWriter.FormatLine(entry);

        Assert.Equal(expectedLine, line);
    }

    [Fact]
    public void CsvIndexWriter_QuotesCommaAndQuoteFields()
    {
        var timestamp = new DateTimeOffset(2024, 2, 3, 4, 5, 6, TimeSpan.Zero);

        var entry = new FileIndexEntry
        {
            Id = 1,
            RelativeDir = "A,B",
            FileName = "quote\"test,final.pdf",
            Crc64Hex = "ABCDEF",
            SizeBytes = 42,
            LastWriteTimeUtc = timestamp
        };

        var expectedTimestamp = timestamp.ToString("O", CultureInfo.InvariantCulture);
        var expectedLine = string.Join(",", new[]
        {
            "1",
            "\"A,B\"",
            "\"quote\"\"test,final.pdf\"",
            "ABCDEF",
            "42",
            expectedTimestamp,
            entry.EntryKind.ToString()
        });

        var line = CsvIndexWriter.FormatLine(entry);

        Assert.Equal(expectedLine, line);
        Assert.Contains("\"A,B\"", line);
        Assert.Contains("\"quote\"\"test,final.pdf\"", line);
    }

    [Fact]
    public async Task Crc64Service_ReturnsUppercaseHex()
    {
        using var temp = new TempDirectory();
        var filePath = Path.Combine(temp.RootPath, "data.bin");
        await File.WriteAllTextAsync(filePath, "abc");

        var crc = await Crc64Service.ComputeCrc64HexAsync(filePath, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(crc));
        Assert.Equal(16, crc.Length);
        Assert.Matches("^[0-9A-F]+$", crc);
    }

    private static string Normalize(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static string CombineRelative(string relativeDir, string fileName)
    {
        return string.IsNullOrEmpty(relativeDir)
            ? fileName
            : $"{relativeDir}/{fileName}";
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            RootPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
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
