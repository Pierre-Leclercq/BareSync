using System.Text.Json;
using BareSync.Domain;
using Xunit;

namespace BareSync.Tests;

public sealed class UiRoutingTpBatch027Tests
{
    [Fact]
    public async Task TP_BATCH_027_SecretPromptCancel_ReturnsToPreflightWithoutExecution()
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
                    string.Empty,
                    "2",
                    "0",
                    "0",
                    "0"
                }) + Environment.NewLine;

        var result = await UiRoutingHarness.RunBareSyncAsync(
            stdin,
            timeout: TimeSpan.FromSeconds(20),
            setup: async appDataRoot =>
            {
                var batchRoot = Path.Combine(appDataRoot, "batches");
                Directory.CreateDirectory(batchRoot);

                var payload = new
                {
                    schemaVersion = 0,
                    id = "77777777-7777-7777-7777-777777777777",
                    name = "Secret cancel",
                    createdUtc = "2026-02-09T10:00:00Z",
                    updatedUtc = "2026-02-09T10:00:00Z",
                    contextSnapshot = new
                    {
                        sourceRoot = "D:/Data/Source",
                        sourceIndexCsvPath = "D:/Data/Index/source.csv",
                        encryptedOutputRoot = "D:/Vault/A"
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
                await File.WriteAllTextAsync(Path.Combine(batchRoot, "tp-batch-027.json"), json);
            });

        Assert.Contains("** Batch / Preflight **", result.Stdout);
        Assert.Contains("Secret required: EncryptionPassword for scope D:/Vault/A", result.Stdout);
        Assert.Contains("Enter password (will not be echoed):", result.Stdout);
        Assert.Contains("Last status: Canceled", result.Stdout);
        Assert.DoesNotContain("** Batch / Execution **", result.Stdout);
    }
}
