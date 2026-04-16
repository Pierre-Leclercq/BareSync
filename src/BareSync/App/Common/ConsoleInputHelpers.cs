using System.Text;
using Bare.Primitive.UI;

namespace BareSync.App.Common;

/// <summary>
/// Helper methods for console input operations with support for cancellation and masking.
/// </summary>
public static class ConsoleInputHelpers
{
    /// <summary>
    /// Reads a password from console without echoing characters.
    /// </summary>
    public static string ReadPassword(
        bool maskInput = false,
        IUiInput? uiInput = null,
        IUiKeyInput? keyInput = null,
        Func<bool>? isInputRedirected = null,
        Action<string>? write = null,
        Action<string>? writeLine = null)
    {
        return ReadLineCore(
                   allowEscape: false,
                   maskInput: maskInput,
                   echoInput: false,
                   uiInput,
                   keyInput,
                   isInputRedirected,
                   write,
                   writeLine)
               ?? string.Empty;
    }

    /// <summary>
    /// Reads a line with ESC key support for cancellation. Returns null if ESC is pressed.
    /// </summary>
    public static string? ReadLineWithEscape(
        IUiInput? uiInput = null,
        IUiKeyInput? keyInput = null,
        Func<bool>? isInputRedirected = null,
        Action<string>? write = null,
        Action<string>? writeLine = null)
    {
        return ReadLineCore(
            allowEscape: true,
            maskInput: false,
            echoInput: true,
            uiInput,
            keyInput,
            isInputRedirected,
            write,
            writeLine);
    }

    /// <summary>
    /// Reads a password and allows ESC cancellation. Returns null if ESC is pressed.
    /// </summary>
    public static string? ReadPasswordWithEscape(
        bool maskInput = true,
        IUiInput? uiInput = null,
        IUiKeyInput? keyInput = null,
        Func<bool>? isInputRedirected = null,
        Action<string>? write = null,
        Action<string>? writeLine = null)
    {
        return ReadLineCore(
            allowEscape: true,
            maskInput: maskInput,
            echoInput: false,
            uiInput,
            keyInput,
            isInputRedirected,
            write,
            writeLine);
    }

    /// <summary>
    /// Prompts for a yes/no confirmation and returns the result.
    /// </summary>
    public static bool ConfirmYesNo(
        string prompt,
        IUiInput? uiInput = null,
        IUiKeyInput? keyInput = null,
        Func<bool>? isInputRedirected = null,
        Action<string>? write = null,
        Action<string>? writeLine = null)
    {
        var resolvedInput = uiInput ?? new ConsoleUiInput();
        var resolvedKeyInput = keyInput ?? new ConsoleUiKeyInput();
        var redirected = isInputRedirected ?? (() => Bare.Primitive.UI.UiConsole.IsInputRedirected);
        var resolvedWrite = write ?? Bare.Primitive.UI.UiConsole.Write;
        var resolvedWriteLine = writeLine ?? Bare.Primitive.UI.UiConsole.WriteLine;

        if (redirected())
        {
            while (true)
            {
                resolvedWrite(prompt);
                var input = resolvedInput.ReadLine();
                if (string.IsNullOrWhiteSpace(input))
                {
                    continue;
                }

                var key = input.Trim()[0];
                if (key == 'y' || key == 'Y')
                {
                    return true;
                }

                if (key == 'n' || key == 'N')
                {
                    return false;
                }

                continue;
            }
        }

        resolvedWrite(prompt);
        while (true)
        {
            if (!resolvedKeyInput.TryReadKey(out var keyInfo, intercept: true))
            {
                Thread.Sleep(10);
                continue;
            }

            resolvedWriteLine(string.Empty);
            if (keyInfo.KeyChar == 'y' || keyInfo.KeyChar == 'Y')
            {
                return true;
            }

            if (keyInfo.KeyChar == 'n' || keyInfo.KeyChar == 'N')
            {
                return false;
            }

            resolvedWrite(prompt);
        }
    }

    private static string? ReadLineCore(
        bool allowEscape,
        bool maskInput,
        bool echoInput,
        IUiInput? uiInput,
        IUiKeyInput? keyInput,
        Func<bool>? isInputRedirected,
        Action<string>? write,
        Action<string>? writeLine)
    {
        var resolvedInput = uiInput ?? new ConsoleUiInput();
        var resolvedKeyInput = keyInput ?? new ConsoleUiKeyInput();
        var redirected = isInputRedirected ?? (() => Bare.Primitive.UI.UiConsole.IsInputRedirected);
        var resolvedWrite = write ?? Bare.Primitive.UI.UiConsole.Write;
        var resolvedWriteLine = writeLine ?? Bare.Primitive.UI.UiConsole.WriteLine;

        if (redirected())
        {
            return resolvedInput.ReadLine();
        }

        var builder = new StringBuilder();
        while (true)
        {
            if (!resolvedKeyInput.TryReadKey(out var key, intercept: true))
            {
                Thread.Sleep(10);
                continue;
            }

            if (allowEscape && key.Key == ConsoleKey.Escape)
            {
                resolvedWriteLine(string.Empty);
                return null;
            }

            if (key.Key == ConsoleKey.Enter)
            {
                resolvedWriteLine(string.Empty);
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (builder.Length > 0)
                {
                    builder.Length--;
                    if (echoInput || maskInput)
                    {
                        resolvedWrite("\b \b");
                    }
                }

                continue;
            }

            if (char.IsControl(key.KeyChar))
            {
                continue;
            }

            builder.Append(key.KeyChar);
            if (maskInput)
            {
                resolvedWrite("*");
            }
            else if (echoInput)
            {
                resolvedWrite(key.KeyChar.ToString());
            }
        }

        return builder.ToString();
    }
}