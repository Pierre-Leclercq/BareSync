using BareSync.App;
using BareSync.Domain;

namespace BareSync.Tests;

public sealed class EncryptedFolderServiceCreateIndexTests
{
    [Fact]
    public async Task CreateEncryptedIndexAsync_Success_WritesIndexAndDataArchive()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.RootPath, "source");
        var outputRoot = Path.Combine(temp.RootPath, "encrypted");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "docs"));
        Directory.CreateDirectory(outputRoot);

        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "docs", "file.txt"), "payload");

        var service = new EncryptedFolderService();
        var config = new AppConfig
        {
            SourceRoot = sourceRoot,
            EncryptedOutputRoot = outputRoot
        };

        var obfuscatedRel = new string('A', 64);
        var entries = new[]
        {
            new EncryptedFolderService.EncryptedIndexEntry(
                Id: 1,
                ObfuscatedName: obfuscatedRel,
                OriginalRelativePath: "docs/file.txt",
                SizeBytes: 7,
                LastWriteTimeUtc: DateTime.UtcNow,
                Crc64Hex: string.Empty)
        };
        var plan = new[]
        {
            new EncryptedFolderService.EncryptedDataPlanItem(
                SourceRelativePath: "docs/file.txt",
                ObfuscatedFileName: obfuscatedRel,
                DestinationRelativePath: obfuscatedRel,
                SizeBytes: 7)
        };

        var status = await service.CreateEncryptedIndexAsync(
            config,
            password: "sëcret",
            entries: entries,
            dataPlan: plan,
            ct: CancellationToken.None);

        Assert.NotNull(status);
        Assert.True(status!.SuccessOrWarningFlag);
        Assert.Contains("Encrypted index written to:", status.StatusLine, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(outputRoot, ".baresync.encindex.bse")));
        Assert.True(File.Exists(Path.Combine(outputRoot, obfuscatedRel + ".bse")));
    }

    [Fact]
    public async Task CreateEncryptedIndexAsync_MirrorEnabled_DeletesOnlyManagedObsoleteArchives()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.RootPath, "source");
        var outputRoot = Path.Combine(temp.RootPath, "encrypted");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "docs"));
        Directory.CreateDirectory(outputRoot);

        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "docs", "file.txt"), "alpha");

        var expectedRel = new string('A', 64);
        var orphanManagedRel = new string('B', 64);
        var orphanManagedArchive = Path.Combine(outputRoot, orphanManagedRel + ".bse");
        var unmanagedArchive = Path.Combine(outputRoot, "manual_backup.bse");
        await File.WriteAllTextAsync(orphanManagedArchive, "old-managed");
        await File.WriteAllTextAsync(unmanagedArchive, "manual");

        var service = new EncryptedFolderService();
        var config = new AppConfig
        {
            SourceRoot = sourceRoot,
            EncryptedOutputRoot = outputRoot,
            Mirror = true
        };

        var entries = new[]
        {
            new EncryptedFolderService.EncryptedIndexEntry(
                Id: 1,
                ObfuscatedName: expectedRel,
                OriginalRelativePath: "docs/file.txt",
                SizeBytes: 5,
                LastWriteTimeUtc: DateTime.UtcNow,
                Crc64Hex: string.Empty)
        };
        var plan = new[]
        {
            new EncryptedFolderService.EncryptedDataPlanItem(
                SourceRelativePath: "docs/file.txt",
                ObfuscatedFileName: expectedRel,
                DestinationRelativePath: expectedRel,
                SizeBytes: 5)
        };

        var status = await service.CreateEncryptedIndexAsync(
            config,
            password: "secret",
            entries: entries,
            dataPlan: plan,
            ct: CancellationToken.None);

        Assert.NotNull(status);
        Assert.True(status!.SuccessOrWarningFlag);
        Assert.Contains("mirror deleted 1 obsolete archive(s)", status.StatusLine, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(outputRoot, expectedRel + ".bse")));
        Assert.False(File.Exists(orphanManagedArchive));
        Assert.True(File.Exists(unmanagedArchive));
    }

    [Fact]
    public async Task CreateEncryptedIndexAsync_RerunRegeneratesExistingArchive_AndCleansCheckpoint()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.RootPath, "source");
        var outputRoot = Path.Combine(temp.RootPath, "encrypted");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "docs"));
        Directory.CreateDirectory(outputRoot);

        var sourcePath = Path.Combine(sourceRoot, "docs", "file.txt");
        await File.WriteAllTextAsync(sourcePath, "v1");

        var obfuscatedRel = new string('C', 64);
        var archivePath = Path.Combine(outputRoot, obfuscatedRel + ".bse");
        await File.WriteAllTextAsync(archivePath, "stale");
        await File.WriteAllTextAsync(Path.Combine(outputRoot, ".baresync.createenc.checkpoint"), "1");
        await File.WriteAllTextAsync(Path.Combine(outputRoot, ".baresync.createenc.checkpoint.work"), "1");

        var service = new EncryptedFolderService();
        var config = new AppConfig
        {
            SourceRoot = sourceRoot,
            EncryptedOutputRoot = outputRoot
        };

        var entries = new[]
        {
            new EncryptedFolderService.EncryptedIndexEntry(
                Id: 1,
                ObfuscatedName: obfuscatedRel,
                OriginalRelativePath: "docs/file.txt",
                SizeBytes: 2,
                LastWriteTimeUtc: DateTime.UtcNow,
                Crc64Hex: string.Empty)
        };
        var plan = new[]
        {
            new EncryptedFolderService.EncryptedDataPlanItem(
                SourceRelativePath: "docs/file.txt",
                ObfuscatedFileName: obfuscatedRel,
                DestinationRelativePath: obfuscatedRel,
                SizeBytes: 2)
        };

        var run1 = await service.CreateEncryptedIndexAsync(config, "secret", entries, plan, CancellationToken.None);
        Assert.NotNull(run1);
        Assert.True(run1!.SuccessOrWarningFlag);
        var bytesAfterRun1 = await File.ReadAllBytesAsync(archivePath);

        await File.WriteAllTextAsync(sourcePath, "v2");
        var run2 = await service.CreateEncryptedIndexAsync(config, "secret", entries, plan, CancellationToken.None);
        Assert.NotNull(run2);
        Assert.True(run2!.SuccessOrWarningFlag);
        var bytesAfterRun2 = await File.ReadAllBytesAsync(archivePath);

        Assert.False(bytesAfterRun1.SequenceEqual(bytesAfterRun2));
        Assert.False(File.Exists(Path.Combine(outputRoot, ".baresync.createenc.checkpoint")));
        Assert.False(File.Exists(Path.Combine(outputRoot, ".baresync.createenc.checkpoint.work")));
    }

    [Fact]
    public async Task CreateEncryptedIndexAsync_EmptyDataPlan_StillWritesEncryptedIndex()
    {
        using var temp = new TempDirectory();
        var outputRoot = Path.Combine(temp.RootPath, "encrypted");
        Directory.CreateDirectory(outputRoot);

        var service = new EncryptedFolderService();
        var config = new AppConfig
        {
            SourceRoot = temp.RootPath,
            EncryptedOutputRoot = outputRoot
        };

        var status = await service.CreateEncryptedIndexAsync(
            config,
            password: "secret",
            entries: Array.Empty<EncryptedFolderService.EncryptedIndexEntry>(),
            dataPlan: Array.Empty<EncryptedFolderService.EncryptedDataPlanItem>(),
            ct: CancellationToken.None);

        Assert.NotNull(status);
        Assert.True(status!.SuccessOrWarningFlag);
        Assert.True(File.Exists(Path.Combine(outputRoot, ".baresync.encindex.bse")));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "BareSync_EncCreate_" + Guid.NewGuid().ToString("N"));
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
