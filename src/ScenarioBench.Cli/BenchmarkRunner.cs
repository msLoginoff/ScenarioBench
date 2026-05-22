using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NBomber.Contracts.Stats;
using NBomber.CSharp;
using NBomber.Sinks.InfluxDB;

namespace ScenarioBench.Cli;

internal sealed class BenchmarkRunner(BenchmarkConfig config, string configPath, string? infraConfigPath)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<BenchmarkRunResult> RunAsync()
    {
        var runId = CreateRunId(config.RunName);
        var artifactDirectory = Path.GetFullPath(Path.Combine("artifacts", runId));
        Directory.CreateDirectory(artifactDirectory);

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

            var targetResult = await RunTargetAsync(runId, artifactDirectory, target);
            targetResults.Add(targetResult);
        }

        var comparisonPath = Path.Combine(artifactDirectory, "comparison.md");
        await ReportWriter.WriteComparisonAsync(config, runId, targetResults, comparisonPath);

        return new BenchmarkRunResult(runId, artifactDirectory, comparisonPath, targetResults);
    }

    private async Task<TargetRunResult> RunTargetAsync(string runId, string artifactDirectory, TargetConfig target)
    {
        using var httpClient = CreateHttpClient(target);
        var targetDirectory = Path.Combine(artifactDirectory, SanitizeName(target.Name));
        Directory.CreateDirectory(targetDirectory);

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
            .WithoutWarmUp()
            .WithLoadSimulations(
                Simulation.Inject(
                    rate: config.Scenario.RatePerSecond,
                    interval: TimeSpan.FromSeconds(1),
                    during: TimeSpan.FromSeconds(config.Scenario.DurationSeconds)));

        var nbomber = NBomberRunner
            .RegisterScenarios(scenario)
            .WithTestSuite(config.RunName)
            .WithTestName($"{config.RunName}/{target.Name}/{config.Scenario.Name}")
            .WithSessionId($"{runId}-{SanitizeName(target.Name)}")
            .WithReportFolder(targetDirectory)
            .WithReportFileName("nbomber")
            .WithReportFormats(ReportFormat.Txt, ReportFormat.Md, ReportFormat.Html, ReportFormat.Csv);

        if (infraConfigPath is not null)
        {
            nbomber = nbomber
                .WithReportingSinks(new InfluxDBSink())
                .LoadInfraConfig(Path.GetFullPath(infraConfigPath));
        }

        var stats = nbomber.Run();

        var result = TargetRunResult.FromStats(target, config.Scenario, stats, targetDirectory);
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

    private static string CreateRunId(string runName)
    {
        return $"{SanitizeName(runName)}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";
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
    double DurationSeconds)
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

        return new TargetRunResult(
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
            DurationSeconds: scenario.Duration.TotalSeconds);
    }
}
