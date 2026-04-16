using Bare.Primitive.UI;
using BareSync.Domain;
using BareSync.UI;

namespace BareSync.Tests;

public sealed class ScreenRendererUiOutputTests
{
    [Fact]
    public void Render_MainMenuScreen_WritesExpectedLinesToInMemoryUiOutput()
    {
        var config = new AppConfig
        {
            SourceRoot = @"C:\Source",
            MirrorRoot = @"D:\Mirror",
            Mirror = true
        };

        var options = new[]
        {
            "Interactive mode",
            "Batch mode"
        };

        var screen = new MainMenuScreen(config, options, "Exit");
        var output = new InMemoryUiOutput(width: 100, height: 20);

        ScreenRenderer.Render(screen, output);

        var lines = output.GetLines();
        Assert.Contains(lines, line => line.Contains("** BareSync **", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("Source = 'C:\\Source'", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("Mirror = 'D:\\Mirror'", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("Mirror Mode = 'ON'", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("1. Interactive mode", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("2. Batch mode", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("0. Exit", StringComparison.Ordinal));
    }

    [Fact]
    public void Render_WithSmallWidth_ClipsLinesWithoutThrowing()
    {
        var config = new AppConfig
        {
            SourceRoot = @"C:\Very\Long\Path\That\Will\Be\Clipped",
            MirrorRoot = @"D:\Another\Long\Path",
            Mirror = false
        };

        var screen = new MainMenuScreen(config, new[] { "Option one" }, "Back");
        var output = new InMemoryUiOutput(width: 16, height: 10);

        ScreenRenderer.Render(screen, output);

        foreach (var line in output.GetLines())
        {
            Assert.Equal(16, line.Length);
        }
    }

    [Fact]
    public void ConsoleMenu_Prompt_WithUiOutputAndInjectedWrite_UsesUiOutputAndReturnsSelection()
    {
        var config = new AppConfig
        {
            SourceRoot = @"C:\Source",
            MirrorRoot = @"D:\Mirror",
            Mirror = false
        };

        var output = new InMemoryUiOutput(width: 80, height: 20);
        var promptBuffer = new System.Text.StringBuilder();

        var selection = ConsoleMenu.Prompt(
            config,
            options: new[] { "Interactive mode", "Batch mode" },
            zeroLabel: "Exit",
            menuStatus: null,
            uiInput: new ScriptedUiInput(new[] { "1" }),
            keyInput: new ScriptedUiKeyInput(Array.Empty<ConsoleKeyInfo>()),
            isInputRedirected: () => true,
            uiOutput: output,
            write: text => promptBuffer.Append(text));

        var lines = output.GetLines();
        Assert.Contains(lines, line => line.Contains("** BareSync **", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("1. Interactive mode", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("2. Batch mode", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("0. Exit", StringComparison.Ordinal));
        Assert.Equal("Select an option: ", promptBuffer.ToString());
        Assert.Equal(1, selection);
    }
}