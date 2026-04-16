namespace BareSync.Tests;

internal static class AppSettingsIsolation
{
    private const string AppSettingsPathOverrideEnvVar = "BARESYNC_APPSETTINGS_PATH";
    private const string LockDirectoryOverrideEnvVar = "BARESYNC_LOCK_DIR";
    private const string AppDataRootOverrideEnvVar = "BARESYNC_APP_DATA_ROOT";
    private static readonly SemaphoreSlim AppSettingsLock = new(1, 1);

    public static async Task<T> WithIsolatedAppSettingsAsync<T>(
        string appDir,
        string? newJson,
        Func<Task<T>> action)
    {
        if (string.IsNullOrWhiteSpace(appDir))
        {
            throw new ArgumentException("App directory must be provided.", nameof(appDir));
        }

        await AppSettingsLock.WaitAsync();
        string? isolationRoot = null;
        string? originalAppSettingsOverride = null;
        string? originalLockDirectoryOverride = null;
        string? originalAppDataRootOverride = null;

        try
        {
            isolationRoot = Path.Combine(
                Path.GetTempPath(),
                "BareSyncTests_Runtime_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(isolationRoot);

            var isolatedAppDataRoot = Path.Combine(isolationRoot, "appdata");
            Directory.CreateDirectory(isolatedAppDataRoot);

            var isolatedConfigPath = Path.Combine(isolatedAppDataRoot, "appsettings.json");
            var isolatedLockDir = Path.Combine(isolatedAppDataRoot, "locks");
            Directory.CreateDirectory(isolatedLockDir);

            if (newJson is not null)
            {
                await File.WriteAllTextAsync(isolatedConfigPath, newJson);
            }

            originalAppSettingsOverride = Environment.GetEnvironmentVariable(AppSettingsPathOverrideEnvVar);
            originalLockDirectoryOverride = Environment.GetEnvironmentVariable(LockDirectoryOverrideEnvVar);
            originalAppDataRootOverride = Environment.GetEnvironmentVariable(AppDataRootOverrideEnvVar);
            Environment.SetEnvironmentVariable(AppSettingsPathOverrideEnvVar, isolatedConfigPath);
            Environment.SetEnvironmentVariable(LockDirectoryOverrideEnvVar, isolatedLockDir);
            Environment.SetEnvironmentVariable(AppDataRootOverrideEnvVar, isolatedAppDataRoot);

            return await action();
        }
        finally
        {
            try
            {
                Environment.SetEnvironmentVariable(
                    AppSettingsPathOverrideEnvVar,
                    originalAppSettingsOverride);
                Environment.SetEnvironmentVariable(
                    LockDirectoryOverrideEnvVar,
                    originalLockDirectoryOverride);
                Environment.SetEnvironmentVariable(
                    AppDataRootOverrideEnvVar,
                    originalAppDataRootOverride);

                if (!string.IsNullOrWhiteSpace(isolationRoot)
                    && Directory.Exists(isolationRoot))
                {
                    Directory.Delete(isolationRoot, recursive: true);
                }
            }
            catch
            {
            }

            AppSettingsLock.Release();
        }
    }

    public static Task WithIsolatedAppSettingsAsync(
        string appDir,
        string? newJson,
        Func<Task> action)
    {
        return WithIsolatedAppSettingsAsync(
            appDir,
            newJson,
            async () =>
            {
                await action();
                return 0;
            });
    }
}
