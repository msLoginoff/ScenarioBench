using ScenarioBench.Abstractions;

namespace ScenarioBench.DemoScenarioPack;

public sealed class DemoScenarioPack : IScenarioPack
{
    public string Name => "demo";

    public IReadOnlyList<IScenarioWorkflow> Workflows { get; } =
    [
        new RequestCountValidationWorkflow()
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
