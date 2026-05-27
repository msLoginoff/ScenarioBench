using ScenarioBench.Abstractions;

namespace ScenarioBench.DemoScenarioPack;

public sealed class DemoScenarioPack : IScenarioPack
{
    public string Name => "demo";

    public IReadOnlyList<IScenarioWorkflow> Workflows { get; } =
    [
        new RequestCountValidationWorkflow(),
        new DemoMultiStepWorkflow()
    ];
}

internal sealed class RequestCountValidationWorkflow : IScenarioWorkflow
{
    public string Name => "request-count-validation";

    public ValueTask PrepareAsync(
        ScenarioPrepareContext context,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<ScenarioValidationResult>> ValidateAsync(
        ScenarioValidationContext context,
        CancellationToken cancellationToken = default)
    {
        var minTotalRequests = ReadInt(context.Properties, "minTotalRequests", fallback: 1);

        if (context.TargetResult.TotalRequests < minTotalRequests)
        {
            return ValueTask.FromResult<IReadOnlyList<ScenarioValidationResult>>(
            [
                ScenarioValidationResult.Fail(
                    "request-count",
                    [
                        new ValidationIssue(
                            ValidationSeverity.Error,
                            $"Total requests {context.TargetResult.TotalRequests} < {minTotalRequests}.",
                            Code: "request_count_too_low")
                    ],
                    new Dictionary<string, string>
                    {
                        ["totalRequests"] = context.TargetResult.TotalRequests.ToString(),
                        ["minTotalRequests"] = minTotalRequests.ToString()
                    })
            ]);
        }

        return ValueTask.FromResult<IReadOnlyList<ScenarioValidationResult>>(
        [
            ScenarioValidationResult.Pass(
                "request-count",
                new Dictionary<string, string>
                {
                    ["totalRequests"] = context.TargetResult.TotalRequests.ToString(),
                    ["minTotalRequests"] = minTotalRequests.ToString()
                })
        ]);
    }

    private static int ReadInt(
        IReadOnlyDictionary<string, string> properties,
        string name,
        int fallback)
    {
        return properties.TryGetValue(name, out var value) && int.TryParse(value, out var parsed)
            ? parsed
            : fallback;
    }
}

internal sealed class DemoMultiStepWorkflow : IScenarioLoadWorkflow
{
    public string Name => "demo-multi-step";

    public ValueTask PrepareAsync(
        ScenarioPrepareContext context,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    public async ValueTask<ScenarioStepResult> ExecuteAsync(
        ScenarioExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        using var httpClient = new HttpClient
        {
            BaseAddress = context.Target.BaseUrl
        };

        foreach (var (name, value) in context.Target.Headers)
        {
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation(name, value);
        }

        var health = await SendAsync(httpClient, "/health", cancellationToken);
        if (!health.IsOk)
        {
            return health;
        }

        var work = await SendAsync(httpClient, "/work?delayMs=5&bytes=64", cancellationToken);
        if (!work.IsOk)
        {
            return work;
        }

        return ScenarioStepResult.Ok(statusCode: "200", sizeBytes: health.SizeBytes + work.SizeBytes);
    }

    public ValueTask<IReadOnlyList<ScenarioValidationResult>> ValidateAsync(
        ScenarioValidationContext context,
        CancellationToken cancellationToken = default)
    {
        var minTotalRequests = ReadInt(context.Properties, "minTotalRequests", fallback: 1);

        if (context.TargetResult.TotalRequests < minTotalRequests)
        {
            return ValueTask.FromResult<IReadOnlyList<ScenarioValidationResult>>(
            [
                ScenarioValidationResult.Fail(
                    "workflow-request-count",
                    [
                        new ValidationIssue(
                            ValidationSeverity.Error,
                            $"Workflow iterations {context.TargetResult.TotalRequests} < {minTotalRequests}.",
                            Code: "workflow_request_count_too_low")
                    ])
            ]);
        }

        return ValueTask.FromResult<IReadOnlyList<ScenarioValidationResult>>(
        [
            ScenarioValidationResult.Pass(
                "workflow-request-count",
                new Dictionary<string, string>
                {
                    ["iterations"] = context.TargetResult.TotalRequests.ToString(),
                    ["minTotalRequests"] = minTotalRequests.ToString()
                })
        ]);
    }

    private static async Task<ScenarioStepResult> SendAsync(
        HttpClient httpClient,
        string path,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var statusCode = ((int)response.StatusCode).ToString();
        var sizeBytes = response.Content.Headers.ContentLength ?? 0;

        return response.IsSuccessStatusCode
            ? ScenarioStepResult.Ok(statusCode, sizeBytes)
            : ScenarioStepResult.Fail(statusCode, $"Unexpected status code for {path}: {statusCode}", sizeBytes);
    }

    private static int ReadInt(
        IReadOnlyDictionary<string, string> properties,
        string name,
        int fallback)
    {
        return properties.TryGetValue(name, out var value) && int.TryParse(value, out var parsed)
            ? parsed
            : fallback;
    }
}
