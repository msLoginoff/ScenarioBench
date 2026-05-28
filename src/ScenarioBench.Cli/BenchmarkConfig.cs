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

    public RunMetadataConfig Metadata { get; init; } = new();

    public IReadOnlyList<TargetConfig> Targets { get; init; } = [];

    public ScenarioConfig Scenario { get; init; } = new();

    public IReadOnlyList<ScenarioConfig>? Scenarios { get; init; }

    public ScenarioPackConfig? ScenarioPack { get; init; }

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

        var scenarios = GetScenarios();
        if (scenarios.Count == 0)
        {
            throw new InvalidOperationException("Config must contain at least one scenario.");
        }

        foreach (var scenario in scenarios)
        {
            scenario.Validate();
        }

        var duplicateScenario = scenarios
            .GroupBy(scenario => scenario.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateScenario is not null)
        {
            throw new InvalidOperationException($"Scenario name must be unique: {duplicateScenario.Key}");
        }

        ScenarioPack?.Validate();
    }

    public IReadOnlyList<ScenarioConfig> GetScenarios()
    {
        return Scenarios is { Count: > 0 } ? Scenarios : [Scenario];
    }
}

internal sealed record RunMetadataConfig
{
    public string? Environment { get; init; }

    public string? Branch { get; init; }

    public string? Commit { get; init; }

    public string? Version { get; init; }

    public string? Build { get; init; }

    public string? Seed { get; init; }

    public string? Notes { get; init; }

    public IReadOnlyDictionary<string, string> Tags { get; init; } = new Dictionary<string, string>();
}

internal sealed record TargetConfig
{
    public string Name { get; init; } = string.Empty;

    public Uri BaseUrl { get; init; } = new("http://localhost");

    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();

    public IReadOnlyDictionary<string, string> Tags { get; init; } = new Dictionary<string, string>();

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

internal sealed record ScenarioPackConfig
{
    public string AssemblyPath { get; init; } = string.Empty;

    public string? TypeName { get; init; }

    public string? Workflow { get; init; }

    public IReadOnlyDictionary<string, string> Properties { get; init; } = new Dictionary<string, string>();

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AssemblyPath))
        {
            throw new InvalidOperationException("Scenario pack field 'assemblyPath' is required when scenarioPack is configured.");
        }
    }
}

internal sealed record ScenarioConfig
{
    public string Name { get; init; } = "http-smoke";

    public string? Workflow { get; init; }

    public string Driver { get; init; } = ScenarioDrivers.Http;

    public string? StepName { get; init; }

    public string Method { get; init; } = "GET";

    public string Path { get; init; } = "/";

    public int RatePerSecond { get; init; } = 10;

    public int DurationSeconds { get; init; } = 30;

    public int WarmupSeconds { get; init; }

    public LoadProfileConfig? LoadProfile { get; init; }

    public int TimeoutSeconds { get; init; } = 30;

    public string? Body { get; init; }

    public string ContentType { get; init; } = "application/json";

    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();

    public IReadOnlyList<int> ExpectedStatusCodes { get; init; } = [200];

    public ThresholdConfig Thresholds { get; init; } = new();

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidOperationException("Scenario name is required.");
        }

        if (Driver is not ScenarioDrivers.Http and not ScenarioDrivers.Workflow)
        {
            throw new InvalidOperationException(
                $"Unsupported scenario driver '{Driver}'. Supported values: http, workflow.");
        }

        if (string.IsNullOrWhiteSpace(Method))
        {
            throw new InvalidOperationException("Scenario method is required.");
        }

        LoadProfile?.Validate();

        if (LoadProfile is null)
        {
            if (RatePerSecond <= 0)
            {
                throw new InvalidOperationException("Scenario ratePerSecond must be greater than 0.");
            }

            if (DurationSeconds <= 0)
            {
                throw new InvalidOperationException("Scenario durationSeconds must be greater than 0.");
            }
        }

        if (WarmupSeconds < 0)
        {
            throw new InvalidOperationException("Scenario warmupSeconds cannot be negative.");
        }

        if (TimeoutSeconds <= 0)
        {
            throw new InvalidOperationException("Scenario timeoutSeconds must be greater than 0.");
        }

        if (ExpectedStatusCodes.Count == 0)
        {
            throw new InvalidOperationException("Scenario expectedStatusCodes must contain at least one status code.");
        }

        Thresholds.Validate();
    }

    public LoadProfileConfig GetEffectiveLoadProfile()
    {
        return LoadProfile ?? new LoadProfileConfig
        {
            Type = LoadProfileTypes.Inject,
            RatePerSecond = RatePerSecond,
            DurationSeconds = DurationSeconds
        };
    }

    public string GetStepName()
    {
        if (!string.IsNullOrWhiteSpace(StepName))
        {
            return StepName;
        }

        return Driver == ScenarioDrivers.Workflow ? "workflow" : "request";
    }
}

internal static class ScenarioDrivers
{
    public const string Http = "http";
    public const string Workflow = "workflow";
}

internal static class LoadProfileTypes
{
    public const string Inject = "inject";
    public const string Constant = "constant";
    public const string RampingInject = "rampingInject";
    public const string RampingConstant = "rampingConstant";
}

internal sealed record LoadProfileConfig
{
    public string Type { get; init; } = LoadProfileTypes.Inject;

    public int? RatePerSecond { get; init; }

    public int? Copies { get; init; }

    public int DurationSeconds { get; init; } = 30;

    public int IntervalSeconds { get; init; } = 1;

    public void Validate()
    {
        if (DurationSeconds <= 0)
        {
            throw new InvalidOperationException("Scenario loadProfile.durationSeconds must be greater than 0.");
        }

        if (IntervalSeconds <= 0)
        {
            throw new InvalidOperationException("Scenario loadProfile.intervalSeconds must be greater than 0.");
        }

        switch (Type)
        {
            case LoadProfileTypes.Inject:
            case LoadProfileTypes.RampingInject:
                if (RatePerSecond is null or <= 0)
                {
                    throw new InvalidOperationException($"Scenario loadProfile.ratePerSecond must be greater than 0 for '{Type}'.");
                }

                break;

            case LoadProfileTypes.Constant:
            case LoadProfileTypes.RampingConstant:
                if (Copies is null or <= 0)
                {
                    throw new InvalidOperationException($"Scenario loadProfile.copies must be greater than 0 for '{Type}'.");
                }

                break;

            default:
                throw new InvalidOperationException(
                    $"Unsupported scenario loadProfile.type '{Type}'. Supported values: inject, constant, rampingInject, rampingConstant.");
        }
    }

    public string Describe()
    {
        return Type switch
        {
            LoadProfileTypes.Inject => $"inject {RatePerSecond} req/sec for {DurationSeconds}s",
            LoadProfileTypes.RampingInject => $"ramping inject to {RatePerSecond} req/sec for {DurationSeconds}s",
            LoadProfileTypes.Constant => $"constant {Copies} copies for {DurationSeconds}s",
            LoadProfileTypes.RampingConstant => $"ramping constant to {Copies} copies for {DurationSeconds}s",
            _ => Type
        };
    }
}

internal sealed record ThresholdConfig
{
    public int MaxFailedRequests { get; init; }

    public double? MaxFailedPercent { get; init; }

    public double? MaxP95Ms { get; init; }

    public double? MaxP99Ms { get; init; }

    public double? MinRequestsPerSecond { get; init; }

    public void Validate()
    {
        if (MaxFailedRequests < 0)
        {
            throw new InvalidOperationException("Scenario thresholds.maxFailedRequests cannot be negative.");
        }

        if (MaxFailedPercent is < 0 or > 100)
        {
            throw new InvalidOperationException("Scenario thresholds.maxFailedPercent must be between 0 and 100.");
        }

        if (MaxP95Ms is <= 0)
        {
            throw new InvalidOperationException("Scenario thresholds.maxP95Ms must be greater than 0.");
        }

        if (MaxP99Ms is <= 0)
        {
            throw new InvalidOperationException("Scenario thresholds.maxP99Ms must be greater than 0.");
        }

        if (MinRequestsPerSecond is <= 0)
        {
            throw new InvalidOperationException("Scenario thresholds.minRequestsPerSecond must be greater than 0.");
        }
    }
}
