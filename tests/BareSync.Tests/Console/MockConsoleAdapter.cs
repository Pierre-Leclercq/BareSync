using System.Text;

namespace BareSync.Tests;

internal sealed class MockConsoleAdapter : IConsoleAdapter
{
    private sealed class QueueTextReader : TextReader
    {
        private readonly Func<string?> _readLine;

        public QueueTextReader(Func<string?> readLine)
        {
            _readLine = readLine;
        }

        public override string? ReadLine()
        {
            return _readLine();
        }
    }

    private readonly Queue<string> _inputLines = new();
    private readonly Queue<ConsoleKeyInfo> _keyInputs = new();
    private readonly StringBuilder _output = new();
    private readonly StringWriter _writer;
    private readonly TextReader _reader;

    public MockConsoleAdapter(int bufferWidth = 120, int windowWidth = 120)
    {
        BufferWidth = bufferWidth;
        WindowWidth = windowWidth;
        _writer = new StringWriter(_output);
        _reader = new QueueTextReader(ReadLine);
        Out = _writer;
        Error = _writer;
        In = _reader;
    }

    public TextWriter Out { get; }
    public TextWriter Error { get; }
    public TextReader In { get; }
    public int CursorTop { get; set; }
    public bool CursorVisible { get; set; } = true;
    public int WindowWidth { get; }
    public int BufferWidth { get; }

    public string OutputText => _output.ToString();

    public void EnqueueLine(string line)
    {
        _inputLines.Enqueue(line);
    }

    public void EnqueueKey(ConsoleKeyInfo key)
    {
        _keyInputs.Enqueue(key);
    }

    public void Write(string text)
    {
        Out.Write(text);
    }

    public void WriteLine(string text)
    {
        Out.WriteLine(text);
    }

    public string? ReadLine()
    {
        return _inputLines.Count == 0 ? null : _inputLines.Dequeue();
    }

    public bool KeyAvailable => _keyInputs.Count > 0;

    public ConsoleKeyInfo ReadKey(bool intercept = false)
    {
        return _keyInputs.Count == 0 ? default : _keyInputs.Dequeue();
    }

    public void SetCursorPosition(int left, int top)
    {
        CursorTop = top;
    }

    public void Clear()
    {
    }
}
