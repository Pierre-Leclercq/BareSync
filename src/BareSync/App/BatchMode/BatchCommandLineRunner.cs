using BareSync.App.Common;
using BareSync.Domain;
using BareSync.Infra;

namespace BareSync.App.BatchMode;

internal sealed class BatchCommandLineRunner
{
    private readonly BatchStorageLoader _loader;
    private readonly string _appDataRoot;
    private readonly AppConfig _config;

    public BatchCommandLineRunner(BatchStorageLoader loader, string appDataRoot, AppConfig config)
    {
        _loader = loader;
        _appDataRoot = appDataRoot;
        _config = config;
    }

    public async Task<int> RunAsync(IReadOnlyList<string> requestedBatchNames)
    {
        var allDescriptors = _loader.LoadAll(_appDataRoot);
        var summaries = new List<BatchCliRunSummary>();

        foreach (var requestedName in requestedBatchNames)
        {
            var descriptor = allDescriptors.FirstOrDefault(entry =>
                string.Equals(entry.Name, requestedName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.Id, requestedName, StringComparison.OrdinalIgnoreCase));

            if (descriptor is null)
            {
                summaries.Add(new BatchCliRunSummary(
                    requestedName,
                    requestedName,
                    BatchRunStepStatus.Fail,
                    "Batch not found."));
                continue;
            }

            var summary = await ExecuteDescriptorAsync(requestedName, descriptor).ConfigureAwait(false);
            summaries.Add(summary);
        }

        PrintSummary(summaries);

        var hasFailure = summaries.Any(summary => summary.Status is not (BatchRunStepStatus.Success or BatchRunStepStatus.Warning));
        return hasFailure ? 1 : 0;
    }

    private async Task<BatchCliRunSummary> ExecuteDescriptorAsync(string requestedName, BatchStorageDescriptor descriptor)
    {
        var batch = BatchUiHelpers.LoadBatchV0(descriptor.Path);
        if (batch is null)
        {
            return new BatchCliRunSummary(requestedName, descriptor.Name, BatchRunStepStatus.Fail, "Invalid batch file.");
        }

        var readiness = BatchExecutionReadinessEvaluator.EvaluateBatchExecutionReadiness(batch);
        if (readiness.ExecutionReadiness != BatchExecutionReadinessStatus.Ready)
        {
            var reason = readiness.Errors.FirstOrDefault() ?? "Batch is not executable.";
            return new BatchCliRunSummary(requestedName, descriptor.Name, BatchRunStepStatus.Fail, reason);
        }

        IReadOnlyDictionary<string, string>? secrets = null;
        if (readiness.RequiresSecret)
        {
            if (!SecretStoreProvider.IsAvailable)
            {
                return new BatchCliRunSummary(
                    requestedName,
                    descriptor.Name,
                    BatchRunStepStatus.Fail,
                    "Vault indisponible pour un batch nécessitant un secret.");
            }

            var requirements = BatchSecretResolver.GetRequiredSecretSlots(batch, _config);
            var loaded = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var requirement in requirements)
            {
                if (!SecretStoreProvider.TryLoadSecret(requirement.SlotKey, out var secret))
                {
                    return new BatchCliRunSummary(
                        requestedName,
                        descriptor.Name,
                        BatchRunStepStatus.Fail,
                        $"Secret manquant dans le vault pour le scope {requirement.Scope}.");
                }

                loaded[requirement.SlotKey] = secret;
            }

            secrets = loaded;
        }

        var progress = new SilentBatchExecutionProgress();
        var result = await BatchRunner.ExecuteAsync(
                batch,
                readiness,
                _config,
                progress,
                secrets,
                CancellationToken.None)
            .ConfigureAwait(false);

        var status = BatchRunStatus.GetOverallStatus(result);
        var message = result.StepResults.LastOrDefault()?.StatusMessage ?? BatchRunStatus.ToLabel(status);

        return new BatchCliRunSummary(
            requestedName,
            descriptor.Name,
            status,
            message,
            result.LogPath,
            result.ReportPath);
    }

    private static void PrintSummary(IReadOnlyList<BatchCliRunSummary> summaries)
    {
        Bare.Primitive.UI.UiConsole.WriteLine("** Batch CLI Summary **");
        Bare.Primitive.UI.UiConsole.WriteLine();
        foreach (var summary in summaries)
        {
            Bare.Primitive.UI.UiConsole.WriteLine($"- {summary.DisplayName}: {BatchRunStatus.ToLabel(summary.Status)}");
            if (!string.IsNullOrWhiteSpace(summary.Message))
            {
                Bare.Primitive.UI.UiConsole.WriteLine($"  {summary.Message}");
            }

            if (!string.IsNullOrWhiteSpace(summary.LogPath))
            {
                Bare.Primitive.UI.UiConsole.WriteLine($"  Log: {summary.LogPath}");
            }

            if (!string.IsNullOrWhiteSpace(summary.ReportPath))
            {
                Bare.Primitive.UI.UiConsole.WriteLine($"  Report: {summary.ReportPath}");
            }
        }
    }

    private sealed record BatchCliRunSummary(
        string RequestedName,
        string ResolvedName,
        BatchRunStepStatus Status,
        string Message,
        string? LogPath = null,
        string? ReportPath = null)
    {
        public string DisplayName => string.Equals(RequestedName, ResolvedName, StringComparison.OrdinalIgnoreCase)
            ? ResolvedName
            : $"{RequestedName} (resolved: {ResolvedName})";
    }

    private sealed class SilentBatchExecutionProgress : IBatchExecutionProgress
    {
        public void OnStepStarting(int stepIndex, string operationType)
        {
        }

        public void OnStepCompleted(int stepIndex, string operationType, bool success, string statusMessage)
        {
        }

        public void OnStepProgress(int stepIndex, string operationType, int processed, int total, string? currentItem)
        {
        }

        public bool IsCancellationRequested => false;
    }
}
