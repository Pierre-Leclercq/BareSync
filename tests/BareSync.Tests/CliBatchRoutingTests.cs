using System.Text.Json;
using BareSync.Domain;
using Xunit;

namespace BareSync.Tests;

public sealed class CliBatchRoutingTests
{
    [Fact]
    public async Task CLI_BATCH_NoArgs_KeepsMainMenuBehavior()
    {
        var stdin = string.Join(Environment.NewLine, new[] { "0" }) + Environment.NewLine;
        var result = await UiRoutingHarness.RunBareSyncAsync(
            stdin,
            timeout: TimeSpan.FromSeconds(10));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("** BareSync **", result.Stdout);
    }

    [Fact]
    public async Task CLI_BATCH_InvalidArgumentFormat_ReturnsExitCode1()
    {
        var result = await UiRoutingHarness.RunBareSyncAsync(
            stdin: string.Empty,
            timeout: TimeSpan.FromSeconds(10),
            arguments: "/BATCH:\"\"".Replace("\\\"", "\""));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Invalid batch argument", result.Stdout);
    }

    [Fact]
    public async Task CLI_BATCH_NotFound_ShowsSummaryAndFail()
    {
        var result = await UiRoutingHarness.RunBareSyncAsync(
            stdin: string.Empty,
            timeout: TimeSpan.FromSeconds(10),
            arguments: "/BATCH:\"MissingBatch\"".Replace("\\\"", "\""));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("** Batch CLI Summary **", result.Stdout);
        Assert.Contains("MissingBatch", result.Stdout);
        Assert.Contains("Fail", result.Stdout);
    }

    [Fact]
    public async Task CLI_BATCH_SecretRequired_VaultUnavailable_FailsBatch()
    {
        var result = await UiRoutingHarness.RunBareSyncAsync(
            stdin: string.Empty,
            timeout: TimeSpan.FromSeconds(20),
            arguments: "/BATCH:\"SecretCli\"".Replace("\\\"", "\""),
            environmentOverrides: new Dictionary<string, string?>
            {
                ["BARESYNC_DISABLE_SECRET_STORE"] = "1"
            },
            setup: async appDataRoot =>
            {
                var batchRoot = Path.Combine(appDataRoot, "batches");
                Directory.CreateDirectory(batchRoot);

                var payload = new
                {
                    schemaVersion = 0,
                    id = "53535353-5353-5353-5353-535353535353",
                    name = "SecretCli",
                    createdUtc = "2026-02-09T10:00:00Z",
                    updatedUtc = "2026-02-09T10:00:00Z",
                    contextSnapshot = new
                    {
                        sourceRoot = "D:/Data/Source",
                        sourceIndexCsvPath = "D:/Data/Index/source.csv",
                        encryptedOutputRoot = "D:/Vault/CLI"
                    },
                    steps = new object[]
                    {
                        new
                        {
                            operationType = BatchOperationCatalog.OperationTypeRefreshEncryptedFolder,
                            operationParams = new { values = new { } },
                            contextOverrides = new { }
                        }
                    }
                };

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(Path.Combine(batchRoot, "cli-secret.json"), json);
            });

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("** Batch CLI Summary **", result.Stdout);
        Assert.Contains("SecretCli", result.Stdout);
        Assert.Contains("Vault indisponible", result.Stdout);
    }

    [Fact]
    public async Task CLI_EXTRACT_MissingSource_FailsWithNotFound()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), "BareSync_Missing_" + Guid.NewGuid().ToString("N"));
        var result = await UiRoutingHarness.RunBareSyncAsync(
            stdin: string.Empty,
            timeout: TimeSpan.FromSeconds(10),
            arguments: $"/EXTRACT:\"{missingPath}\"",
            environmentOverrides: new Dictionary<string, string?>
            {
                ["BARESYNC_DISABLE_SECRET_STORE"] = "1"
            });

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Extract source not found", result.Stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CLI_EXTRACT_CombinedWithBatch_FailsFast()
    {
        var result = await UiRoutingHarness.RunBareSyncAsync(
            stdin: string.Empty,
            timeout: TimeSpan.FromSeconds(10),
            arguments: "/BATCH:\"Any\" /EXTRACT:\"D:/Nope/file.bse\"");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("cannot be combined", result.Stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CLI_EXTRACT_DuplicateArgument_FailsFast()
    {
        var result = await UiRoutingHarness.RunBareSyncAsync(
            stdin: string.Empty,
            timeout: TimeSpan.FromSeconds(10),
            arguments: "/EXTRACT:\"D:/A.bse\" /EXTRACT:\"D:/B.bse\"");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Duplicate /EXTRACT argument", result.Stdout, StringComparison.OrdinalIgnoreCase);
    }
}
