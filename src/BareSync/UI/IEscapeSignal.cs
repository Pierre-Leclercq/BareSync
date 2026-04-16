namespace BareSync.UI;

internal interface IEscapeSignal
{
    Task WaitForEscapeAsync(CancellationToken cancellationToken);
}
