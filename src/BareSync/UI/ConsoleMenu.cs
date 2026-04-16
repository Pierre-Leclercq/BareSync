using BareSync.Domain;
using Bare.Primitive.UI;

namespace BareSync.UI;

internal static class ConsoleMenu
{
    public static int Prompt(
        AppConfig config,
        IReadOnlyList<string> options,
        string zeroLabel = "Exit",
        MenuStatus? menuStatus = null,
        IUiInput? uiInput = null,
        IUiKeyInput? keyInput = null,
        Func<bool>? isInputRedirected = null,
        IUiOutput? uiOutput = null,
        Action<string>? write = null)
    {
        var footerLines = BuildFooterLines(menuStatus);
        var screen = new MainMenuScreen(config, options, zeroLabel, footerLines);
        ScreenRenderer.Render(screen, uiOutput);
        var resolvedWrite = write ?? Bare.Primitive.UI.UiConsole.Write;
        resolvedWrite("Select an option: ");
        return UiInteraction.ReadMenuDigit(
            0,
            options.Count,
            uiInput,
            keyInput,
            isInputRedirected);
    }

    private static IReadOnlyList<string>? BuildFooterLines(MenuStatus? menuStatus)
    {
        if (menuStatus is null)
        {
            return null;
        }

        var statusLine = menuStatus.StatusLine;
        if (string.IsNullOrWhiteSpace(statusLine))
        {
            return null;
        }

        var lines = new List<string>
        {
            $"Last status: {statusLine}"
        };

        if (!string.IsNullOrWhiteSpace(menuStatus.LogPath))
        {
            lines.Add($"Log: {menuStatus.LogPath}");
        }

        if (!string.IsNullOrWhiteSpace(menuStatus.ReportPath))
        {
            lines.Add($"Report: {menuStatus.ReportPath}");
        }

        return lines;
    }
}
