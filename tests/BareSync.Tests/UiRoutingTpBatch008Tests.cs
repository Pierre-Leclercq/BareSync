using System.Text.Json;
using BareSync.Domain;
using Xunit;

namespace BareSync.Tests;

public sealed class UiRoutingTpBatch008Tests
{
    [Fact]
    public async Task TP_BATCH_008_CreateBatchWithValidStep_ShowsValidStatus()
    {
        var dryRunDigit = TestMenuDigits.DigitForOperationString(BatchOperationCatalog.OperationTypeOneWaySyncDryRun);

        var stdin = string.Join(
                Environment.NewLine,
                new[]
                {
                    "2",
                    "2",
                    "MyBatch",
                    "4",
                    "1",
                    dryRunDigit,
                    "2", "S", 
                    "3", "S", 
                    "4", "S", "SRC.csv",
                    "5", "S", "DST.csv",
                    "0", // Back from StepEditor
                    "0", // Back from StepsEditor
                    "0", // Back from Details
                    "0"  // Back from Home
                }) + Environment.NewLine;

        var result = await UiRoutingHarness.RunBareSyncAsync(
            stdin,
            timeout: TimeSpan.FromSeconds(10));

        Assert.Contains("S2.3", result.ScreenTrace);
        Assert.Contains("Status: Valid", result.Stdout);
        Assert.Contains("Steps: 1", result.Stdout);
        Assert.Equal(0, result.ExitCode);
    }
}
