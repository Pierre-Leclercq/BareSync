using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ILogger = BareSync.Infra.SimpleFileLogger;

namespace BareSync.App.Common;

internal static class PowerInhibitor
{
    public static IDisposable AcquireSleepInhibitLease(ILogger? logger = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return NoOpLease.Instance;
        }

        return AcquireWindowsSleepLease(logger);
    }

    [SupportedOSPlatform("windows")]
    private static IDisposable AcquireWindowsSleepLease(ILogger? logger)
    {
        var result = WindowsNativeMethods.SetThreadExecutionState(
            WindowsNativeMethods.ExecutionState.ES_CONTINUOUS
            | WindowsNativeMethods.ExecutionState.ES_SYSTEM_REQUIRED);

        if (result == default)
        {
            logger?.Warn("Sleep inhibition request failed.");
            return NoOpLease.Instance;
        }

        logger?.Info("Sleep inhibition acquired.");
        return new WindowsSleepLease(logger);
    }

    private sealed class NoOpLease : IDisposable
    {
        internal static readonly NoOpLease Instance = new();

        public void Dispose()
        {
        }
    }

    [SupportedOSPlatform("windows")]
    private sealed class WindowsSleepLease : IDisposable
    {
        private readonly ILogger? _logger;
        private int _disposed;

        public WindowsSleepLease(ILogger? logger)
        {
            _logger = logger;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            var result = WindowsNativeMethods.SetThreadExecutionState(
                WindowsNativeMethods.ExecutionState.ES_CONTINUOUS);

            if (result == default)
            {
                _logger?.Warn("Sleep inhibition release failed.");
                return;
            }

            _logger?.Info("Sleep inhibition released.");
        }
    }

    [SupportedOSPlatform("windows")]
    private static class WindowsNativeMethods
    {
        [Flags]
        internal enum ExecutionState : uint
        {
            ES_SYSTEM_REQUIRED = 0x00000001,
            ES_CONTINUOUS = 0x80000000
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern ExecutionState SetThreadExecutionState(ExecutionState esFlags);
    }
}