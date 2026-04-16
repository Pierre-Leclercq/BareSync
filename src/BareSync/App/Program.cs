using BareSync.App.BatchMode;
using BareSync.App.BatchMode.Screens;
using BareSync.App.Common;
using BareSync.Domain;
using BareSync.Infra;
using BareSync.UI;
using Bare.Primitive.UI;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BareSync.App;

internal static class Program
{
    private const string CanonicalUtcTimestampFormat = "yyyy-MM-ddTHH:mm:ss'Z'";
    private const string LockDirectoryOverrideEnvVar = "BARESYNC_LOCK_DIR";
    private const string AppDataRootOverrideEnvVar = "BARESYNC_APP_DATA_ROOT";

    private static string UtcNowTimestamp()
    {
        return DateTimeOffset.UtcNow.ToString(CanonicalUtcTimestampFormat, CultureInfo.InvariantCulture);
    }

    private static async Task<int> Main(string[] args)
    {
        // Handle help request or unknown arguments early, before any initialization
        var cliResult = CliHelp.TryHandleCliHelpOrErrors(args, Bare.Primitive.UI.UiConsole.WriteLine);
        if (cliResult.HasValue)
        {
            return cliResult.Value;
        }

        var lockDir = ResolveLockDirectory();
        Directory.CreateDirectory(lockDir);
        var lockPath = Path.Combine(lockDir, "baresync.lock");
        if (!SingleInstanceLock.TryAcquire(lockPath, out var instanceLock, out var lockError))
        {
            Bare.Primitive.UI.UiConsole.WriteLine($"BareSync is already running or lock could not be recovered: {lockPath}");
            if (!string.IsNullOrWhiteSpace(lockError))
            {
                Bare.Primitive.UI.UiConsole.WriteLine($"Details: {lockError}");
            }
            Environment.ExitCode = 1;
            return 1;
        }

        using var acquiredLock = instanceLock!;

        var config = ConfigService.LoadOrCreate(out var created);

        if (!TryParseCliArguments(args, out var requestedBatchNames, out var requestedExtractSourcePath, out var argsError))
        {
            Bare.Primitive.UI.UiConsole.WriteLine(argsError);
            Environment.ExitCode = 1;
            return 1;
        }

        if (requestedBatchNames.Count > 0)
        {
            var appDataRoot = ResolveAppDataRoot();
            var loader = new BatchStorageLoader();
            var cliRunner = new BatchCommandLineRunner(loader, appDataRoot, config);
            var cliExitCode = await cliRunner.RunAsync(requestedBatchNames).ConfigureAwait(false);
            Environment.ExitCode = cliExitCode;
            return cliExitCode;
        }

        if (!string.IsNullOrWhiteSpace(requestedExtractSourcePath))
        {
            var extractRunner = new ExtractCommandLineRunner(encryptedService: null);
            var extractExitCode = await extractRunner.RunAsync(requestedExtractSourcePath, CancellationToken.None).ConfigureAwait(false);
            Environment.ExitCode = extractExitCode;
            return extractExitCode;
        }

        UiInteraction.Clear();
        PlatformSupportPolicy.WriteStartupNotice();

        Bare.Primitive.UI.UiConsole.WriteLine("BareSync — filesystem integrity & synchronization tool");

        if (created)
        {
            Bare.Primitive.UI.UiConsole.WriteLine("Configuration created: appsettings.json");
        }

        var validationErrors = ConfigService.Validate(config);
        if (validationErrors.Count > 0 && !Bare.Primitive.UI.UiConsole.IsInputRedirected)
        {
            SettingsEditor.ShowValidationErrors(validationErrors);
            SettingsEditor.Run(config);
        }

        // Keep a single instance for this run (purely a convenience / avoids re-instantiation).
        var encryptedService = new EncryptedFolderService();
        MenuStatus? interactiveStatus = null;

        var running = true;
        while (running)
        {
            var selection = PromptMainMenu();
            switch (selection)
            {
                case 0:
                    running = false;
                    break;
                case 1:
                    interactiveStatus = await RunInteractiveModeAsync(config, encryptedService, interactiveStatus)
                        .ConfigureAwait(false);
                    break;
                case 2:
                    RunBatchMode(config);
                    break;
            }
        }

        return 0;
    }

    private static bool TryParseCliArguments(
        IReadOnlyList<string> args,
        out List<string> requestedBatchNames,
        out string? requestedExtractSourcePath,
        out string argsError)
    {
        requestedBatchNames = new List<string>();
        requestedExtractSourcePath = null;
        argsError = string.Empty;

        if (args.Count == 0)
        {
            return true;
        }

        foreach (var arg in args)
        {
            if (string.IsNullOrWhiteSpace(arg))
            {
                argsError = $"Unsupported argument: {arg}";
                return false;
            }

            if (arg.StartsWith("/BATCH:", StringComparison.OrdinalIgnoreCase))
            {
                var name = UnquoteCliValue(arg.Substring("/BATCH:".Length));
                if (string.IsNullOrWhiteSpace(name))
                {
                    argsError = $"Invalid batch argument (empty name): {arg}";
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(requestedExtractSourcePath))
                {
                    argsError = "Arguments /BATCH and /EXTRACT cannot be combined in the same command.";
                    return false;
                }

                requestedBatchNames.Add(name);
                continue;
            }

            if (arg.StartsWith("/EXTRACT:", StringComparison.OrdinalIgnoreCase))
            {
                var sourcePath = UnquoteCliValue(arg.Substring("/EXTRACT:".Length));
                if (string.IsNullOrWhiteSpace(sourcePath))
                {
                    argsError = $"Invalid extract argument (empty path): {arg}";
                    return false;
                }

                if (requestedBatchNames.Count > 0)
                {
                    argsError = "Arguments /BATCH and /EXTRACT cannot be combined in the same command.";
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(requestedExtractSourcePath))
                {
                    argsError = "Duplicate /EXTRACT argument is not supported.";
                    return false;
                }

                requestedExtractSourcePath = sourcePath;
                continue;
            }

            argsError = $"Unsupported argument: {arg}";
            return false;
        }

        return true;
    }

    private static string UnquoteCliValue(string rawValue)
    {
        var value = rawValue.Trim();
        if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
        {
            value = value.Substring(1, value.Length - 2).Trim();
        }

        return value;
    }

    private static string ResolveLockDirectory()
    {
        var overridePath = Environment.GetEnvironmentVariable(LockDirectoryOverrideEnvVar);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return Path.GetFullPath(overridePath);
        }

        return AppContext.BaseDirectory;
    }

    private static int PromptMainMenu()
    {
        UiInteraction.Clear();
        Bare.Primitive.UI.UiConsole.WriteLine("** BareSync **");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.WriteLine("** Menu **");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.WriteLine("1. Interactive mode");
        Bare.Primitive.UI.UiConsole.WriteLine("2. Batch mode");
        Bare.Primitive.UI.UiConsole.WriteLine("0. Exit");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.Write("Select an option: ");
        return UiInteraction.ReadMenuDigit(0, 2);
    }

    private static async Task<MenuStatus?> RunInteractiveModeAsync(
        AppConfig config,
        EncryptedFolderService encryptedService,
        MenuStatus? lastStatus)
    {
        var options = new[]
        {
            "Full refresh CRC indexes (source + destination)",
            "Smart refresh CRC indexes (source + destination)",
            "Dry run one way sync (no files copied)",
            "Sync one way source folder to destination",
            "Create encrypted folder",
            "Refresh encrypted folder",
            "Restore encrypted files",
            "Edit settings"
        };

        var running = true;
        while (running)
        {
            var selection = ConsoleMenu.Prompt(config, options, zeroLabel: "Back", menuStatus: lastStatus);
            switch (selection)
            {
                case 0:
                    running = false;
                    break;

                case 1:
                    var validationErrors = ConfigService.Validate(config);
                    if (validationErrors.Count > 0)
                    {
                        Bare.Primitive.UI.UiConsole.WriteLine("Settings invalid. Use Edit settings.");
                        SettingsEditor.ShowValidationErrors(validationErrors);
                        break;
                    }
                    lastStatus = await RunRefreshIndexesAsync(config, CancellationToken.None, incremental: false);
                    break;

                case 2:
                    validationErrors = ConfigService.Validate(config);
                    if (validationErrors.Count > 0)
                    {
                        Bare.Primitive.UI.UiConsole.WriteLine("Settings invalid. Use Edit settings.");
                        SettingsEditor.ShowValidationErrors(validationErrors);
                        break;
                    }
                    lastStatus = await RunRefreshIndexesAsync(config, CancellationToken.None, incremental: true);
                    break;

                case 3:
                    validationErrors = ConfigService.Validate(config);
                    if (validationErrors.Count > 0)
                    {
                        SettingsEditor.ShowValidationErrors(validationErrors);
                        break;
                    }

                    Bare.Primitive.UI.UiConsole.WriteLine("DRY RUN: no files will be copied; only log/report will be created.");
                    if (!ConfirmOneWaySync(showOverwriteWarning: false))
                    {
                        break;
                    }

                    lastStatus = await RunSyncAsync(config, CancellationToken.None, dryRun: true);
                    break;

                case 4:
                    validationErrors = ConfigService.Validate(config);
                    if (validationErrors.Count > 0)
                    {
                        SettingsEditor.ShowValidationErrors(validationErrors);
                        break;
                    }

                    if (!ConfirmOneWaySync(showOverwriteWarning: true))
                    {
                        break;
                    }

                    lastStatus = await RunSyncAsync(config, CancellationToken.None, dryRun: false);
                    break;

                case 5:
                    validationErrors = ConfigService.Validate(config);
                    if (validationErrors.Count > 0)
                    {
                        SettingsEditor.ShowValidationErrors(validationErrors);
                        break;
                    }

                    lastStatus = await RunEncryptedSyncAsync(config, encryptedService, CancellationToken.None);
                    break;

                case 6:
                    validationErrors = ConfigService.Validate(config);
                    if (validationErrors.Count > 0)
                    {
                        SettingsEditor.ShowValidationErrors(validationErrors);
                        break;
                    }

                    lastStatus = await RunRefreshEncryptedFolderAsync(config, encryptedService, CancellationToken.None);
                    break;

                case 7:
                    if (string.IsNullOrWhiteSpace(config.EncryptedOutputRoot))
                    {
                        Bare.Primitive.UI.UiConsole.WriteLine("EncryptedOutputRoot is not set.");
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(config.RestoreRoot))
                    {
                        Bare.Primitive.UI.UiConsole.WriteLine("RestoreRoot is not set.");
                        break;
                    }

                    var restoreSlot = SecretStoreProvider.GetEncryptionPasswordSlot(config.EncryptedOutputRoot);
                    var restorePassword = ReadPasswordWithSecretStore(restoreSlot);
                    _ = restorePassword;
                    Bare.Primitive.UI.UiConsole.WriteLine();

                    UiInteraction.Clear();

                    var restoreResult = await OperationRunner.RunAsync(
                        new OperationRunnerOptions
                        {
                            OperationTitle = "Restoring encrypted files...",
                            RenderMode = RenderMode.Progress,
                            ClearAtStart = false
                        },
                        CancellationToken.None,
                        (progress, token) => encryptedService.RestoreEncryptedFilesAsync(
                            config,
                            restorePassword,
                            progress,
                            token))
                        .ConfigureAwait(false);

                    lastStatus = BuildMenuStatus(restoreResult);
                    break;

                case 8:
                    SettingsEditor.Run(config);
                    break;
            }
        }

        return lastStatus;
    }

    private static void RunBatchMode(AppConfig config)
    {
        var appDataRoot = ResolveAppDataRoot();
        var loader = new BatchStorageLoader();
        
        // Activate S.O.L.I.D. BatchModeController
        var controller = new BatchModeController(loader, appDataRoot, config);
        controller.Run();
    }

    private static string ResolveAppDataRoot()
    {
        var overridePath = Environment.GetEnvironmentVariable(AppDataRootOverrideEnvVar);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return Path.GetFullPath(overridePath);
        }

        return AppContext.BaseDirectory;
    }

    // ==========================================
    // Helper methods kept for execution/summary screens
    // ==========================================
    private static BatchV0? LoadBatchV0(string batchPath)
    {
        try
        {
            var json = File.ReadAllText(batchPath);
            var root = JsonNode.Parse(json) as JsonObject;
            if (root is null) return null;

            var batch = new BatchV0
            {
                SchemaVersion = root["schemaVersion"]?.GetValue<int>() ?? 0,
                Id = root["id"]?.GetValue<string>() ?? string.Empty,
                Name = root["name"]?.GetValue<string>() ?? string.Empty,
                Description = root["description"]?.GetValue<string?>(),
                Tags = (root["tags"] as JsonArray)?.Select(node => node?.GetValue<string>() ?? string.Empty).ToList(),
                CreatedUtc = root["createdUtc"]?.GetValue<string>() ?? string.Empty,
                UpdatedUtc = root["updatedUtc"]?.GetValue<string>() ?? string.Empty,
                ContextSnapshot = root["contextSnapshot"] as JsonObject ?? new JsonObject()
            };

            if (root["steps"] is JsonArray stepsArray)
            {
                foreach (var node in stepsArray)
                {
                    if (node is not JsonObject stepObject) continue;
                    var opParams = stepObject["operationParams"] as JsonObject;
                    var values = opParams?["values"] as JsonObject ?? new JsonObject();
                    batch.Steps.Add(new BatchStepV0
                    {
                        StepId = stepObject["stepId"]?.GetValue<string>(),
                        OperationType = stepObject["operationType"]?.GetValue<string>() ?? string.Empty,
                        OperationParams = new StepOperationParamsV0
                        {
                            Values = values,
                            Extensions = opParams?["extensions"] as JsonObject
                        },
                        ContextOverrides = stepObject["contextOverrides"] as JsonObject ?? new JsonObject()
                    });
                }
            }
            return batch;
        }
        catch { return null; }
    }

    private static string GetShortId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "?";
        return id.Length <= 8 ? id : id.Substring(0, 8);
    }

    private static async Task<MenuStatus?> RunRefreshIndexesAsync(AppConfig config, CancellationToken ct, bool incremental)
    {
        if (!incremental)
        {
            IndexRefreshService.DeleteIndexArtifacts(config.SourceIndexCsvPath);
            IndexRefreshService.DeleteIndexArtifacts(config.DestIndexCsvPath);
        }

        var title = incremental
            ? "Smart refreshing CRC indexes (source)..."
            : "Refreshing CRC indexes (source)...";

        var result = await OperationRunner.RunAsync(
            new OperationRunnerOptions
            {
                OperationTitle = title,
                RenderMode = RenderMode.Progress
            },
            ct,
            (progress, token) => IndexRefreshService.RefreshIndexesWithProgressAsync(config, token, progress, incremental))
            .ConfigureAwait(false);

        return BuildMenuStatus(result);
    }

    private static async Task<MenuStatus?> RunSyncAsync(AppConfig config, CancellationToken ct, bool dryRun)
    {
        var operationText = SyncOneWay.GetOperationLabel("Applying sync decisions...", dryRun);
        var result = await OperationRunner.RunAsync(
            new OperationRunnerOptions
            {
                OperationTitle = operationText,
                RenderMode = RenderMode.Progress
            },
            ct,
            async (progress, token) =>
            {
                var summary = await SyncOneWay.RunAsync(config, token, dryRun, progress).ConfigureAwait(false);
                return new OperationResult
                {
                    SuccessOrWarningFlag = summary.ErrorCount == 0,
                    StatusLine = string.IsNullOrWhiteSpace(summary.StatusLine)
                        ? summary.StatusLabel
                        : summary.StatusLine,
                    LogPath = summary.LogFilePath,
                    ReportPath = summary.ReportFilePath
                };
            })
            .ConfigureAwait(false);

        return BuildMenuStatus(result);
    }

    private static async Task<MenuStatus?> RunEncryptedSyncAsync(
        AppConfig config,
        EncryptedFolderService encryptedService,
        CancellationToken ct)
    {
        Bare.Primitive.UI.UiConsole.WriteLine("Creating encrypted folder (preview only).");
        var passwordSlot = SecretStoreProvider.GetEncryptionPasswordSlot(config.EncryptedOutputRoot);
        var password = ReadPasswordWithSecretStore(passwordSlot);
        _ = password; // password is intentionally not stored or logged

        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.WriteLine("Encrypted folder creation summary:");
        Bare.Primitive.UI.UiConsole.WriteLine($"SourceRoot: {config.SourceRoot}");
        if (string.IsNullOrWhiteSpace(config.EncryptedOutputRoot))
        {
            Bare.Primitive.UI.UiConsole.WriteLine("EncryptedOutputRoot: <empty> (WARNING: not set)");
        }
        else
        {
            Bare.Primitive.UI.UiConsole.WriteLine($"EncryptedOutputRoot: {config.EncryptedOutputRoot}");
        }
        Bare.Primitive.UI.UiConsole.WriteLine($"RestoreRoot: {config.RestoreRoot}");
        Bare.Primitive.UI.UiConsole.WriteLine("Planned format: native .bse (Brotli + AES + HMAC)");
        Bare.Primitive.UI.UiConsole.WriteLine("  password: ********");

        if (string.IsNullOrWhiteSpace(config.EncryptedOutputRoot))
        {
            Bare.Primitive.UI.UiConsole.WriteLine("EncryptedOutputRoot is not set. Cannot create encrypted index.");
            return new MenuStatus
            {
                StatusLine = "EncryptedOutputRoot is not set."
            };
        }

        // Build once: preview + execution share the same plan (avoids drift + avoids double scan).
        var plan = await encryptedService.BuildEncryptedPlanAsync(config, ct).ConfigureAwait(false);
        encryptedService.PrintEncryptedDataPlan(plan.DataPlan, config.EncryptedOutputRoot, previewCount: 3);

        if (!ConfirmYesNo("Proceed? (Y/N) "))
        {
            return null;
        }

        UiInteraction.Clear();

        try
        {
            var status = await encryptedService.CreateEncryptedIndexAsync(config, password, plan.Entries, plan.DataPlan, ct).ConfigureAwait(false);
            return status;
        }
        catch (OperationCanceledException)
        {
            return new MenuStatus
            {
                StatusLine = "Encrypted index creation canceled."
            };
        }
        catch (Exception ex)
        {
            Bare.Primitive.UI.UiConsole.WriteLine($"Failed to create encrypted index: {ex.Message}");
            return new MenuStatus
            {
                StatusLine = "Encrypted index creation failed."
            };
        }
    }

    private static async Task<MenuStatus?> RunRefreshEncryptedFolderAsync(
        AppConfig config,
        EncryptedFolderService encryptedService,
        CancellationToken ct)
    {
        Bare.Primitive.UI.UiConsole.WriteLine("Refreshing encrypted folder.");
        var passwordSlot = SecretStoreProvider.GetEncryptionPasswordSlot(config.EncryptedOutputRoot);
        var password = ReadPasswordWithSecretStore(passwordSlot);
        _ = password; // password is intentionally not stored or logged

        Bare.Primitive.UI.UiConsole.WriteLine();

        if (string.IsNullOrWhiteSpace(config.EncryptedOutputRoot))
        {
            Bare.Primitive.UI.UiConsole.WriteLine("EncryptedOutputRoot is not set. Cannot refresh encrypted folder.");
            return new MenuStatus
            {
                StatusLine = "EncryptedOutputRoot is not set."
            };
        }

        try
        {
            var result = await OperationRunner.RunAsync(
                new OperationRunnerOptions
                {
                    OperationTitle = "Refreshing encrypted folder...",
                    RenderMode = RenderMode.Progress,
                    ClearAtStart = false
                },
                ct,
                (progress, token) => encryptedService.RefreshEncryptedFolderAsync(config, password, progress, token))
                .ConfigureAwait(false);

            return BuildMenuStatus(result);
        }
        catch (OperationCanceledException)
        {
            return new MenuStatus
            {
                StatusLine = "Encrypted folder refresh canceled."
            };
        }
        catch (Exception ex)
        {
            Bare.Primitive.UI.UiConsole.WriteLine($"Failed to refresh encrypted folder: {ex.Message}");
            return new MenuStatus
            {
                StatusLine = "Encrypted folder refresh failed."
            };
        }
    }

    internal static async Task RefreshIndexesAsync(AppConfig config, CancellationToken ct, bool incremental = false)
    {
        var progress = new Progress<ProgressInfo>(_ => { });
        await IndexRefreshService.RefreshIndexesWithProgressAsync(config, ct, progress, incremental).ConfigureAwait(false);
    }

    private static MenuStatus? BuildMenuStatus(OperationResult result)
    {
        if (result is null)
        {
            return null;
        }

        var statusLine = result.StatusLine;
        if (string.IsNullOrWhiteSpace(statusLine))
        {
            return null;
        }

        return new MenuStatus
        {
            StatusLine = statusLine,
            LogPath = result.LogPath,
            ReportPath = result.ReportPath,
            SuccessOrWarningFlag = result.SuccessOrWarningFlag
        };
    }

    private static bool ConfirmOneWaySync(
        bool showOverwriteWarning,
        IUiInput? uiInput = null,
        IUiKeyInput? keyInput = null,
        Func<bool>? isInputRedirected = null,
        Action<string>? write = null,
        Action<string>? writeLine = null)
    {
        var resolvedWrite = write ?? Bare.Primitive.UI.UiConsole.Write;
        var resolvedWriteLine = writeLine ?? Bare.Primitive.UI.UiConsole.WriteLine;
        if (showOverwriteWarning)
        {
            resolvedWriteLine("WARNING: Destination files may be overwritten.");
        }
        resolvedWriteLine("This is a one-way sync from SourceRoot to MirrorRoot.");

        return ConsoleInputHelpers.ConfirmYesNo(
            "Proceed? (Y/N) ",
            uiInput,
            keyInput,
            isInputRedirected,
            resolvedWrite,
            resolvedWriteLine);
    }

    private static string ReadPassword(
        IUiInput? uiInput = null,
        IUiKeyInput? keyInput = null,
        Func<bool>? isInputRedirected = null,
        Action<string>? write = null,
        Action<string>? writeLine = null)
    {
        return ConsoleInputHelpers.ReadPassword(
            maskInput: true,
            uiInput,
            keyInput,
            isInputRedirected,
            write,
            writeLine);
    }

    private static string ReadPasswordWithSecretStore(
        string slotKey,
        IUiInput? uiInput = null,
        IUiKeyInput? keyInput = null,
        Func<bool>? isInputRedirected = null,
        Action<string>? write = null,
        Action<string>? writeLine = null)
    {
        var redirected = isInputRedirected ?? (() => Bare.Primitive.UI.UiConsole.IsInputRedirected);
        var resolvedWrite = write ?? Bare.Primitive.UI.UiConsole.Write;
        var resolvedWriteLine = writeLine ?? Bare.Primitive.UI.UiConsole.WriteLine;

        if (SecretStoreProvider.TryLoadSecret(slotKey, out var storedSecret))
        {
            resolvedWriteLine("Using password from OS secret store.");
            return storedSecret;
        }

        SecretStoreProvider.WriteUnavailableWarning();
        resolvedWrite("Enter password (will not be echoed): ");
        var password = ReadPassword(
            uiInput,
            keyInput,
            redirected,
            resolvedWrite,
            resolvedWriteLine);

        if (SecretStoreProvider.IsAvailable
            && !redirected()
            && !string.IsNullOrWhiteSpace(password)
            && ConfirmYesNo(
                "Save password to OS secret store for this scope? (Y/N) ",
                uiInput,
                keyInput,
                redirected,
                resolvedWrite,
                resolvedWriteLine))
        {
            if (!SecretStoreProvider.TrySaveSecret(slotKey, password))
            {
                resolvedWriteLine("Warning: failed to store password in OS secret store.");
            }
            else
            {
                resolvedWriteLine("Password saved to OS secret store.");
            }
        }

        return password;
    }

    /// <summary>
    /// Reads a line with ESC key support for cancellation. Returns null if ESC is pressed.
    /// </summary>
    private static string? ReadLineWithEscape(
        IUiInput? uiInput = null,
        IUiKeyInput? keyInput = null,
        Func<bool>? isInputRedirected = null,
        Action<string>? write = null,
        Action<string>? writeLine = null)
    {
        return ConsoleInputHelpers.ReadLineWithEscape(
            uiInput,
            keyInput,
            isInputRedirected,
            write,
            writeLine);
    }

    private static bool ConfirmYesNo(
        string prompt,
        IUiInput? uiInput = null,
        IUiKeyInput? keyInput = null,
        Func<bool>? isInputRedirected = null,
        Action<string>? write = null,
        Action<string>? writeLine = null)
    {
        return ConsoleInputHelpers.ConfirmYesNo(
            prompt,
            uiInput,
            keyInput,
            isInputRedirected,
            write,
            writeLine);
    }

    // ==========================================
    // S2.13 Secret Prompt - Collect secrets for batch execution
    // ==========================================
    private static Dictionary<string, string>? PromptBatchSecrets(IReadOnlyList<BatchPreflightStepSummary> steps)
    {
        var secrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var requiredSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // Identify which secret slots are needed
        foreach (var step in steps.Where(s => s.RequiresSecret))
        {
            // Map operation types to secret slots
            var slot = GetSecretSlotForOperation(step.OperationType);
            if (!string.IsNullOrWhiteSpace(slot))
            {
                requiredSlots.Add(slot);
            }
        }

        if (requiredSlots.Count == 0)
        {
            return secrets;
        }

        UiInteraction.Clear();
        Bare.Primitive.UI.UiConsole.WriteLine("** Batch / Enter Secrets **");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.WriteLine("This batch requires passwords for encrypted operations.");
        Bare.Primitive.UI.UiConsole.WriteLine("Passwords will not be echoed to the screen.");
        Bare.Primitive.UI.UiConsole.WriteLine("Press ESC at any time to cancel.");
        Bare.Primitive.UI.UiConsole.WriteLine();

        foreach (var slot in requiredSlots)
        {
            Bare.Primitive.UI.UiConsole.Write($"Enter password for {slot}: ");
            
            if (Bare.Primitive.UI.UiConsole.IsInputRedirected)
            {
                var password = Bare.Primitive.UI.UiConsole.ReadLine();
                if (password is null)
                {
                    return null; // Cancelled
                }
                secrets[slot] = password;
            }
            else
            {
                var password = ReadPasswordWithEscape();
                if (password is null)
                {
                    return null; // ESC pressed - cancelled
                }
                secrets[slot] = password;
            }
        }

        Bare.Primitive.UI.UiConsole.WriteLine();
        if (!ConfirmYesNo("Proceed with execution? (Y/N) "))
        {
            return null; // Cancelled
        }

        return secrets;
    }

    private static string? ReadPasswordWithEscape()
    {
        return ConsoleInputHelpers.ReadPasswordWithEscape(maskInput: true);
    }

    private static string GetSecretSlotForOperation(string operationType)
    {
        // Map operations to the secret slots they need
        return operationType switch
        {
            "CreateEncryptedFolder" => "EncryptedOutputRoot",
            "RefreshEncryptedFolder" => "EncryptedOutputRoot",
            "RestoreEncryptedFiles" => "EncryptedOutputRoot",
            _ => string.Empty
        };
    }

    // ==========================================
    // S2.14 Run Progress - Execute batch with progress display
    // ==========================================
    private static BatchExecutionResult? RunBatchExecution(
        BatchStorageDescriptor descriptor,
        BatchStorageLoader loader,
        string appDataRoot,
        AppConfig config,
        BatchPreflightResult preflight,
        Dictionary<string, string> secrets)
    {
        var batch = LoadBatchV0(descriptor.Path);
        if (batch is null)
        {
            Bare.Primitive.UI.UiConsole.WriteLine("Failed to load batch for execution.");
            UiInteraction.SkipNextClear();
            return null;
        }

        var readiness = BatchExecutionReadinessEvaluator.EvaluateBatchExecutionReadiness(batch);
        var cts = new CancellationTokenSource();
        var progress = new BatchExecutionProgressTracker();
        
        UiInteraction.Clear();
        Bare.Primitive.UI.UiConsole.WriteLine("** Batch / Execution **");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.WriteLine($"Batch: {batch.Name} [{GetShortId(batch.Id)}]");
        Bare.Primitive.UI.UiConsole.WriteLine($"Steps: {batch.Steps.Count}");
        Bare.Primitive.UI.UiConsole.WriteLine();
        Bare.Primitive.UI.UiConsole.WriteLine("Press ESC to cancel");
        Bare.Primitive.UI.UiConsole.WriteLine();

        var stepResults = new List<BatchStepResult>();

        try
        {
            // Execute using BatchRunner
            var result = BatchRunner.ExecuteAsync(batch, readiness, config, progress, cts.Token).Result;
            return result;
        }
        catch (OperationCanceledException)
        {
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine("Execution cancelled by user.");
            UiInteraction.SkipNextClear();
            return new BatchExecutionResult(
                false,
                batch.Id,
                batch.Name,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                stepResults);
        }
        catch (Exception ex)
        {
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine($"Execution failed: {ex.Message}");
            UiInteraction.SkipNextClear();
            return new BatchExecutionResult(
                false,
                batch.Id,
                batch.Name,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                stepResults);
        }
    }

    // ==========================================
    // S2.15 Run Summary - Display execution results
    // ==========================================
    private static void ShowRunSummary(
        BatchExecutionResult result,
        BatchStorageDescriptor descriptor,
        BatchStorageLoader loader,
        string appDataRoot,
        AppConfig config)
    {
        const int pageSize = 9;
        var pageIndex = 0;
        var running = true;

        while (running)
        {
            var steps = result.StepResults;
            var totalPages = Math.Max(1, (int)Math.Ceiling(steps.Count / (double)pageSize));
            if (pageIndex >= totalPages)
            {
                pageIndex = totalPages - 1;
            }

            var pageSteps = steps
                .Skip(pageIndex * pageSize)
                .Take(pageSize)
                .ToList();

            UiInteraction.Clear();
            Bare.Primitive.UI.UiConsole.WriteLine("** Batch / Run Summary **");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine($"Batch: {result.BatchName} [{GetShortId(result.BatchId)}]");
            Bare.Primitive.UI.UiConsole.WriteLine($"Started: {result.StartedAt:yyyy-MM-dd HH:mm:ss}");
            Bare.Primitive.UI.UiConsole.WriteLine($"Completed: {result.CompletedAt:yyyy-MM-dd HH:mm:ss}");
            Bare.Primitive.UI.UiConsole.WriteLine($"Duration: {(result.CompletedAt - result.StartedAt).TotalSeconds:F1}s");
            Bare.Primitive.UI.UiConsole.WriteLine($"Overall: {(result.Success ? "SUCCESS" : "FAILED")}");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine($"Page {pageIndex + 1}/{totalPages}");
            Bare.Primitive.UI.UiConsole.WriteLine();

            if (steps.Count == 0)
            {
                Bare.Primitive.UI.UiConsole.WriteLine("(no steps executed)");
            }
            else
            {
                for (var i = 0; i < pageSteps.Count; i++)
                {
                    var step = pageSteps[i];
                    var statusSymbol = step.Success ? "[OK]" : "[FAIL]";
                    var durationStr = step.Duration.TotalSeconds < 1 
                        ? $"{step.Duration.TotalMilliseconds:F0}ms" 
                        : $"{step.Duration.TotalSeconds:F1}s";
                    Bare.Primitive.UI.UiConsole.WriteLine($"{step.StepIndex,2}) {statusSymbol} {step.OperationType,-25} {durationStr,-8} {step.StatusMessage}");
                }
            }

            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine("** Menu **");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine("1. View artifacts");
            var maxOption = 1;
            if (totalPages > 1)
            {
                Bare.Primitive.UI.UiConsole.WriteLine("2. Next page");
                Bare.Primitive.UI.UiConsole.WriteLine("3. Previous page");
                maxOption = 3;
            }
            Bare.Primitive.UI.UiConsole.WriteLine("0. Back to batch details");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.Write("Select an option: ");

            var selection = UiInteraction.ReadMenuDigit(0, maxOption);
            switch (selection)
            {
                case 0:
                    running = false;
                    break;
                case 1:
                    ShowArtifacts(result, descriptor, loader, appDataRoot, config);
                    break;
                case 2:
                    if (totalPages > 1)
                    {
                        pageIndex = (pageIndex + 1) % totalPages;
                    }
                    break;
                case 3:
                    if (totalPages > 1)
                    {
                        pageIndex = pageIndex == 0 ? totalPages - 1 : pageIndex - 1;
                    }
                    break;
            }
        }
    }

    // ==========================================
    // S2.15a/b Artifacts Viewer
    // ==========================================
    private static void ShowArtifacts(
        BatchExecutionResult result,
        BatchStorageDescriptor descriptor,
        BatchStorageLoader loader,
        string appDataRoot,
        AppConfig config)
    {
        // Collect all artifacts from all steps
        var allArtifacts = result.StepResults
            .SelectMany(s => s.Artifacts)
            .Concat(result.LogPath is not null ? new[] { result.LogPath } : Array.Empty<string>())
            .Concat(result.ReportPath is not null ? new[] { result.ReportPath } : Array.Empty<string>())
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .ToList();

        const int pageSize = 9;
        var pageIndex = 0;
        var running = true;

        while (running)
        {
            var totalPages = Math.Max(1, (int)Math.Ceiling(allArtifacts.Count / (double)pageSize));
            if (pageIndex >= totalPages)
            {
                pageIndex = totalPages - 1;
            }

            var pageArtifacts = allArtifacts
                .Skip(pageIndex * pageSize)
                .Take(pageSize)
                .ToList();

            UiInteraction.Clear();
            Bare.Primitive.UI.UiConsole.WriteLine("** Batch / Artifacts **");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine($"Batch: {result.BatchName} [{GetShortId(result.BatchId)}]");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine($"Page {pageIndex + 1}/{totalPages}");
            Bare.Primitive.UI.UiConsole.WriteLine();

            if (allArtifacts.Count == 0)
            {
                Bare.Primitive.UI.UiConsole.WriteLine("(no artifacts available)");
            }
            else
            {
                for (var i = 0; i < pageArtifacts.Count; i++)
                {
                    var artifact = pageArtifacts[i];
                    var fileName = Path.GetFileName(artifact);
                    var shortPath = artifact.Length > 60 
                        ? "..." + artifact.Substring(artifact.Length - 57) 
                        : artifact;
                    Bare.Primitive.UI.UiConsole.WriteLine($"{i + 1}) {fileName}");
                    Bare.Primitive.UI.UiConsole.WriteLine($"   {shortPath}");
                }
            }

            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine("** Menu **");
            Bare.Primitive.UI.UiConsole.WriteLine();
            var maxOption = 0;
            if (allArtifacts.Count > 0)
            {
                Bare.Primitive.UI.UiConsole.WriteLine("1. Open artifact folder");
                maxOption = 1;
            }
            if (totalPages > 1)
            {
                Bare.Primitive.UI.UiConsole.WriteLine("2. Next page");
                Bare.Primitive.UI.UiConsole.WriteLine("3. Previous page");
                maxOption = 3;
            }
            Bare.Primitive.UI.UiConsole.WriteLine("0. Back");
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.Write("Select an option: ");

            var selection = UiInteraction.ReadMenuDigit(0, maxOption);
            switch (selection)
            {
                case 0:
                    running = false;
                    break;
                case 1:
                    if (allArtifacts.Count > 0 && pageArtifacts.Count > 0)
                    {
                        // Open the folder of the first artifact on this page
                        var folderPath = Path.GetDirectoryName(pageArtifacts[0]);
                        if (!string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath))
                        {
                            try
                            {
                                System.Diagnostics.Process.Start("explorer.exe", folderPath);
                                Bare.Primitive.UI.UiConsole.WriteLine($"Opening: {folderPath}");
                            }
                            catch
                            {
                                Bare.Primitive.UI.UiConsole.WriteLine("Could not open folder automatically.");
                                Bare.Primitive.UI.UiConsole.WriteLine($"Path: {folderPath}");
                            }
                            UiInteraction.SkipNextClear();
                        }
                    }
                    break;
                case 2:
                    if (totalPages > 1)
                    {
                        pageIndex = (pageIndex + 1) % totalPages;
                    }
                    break;
                case 3:
                    if (totalPages > 1)
                    {
                        pageIndex = pageIndex == 0 ? totalPages - 1 : pageIndex - 1;
                    }
                    break;
            }
        }
    }

    // ==========================================
    // Batch Execution Progress Tracker
    // ==========================================
    private sealed class BatchExecutionProgressTracker : IBatchExecutionProgress
    {
        private bool _cancellationRequested;
        private int _lastInlineProgressLength;
        
        public bool IsCancellationRequested => _cancellationRequested;

        public void OnStepStarting(int stepIndex, string operationType)
        {
            _lastInlineProgressLength = 0;
            Bare.Primitive.UI.UiConsole.WriteLine();
            Bare.Primitive.UI.UiConsole.WriteLine($"Step {stepIndex}: {operationType} ...");
            CheckForCancel();
        }

        public void OnStepCompleted(int stepIndex, string operationType, bool success, string statusMessage)
        {
            var symbol = success ? "OK" : "FAIL";
            Bare.Primitive.UI.UiConsole.WriteLine($"Step {stepIndex}: [{symbol}] {statusMessage}");
            _lastInlineProgressLength = 0;
        }

        public void OnStepProgress(int stepIndex, string operationType, int processed, int total, string? currentItem)
        {
            var progressLine = total > 0
                ? $"  Progress: {processed}/{total} ({(int)((processed * 100.0) / total)}%)"
                : $"  Processed: {processed}";

            var paddedLine = InlineProgressText.PadForRewrite(progressLine, _lastInlineProgressLength);
            Bare.Primitive.UI.UiConsole.Write($"\r{paddedLine}");
            _lastInlineProgressLength = paddedLine.Length;
            
            CheckForCancel();
        }

        private void CheckForCancel()
        {
            if (Bare.Primitive.UI.UiConsole.KeyAvailable)
            {
                var key = Bare.Primitive.UI.UiConsole.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Escape)
                {
                    _cancellationRequested = true;
                    Bare.Primitive.UI.UiConsole.WriteLine();
                    Bare.Primitive.UI.UiConsole.WriteLine("Cancelling...");
                }
            }
        }
    }
}
