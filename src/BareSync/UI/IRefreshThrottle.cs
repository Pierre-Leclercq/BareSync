namespace BareSync.UI;

public interface IRefreshThrottle
{
    bool ShouldRefresh(int processed, int total, bool force = false);
    bool IsHeartbeatDue();
}
