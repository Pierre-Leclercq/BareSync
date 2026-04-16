using System.Globalization;
using System.Text;

namespace BareSync.Infra;

internal sealed class SimpleFileLogger : IDisposable
{
    private readonly string _fullLogPath;
    private readonly int _flushEveryLines;
    private readonly List<string> _buffer = new();
    private readonly object _lock = new();
    private readonly Encoding _encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public bool IsDebugEnabled { get; set; } = false;

    public string FullLogPath => _fullLogPath;

    public SimpleFileLogger(string fullLogPath, int flushEveryLines = 100)
    {
        _fullLogPath = fullLogPath;
        _flushEveryLines = flushEveryLines <= 0 ? 1 : flushEveryLines;
    }

    public void Info(string message)
    {
        Write("INFO", message);
    }

    public void Warn(string message)
    {
        Write("WARN", message);
    }

    public void Error(string message)
    {
        Write("ERROR", message);
    }

    public void Error(string message, Exception ex)
    {
        var sb = new StringBuilder();
        sb.AppendLine(message);
        sb.AppendLine($"Exception: {ex.GetType().FullName}: {ex.Message}");
        sb.AppendLine($"StackTrace: {ex.StackTrace}");
        
        if (ex.InnerException is not null)
        {
            sb.AppendLine($"InnerException: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}");
        }
        
        Write("ERROR", sb.ToString());
    }

    public void Debug(string message)
    {
        if (IsDebugEnabled)
        {
            Write("DEBUG", message);
        }
    }

    public void Flush()
    {
        lock (_lock)
        {
            FlushUnderLock();
        }
    }

    public void Dispose()
    {
        Flush();
    }

    private void Write(string level, string message)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        var line = $"{timestamp} [{level}] {message ?? string.Empty}";

        lock (_lock)
        {
            _buffer.Add(line);
            if (_buffer.Count >= _flushEveryLines)
            {
                FlushUnderLock();
            }
        }
    }

    private void FlushUnderLock()
    {
        if (_buffer.Count == 0)
        {
            return;
        }

        using var stream = new FileStream(
            _fullLogPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read);
        using var writer = new StreamWriter(stream, _encoding);

        foreach (var line in _buffer)
        {
            writer.WriteLine(line);
        }

        _buffer.Clear();
    }
}
