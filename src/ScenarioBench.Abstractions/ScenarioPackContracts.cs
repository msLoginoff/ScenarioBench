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
        ScenarioTargetContext target,
        CancellationToken cancellationToken = default);

    ValueTask<ScenarioValidationResult> ValidateAsync(
        ScenarioValidationContext context,
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

public sealed record ScenarioValidationContext(
    ScenarioRunContext Run,
    ScenarioTargetContext Target,
    string ScenarioName,
    string TargetArtifactDirectory,
    IReadOnlyDictionary<string, string> Properties);
