namespace BareSync.Tests;

public sealed class MockConsoleAdapterTests
{
    [Fact]
    public void MockConsoleAdapter_CapturesOutput()
    {
        var mock = new MockConsoleAdapter();

        mock.WriteLine("hello");
        mock.Write("world");

        Assert.Contains("hello", mock.OutputText, StringComparison.Ordinal);
        Assert.Contains("world", mock.OutputText, StringComparison.Ordinal);
    }

    [Fact]
    public void MockConsoleAdapter_ReadLineDequeues()
    {
        var mock = new MockConsoleAdapter();
        mock.EnqueueLine("first");
        mock.EnqueueLine("second");

        Assert.Equal("first", mock.ReadLine());
        Assert.Equal("second", mock.ReadLine());
        Assert.Null(mock.ReadLine());
    }

    [Fact]
    public void MockConsoleAdapter_CursorApis_DoNotThrow()
    {
        var mock = new MockConsoleAdapter();
        mock.CursorTop = 5;
        mock.SetCursorPosition(0, 10);
        mock.CursorVisible = false;
        mock.Clear();

        Assert.Equal(10, mock.CursorTop);
        Assert.False(mock.CursorVisible);
    }
}
