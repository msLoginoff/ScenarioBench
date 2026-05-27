namespace ScenarioBench.Abstractions;

public sealed record ScenarioValidationResult(
    string Name,
    ValidationStatus Status,
    IReadOnlyList<ValidationIssue> Issues,
    IReadOnlyDictionary<string, string> Metrics)
{
    public bool Passed => Status is ValidationStatus.Passed or ValidationStatus.Skipped;

    public static ScenarioValidationResult Pass(
        string name,
        IReadOnlyDictionary<string, string>? metrics = null)
    {
        return new ScenarioValidationResult(
            name,
            ValidationStatus.Passed,
            [],
            metrics ?? new Dictionary<string, string>());
    }

    public static ScenarioValidationResult Fail(
        string name,
        IReadOnlyList<ValidationIssue> issues,
        IReadOnlyDictionary<string, string>? metrics = null)
    {
        return new ScenarioValidationResult(
            name,
            ValidationStatus.Failed,
            issues,
            metrics ?? new Dictionary<string, string>());
    }

    public static ScenarioValidationResult Skip(
        string name,
        string reason)
    {
        return new ScenarioValidationResult(
            name,
            ValidationStatus.Skipped,
            [new ValidationIssue(ValidationSeverity.Info, reason)],
            new Dictionary<string, string>());
    }
}

public sealed record ValidationIssue(
    ValidationSeverity Severity,
    string Message,
    string? Code = null,
    string? Path = null);

public enum ValidationStatus
{
    Passed,
    Failed,
    Skipped
}

public enum ValidationSeverity
{
    Info,
    Warning,
    Error
}
