using System.Text;
using System.Diagnostics;

namespace BareSync.Infra;

internal sealed class SingleInstanceLock : IDisposable
{
    private const string LockOwnerUnknown = "unknown";
    private readonly string _lockPath;
    private readonly FileStream _stream;
    private bool _disposed;

    private SingleInstanceLock(string lockPath, FileStream stream)
    {
        _lockPath = lockPath;
        _stream = stream;
    }

    public static bool TryAcquire(
        string lockPath,
        out SingleInstanceLock? acquired,
        out string? error)
    {
        acquired = null;
        error = null;

        if (string.IsNullOrWhiteSpace(lockPath))
        {
            error = "Lock path is empty.";
            return false;
        }

        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var parent = Path.GetDirectoryName(lockPath);
                if (!string.IsNullOrWhiteSpace(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                var stream = new FileStream(
                    lockPath,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.None);

                var processName = GetCurrentProcessNameSafe();
                var payload =
                    $"{Environment.ProcessId}{Environment.NewLine}" +
                    $"{DateTime.UtcNow:O}{Environment.NewLine}" +
                    $"{processName}{Environment.NewLine}";
                var bytes = Encoding.UTF8.GetBytes(payload);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);

                acquired = new SingleInstanceLock(lockPath, stream);
                return true;
            }
            catch (IOException ex)
            {
                if (!File.Exists(lockPath))
                {
                    error = ex.Message;
                    return false;
                }

                if (!TryCleanupStaleLock(lockPath, out var cleanupError))
                {
                    error = cleanupError ?? ex.Message;
                    return false;
                }

                if (attempt == maxAttempts)
                {
                    error = "Could not acquire single-instance lock after cleaning stale lock.";
                    return false;
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                error = ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        error = "Could not acquire single-instance lock.";
        return false;
    }

    private static bool TryCleanupStaleLock(string lockPath, out string? error)
    {
        error = null;

        if (TryReadLockMetadata(lockPath, out var ownerPid, out var lockCreatedUtc, out var ownerName))
        {
            if (IsLockOwnerStillAlive(ownerPid, lockCreatedUtc, out var ownerStillValid, out var ownerDescription))
            {
                if (ownerStillValid)
                {
                    error = $"Another BareSync process is already running ({ownerDescription}).";
                    return false;
                }
            }

            if (!string.IsNullOrWhiteSpace(ownerName) &&
                !ownerName.Equals(LockOwnerUnknown, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(ownerName, GetCurrentProcessNameSafe(), StringComparison.OrdinalIgnoreCase))
            {
                // If metadata indicates another owner name but process validation did not prove it alive,
                // we still allow stale cleanup because PID/start-time check is the source of truth.
            }
        }

        return TryDeleteLockFile(lockPath, out error);
    }

    private static bool IsLockOwnerStillAlive(
        int ownerPid,
        DateTimeOffset? lockCreatedUtc,
        out bool ownerStillValid,
        out string ownerDescription)
    {
        ownerStillValid = false;
        ownerDescription = $"PID {ownerPid}";

        try
        {
            using var process = Process.GetProcessById(ownerPid);
            if (process.HasExited)
            {
                return true;
            }

            ownerDescription = $"PID {ownerPid}, process '{SafeProcessName(process)}'";

            if (lockCreatedUtc is null)
            {
                // No timestamp: conservative choice, assume process is the lock owner.
                ownerStillValid = true;
                return true;
            }

            try
            {
                var processStartUtc = process.StartTime.ToUniversalTime();
                // If a process with this PID started after lock creation, PID got reused => stale lock.
                ownerStillValid = processStartUtc <= lockCreatedUtc.Value.UtcDateTime.AddSeconds(1);
            }
            catch
            {
                // If start time cannot be read on this OS/permission context, remain conservative.
                ownerStillValid = true;
            }

            return true;
        }
        catch
        {
            ownerStillValid = false;
            return false;
        }
    }

    private static bool TryDeleteLockFile(string lockPath, out string? error)
    {
        error = null;
        try
        {
            if (File.Exists(lockPath))
            {
                File.Delete(lockPath);
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"Lock file exists but could not be cleaned: {ex.Message}";
            return false;
        }
    }

    private static bool TryReadLockMetadata(
        string lockPath,
        out int pid,
        out DateTimeOffset? lockCreatedUtc,
        out string ownerName)
    {
        pid = 0;
        lockCreatedUtc = null;
        ownerName = LockOwnerUnknown;

        try
        {
            using var stream = new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var pidLine = reader.ReadLine();
            var createdUtcLine = reader.ReadLine();
            var ownerNameLine = reader.ReadLine();

            if (string.IsNullOrWhiteSpace(pidLine))
            {
                return false;
            }

            if (!int.TryParse(pidLine.Trim(), out pid) || pid <= 0)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(createdUtcLine) &&
                DateTimeOffset.TryParse(createdUtcLine.Trim(), out var parsed))
            {
                lockCreatedUtc = parsed;
            }

            if (!string.IsNullOrWhiteSpace(ownerNameLine))
            {
                ownerName = ownerNameLine.Trim();
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetCurrentProcessNameSafe()
    {
        try
        {
            using var current = Process.GetCurrentProcess();
            return SafeProcessName(current);
        }
        catch
        {
            return "BareSync";
        }
    }

    private static string SafeProcessName(Process process)
    {
        try
        {
            return process.ProcessName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _stream.Dispose();
        }
        catch
        {
            // Best-effort cleanup.
        }

        try
        {
            if (File.Exists(_lockPath))
            {
                File.Delete(_lockPath);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}
