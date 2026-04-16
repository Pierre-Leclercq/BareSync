using System.Text.Json.Nodes;
using BareSync.App;
using BareSync.Infra;
using BareSync.UI;

namespace BareSync.Domain;

/// <summary>
/// Result of a batch step execution.
/// </summary>
public sealed record BatchStepResult(
    int StepIndex,
    string OperationType,
    bool Success,
    string StatusMessage,
    TimeSpan Duration,
    IReadOnlyList<string> Artifacts);

/// <summary>
/// Overall result of a batch execution.
/// </summary>
public sealed record BatchExecutionResult(
    bool Success,
    string BatchId,
    string BatchName,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    IReadOnlyList<BatchStepResult> StepResults,
    string? LogPath = null,
    string? ReportPath = null);

/// <summary>
/// Callback interface for batch execution progress reporting.
/// </summary>
public interface IBatchExecutionProgress
{
    void OnStepStarting(int stepIndex, string operationType);
    void OnStepCompleted(int stepIndex, string operationType, bool success, string statusMessage);
    void OnStepProgress(int stepIndex, string operationType, int processed, int total, string? currentItem);
    bool IsCancellationRequested { get; }
}

/// <summary>
/// Executes batch steps sequentially with progress reporting and cancellation support.
/// </summary>
internal static class BatchRunner
{
    /// <summary>
    /// Executes a batch with the provided execution readiness (already validated).
    /// </summary>
    public static async Task<BatchExecutionResult> ExecuteAsync(
        BatchV0 batch,
        BatchExecutionReadiness readiness,
        AppConfig config,
        IBatchExecutionProgress progress,
        CancellationToken cancellationToken)
    {
        return await ExecuteAsync(
                batch,
                readiness,
                config,
                progress,
                secrets: null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a batch with provided secrets for encrypted operations.
    /// </summary>
    public static async Task<BatchExecutionResult> ExecuteAsync(
        BatchV0 batch,
        BatchExecutionReadiness readiness,
        AppConfig config,
        IBatchExecutionProgress progress,
        IReadOnlyDictionary<string, string>? secrets,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var stepResults = new List<BatchStepResult>();
        var logger = TryCreateBatchLogger(batch, config, out var logPath);
        
        // Build effective context for each step
        var stepContexts = BuildStepContexts(batch, config);
        try
        {
            for (int i = 0; i < batch.Steps.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested || progress.IsCancellationRequested)
                {
                    stepResults.Add(new BatchStepResult(
                        i + 1,
                        batch.Steps[i].OperationType,
                        false,
                        "Cancelled",
                        TimeSpan.Zero,
                        Array.Empty<string>()));
                    AddNotRunRemainingSteps(batch, stepResults, i + 1, "NotRun: cancelled before execution.");
                    
                    logger?.Info($"Step {i + 1} cancelled.");
                    return new BatchExecutionResult(
                        false,
                        batch.Id,
                        batch.Name,
                        startedAt,
                        DateTimeOffset.UtcNow,
                        stepResults,
                        logPath);
                }

                var step = batch.Steps[i];
                var stepContext = stepContexts[i];
                
                logger?.Info($"Step {i + 1} starting: {step.OperationType}");
                progress.OnStepStarting(i + 1, step.OperationType);
                var stepStart = DateTimeOffset.UtcNow;
                
                try
                {
                    var result = await ExecuteStepAsync(
                            step,
                            stepContext,
                            config,
                            progress,
                            i + 1,
                            secrets,
                            logger,
                            cancellationToken)
                        .ConfigureAwait(false);
                    var duration = DateTimeOffset.UtcNow - stepStart;
                    
                    stepResults.Add(result with { Duration = duration });
                    progress.OnStepCompleted(i + 1, step.OperationType, result.Success, result.StatusMessage);
                    logger?.Info($"Step {i + 1} {(result.Success ? "OK" : "FAIL")}: {result.StatusMessage}");

                    if (!result.Success)
                    {
                        AddNotRunRemainingSteps(batch, stepResults, i + 1, "NotRun: previous step failed.");
                        logger?.Warn($"Stopping batch after failed step {i + 1}.");
                        return new BatchExecutionResult(
                            false,
                            batch.Id,
                            batch.Name,
                            startedAt,
                            DateTimeOffset.UtcNow,
                            stepResults,
                            logPath);
                    }
                }
                catch (OperationCanceledException)
                {
                    var duration = DateTimeOffset.UtcNow - stepStart;
                    stepResults.Add(new BatchStepResult(
                        i + 1,
                        step.OperationType,
                        false,
                        "Cancelled",
                        duration,
                        Array.Empty<string>()));
                    AddNotRunRemainingSteps(batch, stepResults, i + 1, "NotRun: cancelled.");
                    
                    progress.OnStepCompleted(i + 1, step.OperationType, false, "Cancelled");
                    logger?.Warn($"Step {i + 1} cancelled.");
                    
                    return new BatchExecutionResult(
                        false,
                        batch.Id,
                        batch.Name,
                        startedAt,
                        DateTimeOffset.UtcNow,
                        stepResults,
                        logPath);
                }
                catch (Exception ex)
                {
                    var duration = DateTimeOffset.UtcNow - stepStart;
                    stepResults.Add(new BatchStepResult(
                        i + 1,
                        step.OperationType,
                        false,
                        $"Error: {ex.Message}",
                        duration,
                        Array.Empty<string>()));
                    AddNotRunRemainingSteps(batch, stepResults, i + 1, "NotRun: previous step failed.");
                    
                    progress.OnStepCompleted(i + 1, step.OperationType, false, $"Error: {ex.Message}");
                    logger?.Error($"Step {i + 1} failed ({step.OperationType}): {ex.Message}", ex);

                    return new BatchExecutionResult(
                        false,
                        batch.Id,
                        batch.Name,
                        startedAt,
                        DateTimeOffset.UtcNow,
                        stepResults,
                        logPath);
                }
            }

            var completedAt = DateTimeOffset.UtcNow;
            var allSuccess = stepResults.All(r => r.Success);
            logger?.Info($"Batch completed. Success={allSuccess.ToString().ToLowerInvariant()}");
            
            return new BatchExecutionResult(
                allSuccess,
                batch.Id,
                batch.Name,
                startedAt,
                completedAt,
                stepResults,
                logPath);
        }
        finally
        {
            logger?.Dispose();
        }
    }

    private static void AddNotRunRemainingSteps(
        BatchV0 batch,
        ICollection<BatchStepResult> stepResults,
        int nextStepIndex,
        string reason)
    {
        for (var index = nextStepIndex; index < batch.Steps.Count; index++)
        {
            var step = batch.Steps[index];
            stepResults.Add(new BatchStepResult(
                index + 1,
                step.OperationType,
                false,
                reason,
                TimeSpan.Zero,
                Array.Empty<string>()));
        }
    }

    /// <summary>
    /// Builds the effective context for each step by merging batch context with step overrides.
    /// </summary>
    private static List<JsonObject> BuildStepContexts(BatchV0 batch, AppConfig config)
    {
        var contexts = new List<JsonObject>();
        var batchContext = batch.ContextSnapshot ?? new JsonObject();
        
        foreach (var step in batch.Steps)
        {
            var effectiveContext = new JsonObject();
            
            // Start with batch-level context
            foreach (var property in batchContext)
            {
                effectiveContext[property.Key] = property.Value?.DeepClone();
            }
            
            // Apply step overrides
            var overrides = step.ContextOverrides as JsonObject;
            if (overrides is not null)
            {
                foreach (var property in overrides)
                {
                    effectiveContext[property.Key] = property.Value?.DeepClone();
                }
            }
            
            // Ensure required fields from config if still missing
            EnsureField(effectiveContext, BatchContextFields.SourceRoot, config.SourceRoot);
            EnsureField(effectiveContext, BatchContextFields.MirrorRoot, config.MirrorRoot);
            EnsureField(effectiveContext, BatchContextFields.SourceIndexCsvPath, config.SourceIndexCsvPath);
            EnsureField(effectiveContext, BatchContextFields.DestIndexCsvPath, config.DestIndexCsvPath);
            EnsureField(effectiveContext, BatchContextFields.EncryptedOutputRoot, config.EncryptedOutputRoot);
            EnsureField(effectiveContext, BatchContextFields.RestoreRoot, config.RestoreRoot);
            
            contexts.Add(effectiveContext);
        }
        
        return contexts;
    }

    private static void EnsureField(JsonObject context, string field, string? value)
    {
        if (!context.ContainsKey(field) && !string.IsNullOrWhiteSpace(value))
        {
            context[field] = value;
        }
    }

    private static AppConfig BuildConfigForStep(AppConfig baseConfig, JsonObject effectiveContext)
    {
        return new AppConfig
        {
            SourceRoot = ResolvePathContextValue(GetContextValue(effectiveContext, BatchContextFields.SourceRoot, baseConfig.SourceRoot)),
            MirrorRoot = ResolvePathContextValue(GetContextValue(effectiveContext, BatchContextFields.MirrorRoot, baseConfig.MirrorRoot)),
            Mirror = GetContextBooleanValue(effectiveContext, BatchContextFields.Mirror, baseConfig.Mirror),
            SourceIndexCsvPath = ResolvePathContextValue(GetContextValue(effectiveContext, BatchContextFields.SourceIndexCsvPath, baseConfig.SourceIndexCsvPath)),
            DestIndexCsvPath = ResolvePathContextValue(GetContextValue(effectiveContext, BatchContextFields.DestIndexCsvPath, baseConfig.DestIndexCsvPath)),
            EncryptedOutputRoot = ResolvePathContextValue(GetContextValue(effectiveContext, BatchContextFields.EncryptedOutputRoot, baseConfig.EncryptedOutputRoot)),
            RestoreRoot = ResolvePathContextValue(GetContextValue(effectiveContext, BatchContextFields.RestoreRoot, baseConfig.RestoreRoot)),
            RestoreSmartMode = baseConfig.RestoreSmartMode,
            LogDebug = baseConfig.LogDebug,
            ExcludeFileNames = [.. baseConfig.ExcludeFileNames],
            ExcludeDirectoryNames = [.. baseConfig.ExcludeDirectoryNames],
            ExcludePathGlobs = [.. baseConfig.ExcludePathGlobs],
            OutputCsvFileName = baseConfig.OutputCsvFileName
        };
    }

    private static string ResolvePathContextValue(string value)
    {
        var resolution = ConfigService.ResolveDriveLabelPath(value);
        return resolution.Status == DriveLabelResolutionStatus.Resolved
            ? resolution.ResolvedPath
            : value;
    }

    private static string GetContextValue(JsonObject context, string field, string fallback)
    {
        if (context.TryGetPropertyValue(field, out var value)
            && value is not null
            && !string.IsNullOrWhiteSpace(value.GetValue<string>()))
        {
            return value.GetValue<string>()!;
        }

        return fallback;
    }

    private static bool GetContextBooleanValue(JsonObject context, string field, bool fallback)
    {
        if (!context.TryGetPropertyValue(field, out var value) || value is null)
        {
            return fallback;
        }

        if (value is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<bool>(out var boolValue))
            {
                return boolValue;
            }

            if (jsonValue.TryGetValue<string>(out var text)
                && !string.IsNullOrWhiteSpace(text))
            {
                if (bool.TryParse(text, out var parsedBool))
                {
                    return parsedBool;
                }

                if (text == "1")
                {
                    return true;
                }

                if (text == "0")
                {
                    return false;
                }
            }
        }

        return fallback;
    }

    private static IReadOnlyList<string> BuildArtifacts(params string?[] paths)
    {
        var artifacts = new List<string>();
        foreach (var path in paths)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                artifacts.Add(path);
            }
        }
        return artifacts;
    }

    private static string GetSecretSlotForOperation(string operationType, string encryptedOutputRoot)
    {
        return BatchSecretSlot.GetSecretSlot(operationType, encryptedOutputRoot);
    }

    private static string? TryGetSecretForOperation(
        IReadOnlyDictionary<string, string>? secrets,
        string operationType,
        string encryptedOutputRoot)
    {
        var slot = GetSecretSlotForOperation(operationType, encryptedOutputRoot);
        if (string.IsNullOrWhiteSpace(slot) || secrets is null)
        {
            return null;
        }

        if (secrets.TryGetValue(slot, out var value))
        {
            return value;
        }

        // Backward compatibility with previous single-slot key.
        return secrets.TryGetValue("EncryptedOutputRoot", out var legacyValue)
            ? legacyValue
            : null;
    }

    /// <summary>
    /// Executes a single batch step.
    /// </summary>
    private static async Task<BatchStepResult> ExecuteStepAsync(
        BatchStepV0 step,
        JsonObject effectiveContext,
        AppConfig baseConfig,
        IBatchExecutionProgress progress,
        int stepNumber,
        IReadOnlyDictionary<string, string>? secrets,
        SimpleFileLogger? logger,
        CancellationToken cancellationToken)
    {
        var operationType = step.OperationType;
        var stepConfig = BuildConfigForStep(baseConfig, effectiveContext);
        var stepProgress = new BatchProgressAdapter(progress, stepNumber, operationType);

        return operationType switch
        {
            BatchOperationCatalog.OperationTypeOneWaySyncApply => await ExecuteOneWaySyncAsync(
                step,
                stepConfig,
                dryRun: false,
                stepProgress,
                stepNumber,
                cancellationToken).ConfigureAwait(false),

            BatchOperationCatalog.OperationTypeOneWaySyncDryRun => await ExecuteOneWaySyncAsync(
                step,
                stepConfig,
                dryRun: true,
                stepProgress,
                stepNumber,
                cancellationToken).ConfigureAwait(false),

            BatchOperationCatalog.OperationTypeRefreshIndexesFull => await ExecuteRefreshIndexesAsync(
                step,
                stepConfig,
                incremental: false,
                stepProgress,
                stepNumber,
                cancellationToken).ConfigureAwait(false),

            BatchOperationCatalog.OperationTypeRefreshIndexesSmart => await ExecuteRefreshIndexesAsync(
                step,
                stepConfig,
                incremental: true,
                stepProgress,
                stepNumber,
                cancellationToken).ConfigureAwait(false),

            BatchOperationCatalog.OperationTypeCreateEncryptedFolder => await ExecuteCreateEncryptedFolderAsync(
                step,
                stepConfig,
                stepProgress,
                stepNumber,
                secrets,
                logger,
                cancellationToken).ConfigureAwait(false),

            BatchOperationCatalog.OperationTypeRefreshEncryptedFolder => await ExecuteRefreshEncryptedFolderAsync(
                step,
                stepConfig,
                stepProgress,
                stepNumber,
                secrets,
                logger,
                cancellationToken).ConfigureAwait(false),

            BatchOperationCatalog.OperationTypeRestoreEncryptedFiles => await ExecuteRestoreEncryptedFilesAsync(
                step,
                stepConfig,
                stepProgress,
                stepNumber,
                secrets,
                logger,
                cancellationToken).ConfigureAwait(false),

            _ => new BatchStepResult(
                stepNumber,
                operationType,
                false,
                $"Unknown operation type: {operationType}",
                TimeSpan.Zero,
                Array.Empty<string>())
        };
    }

    private static SimpleFileLogger? TryCreateBatchLogger(BatchV0 batch, AppConfig config, out string? logPath)
    {
        logPath = null;
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var logsDir = Path.Combine(baseDir, "log");
            Directory.CreateDirectory(logsDir);
            logPath = Path.Combine(
                logsDir,
                $"baresync_batch_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.log");

            var logger = new SimpleFileLogger(logPath)
            {
                IsDebugEnabled = config.LogDebug
            };
            logger.Info("Starting batch execution");
            logger.Info($"BatchId={batch.Id}");
            logger.Info($"BatchName={batch.Name}");
            logger.Info($"Steps={batch.Steps.Count}");
            logger.Debug("Batch debug logging is enabled.");
            return logger;
        }
        catch
        {
            logPath = null;
            return null;
        }
    }

    private static async Task<BatchStepResult> ExecuteOneWaySyncAsync(
        BatchStepV0 step,
        AppConfig config,
        bool dryRun,
        IProgress<ProgressInfo> progress,
        int stepNumber,
        CancellationToken cancellationToken)
    {
        var summary = await SyncOneWay.RunAsync(config, cancellationToken, dryRun, progress).ConfigureAwait(false);
        var success = summary.ErrorCount == 0;
        var status = string.IsNullOrWhiteSpace(summary.StatusLine) ? summary.StatusLabel : summary.StatusLine;
        var artifacts = BuildArtifacts(summary.LogFilePath, summary.ReportFilePath);

        return new BatchStepResult(
            stepNumber,
            step.OperationType,
            success,
            status,
            TimeSpan.Zero,
            artifacts);
    }

    private static async Task<BatchStepResult> ExecuteRefreshIndexesAsync(
        BatchStepV0 step,
        AppConfig config,
        bool incremental,
        IProgress<ProgressInfo> progress,
        int stepNumber,
        CancellationToken cancellationToken)
    {
        if (!incremental)
        {
            IndexRefreshService.DeleteIndexArtifacts(config.SourceIndexCsvPath);
            IndexRefreshService.DeleteIndexArtifacts(config.DestIndexCsvPath);
            var result = await IndexRefreshService.RefreshIndexesWithProgressAsync(
                    config,
                    cancellationToken,
                    progress,
                    incremental: false)
                .ConfigureAwait(false);

            var artifacts = BuildArtifacts(result.LogPath, result.ReportPath);
            return new BatchStepResult(
                stepNumber,
                step.OperationType,
                result.SuccessOrWarningFlag,
                result.StatusLine,
                TimeSpan.Zero,
                artifacts);
        }

        // Batch smart: verify and purge existing index rows against current disk state.
        // If an index is missing/invalid, rebuild it first so verification can proceed.
        var sourceIndexValidationError = await EnsureSmartBatchIndexAsync(
                config.SourceRoot,
                config.SourceIndexCsvPath,
                "source",
                config,
                progress,
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(sourceIndexValidationError))
        {
            return new BatchStepResult(
                stepNumber,
                step.OperationType,
                false,
                sourceIndexValidationError,
                TimeSpan.Zero,
                Array.Empty<string>());
        }

        var destIndexValidationError = await EnsureSmartBatchIndexAsync(
                config.MirrorRoot,
                config.DestIndexCsvPath,
                "destination",
                config,
                progress,
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(destIndexValidationError))
        {
            return new BatchStepResult(
                stepNumber,
                step.OperationType,
                false,
                destIndexValidationError,
                TimeSpan.Zero,
                Array.Empty<string>());
        }

        var prunedSourceEntries = await IndexRefreshService.PruneMissingEntriesFromIndexAsync(
                config.SourceRoot,
                config.SourceIndexCsvPath,
                cancellationToken,
                progress,
                "Verifying source index entries against disk...")
            .ConfigureAwait(false);

        var prunedDestinationEntries = await IndexRefreshService.PruneMissingEntriesFromIndexAsync(
                config.MirrorRoot,
                config.DestIndexCsvPath,
                cancellationToken,
                progress,
                "Verifying destination index entries against disk...")
            .ConfigureAwait(false);

        // In batch smart mode we always invalidate resume artifacts to avoid
        // stale rows from old .work/.checkpoint state being reintroduced.
        IndexRefreshService.DeleteResumeArtifacts(config.SourceIndexCsvPath);
        IndexRefreshService.DeleteResumeArtifacts(config.DestIndexCsvPath);

        var statusLine =
            $"Smart batch index verification completed. Pruned stale index entries: source={prunedSourceEntries}, destination={prunedDestinationEntries}.";

        return new BatchStepResult(
            stepNumber,
            step.OperationType,
            true,
            statusLine,
            TimeSpan.Zero,
            Array.Empty<string>());
    }

    private static async Task<string?> EnsureSmartBatchIndexAsync(
        string rootPath,
        string indexPath,
        string label,
        AppConfig config,
        IProgress<ProgressInfo> progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(indexPath))
        {
            return $"Cannot run smart batch index verification: {label} index path is not configured.";
        }

        var validationError = await TryReadSmartBatchIndexAsync(indexPath, label, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(validationError))
        {
            return null;
        }

        await IndexRefreshService.RefreshIndexAsync(
                rootPath,
                indexPath,
                cancellationToken,
                progress,
                $"Smart refreshing CRC indexes ({label})...",
                incremental: true,
                config)
            .ConfigureAwait(false);

        var validationAfterRebuild = await TryReadSmartBatchIndexAsync(indexPath, label, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(validationAfterRebuild))
        {
            return null;
        }

        if (!File.Exists(indexPath))
        {
            return
                $"Cannot run smart batch index verification: {label} index is missing ({indexPath}) and could not be rebuilt from {label} root ({rootPath}).";
        }

        return validationAfterRebuild;
    }

    private static async Task<string?> TryReadSmartBatchIndexAsync(
        string indexPath,
        string label,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(indexPath))
        {
            return $"Cannot run smart batch index verification: {label} index is missing ({indexPath}).";
        }

        try
        {
            _ = await CsvIndexReader.ReadAsync(indexPath, cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (Exception ex)
        {
            return $"Cannot run smart batch index verification: {label} index is invalid ({indexPath}). Details: {ex.Message}";
        }
    }

    private static async Task<BatchStepResult> ExecuteCreateEncryptedFolderAsync(
        BatchStepV0 step,
        AppConfig config,
        IProgress<ProgressInfo> progress,
        int stepNumber,
        IReadOnlyDictionary<string, string>? secrets,
        SimpleFileLogger? logger,
        CancellationToken cancellationToken)
    {
        var password = TryGetSecretForOperation(secrets, step.OperationType, config.EncryptedOutputRoot);
        if (string.IsNullOrWhiteSpace(password))
        {
            return new BatchStepResult(
                stepNumber,
                step.OperationType,
                false,
                "Missing secret for encrypted operation.",
                TimeSpan.Zero,
                Array.Empty<string>());
        }

        var encryptedService = new EncryptedFolderService();
        var plan = await encryptedService.BuildEncryptedPlanAsync(config, cancellationToken, logger).ConfigureAwait(false);
        var status = await encryptedService.CreateEncryptedIndexAsync(
                config,
                password,
                plan.Entries,
                plan.DataPlan,
                cancellationToken,
                logger)
            .ConfigureAwait(false);

        var statusLine = status?.StatusLine ?? "Encrypted index creation failed.";
        var success = status?.SuccessOrWarningFlag ?? !statusLine.Contains("failed", StringComparison.OrdinalIgnoreCase);
        var artifacts = BuildArtifacts(status?.LogPath, status?.ReportPath);

        return new BatchStepResult(
            stepNumber,
            step.OperationType,
            success,
            statusLine,
            TimeSpan.Zero,
            artifacts);
    }

    private static async Task<BatchStepResult> ExecuteRefreshEncryptedFolderAsync(
        BatchStepV0 step,
        AppConfig config,
        IProgress<ProgressInfo> progress,
        int stepNumber,
        IReadOnlyDictionary<string, string>? secrets,
        SimpleFileLogger? logger,
        CancellationToken cancellationToken)
    {
        var password = TryGetSecretForOperation(secrets, step.OperationType, config.EncryptedOutputRoot);
        if (string.IsNullOrWhiteSpace(password))
        {
            return new BatchStepResult(
                stepNumber,
                step.OperationType,
                false,
                "Missing secret for encrypted operation.",
                TimeSpan.Zero,
                Array.Empty<string>());
        }

        var encryptedService = new EncryptedFolderService();
        var result = await encryptedService.RefreshEncryptedFolderAsync(
                config,
                password,
                progress,
                cancellationToken,
                logger)
            .ConfigureAwait(false);

        var statusLine = string.IsNullOrWhiteSpace(result.StatusLine)
            ? (result.SuccessOrWarningFlag ? "Encrypted folder refresh completed." : "Encrypted folder refresh failed.")
            : result.StatusLine;
        var artifacts = BuildArtifacts(result.LogPath, result.ReportPath);

        return new BatchStepResult(
            stepNumber,
            step.OperationType,
            result.SuccessOrWarningFlag,
            statusLine,
            TimeSpan.Zero,
            artifacts);
    }

    private static async Task<BatchStepResult> ExecuteRestoreEncryptedFilesAsync(
        BatchStepV0 step,
        AppConfig config,
        IProgress<ProgressInfo> progress,
        int stepNumber,
        IReadOnlyDictionary<string, string>? secrets,
        SimpleFileLogger? logger,
        CancellationToken cancellationToken)
    {
        var password = TryGetSecretForOperation(secrets, step.OperationType, config.EncryptedOutputRoot);
        if (string.IsNullOrWhiteSpace(password))
        {
            logger?.Error("Missing secret for encrypted restore operation");
            return new BatchStepResult(
                stepNumber,
                step.OperationType,
                false,
                "Missing secret for encrypted operation.",
                TimeSpan.Zero,
                Array.Empty<string>());
        }

        logger?.Info($"Starting encrypted restore: EncryptedOutputRoot={config.EncryptedOutputRoot}, RestoreRoot={config.RestoreRoot}");
        var encryptedService = new EncryptedFolderService();
        var result = await encryptedService.RestoreEncryptedFilesAsync(
                config,
                password,
                progress,
                cancellationToken,
                logger)
            .ConfigureAwait(false);

        var statusLine = string.IsNullOrWhiteSpace(result.StatusLine)
            ? (result.SuccessOrWarningFlag ? "Encrypted restore completed." : "Encrypted restore failed.")
            : result.StatusLine;
        
        if (!result.SuccessOrWarningFlag)
        {
            logger?.Error($"Encrypted restore failed: {statusLine}");
        }
        else if (statusLine.Contains("warning", StringComparison.OrdinalIgnoreCase))
        {
            logger?.Warn($"Encrypted restore completed with warnings: {statusLine}");
        }
        else
        {
            logger?.Info($"Encrypted restore completed successfully: {statusLine}");
        }
        
        var artifacts = BuildArtifacts(result.LogPath, result.ReportPath);

        return new BatchStepResult(
            stepNumber,
            step.OperationType,
            result.SuccessOrWarningFlag,
            statusLine,
            TimeSpan.Zero,
            artifacts);
    }

    private sealed class BatchProgressAdapter : IProgress<ProgressInfo>
    {
        private readonly IBatchExecutionProgress _progress;
        private readonly int _stepIndex;
        private readonly string _operationType;
        private string? _lastOperationTitle;

        public BatchProgressAdapter(IBatchExecutionProgress progress, int stepIndex, string operationType)
        {
            _progress = progress;
            _stepIndex = stepIndex;
            _operationType = operationType;
        }

        public void Report(ProgressInfo value)
        {
            if (value is null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(value.OperationTitle))
            {
                _lastOperationTitle = value.OperationTitle;
            }

            var phaseLabel = !string.IsNullOrWhiteSpace(value.OperationTitle)
                ? value.OperationTitle
                : !string.IsNullOrWhiteSpace(_lastOperationTitle)
                    ? _lastOperationTitle
                    : value.LastLine ?? value.CurrentItem;

            _progress.OnStepProgress(_stepIndex, _operationType, value.Processed, value.Total, phaseLabel);
        }
    }
}
