namespace BareSync.Tests;

internal interface IConsoleAdapter
{
    TextWriter Out { get; }
    TextWriter Error { get; }
    TextReader In { get; }
    void Write(string text);
    void WriteLine(string text);
    string? ReadLine();
    bool KeyAvailable { get; }
    ConsoleKeyInfo ReadKey(bool intercept = false);
    int CursorTop { get; set; }
    bool CursorVisible { get; set; }
    int WindowWidth { get; }
    int BufferWidth { get; }
    void SetCursorPosition(int left, int top);
    void Clear();
}
