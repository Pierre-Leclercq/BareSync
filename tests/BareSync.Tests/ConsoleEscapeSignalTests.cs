using Bare.Primitive.UI;
using BareSync.UI;

namespace BareSync.Tests;

public sealed class ConsoleEscapeSignalTests
{
    [Fact]
    public async Task WaitForEscapeAsync_WhenNotRedirected_CompletesOnEscapeKey()
    {
        var signal = new ConsoleEscapeSignal(
            keyInput: new ScriptedUiKeyInput(new[]
            {
                new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false),
                new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, false, false, false)
            }),
            isInputRedirected: () => false);

        await signal.WaitForEscapeAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task WaitForEscapeAsync_WhenNotRedirected_StopsOnCancellation()
    {
        var signal = new ConsoleEscapeSignal(
            keyInput: new ScriptedUiKeyInput(Array.Empty<ConsoleKeyInfo>()),
            isInputRedirected: () => false);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => signal.WaitForEscapeAsync(cts.Token));
    }

    [Fact]
    public async Task WaitForEscapeAsync_WhenRedirectedWithoutStringReader_WaitsUntilCanceled()
    {
        var originalIn = Console.In;
        try
        {
            using var stream = new MemoryStream();
            using var reader = new StreamReader(stream);
            Console.SetIn(reader);

            var signal = new ConsoleEscapeSignal(
                keyInput: new ScriptedUiKeyInput(Array.Empty<ConsoleKeyInfo>()),
                isInputRedirected: () => true);

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => signal.WaitForEscapeAsync(cts.Token));
        }
        finally
        {
            Console.SetIn(originalIn);
        }
    }

    [Fact]
    public async Task WaitForEscapeAsync_WhenRedirected_IgnoresKeyInputAndWaitsUntilCanceled()
    {
        var originalIn = Console.In;
        try
        {
            using var stream = new MemoryStream();
            using var reader = new StreamReader(stream);
            Console.SetIn(reader);

            var signal = new ConsoleEscapeSignal(
                keyInput: new ScriptedUiKeyInput(new[]
                {
                    new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, false, false, false)
                }),
                isInputRedirected: () => true);

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => signal.WaitForEscapeAsync(cts.Token));
        }
        finally
        {
            Console.SetIn(originalIn);
        }
    }
}