using BareSync.Domain;
using Xunit;

namespace BareSync.Tests;

public sealed class UiRoutingTpBatch040Tests
{
    [Fact]
    public async Task TP_BATCH_040_PreflightConfirmDeclined_ReturnsToPreflightWithoutExecution()
    {
        // Use dynamic digit for OneWaySyncApply (requires confirmation)
        var applyDigit = TestMenuDigits.DigitForOperationString(BatchOperationCatalog.OperationTypeOneWaySyncApply);
        
        var stdin = string.Join(
                Environment.NewLine,
                new[]
                {
                    "2", "2", "NeedConfirm",
                    "4", "1", applyDigit,
                    "2", "S", "3", "S", "4", "S", "SRC.csv", "5", "S", "DST.csv", "0",
                    "0",
                    "7", // Details -> Run
                    "1", // Preflight -> Confirm & run
                    "n", // Proceed (No!)
                    "2", // Back to details
                    "0", // Back to list
                    "0", "0"
                }) + Environment.NewLine;

        var result = await UiRoutingHarness.RunBareSyncAsync(
            stdin,
            timeout: TimeSpan.FromSeconds(30));

        Assert.Contains("** Batch / Preflight **", result.Stdout);
        Assert.Contains("Proceed? (y/n):", result.Stdout);
        Assert.DoesNotContain("** Batch / Execution **", result.Stdout);
    }
}
