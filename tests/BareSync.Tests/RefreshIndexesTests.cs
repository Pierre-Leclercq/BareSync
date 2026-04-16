using BareSync.App;
using BareSync.Domain;
using BareSync.Infra;
using Xunit;

namespace BareSync.Tests;

public sealed class RefreshIndexesTests
{
    private const string CsvHeader = CsvIndexWriter.Header;

    [Fact]
    public async Task RefreshIndexesAsync_WritesSourceAndDestIndexes()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.RootPath, "src");
        var destRoot = Path.Combine(temp.RootPath, "dest");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(destRoot);

        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "alpha.txt"), "alpha");
        await File.WriteAllTextAsync(Path.Combine(destRoot, "bravo.txt"), "bravo");

        var config = new AppConfig
        {
            SourceRoot = sourceRoot,
            MirrorRoot = destRoot,
            SourceIndexCsvPath = Path.Combine(sourceRoot, "baresync_source_index.csv"),
            DestIndexCsvPath = Path.Combine(destRoot, "baresync_dest_index.csv"),
            EncryptedOutputRoot = null!
        };

        var validationErrors = ConfigService.Validate(config);
        Assert.Empty(validationErrors);

        await Program.RefreshIndexesAsync(config, CancellationToken.None);

        Assert.True(File.Exists(config.SourceIndexCsvPath));
        Assert.True(File.Exists(config.DestIndexCsvPath));

        var sourceLines = File.ReadAllLines(config.SourceIndexCsvPath);
        var destLines = File.ReadAllLines(config.DestIndexCsvPath);
        Assert.True(sourceLines.Length >= 2);
        Assert.True(destLines.Length >= 2);
        Assert.Equal(CsvHeader, sourceLines[0]);
        Assert.Equal(CsvHeader, destLines[0]);
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
