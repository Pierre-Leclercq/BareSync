using BareSync.App.BatchMode;

namespace BareSync.Tests;

/// <summary>
/// Fake/mock implementation of IPathPromptService for testing.
/// </summary>
public sealed class FakePathPromptService : IPathPromptService
{
    private readonly Queue<string> _directoryInputs = new();
    private readonly Queue<string> _fileInputs = new();
    private readonly Queue<string> _sourceIndexInputs = new();
    private readonly Queue<string> _destIndexInputs = new();
    private readonly Queue<bool> _confirmInputs = new();
    public string? LastSourceDefaultFileName { get; private set; }
    public string? LastDestDefaultFileName { get; private set; }
    public bool? LastSourcePreferDefaultFileName { get; private set; }
    public bool? LastDestPreferDefaultFileName { get; private set; }

    public void EnqueueDirectory(string path) => _directoryInputs.Enqueue(path);
    public void EnqueueFile(string path) => _fileInputs.Enqueue(path);
    public void EnqueueSourceIndexPath(string path) => _sourceIndexInputs.Enqueue(path);
    public void EnqueueDestIndexPath(string path) => _destIndexInputs.Enqueue(path);
    public void EnqueueConfirm(bool result) => _confirmInputs.Enqueue(result);

    public string? PickDirectory(string title, string? defaultPath)
    {
        if (_directoryInputs.Count == 0)
        {
            return defaultPath;
        }
        return _directoryInputs.Dequeue();
    }

    public string? PickDefaultSourceIndexCsvPath(
        string title,
        string sourceRoot,
        string? currentValue,
        string defaultFileName,
        bool preferDefaultFileName = false)
    {
        LastSourceDefaultFileName = defaultFileName;
        LastSourcePreferDefaultFileName = preferDefaultFileName;
        if (_sourceIndexInputs.Count == 0)
        {
            return preferDefaultFileName
                ? Path.Combine(sourceRoot, defaultFileName)
                : currentValue ?? Path.Combine(sourceRoot, defaultFileName);
        }
        return _sourceIndexInputs.Dequeue();
    }

    public string? PickDefaultDestIndexCsvPath(
        string title,
        string destRoot,
        string? currentValue,
        string defaultFileName,
        bool preferDefaultFileName = false)
    {
        LastDestDefaultFileName = defaultFileName;
        LastDestPreferDefaultFileName = preferDefaultFileName;
        if (_destIndexInputs.Count == 0)
        {
            return preferDefaultFileName
                ? Path.Combine(destRoot, defaultFileName)
                : currentValue ?? Path.Combine(destRoot, defaultFileName);
        }
        return _destIndexInputs.Dequeue();
    }

    public string? PickFilePath(string title, string? currentValue, string defaultFileName)
    {
        if (_fileInputs.Count == 0)
        {
            return currentValue;
        }
        return _fileInputs.Dequeue();
    }

    public void Clear()
    {
        _directoryInputs.Clear();
        _fileInputs.Clear();
        _sourceIndexInputs.Clear();
        _destIndexInputs.Clear();
        _confirmInputs.Clear();
        LastSourceDefaultFileName = null;
        LastDestDefaultFileName = null;
        LastSourcePreferDefaultFileName = null;
        LastDestPreferDefaultFileName = null;
    }
}
