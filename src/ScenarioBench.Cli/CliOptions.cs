namespace ScenarioBench.Cli;

internal sealed record CliOptions(
    string ConfigPath,
    string? InfraConfigPath,
    RunSelection Selection,
    bool ListScenarios,
    bool ShowHelp,
    string? Error)
{
    public const string HelpText = """
        Usage:
          dotnet run --project src/ScenarioBench.Cli -- --config <path> [--infra-config <path>] [--scenario <names>] [--target <names>]
          dotnet run --project src/ScenarioBench.Cli -- --config <path> --list-scenarios

        Example:
          dotnet run --project src/ScenarioBench.Cli -- --config examples/http-smoke.json

          dotnet run --project src/ScenarioBench.Cli -- \
            --config examples/http-smoke.json \
            --infra-config examples/infra/influxdb.json

          dotnet run --project src/ScenarioBench.Cli -- \
            --config examples/http-workflow-with-pack.json

          dotnet run --project src/ScenarioBench.Cli -- \
            --config examples/http-workflow-suite.json

          dotnet run --project src/ScenarioBench.Cli -- \
            --config examples/http-workflow-suite.json \
            --scenario demo-multi-step,http-smoke \
            --target old,new

          dotnet run --project src/ScenarioBench.Cli -- \
            --config examples/http-workflow-suite.json \
            --list-scenarios
        """;

    public static CliOptions Parse(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            return new CliOptions(string.Empty, null, RunSelection.Empty, ListScenarios: false, ShowHelp: true, Error: null);
        }

        var configPath = ReadValue(args, "--config") ?? ReadValue(args, "-c");
        var infraConfigPath = ReadValue(args, "--infra-config");
        var selection = new RunSelection(
            ParseNameSet(ReadValue(args, "--scenario")),
            ParseNameSet(ReadValue(args, "--target")));
        var listScenarios = args.Contains("--list-scenarios");

        if (string.IsNullOrWhiteSpace(configPath))
        {
            return new CliOptions(
                string.Empty,
                null,
                RunSelection.Empty,
                ListScenarios: false,
                ShowHelp: false,
                Error: "Missing required --config <path> argument.");
        }

        if (args.Contains("--infra-config") && string.IsNullOrWhiteSpace(infraConfigPath))
        {
            return new CliOptions(
                string.Empty,
                null,
                RunSelection.Empty,
                ListScenarios: false,
                ShowHelp: false,
                Error: "Missing value for --infra-config <path> argument.");
        }

        if (args.Contains("--scenario") && selection.ScenarioNames.Count == 0)
        {
            return new CliOptions(
                string.Empty,
                null,
                RunSelection.Empty,
                ListScenarios: false,
                ShowHelp: false,
                Error: "Missing value for --scenario <names> argument.");
        }

        if (args.Contains("--target") && selection.TargetNames.Count == 0)
        {
            return new CliOptions(
                string.Empty,
                null,
                RunSelection.Empty,
                ListScenarios: false,
                ShowHelp: false,
                Error: "Missing value for --target <names> argument.");
        }

        return new CliOptions(configPath, infraConfigPath, selection, listScenarios, ShowHelp: false, Error: null);
    }

    private static string? ReadValue(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        if (index < 0)
        {
            return null;
        }

        if (index + 1 >= args.Length || args[index + 1].StartsWith("-", StringComparison.Ordinal))
        {
            return null;
        }

        return args[index + 1];
    }

    private static IReadOnlySet<string> ParseNameSet(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
