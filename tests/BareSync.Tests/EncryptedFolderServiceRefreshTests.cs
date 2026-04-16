using System.Security.Cryptography;
using System.Text;
using BareSync.App;
using BareSync.Domain;
using BareSync.Infra;
using BareSync.UI;

namespace BareSync.Tests;

public sealed class EncryptedFolderServiceRefreshTests
{
    [Fact]
    public async Task RefreshEncryptedFolderAsync_OnlyUpsertsChangedFiles_AndRewritesIndex()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.RootPath, "source");
        var outputRoot = Path.Combine(temp.RootPath, "encrypted");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "docs"));
        Directory.CreateDirectory(outputRoot);

        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "docs", "a.txt"), "A-v1");
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "docs", "b.txt"), "B-v2");

        var sourceIndexPath = Path.Combine(sourceRoot, "baresync_source_index.csv");
        await BuildSourceIndexAsync(sourceRoot, sourceIndexPath);

        var service = new EncryptedFolderService();
        var config = new AppConfig
        {
            SourceRoot = sourceRoot,
            SourceIndexCsvPath = sourceIndexPath,
            EncryptedOutputRoot = outputRoot
        };

        var initialPlan = await service.BuildEncryptedPlanAsync(config, CancellationToken.None);
        var seedStatus = await service.CreateEncryptedIndexAsync(
            config,
            password: "secret",
            entries: initialPlan.Entries,
            dataPlan: initialPlan.DataPlan,
            ct: CancellationToken.None);

        Assert.NotNull(seedStatus);
        Assert.True(seedStatus!.SuccessOrWarningFlag);

        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "docs", "b.txt"), "B-v3");
        await BuildSourceIndexAsync(sourceRoot, sourceIndexPath);

        var result = await service.RefreshEncryptedFolderAsync(
            config,
            password: "secret",
            progress: new Progress<ProgressInfo>(),
            ct: CancellationToken.None);

        Assert.True(result.SuccessOrWarningFlag);
        Assert.Contains("upserted 1 file(s)", result.StatusLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unchanged 1", result.StatusLine, StringComparison.OrdinalIgnoreCase);

        var sourceRows = await CsvIndexReader.ReadAsync(sourceIndexPath, CancellationToken.None);
        var entryB = sourceRows["docs/b.txt"];
        var obfB = BuildObfuscatedRelativePath("docs/b.txt");
        Assert.True(File.Exists(Path.Combine(outputRoot, obfB + ".bse")));

        var load = await service.TryLoadEncryptedIndexAsync(outputRoot, "secret", CancellationToken.None);
        Assert.Null(load.Error);
        Assert.NotNull(load.Payload);
        var rewrittenB = load.Payload!.Entries.Single(e =>
            string.Equals(NormalizePath(e.OriginalRelativePath), NormalizePath("docs/b.txt"), StringComparison.OrdinalIgnoreCase));
        Assert.Equal(entryB.Crc64Hex, rewrittenB.Crc64Hex);
        Assert.Equal(entryB.SizeBytes, rewrittenB.SizeBytes);
    }

    [Fact]
    public async Task RefreshEncryptedFolderAsync_MirrorDeletesDestinationOnlyArchives()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.RootPath, "source");
        var outputRoot = Path.Combine(temp.RootPath, "encrypted");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(outputRoot);

        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "keep.txt"), "keep");

        var sourceIndexPath = Path.Combine(sourceRoot, "baresync_source_index.csv");
        await BuildSourceIndexAsync(sourceRoot, sourceIndexPath);

        var service = new EncryptedFolderService();
        var createConfig = new AppConfig
        {
            SourceRoot = sourceRoot,
            SourceIndexCsvPath = sourceIndexPath,
            EncryptedOutputRoot = outputRoot,
            Mirror = false
        };

        var sourcePlan = await service.BuildEncryptedPlanAsync(createConfig, CancellationToken.None);
        var staleObf = BuildObfuscatedRelativePath("stale.txt");
        var entriesWithStale = sourcePlan.Entries
            .Concat(
            [
                new EncryptedFolderService.EncryptedIndexEntry(
                    Id: 999,
                    ObfuscatedName: staleObf,
                    OriginalRelativePath: "stale.txt",
                    SizeBytes: 10,
                    LastWriteTimeUtc: DateTime.UtcNow,
                    Crc64Hex: "AAAA",
                    EntryKind: IndexEntryKind.File)
            ])
            .ToArray();

        var seedStatus = await service.CreateEncryptedIndexAsync(
            createConfig,
            password: "secret",
            entries: entriesWithStale,
            dataPlan: sourcePlan.DataPlan,
            ct: CancellationToken.None);

        Assert.NotNull(seedStatus);
        Assert.True(seedStatus!.SuccessOrWarningFlag);

        var staleArchive = Path.Combine(outputRoot, staleObf + ".bse");
        var staleParent = Path.GetDirectoryName(staleArchive);
        if (!string.IsNullOrWhiteSpace(staleParent))
        {
            Directory.CreateDirectory(staleParent);
        }

        await File.WriteAllTextAsync(staleArchive, "stale");

        var refreshConfig = new AppConfig
        {
            SourceRoot = sourceRoot,
            SourceIndexCsvPath = sourceIndexPath,
            EncryptedOutputRoot = outputRoot,
            Mirror = true
        };

        var result = await service.RefreshEncryptedFolderAsync(
            refreshConfig,
            password: "secret",
            progress: new Progress<ProgressInfo>(),
            ct: CancellationToken.None);

        Assert.True(result.SuccessOrWarningFlag);
        Assert.False(File.Exists(staleArchive));
        Assert.Contains("deleted 1 stale archive(s)", result.StatusLine, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefreshEncryptedFolderAsync_RefreshesSourceIndex_WhenSourceIndexContainsDeletedFile()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.RootPath, "source");
        var outputRoot = Path.Combine(temp.RootPath, "encrypted");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(outputRoot);

        var sourceFile = Path.Combine(sourceRoot, "file.txt");
        var keepFile = Path.Combine(sourceRoot, "keep.txt");
        await File.WriteAllTextAsync(sourceFile, "v1");
        await File.WriteAllTextAsync(keepFile, "keep");

        var sourceIndexPath = Path.Combine(sourceRoot, "baresync_source_index.csv");
        await BuildSourceIndexAsync(sourceRoot, sourceIndexPath);

        var service = new EncryptedFolderService();
        var config = new AppConfig
        {
            SourceRoot = sourceRoot,
            SourceIndexCsvPath = sourceIndexPath,
            EncryptedOutputRoot = outputRoot
        };

        var sourcePlan = await service.BuildEncryptedPlanAsync(config, CancellationToken.None);
        var seedStatus = await service.CreateEncryptedIndexAsync(
            config,
            password: "secret",
            entries: sourcePlan.Entries,
            dataPlan: sourcePlan.DataPlan,
            ct: CancellationToken.None);

        Assert.NotNull(seedStatus);
        Assert.True(seedStatus!.SuccessOrWarningFlag);

        var staleArchive = Path.Combine(outputRoot, BuildObfuscatedRelativePath("file.txt") + ".bse");
        Assert.True(File.Exists(staleArchive));

        File.Delete(sourceFile);

        // Keep source index stale on purpose (contains file.txt) to validate refresh behavior.
        var resultConfig = new AppConfig
        {
            SourceRoot = config.SourceRoot,
            SourceIndexCsvPath = config.SourceIndexCsvPath,
            EncryptedOutputRoot = config.EncryptedOutputRoot,
            Mirror = true,
            MirrorRoot = config.MirrorRoot,
            DestIndexCsvPath = config.DestIndexCsvPath,
            RestoreRoot = config.RestoreRoot,
            OutputCsvFileName = config.OutputCsvFileName,
            IndexCheckpointEveryFiles = config.IndexCheckpointEveryFiles,
            IndexCheckpointMinIntervalMs = config.IndexCheckpointMinIntervalMs,
            IndexIoCooldownMs = config.IndexIoCooldownMs,
            IndexInterStageCooldownMs = config.IndexInterStageCooldownMs,
            IndexForceGcBetweenStages = config.IndexForceGcBetweenStages
        };
        var result = await service.RefreshEncryptedFolderAsync(
            resultConfig,
            password: "secret",
            progress: new Progress<ProgressInfo>(),
            ct: CancellationToken.None);

        Assert.True(result.SuccessOrWarningFlag);
        Assert.DoesNotContain("Source file not found for encryption", result.StatusLine, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(staleArchive));

        var load = await service.TryLoadEncryptedIndexAsync(outputRoot, "secret", CancellationToken.None);
        Assert.Null(load.Error);
        Assert.NotNull(load.Payload);
        Assert.DoesNotContain(load.Payload!.Entries, e =>
            string.Equals(NormalizePath(e.OriginalRelativePath), NormalizePath("file.txt"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RefreshEncryptedFolderAsync_MissingEncryptedIndex_RebuildsDestination()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.RootPath, "source");
        var outputRoot = Path.Combine(temp.RootPath, "encrypted");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(outputRoot);

        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "file.txt"), "v1");
        var sourceIndexPath = Path.Combine(sourceRoot, "baresync_source_index.csv");
        await BuildSourceIndexAsync(sourceRoot, sourceIndexPath);

        var service = new EncryptedFolderService();
        var config = new AppConfig
        {
            SourceRoot = sourceRoot,
            SourceIndexCsvPath = sourceIndexPath,
            EncryptedOutputRoot = outputRoot
        };

        var result = await service.RefreshEncryptedFolderAsync(
            config,
            password: "secret",
            progress: new Progress<ProgressInfo>(),
            ct: CancellationToken.None);

        Assert.True(result.SuccessOrWarningFlag);
        Assert.Contains("rebuilt destination", result.StatusLine, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(outputRoot, ".baresync.encindex.bse")));

        var archivePath = Path.Combine(outputRoot, BuildObfuscatedRelativePath("file.txt") + ".bse");
        Assert.True(File.Exists(archivePath));

        var load = await service.TryLoadEncryptedIndexAsync(outputRoot, "secret", CancellationToken.None);
        Assert.Null(load.Error);
        Assert.NotNull(load.Payload);
        Assert.Contains(load.Payload!.Entries, e =>
            string.Equals(NormalizePath(e.OriginalRelativePath), NormalizePath("file.txt"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildEncryptedPlanAsync_ReportsProgress_ForSmartSourceRefresh()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.RootPath, "source");
        var outputRoot = Path.Combine(temp.RootPath, "encrypted");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(outputRoot);

        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "file.txt"), "v1");

        var sourceIndexPath = Path.Combine(sourceRoot, "baresync_source_index.csv");
        await BuildSourceIndexAsync(sourceRoot, sourceIndexPath);
        var service = new EncryptedFolderService();
        var config = new AppConfig
        {
            SourceRoot = sourceRoot,
            SourceIndexCsvPath = sourceIndexPath,
            EncryptedOutputRoot = outputRoot
        };

        var reported = new List<ProgressInfo>();
        var progress = new CapturingProgress(reported);

        var plan = await service.BuildEncryptedPlanAsync(config, progress, CancellationToken.None);

        Assert.NotEmpty(plan.Entries);
        Assert.Contains(reported, info =>
            !string.IsNullOrWhiteSpace(info.OperationTitle)
            && info.OperationTitle.Contains("Smart refreshing source index for encrypted operation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildEncryptedPlanAsync_ReportsProgress_ForSourceRebuild_WhenIndexMissing()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.RootPath, "source");
        var outputRoot = Path.Combine(temp.RootPath, "encrypted");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(outputRoot);

        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "file.txt"), "v1");

        var sourceIndexPath = Path.Combine(sourceRoot, "baresync_source_index.csv");
        var service = new EncryptedFolderService();
        var config = new AppConfig
        {
            SourceRoot = sourceRoot,
            SourceIndexCsvPath = sourceIndexPath,
            EncryptedOutputRoot = outputRoot
        };

        var reported = new List<ProgressInfo>();
        var progress = new CapturingProgress(reported);

        var plan = await service.BuildEncryptedPlanAsync(config, progress, CancellationToken.None);

        Assert.NotEmpty(plan.Entries);
        Assert.Contains(reported, info =>
            !string.IsNullOrWhiteSpace(info.OperationTitle)
            && info.OperationTitle.Contains("Rebuilding source index for encrypted operation", StringComparison.OrdinalIgnoreCase));
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

    private sealed class CapturingProgress : IProgress<ProgressInfo>
    {
        private readonly List<ProgressInfo> _target;

        public CapturingProgress(List<ProgressInfo> target)
        {
            _target = target;
        }

        public void Report(ProgressInfo value)
        {
            _target.Add(value);
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "BareSync_EncRefresh_" + Guid.NewGuid().ToString("N"));
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
