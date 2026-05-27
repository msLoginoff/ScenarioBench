namespace ScenarioBench.Cli;

internal sealed record RunManifest(
    string RunId,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    string ConfigPath,
    string? InfraConfigPath,
    string ArtifactDirectory,
    string RunName,
    RunMetadataConfig Metadata,
    ScenarioManifest Scenario,
    IReadOnlyList<TargetRunResult> Targets)
{
    public bool Passed => Targets.All(target => target.Passed);
}

internal sealed record ScenarioManifest(
    string Name,
    string Method,
    string Path,
    string LoadProfile,
    int WarmupSeconds,
    ThresholdConfig Thresholds);
