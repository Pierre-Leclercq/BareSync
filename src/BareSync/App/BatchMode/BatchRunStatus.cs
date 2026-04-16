using BareSync.Domain;

namespace BareSync.App.BatchMode;

internal enum BatchRunStepStatus
{
    Success,
    Warning,
    Fail,
    Canceled,
    NotRun
}

internal static class BatchRunStatus
{
    public static BatchRunStepStatus GetStepStatus(BatchStepResult step)
    {
        if (step.StatusMessage.StartsWith("NotRun", StringComparison.OrdinalIgnoreCase))
        {
            return BatchRunStepStatus.NotRun;
        }

        if (step.StatusMessage.Contains("cancel", StringComparison.OrdinalIgnoreCase))
        {
            return BatchRunStepStatus.Canceled;
        }

        if (!step.Success)
        {
            return BatchRunStepStatus.Fail;
        }

        if (step.StatusMessage.Contains("warning", StringComparison.OrdinalIgnoreCase))
        {
            return BatchRunStepStatus.Warning;
        }

        return BatchRunStepStatus.Success;
    }

    public static BatchRunStepStatus GetOverallStatus(BatchExecutionResult result)
    {
        var statuses = result.StepResults.Select(GetStepStatus).ToList();
        if (statuses.Any(status => status == BatchRunStepStatus.Canceled))
        {
            return BatchRunStepStatus.Canceled;
        }

        if (statuses.Any(status => status == BatchRunStepStatus.Fail))
        {
            return BatchRunStepStatus.Fail;
        }

        if (statuses.Any(status => status == BatchRunStepStatus.Warning))
        {
            return BatchRunStepStatus.Warning;
        }

        return BatchRunStepStatus.Success;
    }

    public static string ToLabel(BatchRunStepStatus status)
    {
        return status switch
        {
            BatchRunStepStatus.Success => "Success",
            BatchRunStepStatus.Warning => "Warning",
            BatchRunStepStatus.Fail => "Fail",
            BatchRunStepStatus.Canceled => "Canceled",
            BatchRunStepStatus.NotRun => "NotRun",
            _ => "Fail"
        };
    }
}
