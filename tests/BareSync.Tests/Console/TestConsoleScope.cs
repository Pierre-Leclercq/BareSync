namespace BareSync.Tests;

internal sealed class TestConsoleScope : IDisposable
{
    private readonly TextWriter _originalOut;
    private readonly TextWriter _originalError;
    private readonly TextReader _originalIn;

    public TestConsoleScope(IConsoleAdapter console)
    {
        _originalOut = Console.Out;
        _originalError = Console.Error;
        _originalIn = Console.In;

        Console.SetOut(console.Out);
        Console.SetError(console.Error);
        Console.SetIn(console.In);
    }

    public void Dispose()
    {
        Console.SetOut(_originalOut);
        Console.SetError(_originalError);
        Console.SetIn(_originalIn);
    }
}
