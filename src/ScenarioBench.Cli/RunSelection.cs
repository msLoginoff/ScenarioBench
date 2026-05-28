namespace ScenarioBench.Cli;

internal sealed record RunSelection(
    IReadOnlySet<string> ScenarioNames,
    IReadOnlySet<string> TargetNames)
{
    public static RunSelection Empty { get; } = new(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    public bool HasScenarioFilter => ScenarioNames.Count > 0;

    public bool HasTargetFilter => TargetNames.Count > 0;
}
