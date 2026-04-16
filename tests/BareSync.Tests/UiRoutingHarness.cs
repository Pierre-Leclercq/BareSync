using System.Diagnostics;

namespace BareSync.Tests;

internal static class UiRoutingHarness
{
    private const string AppSettingsPathOverrideEnvVar = "BARESYNC_APPSETTINGS_PATH";
    private const string LockDirectoryOverrideEnvVar = "BARESYNC_LOCK_DIR";
    private const string AppDataRootOverrideEnvVar = "BARESYNC_APP_DATA_ROOT";

    internal sealed record UiRunResult(
        IReadOnlyList<string> ScreenTrace,
        string Stdout,
        string Stderr,
        string StdoutRaw,
        string StderrRaw,
        int ExitCode);

    private const int StdoutSampleLineLimit = 200;
    private const int StderrSampleCharLimit = 2000;

    public static async Task<UiRunResult> RunBareSyncAsync(
        string stdin,
        TimeSpan timeout,
        string? configJson = null,
        Func<string, Task>? setup = null,
        Func<string, Task>? capture = null,
        string? arguments = null,
        IReadOnlyDictionary<string, string?>? environmentOverrides = null)
    {
        var binaryDir = AppContext.BaseDirectory;
        if (string.IsNullOrWhiteSpace(binaryDir))
        {
            throw new InvalidOperationException("BareSync base directory is empty.");
        }

        return await AppSettingsIsolation.WithIsolatedAppSettingsAsync(
            binaryDir,
            configJson,
            async () =>
            {
                var appDataRoot = ResolveAppDataRoot(binaryDir);
                var lockPath = ResolveLockPath(binaryDir);
                try
                {
                    if (File.Exists(lockPath))
                    {
                        File.Delete(lockPath);
                    }
                }
                catch
                {
                }

                if (setup is not null)
                {
                    await setup(appDataRoot);
                }

                var startInfo = CreateStartInfo(binaryDir, arguments, environmentOverrides);
                using var process = new Process { StartInfo = startInfo };
                if (!process.Start())
                {
                    throw new InvalidOperationException("Failed to start BareSync process.");
                }

                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();
                var exitTask = process.WaitForExitAsync();

                if (!string.IsNullOrEmpty(stdin))
                {
                    await process.StandardInput.WriteAsync(stdin);
                }

                process.StandardInput.Close();

                if (await Task.WhenAny(exitTask, Task.Delay(timeout)) != exitTask)
                {
                    TryKill(process);
                    try
                    {
                        if (File.Exists(lockPath))
                        {
                            File.Delete(lockPath);
                        }
                    }
                    catch
                    {
                    }
                    throw new TimeoutException(
                        $"BareSync run did not exit within {timeout.TotalSeconds} seconds.");
                }

                await exitTask;

                await Task.WhenAll(stdoutTask, stderrTask);

                var stdout = await stdoutTask;
                var stderr = await stderrTask;
                var stdoutSample = CaptureStdoutSample(stdout);
                var stderrSample = TrimStderr(stderr);

                if (capture is not null)
                {
                    await capture(appDataRoot);
                }

                return new UiRunResult(
                    ScreenTrace: UiTraceParser.Parse(stdout),
                    Stdout: stdout,
                    Stderr: stderr,
                    StdoutRaw: stdoutSample,
                    StderrRaw: stderrSample,
                    ExitCode: process.ExitCode);
            });
    }

    private static ProcessStartInfo CreateStartInfo(
        string appDir,
        string? arguments,
        IReadOnlyDictionary<string, string?>? environmentOverrides)
    {
        var exePath = Path.Combine(appDir, "BareSync.exe");
        if (File.Exists(exePath))
        {
            return BuildStartInfoWithRuntimeOverrides(exePath, arguments ?? string.Empty, appDir, environmentOverrides);
        }

        var dllPath = Path.Combine(appDir, "BareSync.dll");
        if (File.Exists(dllPath))
        {
            var extraArgs = string.IsNullOrWhiteSpace(arguments) ? string.Empty : $" {arguments}";
            return BuildStartInfoWithRuntimeOverrides("dotnet", $"\"{dllPath}\"{extraArgs}", appDir, environmentOverrides);
        }

        var unixPath = Path.Combine(appDir, "BareSync");
        if (File.Exists(unixPath))
        {
            return BuildStartInfoWithRuntimeOverrides(unixPath, arguments ?? string.Empty, appDir, environmentOverrides);
        }

        throw new FileNotFoundException(
            "BareSync executable not found in the test output directory.",
            exePath);
    }

    private static ProcessStartInfo BuildStartInfo(string fileName, string arguments, string appDir)
    {
        return new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = appDir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }

    private static ProcessStartInfo BuildStartInfoWithRuntimeOverrides(
        string fileName,
        string arguments,
        string appDir,
        IReadOnlyDictionary<string, string?>? environmentOverrides)
    {
        var startInfo = BuildStartInfo(fileName, arguments, appDir);
        CopyEnvironmentOverride(startInfo, AppSettingsPathOverrideEnvVar);
        CopyEnvironmentOverride(startInfo, LockDirectoryOverrideEnvVar);
        CopyEnvironmentOverride(startInfo, AppDataRootOverrideEnvVar);
        ApplyEnvironmentOverrides(startInfo, environmentOverrides);
        return startInfo;
    }

    private static void ApplyEnvironmentOverrides(
        ProcessStartInfo startInfo,
        IReadOnlyDictionary<string, string?>? environmentOverrides)
    {
        if (environmentOverrides is null)
        {
            return;
        }

        foreach (var pair in environmentOverrides)
        {
            if (pair.Value is null)
            {
                _ = startInfo.Environment.Remove(pair.Key);
                continue;
            }

            startInfo.Environment[pair.Key] = pair.Value;
        }
    }

    private static void CopyEnvironmentOverride(ProcessStartInfo startInfo, string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            startInfo.Environment[variableName] = value;
        }
    }

    private static string ResolveLockPath(string appDir)
    {
        var lockDir = Environment.GetEnvironmentVariable(LockDirectoryOverrideEnvVar);
        if (string.IsNullOrWhiteSpace(lockDir))
        {
            lockDir = appDir;
        }

        return Path.Combine(lockDir, "baresync.lock");
    }

    private static string ResolveAppDataRoot(string appDir)
    {
        var appDataRoot = Environment.GetEnvironmentVariable(AppDataRootOverrideEnvVar);
        if (string.IsNullOrWhiteSpace(appDataRoot))
        {
            appDataRoot = appDir;
        }

        return appDataRoot;
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }

    private static string CaptureStdoutSample(string stdout)
    {
        if (string.IsNullOrEmpty(stdout))
        {
            return string.Empty;
        }

        var normalized = stdout
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');
        var count = Math.Min(lines.Length, StdoutSampleLineLimit);
        return string.Join(Environment.NewLine, lines, 0, count);
    }

    private static string TrimStderr(string stderr)
    {
        if (string.IsNullOrEmpty(stderr))
        {
            return string.Empty;
        }

        return stderr.Length <= StderrSampleCharLimit
            ? stderr
            : stderr.Substring(0, StderrSampleCharLimit);
    }
}

internal static class UiTraceParser
{
    public static IReadOnlyList<string> Parse(string stdout)
    {
        var lines = stdout
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Split('\n');

        var trace = new List<string>();
        var sawInvalidSettings = false;

        void Add(string id)
        {
            if (trace.Count == 0 || trace[^1] != id)
            {
                trace.Add(id);
            }
        }

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd();

            if (line.Contains("Missing or invalid settings:", StringComparison.Ordinal))
            {
                Add("S1.5a");
                sawInvalidSettings = true;
                continue;
            }

            if (line.StartsWith("Proceed?", StringComparison.OrdinalIgnoreCase)
                && line.IndexOf("y/n", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Add("S1.6");
                continue;
            }

            if (line.Contains("** Current config **", StringComparison.Ordinal))
            {
                Add("S1.5");
                continue;
            }

            if (line.Contains("** Batch mode **", StringComparison.Ordinal))
            {
                Add("S2.1");
                continue;
            }

            if (line.Contains("** Batch / List **", StringComparison.Ordinal))
            {
                Add("S2.2");
                continue;
            }

            if (line.Contains("Select batch number", StringComparison.OrdinalIgnoreCase))
            {
                Add("S2.2a");
                continue;
            }

            if (line.Contains("** Batch / Details **", StringComparison.Ordinal))
            {
                Add("S2.3");
                continue;
            }

            if (line.Contains("Enter batch name (empty or ESC to cancel):", StringComparison.Ordinal))
            {
                Add("S2.1a");
                continue;
            }

            if (line.Contains("** Batch / Preflight **", StringComparison.Ordinal))
            {
                Add("S2.12");
                continue;
            }

            if (line.Contains("** Batch / Preflight (FAILED) **", StringComparison.Ordinal))
            {
                Add("S2.12a");
                continue;
            }

            if (line.Contains("** Batch / Validity details **", StringComparison.Ordinal))
            {
                Add("S2.16");
                continue;
            }

            if (line.Contains("** Batch / Steps **", StringComparison.Ordinal))
            {
                Add("S2.6");
                continue;
            }

            if (line.Contains("** Step / Select operation **", StringComparison.Ordinal))
            {
                Add("S2.7");
                continue;
            }

            if (line.Contains("** Step / Edit **", StringComparison.Ordinal))
            {
                Add("S2.8");
                continue;
            }

            if (line.Contains("** Step / Overrides **", StringComparison.Ordinal))
            {
                Add("S2.9");
                continue;
            }

            if (line.Contains("** Steps / Reorder **", StringComparison.Ordinal))
            {
                Add("S2.10");
                continue;
            }

            if (line.Contains("Remove step", StringComparison.OrdinalIgnoreCase) 
                && line.Contains("Proceed?", StringComparison.OrdinalIgnoreCase))
            {
                Add("S2.11");
                continue;
            }

            if (line.Contains("unsaved changes", StringComparison.OrdinalIgnoreCase))
            {
                Add("S2.17");
                continue;
            }

            if (line.Contains("** BareSync **", StringComparison.Ordinal))
            {
                var sawMenu = false;
                var sawOperation = false;
                var windowEnd = Math.Min(lines.Length - 1, index + 40);
                for (var lookahead = index + 1; lookahead <= windowEnd; lookahead++)
                {
                    var candidate = lines[lookahead].TrimEnd();
                    if (candidate.StartsWith("Operation:", StringComparison.Ordinal))
                    {
                        sawOperation = true;
                        break;
                    }

                    if (candidate == "** Menu **")
                    {
                        sawMenu = true;
                    }
                }

                if (sawOperation)
                {
                    Add("S1.8");
                }
                else if (sawMenu)
                {
                    Add("S0.1");
                    if (sawInvalidSettings)
                    {
                        break;
                    }
                }
            }
        }

        return trace.ToArray();
    }
}
