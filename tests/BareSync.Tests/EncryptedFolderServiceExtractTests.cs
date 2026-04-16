using BareSync.App;
using BareSync.Domain;
using BareSync.Infra;

namespace BareSync.Tests;

public sealed class EncryptedFolderServiceExtractTests
{
    [Fact]
    public async Task IsNativeBseArchive_DetectsNativeArchiveAndRejectsPlainFile()
    {
        using var temp = new TempDirectory();
        var fixture = await CreateFixtureAsync(temp.RootPath);

        Assert.True(EncryptedFolderService.IsNativeBseArchive(fixture.ArchivePath));

        var plainFilePath = Path.Combine(temp.RootPath, "not-native.bse");
        await File.WriteAllTextAsync(plainFilePath, "plain-text");
        Assert.False(EncryptedFolderService.IsNativeBseArchive(plainFilePath));
    }

    [Fact]
    public async Task ValidateArchivePasswordAsync_ReturnsNullForValidPassword_AndErrorForInvalidPassword()
    {
        using var temp = new TempDirectory();
        var fixture = await CreateFixtureAsync(temp.RootPath);

        var ok = await fixture.Service.ValidateArchivePasswordAsync(
            fixture.ArchivePath,
            fixture.Password,
            CancellationToken.None);
        var ko = await fixture.Service.ValidateArchivePasswordAsync(
            fixture.ArchivePath,
            "wrong-password",
            CancellationToken.None);

        Assert.Null(ok);
        Assert.False(string.IsNullOrWhiteSpace(ko));
    }

    [Fact]
    public async Task TryResolveEntryForArchiveAsync_ReturnsMatchingEntry()
    {
        using var temp = new TempDirectory();
        var fixture = await CreateFixtureAsync(temp.RootPath);

        var resolve = await fixture.Service.TryResolveEntryForArchiveAsync(
            fixture.EncryptedOutputRoot,
            fixture.ArchivePath,
            fixture.Password,
            CancellationToken.None);

        Assert.Null(resolve.Error);
        Assert.NotNull(resolve.Entry);
        Assert.Equal(NormalizePath(fixture.OriginalRelativePath), NormalizePath(resolve.Entry!.OriginalRelativePath));
        Assert.Equal(fixture.ExpectedCrc64Hex, resolve.Entry.Crc64Hex, ignoreCase: true);
    }

    [Fact]
    public async Task ExtractSingleEncryptedArchiveAsync_WithExpectedCrc_SucceedsAndRestoresPayload()
    {
        using var temp = new TempDirectory();
        var fixture = await CreateFixtureAsync(temp.RootPath);

        var destinationPath = Path.Combine(temp.RootPath, "extract", "restored.txt");
        var result = await fixture.Service.ExtractSingleEncryptedArchiveAsync(
            fixture.ArchivePath,
            fixture.Password,
            destinationPath,
            fixture.ExpectedCrc64Hex,
            CancellationToken.None);

        Assert.True(result.SuccessOrWarningFlag);
        Assert.True(File.Exists(destinationPath));
        Assert.Equal(fixture.SourcePayload, await File.ReadAllTextAsync(destinationPath));
    }

    [Fact]
    public async Task ExtractSingleEncryptedArchiveAsync_WithWrongExpectedCrc_FailsAndDoesNotWriteDestination()
    {
        using var temp = new TempDirectory();
        var fixture = await CreateFixtureAsync(temp.RootPath);

        var destinationPath = Path.Combine(temp.RootPath, "extract", "restored.txt");
        var result = await fixture.Service.ExtractSingleEncryptedArchiveAsync(
            fixture.ArchivePath,
            fixture.Password,
            destinationPath,
            expectedCrc64Hex: "DEADBEEF",
            CancellationToken.None);

        Assert.False(result.SuccessOrWarningFlag);
        Assert.Contains("CRC mismatch", result.StatusLine, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(destinationPath));
    }

    private static async Task<ExtractFixture> CreateFixtureAsync(string rootPath)
    {
        var sourceRoot = Path.Combine(rootPath, "source");
        var encryptedOutputRoot = Path.Combine(rootPath, "encrypted");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "docs"));
        Directory.CreateDirectory(encryptedOutputRoot);

        const string originalRelativePath = "docs/file-x.txt";
        const string sourcePayload = "extract-payload";
        var sourceFilePath = Path.Combine(sourceRoot, "docs", "file-x.txt");
        await File.WriteAllTextAsync(sourceFilePath, sourcePayload);

        var sourceIndexPath = Path.Combine(sourceRoot, "baresync_source_index.csv");
        await BuildSourceIndexAsync(sourceRoot, sourceIndexPath);

        var service = new EncryptedFolderService();
        var config = new AppConfig
        {
            SourceRoot = sourceRoot,
            SourceIndexCsvPath = sourceIndexPath,
            EncryptedOutputRoot = encryptedOutputRoot
        };

        var plan = await service.BuildEncryptedPlanAsync(config, CancellationToken.None);
        var create = await service.CreateEncryptedIndexAsync(config, "secret", plan.Entries, plan.DataPlan, CancellationToken.None);
        Assert.NotNull(create);
        Assert.True(create!.SuccessOrWarningFlag);

        var entry = Assert.Single(plan.Entries, e => NormalizePath(e.OriginalRelativePath) == NormalizePath(originalRelativePath));
        var archivePath = Path.Combine(encryptedOutputRoot, entry.ObfuscatedName + ".bse");
        Assert.True(File.Exists(archivePath));

        return new ExtractFixture(
            Service: service,
            EncryptedOutputRoot: encryptedOutputRoot,
            ArchivePath: archivePath,
            Password: "secret",
            OriginalRelativePath: originalRelativePath,
            SourcePayload: sourcePayload,
            ExpectedCrc64Hex: entry.Crc64Hex);
    }

    private static async Task BuildSourceIndexAsync(string sourceRoot, string sourceIndexPath)
    {
        var index = await FileScanner.BuildIndexAsync(sourceRoot, sourceIndexPath, CancellationToken.None);
        await CsvIndexWriter.WriteAsync(index, sourceIndexPath, CancellationToken.None);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private sealed record ExtractFixture(
        EncryptedFolderService Service,
        string EncryptedOutputRoot,
        string ArchivePath,
        string Password,
        string OriginalRelativePath,
        string SourcePayload,
        string ExpectedCrc64Hex);

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "BareSync_EncExtract_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(RootPath))
                {
                    Directory.Delete(RootPath, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
