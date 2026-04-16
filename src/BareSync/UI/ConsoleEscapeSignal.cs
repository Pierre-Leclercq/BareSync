using Bare.Primitive.UI;

namespace BareSync.UI;

internal sealed class ConsoleEscapeSignal : IEscapeSignal
{
    private const int CancelPollIntervalMs = 50;
    private readonly IUiKeyInput _keyInput;
    private readonly Func<bool> _isInputRedirected;

    public static readonly ConsoleEscapeSignal Instance = new();

    internal ConsoleEscapeSignal(
        IUiKeyInput? keyInput = null,
        Func<bool>? isInputRedirected = null)
    {
        _keyInput = keyInput ?? new ConsoleUiKeyInput();
        _isInputRedirected = isInputRedirected ?? (() => Bare.Primitive.UI.UiConsole.IsInputRedirected);
    }

    public Task WaitForEscapeAsync(CancellationToken cancellationToken)
    {
        if (_isInputRedirected() && Bare.Primitive.UI.UiConsole.In is not StringReader)
        {
            return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        if (_isInputRedirected())
        {
            return WatchRedirectedInputAsync(cancellationToken);
        }

        return WatchConsoleKeyAsync(_keyInput, cancellationToken);
    }

    private static async Task WatchConsoleKeyAsync(IUiKeyInput keyInput, CancellationToken stopToken)
    {
        while (!stopToken.IsCancellationRequested)
        {
            if (!keyInput.TryReadKey(out var keyInfo, intercept: true))
            {
                await Task.Delay(CancelPollIntervalMs, stopToken).ConfigureAwait(false);
                continue;
            }

            if (keyInfo.Key == ConsoleKey.Escape)
            {
                return;
            }
        }
        
        stopToken.ThrowIfCancellationRequested();
    }

    private static async Task WatchRedirectedInputAsync(CancellationToken stopToken)
    {
        while (!stopToken.IsCancellationRequested)
        {
            int peek;
            try
            {
                peek = Bare.Primitive.UI.UiConsole.In.Peek();
            }
            catch
            {
                break;
            }

            if (peek < 0)
            {
                await Task.Delay(CancelPollIntervalMs, stopToken).ConfigureAwait(false);
                continue;
            }

            var value = Bare.Primitive.UI.UiConsole.In.Read();
            if (value == 27)
            {
                return;
            }
        }
        
        stopToken.ThrowIfCancellationRequested();
    }
}
