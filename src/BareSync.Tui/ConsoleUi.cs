namespace BareSync.Tui;

public static class ConsoleUi
{
    private static bool _skipNextClear;

    public static void SkipNextClear()
    {
        _skipNextClear = true;
    }

    public static void Clear()
    {
        if (_skipNextClear)
        {
            _skipNextClear = false;
            return;
        }

        if (Bare.Primitive.UI.UiConsole.IsOutputRedirected)
        {
            return;
        }

        Bare.Primitive.UI.UiConsole.Clear();
    }
}
