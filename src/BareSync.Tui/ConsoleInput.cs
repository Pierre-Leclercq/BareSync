using Bare.Primitive.UI;

namespace BareSync.Tui;

public static class ConsoleInput
{
    public static int ReadMenuDigit(int min, int max)
    {
        return ReadMenuDigit(min, max, uiInput: null, keyInput: null, isInputRedirected: null);
    }

    public static int ReadMenuDigit(
        int min,
        int max,
        IUiInput? uiInput,
        IUiKeyInput? keyInput,
        Func<bool>? isInputRedirected)
    {
        var resolvedInput = uiInput ?? new ConsoleUiInput();
        var resolvedKeyInput = keyInput ?? new ConsoleUiKeyInput();
        var redirected = isInputRedirected?.Invoke() ?? Bare.Primitive.UI.UiConsole.IsInputRedirected;

        if (redirected)
        {
            while (true)
            {
                var input = resolvedInput.ReadLine();
                if (input is null)
                {
                    return min;
                }

                var trimmed = input.Trim();
                if (trimmed.Length != 1)
                {
                    continue;
                }

                if (TryMapMenuDigit(trimmed[0], min, max, out var selection))
                {
                    return selection;
                }
            }
        }

        while (true)
        {
            if (!resolvedKeyInput.TryReadKey(out var keyInfo, intercept: true))
            {
                Thread.Sleep(10);
                continue;
            }

            if (TryMapMenuDigit(keyInfo.KeyChar, min, max, out var selection))
            {
                Bare.Primitive.UI.UiConsole.WriteLine();
                return selection;
            }
        }
    }

    internal static bool TryMapMenuDigit(char keyChar, int min, int max, out int selection)
    {
        selection = 0;
        if (keyChar == '\u001b')
        {
            return min == 0;
        }

        if (keyChar < '0' || keyChar > '9')
        {
            return false;
        }

        var value = keyChar - '0';
        if (value < min || value > max)
        {
            return false;
        }

        selection = value;
        return true;
    }
}
