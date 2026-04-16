using System.Security.Cryptography;
using System.Text;
using BareSync.App;
using BareSync.Domain;
using BareSync.Infra;
using BareSync.UI;

namespace BareSync.Tests;

public sealed class EncryptedFolderServiceRestoreTests
{
    [Fact]
    public async Task RestoreEncryptedFilesAsync_Success_RestoresFileContent()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.RootPath, "source");
        var encryptedOutputRoot = Path.Combine(temp.RootPath, "encrypted");
        var restoreRoot = Path.Combine(temp.RootPath, "restore");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "docs"));
        Directory.CreateDirectory(encryptedOutputRoot);
        Directory.CreateDirectory(restoreRoot);

        var sourceContent = "restored-a";
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "docs", "file-a.txt"), sourceContent);

        var service = new EncryptedFolderService();
        await BuildSourceIndexAsync(sourceRoot, Path.Combine(sourceRoot, "baresync_source_index.csv"));

        var createConfig = new AppConfig
        {
            SourceRoot = sourceRoot,
            SourceIndexCsvPath = Path.Combine(sourceRoot, "baresync_source_index.csv"),
            EncryptedOutputRoot = encryptedOutputRoot
        };

        var plan = await service.BuildEncryptedPlanAsync(createConfig, CancellationToken.None);
        var create = await service.CreateEncryptedIndexAsync(createConfig, "secret", plan.Entries, plan.DataPlan, CancellationToken.None);
        Assert.NotNull(create);
        Assert.True(create!.SuccessOrWarningFlag);

        var restoreConfig = new AppConfig
        {
            EncryptedOutputRoot = encryptedOutputRoot,
            RestoreRoot = restoreRoot
        };

        var result = await service.RestoreEncryptedFilesAsync(
            restoreConfig,
            password: "secret",
            progress: new Progress<ProgressInfo>(),
            ct: CancellationToken.None);

        Assert.True(result.SuccessOrWarningFlag);
        Assert.Contains("Restored 1 file(s)", result.StatusLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("skipped 0", result.StatusLine, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(sourceContent, await File.ReadAllTextAsync(Path.Combine(restoreRoot, "docs", "file-a.txt")));
    }

    [Fact]
    public async Task RestoreEncryptedFilesAsync_SmartMode_SecondRunSkipsUnchangedFile()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.RootPath, "source");
        var encryptedOutputRoot = Path.Combine(temp.RootPath, "encrypted");
        var restoreRoot = Path.Combine(temp.RootPath, "restore");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "docs"));
        Directory.CreateDirectory(encryptedOutputRoot);
        Directory.CreateDirectory(restoreRoot);

        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "docs", "smart.txt"), "smart-content");

        var service = new EncryptedFolderService();
        await BuildSourceIndexAsync(sourceRoot, Path.Combine(sourceRoot, "baresync_source_index.csv"));

        var createConfig = new AppConfig
        {
            SourceRoot = sourceRoot,
            SourceIndexCsvPath = Path.Combine(sourceRoot, "baresync_source_index.csv"),
            EncryptedOutputRoot = encryptedOutputRoot
        };

        var plan = await service.BuildEncryptedPlanAsync(createConfig, CancellationToken.None);
        var create = await service.CreateEncryptedIndexAsync(createConfig, "secret", plan.Entries, plan.DataPlan, CancellationToken.None);
        Assert.NotNull(create);
        Assert.True(create!.SuccessOrWarningFlag);

        var restoreConfig = new AppConfig
        {
            EncryptedOutputRoot = encryptedOutputRoot,
            RestoreRoot = restoreRoot,
            RestoreSmartMode = RestoreSmartMode.Smart
        };

        var first = await service.RestoreEncryptedFilesAsync(
            restoreConfig,
            password: "secret",
            progress: new Progress<ProgressInfo>(),
            ct: CancellationToken.None);
        Assert.True(first.SuccessOrWarningFlag);
        Assert.Contains("Restored 1 file(s)", first.StatusLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("skipped 0", first.StatusLine, StringComparison.OrdinalIgnoreCase);

        var second = await service.RestoreEncryptedFilesAsync(
            restoreConfig,
            password: "secret",
            progress: new Progress<ProgressInfo>(),
            ct: CancellationToken.None);

        Assert.True(second.SuccessOrWarningFlag);
        Assert.Contains("Restored 0 file(s)", second.StatusLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("skipped 1", second.StatusLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("skipped timestamp aligned 1", second.StatusLine, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RestoreEncryptedFilesAsync_SmartMode_SkipRealignsTimestampForRobocopyFriendlyValidation()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.RootPath, "source");
        var encryptedOutputRoot = Path.Combine(temp.RootPath, "encrypted");
        var restoreRoot = Path.Combine(temp.RootPath, "restore");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "docs"));
        Directory.CreateDirectory(encryptedOutputRoot);
        Directory.CreateDirectory(restoreRoot);

        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "docs", "timestamp.txt"), "timestamp-content");

        var service = new EncryptedFolderService();
        await BuildSourceIndexAsync(sourceRoot, Path.Combine(sourceRoot, "baresync_source_index.csv"));

        var createConfig = new AppConfig
        {
            SourceRoot = sourceRoot,
            SourceIndexCsvPath = Path.Combine(sourceRoot, "baresync_source_index.csv"),
            EncryptedOutputRoot = encryptedOutputRoot
        };

        var plan = await service.BuildEncryptedPlanAsync(createConfig, CancellationToken.None);
        var create = await service.CreateEncryptedIndexAsync(createConfig, "secret", plan.Entries, plan.DataPlan, CancellationToken.None);
        Assert.NotNull(create);
        Assert.True(create!.SuccessOrWarningFlag);

        var restoreConfig = new AppConfig
        {
            EncryptedOutputRoot = encryptedOutputRoot,
            RestoreRoot = restoreRoot,
            RestoreSmartMode = RestoreSmartMode.Smart
        };

        var first = await service.RestoreEncryptedFilesAsync(
            restoreConfig,
            password: "secret",
            progress: new Progress<ProgressInfo>(),
            ct: CancellationToken.None);
        Assert.True(first.SuccessOrWarningFlag);

        var restoredPath = Path.Combine(restoreRoot, "docs", "timestamp.txt");
        var indexedEntry = plan.Entries.Single(e => NormalizePath(e.OriginalRelativePath) == NormalizePath("docs/timestamp.txt"));

        File.SetLastWriteTimeUtc(restoredPath, indexedEntry.LastWriteTimeUtc.AddMinutes(15));
        Assert.NotEqual(indexedEntry.LastWriteTimeUtc, File.GetLastWriteTimeUtc(restoredPath));

        var second = await service.RestoreEncryptedFilesAsync(
            restoreConfig,
            password: "secret",
            progress: new Progress<ProgressInfo>(),
            ct: CancellationToken.None);

        Assert.True(second.SuccessOrWarningFlag);
        Assert.Contains("Restored 0 file(s)", second.StatusLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("skipped 1", second.StatusLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("skipped timestamp aligned 1", second.StatusLine, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(indexedEntry.LastWriteTimeUtc, File.GetLastWriteTimeUtc(restoredPath));
    }

    [Fact]
    public async Task RestoreEncryptedFilesAsync_FastSmartMode_SecondRunSkipsUsingSizeAndTimestamp()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.RootPath, "source");
        var encryptedOutputRoot = Path.Combine(temp.RootPath, "encrypted");
        var restoreRoot = Path.Combine(temp.RootPath, "restore");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "docs"));
        Directory.CreateDirectory(encryptedOutputRoot);
        Directory.CreateDirectory(restoreRoot);

        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "docs", "fast.txt"), "fast-content");

        var service = new EncryptedFolderService();
        await BuildSourceIndexAsync(sourceRoot, Path.Combine(sourceRoot, "baresync_source_index.csv"));

        var createConfig = new AppConfig
        {
            SourceRoot = sourceRoot,
            SourceIndexCsvPath = Path.Combine(sourceRoot, "baresync_source_index.csv"),
            EncryptedOutputRoot = encryptedOutputRoot
        };

        var plan = await service.BuildEncryptedPlanAsync(createConfig, CancellationToken.None);
        var create = await service.CreateEncryptedIndexAsync(createConfig, "secret", plan.Entries, plan.DataPlan, CancellationToken.None);
        Assert.NotNull(create);
        Assert.True(create!.SuccessOrWarningFlag);

        var restoreConfig = new AppConfig
        {
            EncryptedOutputRoot = encryptedOutputRoot,
            RestoreRoot = restoreRoot,
            RestoreSmartMode = RestoreSmartMode.FastSmart
        };

        var first = await service.RestoreEncryptedFilesAsync(
            restoreConfig,
            password: "secret",
            progress: new Progress<ProgressInfo>(),
            ct: CancellationToken.None);
        Assert.True(first.SuccessOrWarningFlag);

        var restoredPath = Path.Combine(restoreRoot, "docs", "fast.txt");
        var beforeSecondRun = File.GetLastWriteTimeUtc(restoredPath);

        var second = await service.RestoreEncryptedFilesAsync(
            restoreConfig,
            password: "secret",
            progress: new Progress<ProgressInfo>(),
            ct: CancellationToken.None);

        Assert.True(second.SuccessOrWarningFlag);
        Assert.Contains("Restored 0 file(s)", second.StatusLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("skipped 1", second.StatusLine, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(beforeSecondRun, File.GetLastWriteTimeUtc(restoredPath));
    }

    [Fact]
    public async Task RestoreEncryptedFilesAsync_FastSmartMode_RestoresWhenTimestampOrSizeDiffers()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.RootPath, "source");
        var encryptedOutputRoot = Path.Combine(temp.RootPath, "encrypted");
        var restoreRoot = Path.Combine(temp.RootPath, "restore");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "docs"));
        Directory.CreateDirectory(encryptedOutputRoot);
        Directory.CreateDirectory(restoreRoot);

        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "docs", "force.txt"), "expected-content");

        var service = new EncryptedFolderService();
        await BuildSourceIndexAsync(sourceRoot, Path.Combine(sourceRoot, "baresync_source_index.csv"));

        var createConfig = new AppConfig
        {
            SourceRoot = sourceRoot,
            SourceIndexCsvPath = Path.Combine(sourceRoot, "baresync_source_index.csv"),
            EncryptedOutputRoot = encryptedOutputRoot
        };

        var plan = await service.BuildEncryptedPlanAsync(createConfig, CancellationToken.None);
        var create = await service.CreateEncryptedIndexAsync(createConfig, "secret", plan.Entries, plan.DataPlan, CancellationToken.None);
        Assert.NotNull(create);
        Assert.True(create!.SuccessOrWarningFlag);

        var restoreConfig = new AppConfig
        {
            EncryptedOutputRoot = encryptedOutputRoot,
            RestoreRoot = restoreRoot,
            RestoreSmartMode = RestoreSmartMode.FastSmart
        };

        var first = await service.RestoreEncryptedFilesAsync(
            restoreConfig,
            password: "secret",
            progress: new Progress<ProgressInfo>(),
            ct: CancellationToken.None);
        Assert.True(first.SuccessOrWarningFlag);

        var restoredPath = Path.Combine(restoreRoot, "docs", "force.txt");
        await File.WriteAllTextAsync(restoredPath, "tampered-content");
        File.SetLastWriteTimeUtc(restoredPath, DateTime.UtcNow.AddMinutes(5));

        var second = await service.RestoreEncryptedFilesAsync(
            restoreConfig,
            password: "secret",
            progress: new Progress<ProgressInfo>(),
            ct: CancellationToken.None);

        Assert.True(second.SuccessOrWarningFlag);
        Assert.Contains("Restored 1 file(s)", second.StatusLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("skipped 0", second.StatusLine, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("expected-content", await File.ReadAllTextAsync(restoredPath));
    }

    [Fact]
    public async Task RestoreEncryptedFilesAsync_MirrorEnabled_DeletesOrphansAndEmptyDirectories()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.RootPath, "source");
        var encryptedOutputRoot = Path.Combine(temp.RootPath, "encrypted");
        var restoreRoot = Path.Combine(temp.RootPath, "restore");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "docs"));
        Directory.CreateDirectory(encryptedOutputRoot);
        Directory.CreateDirectory(restoreRoot);

        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "docs", "file-c.txt"), "expected");

        var expectedDir = Path.Combine(restoreRoot, "docs");
        Directory.CreateDirectory(expectedDir);
        await File.WriteAllTextAsync(Path.Combine(expectedDir, "file-c.txt"), "old-content");

        var orphanDir = Path.Combine(restoreRoot, "old");
        Directory.CreateDirectory(orphanDir);
        var orphanPath = Path.Combine(orphanDir, "ghost.txt");
        await File.WriteAllTextAsync(orphanPath, "orphan");

        var service = new EncryptedFolderService();
        await BuildSourceIndexAsync(sourceRoot, Path.Combine(sourceRoot, "baresync_source_index.csv"));

        var createConfig = new AppConfig
        {
            SourceRoot = sourceRoot,
            SourceIndexCsvPath = Path.Combine(sourceRoot, "baresync_source_index.csv"),
            EncryptedOutputRoot = encryptedOutputRoot
        };

        var plan = await service.BuildEncryptedPlanAsync(createConfig, CancellationToken.None);
        var create = await service.CreateEncryptedIndexAsync(createConfig, "secret", plan.Entries, plan.DataPlan, CancellationToken.None);
        Assert.NotNull(create);
        Assert.True(create!.SuccessOrWarningFlag);

        var restoreConfig = new AppConfig
        {
            EncryptedOutputRoot = encryptedOutputRoot,
            RestoreRoot = restoreRoot,
            Mirror = true
        };

        var result = await service.RestoreEncryptedFilesAsync(
            restoreConfig,
            password: "secret",
            progress: new Progress<ProgressInfo>(),
            ct: CancellationToken.None);

        Assert.True(result.SuccessOrWarningFlag);
        Assert.True(File.Exists(Path.Combine(restoreRoot, "docs", "file-c.txt")));
        Assert.False(File.Exists(orphanPath));
        Assert.False(Directory.Exists(orphanDir));
        Assert.Contains("mirror deleted 1 stale file(s)", result.StatusLine, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RestoreEncryptedFilesAsync_MirrorEnabled_PreservesExpectedEmptyDirectories()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.RootPath, "source");
        var encryptedOutputRoot = Path.Combine(temp.RootPath, "encrypted");
        var restoreRoot = Path.Combine(temp.RootPath, "restore");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "docs"));
        Directory.CreateDirectory(Path.Combine(sourceRoot, "docs", "empty-dir"));
        Directory.CreateDirectory(encryptedOutputRoot);
        Directory.CreateDirectory(restoreRoot);

        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "docs", "file-d.txt"), "expected");

        var orphanDir = Path.Combine(restoreRoot, "old");
        Directory.CreateDirectory(orphanDir);
        await File.WriteAllTextAsync(Path.Combine(orphanDir, "ghost.txt"), "orphan");

        var service = new EncryptedFolderService();
        await BuildSourceIndexAsync(sourceRoot, Path.Combine(sourceRoot, "baresync_source_index.csv"));

        var createConfig = new AppConfig
        {
            SourceRoot = sourceRoot,
            SourceIndexCsvPath = Path.Combine(sourceRoot, "baresync_source_index.csv"),
            EncryptedOutputRoot = encryptedOutputRoot
        };

        var plan = await service.BuildEncryptedPlanAsync(createConfig, CancellationToken.None);
        var create = await service.CreateEncryptedIndexAsync(createConfig, "secret", plan.Entries, plan.DataPlan, CancellationToken.None);
        Assert.NotNull(create);
        Assert.True(create!.SuccessOrWarningFlag);

        var restoreConfig = new AppConfig
        {
            EncryptedOutputRoot = encryptedOutputRoot,
            RestoreRoot = restoreRoot,
            Mirror = true
        };

        var result = await service.RestoreEncryptedFilesAsync(
            restoreConfig,
            password: "secret",
            progress: new Progress<ProgressInfo>(),
            ct: CancellationToken.None);

        Assert.True(result.SuccessOrWarningFlag);
        Assert.True(File.Exists(Path.Combine(restoreRoot, "docs", "file-d.txt")));
        Assert.True(Directory.Exists(Path.Combine(restoreRoot, "docs", "empty-dir")));
        Assert.False(Directory.Exists(orphanDir));
    }

    [Fact]
    public async Task RestoreEncryptedFilesAsync_FailsWhenEncryptedArchiveMissing()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.RootPath, "source");
        var encryptedOutputRoot = Path.Combine(temp.RootPath, "encrypted");
        var restoreRoot = Path.Combine(temp.RootPath, "restore");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "docs"));
        Directory.CreateDirectory(encryptedOutputRoot);
        Directory.CreateDirectory(restoreRoot);

        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "docs", "missing.txt"), "x");

        var service = new EncryptedFolderService();
        await BuildSourceIndexAsync(sourceRoot, Path.Combine(sourceRoot, "baresync_source_index.csv"));
        var createConfig = new AppConfig
        {
            SourceRoot = sourceRoot,
            SourceIndexCsvPath = Path.Combine(sourceRoot, "baresync_source_index.csv"),
            EncryptedOutputRoot = encryptedOutputRoot
        };

        var plan = await service.BuildEncryptedPlanAsync(createConfig, CancellationToken.None);
        var create = await service.CreateEncryptedIndexAsync(createConfig, "secret", plan.Entries, plan.DataPlan, CancellationToken.None);
        Assert.NotNull(create);
        Assert.True(create!.SuccessOrWarningFlag);

        var entry = plan.Entries.Single();
        File.Delete(Path.Combine(encryptedOutputRoot, entry.ObfuscatedName + ".bse"));

        var restoreConfig = new AppConfig
        {
            EncryptedOutputRoot = encryptedOutputRoot,
            RestoreRoot = restoreRoot
        };

        var result = await service.RestoreEncryptedFilesAsync(
            restoreConfig,
            password: "secret",
            progress: new Progress<ProgressInfo>(),
            ct: CancellationToken.None);

        Assert.False(result.SuccessOrWarningFlag);
        Assert.Contains("Encrypted archive not found", result.StatusLine, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RestoreEncryptedFilesAsync_CrcMismatch_FailsWithExplicitStatus()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.RootPath, "source");
        var encryptedOutputRoot = Path.Combine(temp.RootPath, "encrypted");
        var restoreRoot = Path.Combine(temp.RootPath, "restore");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "docs"));
        Directory.CreateDirectory(encryptedOutputRoot);
        Directory.CreateDirectory(restoreRoot);

        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "docs", "file-crc.txt"), "actual-content");

        var service = new EncryptedFolderService();
        await BuildSourceIndexAsync(sourceRoot, Path.Combine(sourceRoot, "baresync_source_index.csv"));

        var createConfig = new AppConfig
        {
            SourceRoot = sourceRoot,
            SourceIndexCsvPath = Path.Combine(sourceRoot, "baresync_source_index.csv"),
            EncryptedOutputRoot = encryptedOutputRoot
        };

        var plan = await service.BuildEncryptedPlanAsync(createConfig, CancellationToken.None);
        var create = await service.CreateEncryptedIndexAsync(createConfig, "secret", plan.Entries, plan.DataPlan, CancellationToken.None);
        Assert.NotNull(create);
        Assert.True(create!.SuccessOrWarningFlag);

        var wrongEntries = plan.Entries
            .Select(entry => entry with { Crc64Hex = "DEADBEEF" })
            .ToArray();
        var rewriteIndexOnly = await service.CreateEncryptedIndexAsync(
            createConfig,
            password: "secret",
            entries: wrongEntries,
            dataPlan: Array.Empty<EncryptedFolderService.EncryptedDataPlanItem>(),
            ct: CancellationToken.None);

        Assert.NotNull(rewriteIndexOnly);
        Assert.True(rewriteIndexOnly!.SuccessOrWarningFlag);

        var restoreConfig = new AppConfig
        {
            EncryptedOutputRoot = encryptedOutputRoot,
            RestoreRoot = restoreRoot
        };

        var result = await service.RestoreEncryptedFilesAsync(
            restoreConfig,
            password: "secret",
            progress: new Progress<ProgressInfo>(),
            ct: CancellationToken.None);

        Assert.False(result.SuccessOrWarningFlag);
        Assert.Contains("CRC mismatch after restore", result.StatusLine, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RestoreEncryptedFilesAsync_DirectoryEntry_DoesNotRequireArchive()
    {
        using var temp = new TempDirectory();
        var encryptedOutputRoot = Path.Combine(temp.RootPath, "encrypted");
        var restoreRoot = Path.Combine(temp.RootPath, "restore");
        Directory.CreateDirectory(encryptedOutputRoot);
        Directory.CreateDirectory(restoreRoot);

        var service = new EncryptedFolderService();
        var config = new AppConfig
        {
            SourceRoot = temp.RootPath,
            EncryptedOutputRoot = encryptedOutputRoot
        };

        var entries = new[]
        {
            new EncryptedFolderService.EncryptedIndexEntry(
                Id: 1,
                ObfuscatedName: BuildObfuscatedRelativePath("docs/empty-dir"),
                OriginalRelativePath: "docs/empty-dir",
                SizeBytes: 0,
                LastWriteTimeUtc: DateTime.UtcNow,
                Crc64Hex: string.Empty,
                EntryKind: IndexEntryKind.Directory)
        };

        var create = await service.CreateEncryptedIndexAsync(
            config,
            password: "secret",
            entries: entries,
            dataPlan: Array.Empty<EncryptedFolderService.EncryptedDataPlanItem>(),
            ct: CancellationToken.None);

        Assert.NotNull(create);
        Assert.True(create!.SuccessOrWarningFlag);

        var restoreConfig = new AppConfig
        {
            EncryptedOutputRoot = encryptedOutputRoot,
            RestoreRoot = restoreRoot
        };

        var result = await service.RestoreEncryptedFilesAsync(
            restoreConfig,
            password: "secret",
            progress: new Progress<ProgressInfo>(),
            ct: CancellationToken.None);

        Assert.True(result.SuccessOrWarningFlag);
        Assert.True(Directory.Exists(Path.Combine(restoreRoot, "docs", "empty-dir")));
    }

    [Fact]
    public async Task RestoreEncryptedFilesAsync_FailsFastOnMissingEntry_AndDoesNotRestoreRemainingEntries()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.RootPath, "source");
        var encryptedOutputRoot = Path.Combine(temp.RootPath, "encrypted");
        var restoreRoot = Path.Combine(temp.RootPath, "restore");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "docs"));
        Directory.CreateDirectory(encryptedOutputRoot);
        Directory.CreateDirectory(restoreRoot);

        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "docs", "missing-2.txt"), "x");
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "docs", "ok.txt"), "ok");

        var service = new EncryptedFolderService();
        await BuildSourceIndexAsync(sourceRoot, Path.Combine(sourceRoot, "baresync_source_index.csv"));
        var createConfig = new AppConfig
        {
            SourceRoot = sourceRoot,
            SourceIndexCsvPath = Path.Combine(sourceRoot, "baresync_source_index.csv"),
            EncryptedOutputRoot = encryptedOutputRoot
        };

        var plan = await service.BuildEncryptedPlanAsync(createConfig, CancellationToken.None);
        var create = await service.CreateEncryptedIndexAsync(createConfig, "secret", plan.Entries, plan.DataPlan, CancellationToken.None);
        Assert.NotNull(create);
        Assert.True(create!.SuccessOrWarningFlag);

        var missingEntry = plan.Entries.Single(e => NormalizePath(e.OriginalRelativePath) == NormalizePath("docs/missing-2.txt"));
        File.Delete(Path.Combine(encryptedOutputRoot, missingEntry.ObfuscatedName + ".bse"));

        var restoreConfig = new AppConfig
        {
            EncryptedOutputRoot = encryptedOutputRoot,
            RestoreRoot = restoreRoot
        };

        var result = await service.RestoreEncryptedFilesAsync(
            restoreConfig,
            password: "secret",
            progress: new Progress<ProgressInfo>(),
            ct: CancellationToken.None);

        Assert.False(result.SuccessOrWarningFlag);
        Assert.Contains("Encrypted archive not found", result.StatusLine, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(restoreRoot, "docs", "ok.txt")));
    }

    private static async Task BuildSourceIndexAsync(string sourceRoot, string sourceIndexPath)
    {
        var index = await FileScanner.BuildIndexAsync(sourceRoot, sourceIndexPath, CancellationToken.None);
        await CsvIndexWriter.WriteAsync(index, sourceIndexPath, CancellationToken.None);
    }

    private static string BuildObfuscatedRelativePath(string relativePath)
    {
        var normalized = NormalizePath(relativePath)
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

        var segments = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var obfuscatedSegments = segments.Select(segment =>
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(segment));
            return Convert.ToHexString(hash);
        });

        return string.Join(Path.DirectorySeparatorChar, obfuscatedSegments);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "BareSync_EncRestore_" + Guid.NewGuid().ToString("N"));
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
