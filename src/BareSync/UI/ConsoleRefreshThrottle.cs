using System.Diagnostics;

namespace BareSync.UI;

public sealed class ConsoleRefreshThrottle : IRefreshThrottle
{
    public const int Step = 250;
    public static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(200);
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(1);

    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private int _lastProcessed = -1;
    private int _lastTotal = -1;

    public bool ShouldRefresh(int processed, int total, bool force = false)
    {
        if (force)
        {
            AcceptRefresh(processed, total);
            return true;
        }

        var heartbeatHit = _stopwatch.Elapsed >= HeartbeatInterval;

        if (heartbeatHit)
        {
            AcceptRefresh(processed, total);
            return true;
        }

        if (processed == _lastProcessed && total == _lastTotal)
        {
            return false;
        }

        if (processed == 0)
        {
            AcceptRefresh(processed, total);
            return true;
        }

        if (total >= 0 && processed == total)
        {
            AcceptRefresh(processed, total);
            return true;
        }

        var stepHit = Step > 0 && processed % Step == 0;
        var intervalHit = _stopwatch.Elapsed >= MinInterval;

        if ((stepHit || intervalHit) && (processed != _lastProcessed || total != _lastTotal))
        {
            AcceptRefresh(processed, total);
            return true;
        }

        return false;
    }

    private void AcceptRefresh(int processed, int total)
    {
        _lastProcessed = processed;
        _lastTotal = total;
        _stopwatch.Restart();
    }

    public bool IsHeartbeatDue()
    {
        return _stopwatch.Elapsed >= HeartbeatInterval;
    }
}
