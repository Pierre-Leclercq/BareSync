
namespace BareSync.UI;

internal sealed class ConsoleUiAdapter : IConsoleUi
{
    public void Clear()
    {
        ConsoleUi.Clear();
    }

    public void Write(string text)
    {
        Bare.Primitive.UI.UiConsole.Write(text);
    }

    public void WriteLine(string text)
    {
        Bare.Primitive.UI.UiConsole.WriteLine(text);
    }

    public string? ReadLine()
    {
        return Bare.Primitive.UI.UiConsole.ReadLine();
    }

    public bool KeyAvailable => Bare.Primitive.UI.UiConsole.KeyAvailable;

    public ConsoleKeyInfo ReadKey(bool intercept = false)
    {
        return Bare.Primitive.UI.UiConsole.ReadKey(intercept);
    }
}