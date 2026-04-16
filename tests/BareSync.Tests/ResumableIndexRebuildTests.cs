using BareSync.App;
using BareSync.Domain;
using BareSync.Infra;
using BareSync.UI;

namespace BareSync.Tests;

public sealed class ResumableIndexRebuildTests
{
    [Fact]
    public async Task RebuildIndexResumableAsync_CancelLeavesResumableState()
    {
        using var temp = new TempDirectory();
        var root = Path.Combine(temp.RootPath, "root");
        Directory.CreateDirectory(root);

        await File.WriteAllTextAsync(Path.Combine(root, "alpha.txt"), "alpha");
        await File.WriteAllTextAsync(Path.Combine(root, "bravo.txt"), "bravo");
        await File.WriteAllTextAsync(Path.Combine(root, "charlie.txt"), "charlie");

        var indexPath = Path.Combine(root, "index.csv");
        using var cts = new CancellationTokenSource();
        var cancelRequested = false;

        try
        {
            await FileScanner.RebuildIndexResumableAsync(
                root,
                indexPath,
                indexPath,
                cts.Token,
                progress: (processed, total, _, _) =>
                {
                    if (!cancelRequested && total > 0 && processed >= 1)
                    {
                        cancelRequested = true;
                        cts.Cancel();
                    }
                });
            Assert.Fail("Expected cancellation during rebuild.");
        }
        catch (OperationCanceledException)
        {
        }

        Assert.True(File.Exists(indexPath + ".work"));
        Assert.True(File.Exists(indexPath + ".checkpoint"));
        Assert.False(File.Exists(indexPath));
    }

    [Fact]
    public async Task RebuildIndexResumableAsync_ResumesAndFinalizes()
    {
        using var temp = new TempDirectory();
        var root = Path.Combine(temp.RootPath, "root");
        Directory.CreateDirectory(root);

        await File.WriteAllTextAsync(Path.Combine(root, "alpha.txt"), "alpha");
        await File.WriteAllTextAsync(Path.Combine(root, "bravo.txt"), "bravo");
        await File.WriteAllTextAsync(Path.Combine(root, "charlie.txt"), "charlie");

        var indexPath = Path.Combine(root, "index.csv");
        using var cts = new CancellationTokenSource();
        var cancelRequested = false;

        try
        {
            await FileScanner.RebuildIndexResumableAsync(
                root,
                indexPath,
                indexPath,
                cts.Token,
                progress: (processed, total, _, _) =>
                {
                    if (!cancelRequested && total > 0 && processed >= 1)
                    {
                        cancelRequested = true;
                        cts.Cancel();
                    }
                });
        }
        catch (OperationCanceledException)
        {
        }

        await FileScanner.RebuildIndexResumableAsync(
            root,
            indexPath,
            indexPath,
            CancellationToken.None);

        Assert.True(File.Exists(indexPath));
        Assert.False(File.Exists(indexPath + ".work"));
        Assert.False(File.Exists(indexPath + ".checkpoint"));

        var lines = File.ReadAllLines(indexPath);
        Assert.Equal(CsvIndexWriter.Header, lines[0]);
        Assert.Equal(4, lines.Length);
    }

    [Fact]
    public async Task RebuildIndexResumableAsync_ResumeSkipsAlreadyProcessedRows()
    {
        using var temp = new TempDirectory();
        var root = Path.Combine(temp.RootPath, "root");
        Directory.CreateDirectory(root);

        var indexPath = Path.Combine(root, "index.csv");
        var totalFiles = 10;

        for (var i = 0; i < totalFiles; i++)
        {
            var fileName = $"file{i:D2}.txt";
            await File.WriteAllTextAsync(Path.Combine(root, fileName), fileName);
        }

        using var cts = new CancellationTokenSource();
        var cancelRequested = false;
        var cancelAfter = 5;

        try
        {
            await FileScanner.RebuildIndexResumableAsync(
                root,
                indexPath,
                indexPath,
                cts.Token,
                progress: (processed, total, _, _) =>
                {
                    if (!cancelRequested && total > 0 && processed >= cancelAfter)
                    {
                        cancelRequested = true;
                        cts.Cancel();
                    }
                });
        }
        catch (OperationCanceledException)
        {
        }

        await FileScanner.RebuildIndexResumableAsync(
            root,
            indexPath,
            indexPath,
            CancellationToken.None);

        var lines = File.ReadAllLines(indexPath);
        Assert.Equal(CsvIndexWriter.Header, lines[0]);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 1; i < lines.Length; i++)
        {
            var parts = lines[i].Split(',');
            Assert.True(parts.Length >= 3, "Expected at least 3 columns per data row.");

            var relativeDir = parts[1];
            var fileName = parts[2];
            var relativePath = string.IsNullOrEmpty(relativeDir)
                ? fileName
                : $"{relativeDir}/{fileName}";

            Assert.True(seen.Add(relativePath), $"Duplicate relative path found: {relativePath}");
        }

        Assert.Equal(totalFiles, seen.Count);
    }

    [Fact]
    public async Task RebuildIndexResumableAsync_IncompatibleCheckpointStartsFresh()
    {
        using var temp = new TempDirectory();
        var root = Path.Combine(temp.RootPath, "root");
        Directory.CreateDirectory(root);

        await File.WriteAllTextAsync(Path.Combine(root, "alpha.txt"), "alpha");
        await File.WriteAllTextAsync(Path.Combine(root, "bravo.txt"), "bravo");

        var indexPath = Path.Combine(root, "index.csv");
        var workPath = indexPath + ".work";
        var checkpointPath = indexPath + ".checkpoint";

        var staleLine = string.Join(
            ',',
            "999",
            string.Empty,
            "stale.txt",
            "ERROR",
            "0",
            "2000-01-01T00:00:00.0000000Z");
        await File.WriteAllTextAsync(
            workPath,
            CsvIndexWriter.Header + Environment.NewLine + staleLine + Environment.NewLine);

        var checkpointContent = string.Join(Environment.NewLine, new[]
        {
            "Version=1",
            $"RootPath={Path.Combine(temp.RootPath, "mismatch")}",
            $"IndexPath={indexPath}",
            $"StartedUtc={DateTime.UtcNow:O}",
            "LastRelativePath=stale.txt",
            "ProcessedCount=1"
        });
        await File.WriteAllTextAsync(checkpointPath, checkpointContent);

        await FileScanner.RebuildIndexResumableAsync(
            root,
            indexPath,
            indexPath,
            CancellationToken.None);

        Assert.True(File.Exists(indexPath));
        Assert.False(File.Exists(workPath));
        Assert.False(File.Exists(checkpointPath));

        var lines = File.ReadAllLines(indexPath);
        Assert.Equal(CsvIndexWriter.Header, lines[0]);
        Assert.DoesNotContain(lines, line => line.Contains("stale.txt", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RebuildIndexResumableAsync_ResumesAfterFileInsertionBeforeLastProcessed()
    {
        using var temp = new TempDirectory();
        var root = Path.Combine(temp.RootPath, "root");
        Directory.CreateDirectory(root);

        // Initial state: alpha, bravo, charlie
        await File.WriteAllTextAsync(Path.Combine(root, "alpha.txt"), "alpha");
        await File.WriteAllTextAsync(Path.Combine(root, "bravo.txt"), "bravo");
        await File.WriteAllTextAsync(Path.Combine(root, "charlie.txt"), "charlie");

        var indexPath = Path.Combine(root, "index.csv");
        using var cts = new CancellationTokenSource();
        var cancelRequested = false;

        try
        {
            await FileScanner.RebuildIndexResumableAsync(
                root,
                indexPath,
                indexPath,
                cts.Token,
                progress: (processed, total, _, _) =>
                {
                    if (!cancelRequested && total > 0 && processed >= 1)
                    {
                        cancelRequested = true;
                        cts.Cancel();
                    }
                });
        }
        catch (OperationCanceledException)
        {
        }

        // Now insert a new file that sorts before 'bravo.txt' (the last processed file)
        await File.WriteAllTextAsync(Path.Combine(root, "alpha2.txt"), "alpha2");

        // Resume should succeed even though the position of 'bravo.txt' shifted
        await FileScanner.RebuildIndexResumableAsync(
            root,
            indexPath,
            indexPath,
            CancellationToken.None);

        Assert.True(File.Exists(indexPath));
        Assert.False(File.Exists(indexPath + ".work"));
        Assert.False(File.Exists(indexPath + ".checkpoint"));

        var lines = File.ReadAllLines(indexPath);
        Assert.Equal(CsvIndexWriter.Header, lines[0]);
        // Should have header + 4 data files (alpha, alpha2, bravo, charlie)
        Assert.Equal(5, lines.Length);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 1; i < lines.Length; i++)
        {
            var parts = lines[i].Split(',');
            Assert.True(parts.Length >= 3, "Expected at least 3 columns per data row.");

            var relativeDir = parts[1];
            var fileName = parts[2];
            var relativePath = string.IsNullOrEmpty(relativeDir)
                ? fileName
                : $"{relativeDir}/{fileName}";

            Assert.True(seen.Add(relativePath), $"Duplicate relative path found: {relativePath}");
        }

        Assert.Contains("alpha2.txt", seen);
        Assert.Equal(4, seen.Count);
    }

    [Fact]
    public async Task BuildIndexIncrementalAsync_ReusesCrcForUnchangedFiles()
    {
        using var temp = new TempDirectory();
        var root = Path.Combine(temp.RootPath, "root");
        Directory.CreateDirectory(root);

        var indexPath = Path.Combine(root, "index.csv");
        var alphaPath = Path.Combine(root, "alpha.txt");
        var bravoPath = Path.Combine(root, "bravo.txt");

        await File.WriteAllTextAsync(alphaPath, "alpha");
        await File.WriteAllTextAsync(bravoPath, "bravo");

        await FileScanner.RebuildIndexResumableAsync(
            root,
            indexPath,
            indexPath,
            CancellationToken.None);

        var initialRows = await CsvIndexReader.ReadAsync(indexPath, CancellationToken.None);
        var alphaInitial = initialRows["alpha.txt"];
        var bravoInitial = initialRows["bravo.txt"];

        using var alphaLock = new FileStream(alphaPath, FileMode.Open, FileAccess.Read, FileShare.None);
        await File.WriteAllTextAsync(bravoPath, "bravo-updated");

        await FileScanner.BuildIndexIncrementalAsync(
            root,
            indexPath,
            indexPath,
            CancellationToken.None);

        var refreshed = await CsvIndexReader.ReadAsync(indexPath, CancellationToken.None);

        Assert.Equal(alphaInitial.Crc64Hex, refreshed["alpha.txt"].Crc64Hex);
        Assert.Equal(alphaInitial.Id, refreshed["alpha.txt"].Id);

        Assert.Equal(bravoInitial.Id, refreshed["bravo.txt"].Id);
        Assert.NotEqual(bravoInitial.Crc64Hex, refreshed["bravo.txt"].Crc64Hex);
    }

    [Fact]
    public async Task BuildIndexIncrementalAsync_AssignsNewIdToNewFiles()
    {
        using var temp = new TempDirectory();
        var root = Path.Combine(temp.RootPath, "root");
        Directory.CreateDirectory(root);

        var indexPath = Path.Combine(root, "index.csv");

        await File.WriteAllTextAsync(Path.Combine(root, "alpha.txt"), "alpha");
        await File.WriteAllTextAsync(Path.Combine(root, "bravo.txt"), "bravo");

        await FileScanner.RebuildIndexResumableAsync(
            root,
            indexPath,
            indexPath,
            CancellationToken.None);

        var initialRows = await CsvIndexReader.ReadAsync(indexPath, CancellationToken.None);
        var alphaId = initialRows["alpha.txt"].Id;
        var bravoId = initialRows["bravo.txt"].Id;
        var maxInitialId = Math.Max(alphaId, bravoId);

        await File.WriteAllTextAsync(Path.Combine(root, "charlie.txt"), "charlie");

        await FileScanner.BuildIndexIncrementalAsync(
            root,
            indexPath,
            indexPath,
            CancellationToken.None);

        var refreshed = await CsvIndexReader.ReadAsync(indexPath, CancellationToken.None);

        Assert.Equal(alphaId, refreshed["alpha.txt"].Id);
        Assert.Equal(bravoId, refreshed["bravo.txt"].Id);
        Assert.True(refreshed.TryGetValue("charlie.txt", out var charlieRow));
        Assert.Equal(maxInitialId + 1, charlieRow.Id);
    }

    [Fact]
    public async Task RefreshIndexesAsync_SkipsWhenIndexCompleteAndNoResumeState()
    {
        using var temp = new TempDirectory();
        var root = Path.Combine(temp.RootPath, "root");
        Directory.CreateDirectory(root);
    
        // Create two files to index
        await File.WriteAllTextAsync(Path.Combine(root, "alpha.txt"), "alpha");
        await File.WriteAllTextAsync(Path.Combine(root, "bravo.txt"), "bravo");
    
        var indexPath = Path.Combine(root, "index.csv");
        var workPath = indexPath + ".work";
        var checkpointPath = indexPath + ".checkpoint";
    
        // Create a complete, valid index
        var expectedContent = CsvIndexWriter.Header + Environment.NewLine +
            "0,,alpha.txt,ERROR,0,2000-01-01T00:00:00.0000000Z" + Environment.NewLine +
            "1,,bravo.txt,ERROR,0,2000-01-01T00:00:00.0000000Z" + Environment.NewLine;
        await File.WriteAllTextAsync(indexPath, expectedContent);
    
        // Ensure no .work or .checkpoint files exist
        Assert.False(File.Exists(workPath));
        Assert.False(File.Exists(checkpointPath));
    
        // Read initial bytes for strict comparison
        var before = await File.ReadAllBytesAsync(indexPath);
    
        // Create config pointing to our test directory
        var config = new AppConfig
        {
            SourceRoot = root,
            MirrorRoot = root,
            SourceIndexCsvPath = indexPath,
            DestIndexCsvPath = Path.Combine(root, "destIndex.csv")
        };
    
        // Call RefreshIndexesAsync - it should skip when index is valid and no resume state exists
        await Program.RefreshIndexesAsync(config, CancellationToken.None);
    
        // Read final bytes and verify they are identical (strict no-rewrite check)
        var after = await File.ReadAllBytesAsync(indexPath);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task RefreshIndexAsync_IncrementalWithMissingIndex_DropsStaleResumeAndRebuilds()
    {
        using var temp = new TempDirectory();
        var root = Path.Combine(temp.RootPath, "root");
        Directory.CreateDirectory(root);

        await File.WriteAllTextAsync(Path.Combine(root, "alpha.txt"), "alpha");
        await File.WriteAllTextAsync(Path.Combine(root, "bravo.txt"), "bravo");

        var indexPath = Path.Combine(root, "index.csv");
        var workPath = indexPath + ".work";
        var checkpointPath = indexPath + ".checkpoint";

        await File.WriteAllTextAsync(
            workPath,
            string.Join(
                Environment.NewLine,
                new[]
                {
                    CsvIndexWriter.Header,
                    "0,,ghost.txt,ERROR,0,2000-01-01T00:00:00.0000000Z,File",
                    string.Empty
                }));
        await File.WriteAllTextAsync(
            checkpointPath,
            string.Join(
                Environment.NewLine,
                new[]
                {
                    "Version=1",
                    $"RootPath={root}",
                    $"IndexPath={indexPath}",
                    $"StartedUtc={DateTime.UtcNow:O}",
                    "LastRelativePath=ghost.txt",
                    "ProcessedCount=1"
                }));

        var config = new AppConfig
        {
            SourceRoot = root,
            SourceIndexCsvPath = indexPath
        };

        var progressEvents = new List<ProgressInfo>();
        var progress = new Progress<ProgressInfo>(info => progressEvents.Add(info));

        var count = await IndexRefreshService.RefreshIndexAsync(
            root,
            indexPath,
            CancellationToken.None,
            progress,
            "Smart refreshing CRC indexes (source)...",
            incremental: true,
            config);

        Assert.Equal(2, count);
        Assert.True(File.Exists(indexPath));
        Assert.False(File.Exists(workPath));
        Assert.False(File.Exists(checkpointPath));

        var rows = await CsvIndexReader.ReadAsync(indexPath, CancellationToken.None);
        Assert.Contains("alpha.txt", rows.Keys);
        Assert.Contains("bravo.txt", rows.Keys);
        Assert.DoesNotContain("ghost.txt", rows.Keys);

        Assert.Contains(progressEvents, info =>
            !string.IsNullOrWhiteSpace(info.LastLine)
            && info.LastLine.Contains("Index missing, rebuilding from scratch", StringComparison.OrdinalIgnoreCase));
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
