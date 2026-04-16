using Bare.Infrastructure.Controls;
using Bare.Primitive.UI;

namespace BareSync.UI;

internal static class ScreenRenderer
{
    public static void Render(IScreen screen, IUiOutput? uiOutput = null)
    {
        if (uiOutput is not null)
        {
            RenderToUiOutput(screen, uiOutput);
            return;
        }

        RenderToConsole(screen);
    }

    private static void RenderToConsole(IScreen screen)
    {
        if (screen.ShouldClearBeforeRender)
        {
            UiInteraction.Clear();
        }

        var model = screen.Build();

        Bare.Primitive.UI.UiConsole.WriteLine(model.Header);
        Bare.Primitive.UI.UiConsole.WriteLine();

        foreach (var line in model.BodyLines)
        {
            Bare.Primitive.UI.UiConsole.WriteLine(line);
        }

        Bare.Primitive.UI.UiConsole.WriteLine();

        foreach (var line in model.FooterLines)
        {
            Bare.Primitive.UI.UiConsole.WriteLine(line);
        }
    }

    private static void RenderToUiOutput(IScreen screen, IUiOutput uiOutput)
    {
        if (screen.ShouldClearBeforeRender)
        {
            uiOutput.Clear();
        }

        var model = screen.Build();
        var lines = BuildLines(model);
        var width = Math.Max(1, uiOutput.Width);
        var height = Math.Max(1, uiOutput.Height);
        var surfaceHeight = Math.Min(height, Math.Max(1, lines.Count));

        var surface = new TextSurface(width, surfaceHeight);
        for (var index = 0; index < surfaceHeight; index++)
        {
            surface.SetText(0, index, UiText.Clip(lines[index], width));
        }

        surface.RenderTo(uiOutput);
    }

    private static List<string> BuildLines(ScreenModel model)
    {
        var lines = new List<string>
        {
            model.Header,
            string.Empty
        };

        lines.AddRange(model.BodyLines);
        lines.Add(string.Empty);
        lines.AddRange(model.FooterLines);

        return lines;
    }
}
