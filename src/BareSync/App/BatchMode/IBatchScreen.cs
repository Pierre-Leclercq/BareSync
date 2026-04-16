using BareSync.Domain;
using BareSync.Infra;

namespace BareSync.App.BatchMode;

/// <summary>
/// Context passed to batch screens.
/// </summary>
internal sealed record BatchScreenContext(
    BatchStorageDescriptor Descriptor,
    BatchStorageLoader Loader,
    string AppDataRoot,
    BareSync.Domain.AppConfig Config,
    IPathPromptService? PathPrompt = null)
{
    public IPathPromptService PathPromptService => PathPrompt ?? new PathPromptService();
}

/// <summary>
/// Interface for batch mode screens.
/// </summary>
internal interface IBatchScreen
{
    /// <summary>
    /// Displays the screen and returns the potentially updated descriptor.
    /// Returns null if the user wants to go back.
    /// </summary>
    BatchStorageDescriptor? Show(BatchScreenContext context);
}