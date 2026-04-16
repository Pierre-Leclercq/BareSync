namespace BareSync.UI;

public interface IConsoleUi
{
    void Clear();
    void Write(string text);
    void WriteLine(string text);
    string? ReadLine();
    bool KeyAvailable { get; }
    ConsoleKeyInfo ReadKey(bool intercept = false);
}