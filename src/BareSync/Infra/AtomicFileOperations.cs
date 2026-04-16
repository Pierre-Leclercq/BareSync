namespace BareSync.Infra;

internal static class AtomicFileOperations
{
    private const int MaxAttempts = 6;
    private const int BaseDelayMs = 25;

    public static void ReplaceFile(string sourcePath, string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("Source path is required.", nameof(sourcePath));
        }

        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new ArgumentException("Destination path is required.", nameof(destinationPath));
        }

        Exception? lastReplaceError = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                ReplaceOnce(sourcePath, destinationPath);
                return;
            }
            catch (Exception ex) when (IsRetryable(ex))
            {
                lastReplaceError = ex;
                if (attempt == MaxAttempts)
                {
                    break;
                }

                DelayBeforeRetry(attempt);
            }
        }

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                OverwriteWithCopyFallback(sourcePath, destinationPath);
                return;
            }
            catch (Exception ex) when (IsRetryable(ex))
            {
                if (attempt == MaxAttempts)
                {
                    throw new IOException(
                        $"Failed to replace destination file after retries. Source='{sourcePath}', Destination='{destinationPath}'.",
                        lastReplaceError ?? ex);
                }

                DelayBeforeRetry(attempt);
            }
        }
    }

    private static void ReplaceOnce(string sourcePath, string destinationPath)
    {
        if (File.Exists(destinationPath))
        {
            EnsureWritable(destinationPath);
            File.Replace(sourcePath, destinationPath, null, ignoreMetadataErrors: true);
            return;
        }

        var parent = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.Move(sourcePath, destinationPath);
    }

    private static void OverwriteWithCopyFallback(string sourcePath, string destinationPath)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        var parent = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        EnsureWritable(destinationPath);
        File.Copy(sourcePath, destinationPath, overwrite: true);
        File.Delete(sourcePath);
    }

    private static void EnsureWritable(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReadOnly) != 0)
        {
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        }
    }

    private static bool IsRetryable(Exception ex)
    {
        return ex is IOException or UnauthorizedAccessException;
    }

    private static void DelayBeforeRetry(int attempt)
    {
        Thread.Sleep(BaseDelayMs * attempt);
    }
}
