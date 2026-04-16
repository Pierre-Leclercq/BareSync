using Bare.Primitive.UI;

namespace BareSync.UI;

/// <summary>
/// Incremental UI interaction facade to reduce direct coupling with legacy TUI helpers.
/// </summary>
internal static class UiInteraction
{
    public static bool IsRenderingEnabled => UiMode.EnableRendering;

    public static void Clear()
    {
        ConsoleUi.Clear();
    }

    public static void SkipNextClear()
    {
        ConsoleUi.SkipNextClear();
    }

    public static int ReadMenuDigit(
        int min,
        int max,
        IUiInput? uiInput = null,
        IUiKeyInput? keyInput = null,
        Func<bool>? isInputRedirected = null)
    {
        var resolvedInput = uiInput ?? new ConsoleUiInput();
        var resolvedKeyInput = keyInput ?? new ConsoleUiKeyInput();
        var redirected = isInputRedirected?.Invoke() ?? UiConsole.IsInputRedirected;

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
                UiConsole.WriteLine();
                return selection;
            }
        }
    }

    private static bool TryMapMenuDigit(char keyChar, int min, int max, out int selection)
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

    public static string? ReadLineWithEscape(
        IUiInput? uiInput = null,
        IUiKeyInput? keyInput = null,
        Func<bool>? isInputRedirected = null)
    {
        var resolvedInput = uiInput ?? new ConsoleUiInput();
        var resolvedKeyInput = keyInput ?? new ConsoleUiKeyInput();
        var redirected = isInputRedirected?.Invoke() ?? UiConsole.IsInputRedirected;

        if (redirected)
        {
            var input = resolvedInput.ReadLine();
            return string.Equals(input, "\u001b", StringComparison.Ordinal) ? null : input;
        }

        var buffer = new System.Text.StringBuilder();

        while (true)
        {
            if (!resolvedKeyInput.TryReadKey(out var keyInfo, intercept: true))
            {
                Thread.Sleep(10);
                continue;
            }

            switch (keyInfo.Key)
            {
                case ConsoleKey.Escape:
                    UiConsole.WriteLine();
                    return null;

                case ConsoleKey.Enter:
                    UiConsole.WriteLine();
                    return buffer.ToString();

                case ConsoleKey.Backspace:
                    if (buffer.Length > 0)
                    {
                        buffer.Length--;
                        UiConsole.Write("\b \b");
                    }
                    break;

                default:
                    // Only accept printable characters (roughly)
                    if (!char.IsControl(keyInfo.KeyChar))
                    {
                        buffer.Append(keyInfo.KeyChar);
                        UiConsole.Write(keyInfo.KeyChar);
                    }
                    break;
            }
        }
    }
}