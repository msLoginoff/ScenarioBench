using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NBomber.Contracts;
using NBomber.Contracts.Stats;
using NBomber.CSharp;
using NBomber.Sinks.InfluxDB;
using ScenarioBench.Abstractions;

namespace ScenarioBench.Cli;

internal sealed class BenchmarkRunner(BenchmarkConfig config, string configPath, string? infraConfigPath)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<BenchmarkRunResult> RunAsync()
    {
        var scenarioPack = ScenarioPackLoader.Load(config.ScenarioPack, configPath, config.Scenario.Name);
        var runId = CreateRunId(config.RunName);
        var startedAt = DateTimeOffset.UtcNow;
        var artifactDirectory = Path.GetFullPath(Path.Combine("artifacts", runId));
        Directory.CreateDirectory(artifactDirectory);
        var runContext = CreateRunContext(runId, artifactDirectory);

        if (scenarioPack is not null)
        {
            Console.WriteLine(
                $"Loaded scenario pack '{scenarioPack.Pack.Name}' workflow '{scenarioPack.Workflow.Name}'.");
        }

        File.Copy(configPath, Path.Combine(artifactDirectory, "config.json"), overwrite: true);
        if (infraConfigPath is not null)
        {
            if (!File.Exists(infraConfigPath))
            {
                throw new FileNotFoundException($"Infrastructure config file was not found: {infraConfigPath}");
            }

            File.Copy(infraConfigPath, Path.Combine(artifactDirectory, "infra-config.json"), overwrite: true);
        }

        var targetResults = new List<TargetRunResult>();

        foreach (var target in config.Targets)
        {
            Console.WriteLine($"Running '{config.Scenario.Name}' against target '{target.Name}' ({target.BaseUrl})...");

            var targetResult = await RunTargetAsync(runContext, artifactDirectory, target, scenarioPack);
            targetResults.Add(targetResult);
        }

        var comparisonPath = Path.Combine(artifactDirectory, "comparison.md");
        await ReportWriter.WriteComparisonAsync(config, runId, targetResults, comparisonPath);

        var manifest = new RunManifest(
            RunId: runId,
            StartedAt: startedAt,
            FinishedAt: DateTimeOffset.UtcNow,
            ConfigPath: Path.GetFullPath(configPath),
            InfraConfigPath: infraConfigPath is null ? null : Path.GetFullPath(infraConfigPath),
            ArtifactDirectory: artifactDirectory,
            RunName: config.RunName,
            Metadata: config.Metadata,
            Scenario: new ScenarioManifest(
                Name: config.Scenario.Name,
                Method: config.Scenario.Method,
                Path: config.Scenario.Path,
                LoadProfile: config.Scenario.GetEffectiveLoadProfile().Describe(),
                WarmupSeconds: config.Scenario.WarmupSeconds,
                Thresholds: config.Scenario.Thresholds,
                ScenarioPack: scenarioPack is null
                    ? null
                    : new ScenarioPackManifest(scenarioPack.Pack.Name, scenarioPack.Workflow.Name)),
            Targets: targetResults);

        var manifestPath = Path.Combine(artifactDirectory, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));

        return new BenchmarkRunResult(runId, artifactDirectory, comparisonPath, manifestPath, targetResults);
    }

    private async Task<TargetRunResult> RunTargetAsync(
        ScenarioRunContext runContext,
        string artifactDirectory,
        TargetConfig target,
        LoadedScenarioPack? scenarioPack)
    {
        using var httpClient = CreateHttpClient(target);
        var targetDirectory = Path.Combine(artifactDirectory, SanitizeName(target.Name));
        Directory.CreateDirectory(targetDirectory);
        var targetContext = CreateTargetContext(target);

        if (scenarioPack is not null)
        {
            await scenarioPack.Workflow.PrepareAsync(
                new ScenarioPrepareContext(
                    runContext,
                    targetContext,
                    config.Scenario.Name,
                    targetDirectory,
                    scenarioPack.Properties));
        }

        var scenario = Scenario
            .Create(config.Scenario.Name, async context =>
                await Step.Run("request", context, async () =>
                {
                    using var request = CreateRequest(target);
                    using var response = await httpClient.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        context.ScenarioCancellationToken);

                    var statusCode = (int)response.StatusCode;
                    var sizeBytes = response.Content.Headers.ContentLength ?? 0;

                    if (config.Scenario.ExpectedStatusCodes.Contains(statusCode))
                    {
                        return Response.Ok(statusCode: statusCode.ToString(CultureInfo.InvariantCulture), sizeBytes: sizeBytes);
                    }

                    return Response.Fail(
                        statusCode: statusCode.ToString(CultureInfo.InvariantCulture),
                        message: $"Unexpected status code: {statusCode}",
                        sizeBytes: sizeBytes);
                }))
            .WithLoadSimulations(CreateLoadSimulation(config.Scenario.GetEffectiveLoadProfile()));

        scenario = config.Scenario.WarmupSeconds > 0
            ? scenario.WithWarmUpDuration(TimeSpan.FromSeconds(config.Scenario.WarmupSeconds))
            : scenario.WithoutWarmUp();

        var nbomber = NBomberRunner
            .RegisterScenarios(scenario)
            .WithTestSuite(config.RunName)
            .WithTestName($"{config.RunName}/{target.Name}/{config.Scenario.Name}")
            .WithSessionId($"{runContext.RunId}-{SanitizeName(target.Name)}")
            .WithReportFolder(targetDirectory)
            .WithReportFileName("nbomber")
            .WithReportFormats(ReportFormat.Txt, ReportFormat.Md, ReportFormat.Html, ReportFormat.Csv);

        if (infraConfigPath is not null)
        {
            var generatedInfraConfigPath = await InfluxInfraConfigWriter.WriteTargetConfigAsync(
                Path.GetFullPath(infraConfigPath),
                Path.Combine(artifactDirectory, "infra-config.generated", $"{SanitizeName(target.Name)}.json"),
                CreateInfluxTags(runContext.RunId, target));

            nbomber = nbomber
                .WithReportingSinks(new InfluxDBSink())
                .LoadInfraConfig(generatedInfraConfigPath);
        }

        var stats = nbomber.Run();

        var result = TargetRunResult.FromStats(target, config.Scenario, stats, targetDirectory);
        if (scenarioPack is not null)
        {
            var validationResults = await scenarioPack.Workflow.ValidateAsync(
                new ScenarioValidationContext(
                    runContext,
                    targetContext,
                    config.Scenario.Name,
                    targetDirectory,
                    result.ToScenarioTargetResult(),
                    scenarioPack.Properties));

            result = result.WithValidationResults(validationResults);
        }

        var resultPath = Path.Combine(targetDirectory, "result.json");
        await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(result, JsonOptions));

        return result;
    }

    private HttpClient CreateHttpClient(TargetConfig target)
    {
        var httpClient = new HttpClient
        {
            BaseAddress = target.BaseUrl,
            Timeout = TimeSpan.FromSeconds(config.Scenario.TimeoutSeconds)
        };

        foreach (var (name, value) in target.Headers)
        {
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation(name, value);
        }

        return httpClient;
    }

    private HttpRequestMessage CreateRequest(TargetConfig target)
    {
        var request = new HttpRequestMessage(new HttpMethod(config.Scenario.Method), config.Scenario.Path);

        foreach (var (name, value) in config.Scenario.Headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        if (config.Scenario.Body is not null)
        {
            request.Content = new StringContent(config.Scenario.Body, Encoding.UTF8);
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(config.Scenario.ContentType);
        }

        request.Headers.TryAddWithoutValidation("X-ScenarioBench-Target", target.Name);
        request.Headers.TryAddWithoutValidation("X-ScenarioBench-Scenario", config.Scenario.Name);

        return request;
    }

    private static LoadSimulation CreateLoadSimulation(LoadProfileConfig profile)
    {
        var duration = TimeSpan.FromSeconds(profile.DurationSeconds);
        var interval = TimeSpan.FromSeconds(profile.IntervalSeconds);

        return profile.Type switch
        {
            LoadProfileTypes.Inject => Simulation.Inject(
                rate: profile.RatePerSecond!.Value,
                interval: interval,
                during: duration),

            LoadProfileTypes.RampingInject => Simulation.RampingInject(
                rate: profile.RatePerSecond!.Value,
                interval: interval,
                during: duration),

            LoadProfileTypes.Constant => Simulation.KeepConstant(
                copies: profile.Copies!.Value,
                during: duration),

            LoadProfileTypes.RampingConstant => Simulation.RampingConstant(
                copies: profile.Copies!.Value,
                during: duration),

            _ => throw new InvalidOperationException($"Unsupported load profile type: {profile.Type}")
        };
    }

    private static string CreateRunId(string runName)
    {
        return $"{SanitizeName(runName)}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";
    }

    private ScenarioRunContext CreateRunContext(string runId, string artifactDirectory)
    {
        return new ScenarioRunContext(
            RunId: runId,
            RunName: config.RunName,
            ArtifactDirectory: artifactDirectory,
            Metadata: new ScenarioRunMetadata(
                Environment: config.Metadata.Environment,
                Branch: config.Metadata.Branch,
                Commit: config.Metadata.Commit,
                Version: config.Metadata.Version,
                Build: config.Metadata.Build,
                Seed: config.Metadata.Seed,
                Notes: config.Metadata.Notes,
                Tags: config.Metadata.Tags));
    }

    private static ScenarioTargetContext CreateTargetContext(TargetConfig target)
    {
        return new ScenarioTargetContext(
            Name: target.Name,
            BaseUrl: target.BaseUrl,
            Headers: target.Headers,
            Tags: target.Tags);
    }

    private IReadOnlyDictionary<string, string> CreateInfluxTags(string runId, TargetConfig target)
    {
        var tags = new Dictionary<string, string>
        {
            ["run_id"] = runId,
            ["target"] = target.Name
        };

        AddOptional(tags, "environment", config.Metadata.Environment);
        AddOptional(tags, "branch", config.Metadata.Branch);
        AddOptional(tags, "commit", config.Metadata.Commit);
        AddOptional(tags, "version", config.Metadata.Version);
        AddOptional(tags, "build", config.Metadata.Build);
        AddOptional(tags, "seed", config.Metadata.Seed);

        foreach (var (key, value) in config.Metadata.Tags)
        {
            AddOptional(tags, key, value);
        }

        foreach (var (key, value) in target.Tags)
        {
            AddOptional(tags, $"target_{key}", value);
        }

        return tags;
    }

    private static void AddOptional(IDictionary<string, string> tags, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
        {
            tags[key] = value;
        }
    }

    internal static string SanitizeName(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var ch in value.Trim())
        {
            builder.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '-');
        }

        var result = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(result) ? "unnamed" : result;
    }
}

internal sealed record BenchmarkRunResult(
    string RunId,
    string ArtifactDirectory,
    string ComparisonReportPath,
    string ManifestPath,
    IReadOnlyList<TargetRunResult> Targets);

internal sealed record TargetRunResult(
    string TargetName,
    string BaseUrl,
    string ScenarioName,
    string ArtifactDirectory,
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
    bool Passed,
    IReadOnlyList<string> FailureReasons,
    IReadOnlyList<ScenarioValidationResult> ValidationResults)
{
    public static TargetRunResult FromStats(
        TargetConfig target,
        ScenarioConfig scenarioConfig,
        NodeStats stats,
        string artifactDirectory)
    {
        var scenario = stats.ScenarioStats.Single(stats => stats.ScenarioName == scenarioConfig.Name);
        var requestStep = scenario.StepStats.Single(stats => stats.StepName == "request");
        var okRequests = requestStep.Ok.Request.Count;
        var failedRequests = requestStep.Fail.Request.Count;

        var result = new TargetRunResult(
            TargetName: target.Name,
            BaseUrl: target.BaseUrl.ToString(),
            ScenarioName: scenario.ScenarioName,
            ArtifactDirectory: artifactDirectory,
            TotalRequests: okRequests + failedRequests,
            OkRequests: okRequests,
            FailedRequests: failedRequests,
            RequestsPerSecond: requestStep.Ok.Request.RPS,
            MeanMs: requestStep.Ok.Latency.MeanMs,
            P50Ms: requestStep.Ok.Latency.Percent50,
            P95Ms: requestStep.Ok.Latency.Percent95,
            P99Ms: requestStep.Ok.Latency.Percent99,
            MinMs: requestStep.Ok.Latency.MinMs,
            MaxMs: requestStep.Ok.Latency.MaxMs,
            DurationSeconds: scenario.Duration.TotalSeconds,
            Passed: true,
            FailureReasons: [],
            ValidationResults: []);

        var failureReasons = EvaluateThresholds(result, scenarioConfig.Thresholds);
        return result with
        {
            Passed = failureReasons.Count == 0 && result.ValidationResults.All(validation => validation.Passed),
            FailureReasons = failureReasons
        };
    }

    public TargetRunResult WithValidationResults(IReadOnlyList<ScenarioValidationResult> validationResults)
    {
        return this with
        {
            Passed = FailureReasons.Count == 0 && validationResults.All(validation => validation.Passed),
            ValidationResults = validationResults
        };
    }

    public ScenarioTargetResult ToScenarioTargetResult()
    {
        return new ScenarioTargetResult(
            TotalRequests: TotalRequests,
            OkRequests: OkRequests,
            FailedRequests: FailedRequests,
            RequestsPerSecond: RequestsPerSecond,
            MeanMs: MeanMs,
            P50Ms: P50Ms,
            P95Ms: P95Ms,
            P99Ms: P99Ms,
            MinMs: MinMs,
            MaxMs: MaxMs,
            DurationSeconds: DurationSeconds,
            ThresholdsPassed: FailureReasons.Count == 0,
            ThresholdFailureReasons: FailureReasons);
    }

    private static IReadOnlyList<string> EvaluateThresholds(TargetRunResult result, ThresholdConfig thresholds)
    {
        var failures = new List<string>();

        if (result.FailedRequests > thresholds.MaxFailedRequests)
        {
            failures.Add($"failed requests {result.FailedRequests} > {thresholds.MaxFailedRequests}");
        }

        if (thresholds.MaxFailedPercent is not null && result.TotalRequests > 0)
        {
            var failedPercent = (double)result.FailedRequests / result.TotalRequests * 100;
            if (failedPercent > thresholds.MaxFailedPercent.Value)
            {
                failures.Add($"failed percent {failedPercent:F2}% > {thresholds.MaxFailedPercent.Value:F2}%");
            }
        }

        if (thresholds.MaxP95Ms is not null && result.P95Ms > thresholds.MaxP95Ms.Value)
        {
            failures.Add($"p95 {result.P95Ms:F2}ms > {thresholds.MaxP95Ms.Value:F2}ms");
        }

        if (thresholds.MaxP99Ms is not null && result.P99Ms > thresholds.MaxP99Ms.Value)
        {
            failures.Add($"p99 {result.P99Ms:F2}ms > {thresholds.MaxP99Ms.Value:F2}ms");
        }

        if (thresholds.MinRequestsPerSecond is not null && result.RequestsPerSecond < thresholds.MinRequestsPerSecond.Value)
        {
            failures.Add($"RPS {result.RequestsPerSecond:F2} < {thresholds.MinRequestsPerSecond.Value:F2}");
        }

        return failures;
    }
}
