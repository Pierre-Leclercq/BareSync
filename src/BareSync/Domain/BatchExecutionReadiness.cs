namespace BareSync.Domain;

internal enum BatchSchemaValidity
{
    Valid,
    Invalid,
    Incompatible
}

internal enum BatchExecutionReadinessStatus
{
    Ready,
    NonExecutable
}

internal sealed record BatchPreflightStepSummary(
    int StepIndex,
    string OperationType,
    string ParamSummary,
    bool RequiresConfirmation,
    bool RequiresSecret);

internal sealed record BatchExecutionReadiness(
    BatchSchemaValidity SchemaValidity,
    BatchExecutionReadinessStatus ExecutionReadiness,
    IReadOnlyList<string> Errors,
    bool RequiresConfirmation,
    bool RequiresSecret,
    IReadOnlyList<BatchPreflightStepSummary> Steps);
    