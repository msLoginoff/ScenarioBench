namespace ScenarioBench.Cli;

internal sealed record CliOptions(string ConfigPath, string? InfraConfigPath, bool ShowHelp, string? Error)
{
    public const string HelpText = """
        Usage:
          dotnet run --project src/ScenarioBench.Cli -- --config <path> [--infra-config <path>]

        Example:
          dotnet run --project src/ScenarioBench.Cli -- --config examples/http-smoke.json

          dotnet run --project src/ScenarioBench.Cli -- \
            --config examples/http-smoke.json \
            --infra-config examples/infra/influxdb.json
        """;

    public static CliOptions Parse(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            return new CliOptions(string.Empty, null, ShowHelp: true, Error: null);
        }

        var configPath = ReadValue(args, "--config") ?? ReadValue(args, "-c");
        var infraConfigPath = ReadValue(args, "--infra-config");

        if (string.IsNullOrWhiteSpace(configPath))
        {
            return new CliOptions(
                string.Empty,
                null,
                ShowHelp: false,
                Error: "Missing required --config <path> argument.");
        }

        if (args.Contains("--infra-config") && string.IsNullOrWhiteSpace(infraConfigPath))
        {
            return new CliOptions(
                string.Empty,
                null,
                ShowHelp: false,
                Error: "Missing value for --infra-config <path> argument.");
        }

        return new CliOptions(configPath, infraConfigPath, ShowHelp: false, Error: null);
    }

    private static string? ReadValue(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        if (index < 0)
        {
            return null;
        }

        return index + 1 < args.Length ? args[index + 1] : null;
    }
}
