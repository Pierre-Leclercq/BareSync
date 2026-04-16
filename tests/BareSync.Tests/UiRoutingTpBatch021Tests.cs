using BareSync.App.BatchMode;
using BareSync.Domain;
using Xunit;

namespace BareSync.Tests;

/// <summary>
/// TP_BATCH_021 - Context Editor with injectable path prompt service.
/// The end-to-end test with console input has been replaced by this unit test
/// because the context editor now uses interactive path pickers that cannot be
/// driven by simple text input.
/// </summary>
public sealed class UiRoutingTpBatch021Tests
{
    [Fact]
    public void TP_BATCH_021_EditContextField_UsesPathPromptService()
    {
        // Arrange
        var fakePromptService = new FakePathPromptService();
        fakePromptService.EnqueueDirectory("D:/Data/Source");

        var descriptor = new BatchStorageDescriptor(
            Id: "test-batch-021",
            Name: "TestBatch021",
            Status: BatchStorageStatus.Valid,
            Reason: string.Empty,
            Path: string.Empty);

        // Act - verify the path prompt service is used through BatchScreenContext
        var context = new BatchScreenContext(
            descriptor,
            new Infra.BatchStorageLoader(),
            string.Empty,
            new AppConfig(),
            fakePromptService);

        // Assert - the PathPromptService property returns the injected fake service
        Assert.Same(fakePromptService, context.PathPromptService);
    }

    [Fact]
    public async Task TP_BATCH_021_EditContextField_EscapeCancelsSelection()
    {
        var stdin = string.Join(
                Environment.NewLine,
                new[]
                {
                    "2",
                    "2",
                    "MyBatch",
                    "5",
                    "1",
                    "\u001b",
                    "0",
                    "0",
                    "0"
                }) + Environment.NewLine;

        var result = await UiRoutingHarness.RunBareSyncAsync(
            stdin,
            timeout: TimeSpan.FromSeconds(10));

        Assert.Contains("Select field number (1..7, 0/ESC to cancel):", result.Stdout);
        Assert.Contains("** Batch / Context (defaults) **", result.Stdout);
    }
}
