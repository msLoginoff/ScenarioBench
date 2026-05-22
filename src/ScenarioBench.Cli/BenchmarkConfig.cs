using System.Text.Json;

namespace ScenarioBench.Cli;

internal sealed record BenchmarkConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public string RunName { get; init; } = "scenario-bench";

    public IReadOnlyList<TargetConfig> Targets { get; init; } = [];

    public ScenarioConfig Scenario { get; init; } = new();

    public static async Task<BenchmarkConfig> LoadAsync(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Config file was not found: {path}");
        }

        await using var stream = File.OpenRead(path);
        var config = await JsonSerializer.DeserializeAsync<BenchmarkConfig>(stream, JsonOptions);

        if (config is null)
        {
            throw new InvalidOperationException($"Config file is empty or invalid: {path}");
        }

        config.Validate();
        return config;
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(RunName))
        {
            throw new InvalidOperationException("Config field 'runName' is required.");
        }

        if (Targets.Count == 0)
        {
            throw new InvalidOperationException("Config must contain at least one target.");
        }

        foreach (var target in Targets)
        {
            target.Validate();
        }

        var duplicateTarget = Targets
            .GroupBy(target => target.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateTarget is not null)
        {
            throw new InvalidOperationException($"Target name must be unique: {duplicateTarget.Key}");
        }

        Scenario.Validate();
    }
}

internal sealed record TargetConfig
{
    public string Name { get; init; } = string.Empty;

    public Uri BaseUrl { get; init; } = new("http://localhost");

    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidOperationException("Each target must have a non-empty name.");
        }

        if (!BaseUrl.IsAbsoluteUri)
        {
            throw new InvalidOperationException($"Target '{Name}' baseUrl must be an absolute URL.");
        }
    }
}

internal sealed record ScenarioConfig
{
    public string Name { get; init; } = "http-smoke";

    public string Method { get; init; } = "GET";

    public string Path { get; init; } = "/";

    public int RatePerSecond { get; init; } = 10;

    public int DurationSeconds { get; init; } = 30;

    public int TimeoutSeconds { get; init; } = 30;

    public string? Body { get; init; }

    public string ContentType { get; init; } = "application/json";

    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();

    public IReadOnlyList<int> ExpectedStatusCodes { get; init; } = [200];

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidOperationException("Scenario name is required.");
        }

        if (string.IsNullOrWhiteSpace(Method))
        {
            throw new InvalidOperationException("Scenario method is required.");
        }

        if (RatePerSecond <= 0)
        {
            throw new InvalidOperationException("Scenario ratePerSecond must be greater than 0.");
        }

        if (DurationSeconds <= 0)
        {
            throw new InvalidOperationException("Scenario durationSeconds must be greater than 0.");
        }

        if (TimeoutSeconds <= 0)
        {
            throw new InvalidOperationException("Scenario timeoutSeconds must be greater than 0.");
        }

        if (ExpectedStatusCodes.Count == 0)
        {
            throw new InvalidOperationException("Scenario expectedStatusCodes must contain at least one status code.");
        }
    }
}
