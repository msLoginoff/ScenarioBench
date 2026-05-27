namespace ScenarioBench.Abstractions;

public interface IScenarioPack
{
    string Name { get; }

    IReadOnlyList<IScenarioWorkflow> Workflows { get; }
}

public interface IScenarioWorkflow
{
    string Name { get; }

    ValueTask PrepareAsync(
        ScenarioPrepareContext context,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ScenarioValidationResult>> ValidateAsync(
        ScenarioValidationContext context,
        CancellationToken cancellationToken = default);
}

public interface IScenarioLoadWorkflow : IScenarioWorkflow
{
    ValueTask<ScenarioStepResult> ExecuteAsync(
        ScenarioExecutionContext context,
        CancellationToken cancellationToken = default);
}

public sealed record ScenarioRunContext(
    string RunId,
    string RunName,
    string ArtifactDirectory,
    ScenarioRunMetadata Metadata);

public sealed record ScenarioRunMetadata(
    string? Environment,
    string? Branch,
    string? Commit,
    string? Version,
    string? Build,
    string? Seed,
    string? Notes,
    IReadOnlyDictionary<string, string> Tags);

public sealed record ScenarioTargetContext(
    string Name,
    Uri BaseUrl,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyDictionary<string, string> Tags);

public sealed record ScenarioPrepareContext(
    ScenarioRunContext Run,
    ScenarioTargetContext Target,
    string ScenarioName,
    string TargetArtifactDirectory,
    IReadOnlyDictionary<string, string> Properties);

public sealed record ScenarioExecutionContext(
    ScenarioRunContext Run,
    ScenarioTargetContext Target,
    string ScenarioName,
    long Iteration,
    string TargetArtifactDirectory,
    IReadOnlyDictionary<string, string> Properties);

public sealed record ScenarioValidationContext(
    ScenarioRunContext Run,
    ScenarioTargetContext Target,
    string ScenarioName,
    string TargetArtifactDirectory,
    ScenarioTargetResult TargetResult,
    IReadOnlyDictionary<string, string> Properties);

public sealed record ScenarioTargetResult(
    int TotalRequests,
    int OkRequests,
    int FailedRequests,
    double RequestsPerSecond,
    double MeanMs,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double MinMs,
    double MaxMs,
    double DurationSeconds,
    bool ThresholdsPassed,
    IReadOnlyList<string> ThresholdFailureReasons);

public sealed record ScenarioStepResult(
    bool IsOk,
    string? StatusCode = null,
    string? Message = null,
    long SizeBytes = 0)
{
    public static ScenarioStepResult Ok(
        string? statusCode = null,
        long sizeBytes = 0)
    {
        return new ScenarioStepResult(
            IsOk: true,
            StatusCode: statusCode,
            SizeBytes: sizeBytes);
    }

    public static ScenarioStepResult Fail(
        string? statusCode = null,
        string? message = null,
        long sizeBytes = 0)
    {
        return new ScenarioStepResult(
            IsOk: false,
            StatusCode: statusCode,
            Message: message,
            SizeBytes: sizeBytes);
    }
}
