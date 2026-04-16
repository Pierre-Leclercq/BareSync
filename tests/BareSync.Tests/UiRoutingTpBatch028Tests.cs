using System.Text.Json;
using BareSync.Domain;
using Xunit;

namespace BareSync.Tests;

public sealed class UiRoutingTpBatch028Tests
{
    [Fact]
    public async Task TP_BATCH_028_SecretPromptSingleScope_RequestsPasswordOnceAndStartsExecution()
    {
        var stdin = string.Join(
                Environment.NewLine,
                new[]
                {
                    "2",
                    "1",
                    "1",
                    "1",
                    "7",
                    "1",
                    "y",
                    "secret",
                    "2",
                    "0",
                    "0",
                    "0"
                }) + Environment.NewLine;

        var result = await UiRoutingHarness.RunBareSyncAsync(
            stdin,
            timeout: TimeSpan.FromSeconds(30),
            setup: async appDataRoot =>
            {
                var batchRoot = Path.Combine(appDataRoot, "batches");
                Directory.CreateDirectory(batchRoot);

                var payload = new
                {
                    schemaVersion = 0,
                    id = "28282828-2828-2828-2828-282828282828",
                    name = "Secret run",
                    createdUtc = "2026-02-09T10:00:00Z",
                    updatedUtc = "2026-02-09T10:00:00Z",
                    contextSnapshot = new
                    {
                        sourceRoot = "D:/Data/Source",
                        sourceIndexCsvPath = "D:/Data/Index/source.csv",
                        encryptedOutputRoot = "D:/Vault/A",
                        restoreRoot = "D:/Restore/A"
                    },
                    steps = new object[]
                    {
                        new
                        {
                            operationType = BatchOperationCatalog.OperationTypeRefreshEncryptedFolder,
                            operationParams = new { values = new { } },
                            contextOverrides = new { }
                        },
                        new
                        {
                            operationType = BatchOperationCatalog.OperationTypeRestoreEncryptedFiles,
                            operationParams = new { values = new { } },
                            contextOverrides = new { }
                        }
                    }
                };

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(Path.Combine(batchRoot, "tp-batch-028.json"), json);
            });

        Assert.Contains("** Batch / Preflight **", result.Stdout);
        Assert.Contains("requiresSecret=yes", result.Stdout);
        Assert.Contains("Secret required: EncryptionPassword for scope D:/Vault/A", result.Stdout);
        Assert.Equal(1, CountOccurrences(result.Stdout, "Enter password (will not be echoed):"));
        Assert.Contains("** Batch / Execution **", result.Stdout);
    }

    private static int CountOccurrences(string text, string value)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(value))
        {
            return 0;
        }

        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
