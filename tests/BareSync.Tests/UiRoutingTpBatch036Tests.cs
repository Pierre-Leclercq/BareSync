using Xunit;

namespace BareSync.Tests;

/// <summary>
/// TP_BATCH_036-039: Batch home/menu regression tests with quick execute available.
/// </summary>
public sealed class UiRoutingTpBatch036Tests
{
    [Fact]
    public async Task TP_BATCH_036_BatchHome_ExposesQuickExecuteAndPurgeOptions()
    {
        var stdin = string.Join(
            Environment.NewLine,
            new[] { "2", "0", "0" }) + Environment.NewLine;

        var result = await UiRoutingHarness.RunBareSyncAsync(
            stdin,
            timeout: TimeSpan.FromSeconds(10));

        Assert.Contains("** Batch mode **", result.Stdout);
        Assert.Contains("3. Execute batch", result.Stdout, StringComparison.Ordinal);
        Assert.Contains("4. Purge Batch indexes", result.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TP_BATCH_037_BatchCreateAndPreflight_FromDetailsFlow()
    {
        var stdin = string.Join(
            Environment.NewLine,
            new[]
            {
                "2", "2", "QuickRun",
                "4", "1", "4",
                "2", "S", "3", "S", "4", "S", "SRC.csv", "5", "S", "DST.csv", "0",
                "0",
                "7",
                "2",
                "0",
                "0",
                "0"
            }) + Environment.NewLine;

        var result = await UiRoutingHarness.RunBareSyncAsync(
            stdin,
            timeout: TimeSpan.FromSeconds(30));

        Assert.Contains("** Batch / Details **", result.Stdout);
        Assert.Contains("** Batch / Preflight **", result.Stdout);
        Assert.Contains("QuickRun", result.Stdout);
    }

    [Fact]
    public async Task TP_BATCH_038_DetailsPreflightRun_ExecutesSteps()
    {
        var stdin = string.Join(
            Environment.NewLine,
            new[]
            {
                "2", "2", "ExecTest",
                "4", "1", "4",
                "2", "S", "3", "S", "4", "S", "SRC.csv", "5", "S", "DST.csv", "0",
                "0",
                "7",
                "1",
                "y",
                "0",
                "0",
                "0"
            }) + Environment.NewLine;

        var result = await UiRoutingHarness.RunBareSyncAsync(
            stdin,
            timeout: TimeSpan.FromSeconds(30));

        Assert.Contains("** Batch / Preflight **", result.Stdout);
        Assert.Contains("** Batch / Execution **", result.Stdout);
        Assert.Contains("Step 1:", result.Stdout);
    }

    [Fact]
    public async Task TP_BATCH_039_QuickExecuteBack_ReturnsToBatchHomeAndMainMenu()
    {
        var stdin = string.Join(
            Environment.NewLine,
            new[]
            {
                "2", "2", "QuickBack", // Batch mode -> Create new batch
                "0",                       // Back from details
                "3", "0",                // Execute batch -> Back
                "0", "0"                 // Back to main -> Exit
            }) + Environment.NewLine;

        var result = await UiRoutingHarness.RunBareSyncAsync(
            stdin,
            timeout: TimeSpan.FromSeconds(10));

        Assert.Contains("** BareSync **", result.Stdout);
        Assert.Contains("** Batch mode **", result.Stdout);
        Assert.Contains("** Batch / Execute **", result.Stdout);
    }

    [Fact]
    public async Task TP_BATCH_041_PurgeBatchIndexes_DeletesIndexAndArtifactsForSelectedBatch()
    {
        var stdin = string.Join(
            Environment.NewLine,
            new[]
            {
                "2",            // main -> batch mode
                "4",            // batch mode -> purge batch indexes
                "1",            // selection menu -> select batch
                "1",            // select first batch
                "0", "0"       // back to main then exit
            }) + Environment.NewLine;

        var sourceIndexPath = string.Empty;
        var destIndexPath = string.Empty;
        var sourceExists = true;
        var sourceWorkExists = true;
        var sourceCheckpointExists = true;
        var destExists = true;
        var destWorkExists = true;
        var destCheckpointExists = true;

        var result = await UiRoutingHarness.RunBareSyncAsync(
            stdin,
            timeout: TimeSpan.FromSeconds(30),
            setup: async appDataRoot =>
            {
                var batchRoot = Path.Combine(appDataRoot, "batches");
                Directory.CreateDirectory(batchRoot);

                sourceIndexPath = Path.Combine(batchRoot, "src-purge.csv");
                destIndexPath = Path.Combine(batchRoot, "dst-purge.csv");

                await File.WriteAllTextAsync(sourceIndexPath, "Id,RelativeDir,FileName,Crc64Hex,SizeBytes,LastWriteTimeUtc,EntryKind\n");
                await File.WriteAllTextAsync(destIndexPath, "Id,RelativeDir,FileName,Crc64Hex,SizeBytes,LastWriteTimeUtc,EntryKind\n");
                await File.WriteAllTextAsync($"{sourceIndexPath}.work", "tmp");
                await File.WriteAllTextAsync($"{sourceIndexPath}.checkpoint", "0");
                await File.WriteAllTextAsync($"{destIndexPath}.work", "tmp");
                await File.WriteAllTextAsync($"{destIndexPath}.checkpoint", "0");

                var payload = new
                {
                    schemaVersion = 0,
                    id = "41414141-4141-4141-4141-414141414141",
                    name = "Purge target",
                    createdUtc = "2026-02-11T00:00:00Z",
                    updatedUtc = "2026-02-11T00:00:00Z",
                    contextSnapshot = new
                    {
                        sourceIndexCsvPath = sourceIndexPath,
                        destIndexCsvPath = destIndexPath
                    },
                    steps = Array.Empty<object>()
                };

                var json = System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(Path.Combine(batchRoot, "tp-batch-041.json"), json);
            },
            capture: appDataRoot =>
            {
                sourceExists = File.Exists(sourceIndexPath);
                sourceWorkExists = File.Exists($"{sourceIndexPath}.work");
                sourceCheckpointExists = File.Exists($"{sourceIndexPath}.checkpoint");
                destExists = File.Exists(destIndexPath);
                destWorkExists = File.Exists($"{destIndexPath}.work");
                destCheckpointExists = File.Exists($"{destIndexPath}.checkpoint");
                return Task.CompletedTask;
            });

        Assert.Contains("** Batch / Purge indexes **", result.Stdout);
        Assert.Contains("Purge indexes done", result.Stdout);

        Assert.False(sourceExists);
        Assert.False(sourceWorkExists);
        Assert.False(sourceCheckpointExists);
        Assert.False(destExists);
        Assert.False(destWorkExists);
        Assert.False(destCheckpointExists);
    }
}
