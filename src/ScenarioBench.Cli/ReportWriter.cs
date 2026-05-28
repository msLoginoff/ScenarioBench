using System.Globalization;
using System.Text;

namespace ScenarioBench.Cli;

internal static class ReportWriter
{
    public static async Task WriteComparisonAsync(
        BenchmarkConfig config,
        string runId,
        IReadOnlyList<ScenarioConfig> scenarios,
        IReadOnlyList<TargetRunResult> targets,
        string path)
    {
        var builder = new StringBuilder();

        builder.AppendLine($"# ScenarioBench Comparison: {config.RunName}");
        builder.AppendLine();
        builder.AppendLine($"- Run ID: `{runId}`");
        builder.AppendLine($"- Scenarios: `{scenarios.Count}`");
        AppendMetadata(builder, config.Metadata);
        builder.AppendLine();

        foreach (var scenario in scenarios)
        {
            var scenarioTargets = targets
                .Where(target => string.Equals(target.ScenarioName, scenario.Name, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            AppendScenarioComparison(builder, scenario, scenarioTargets);
        }

        await File.WriteAllTextAsync(path, builder.ToString());
    }

    private static void AppendScenarioComparison(
        StringBuilder builder,
        ScenarioConfig scenario,
        IReadOnlyList<TargetRunResult> targets)
    {
        builder.AppendLine($"## Scenario `{scenario.Name}`");
        builder.AppendLine();
        builder.AppendLine($"- Driver: `{scenario.Driver}`");
        builder.AppendLine($"- Step: `{scenario.GetStepName()}`");
        builder.AppendLine($"- Method: `{scenario.Method}`");
        builder.AppendLine($"- Path: `{scenario.Path}`");
        builder.AppendLine($"- Load profile: `{scenario.GetEffectiveLoadProfile().Describe()}`");
        builder.AppendLine($"- Warmup: `{scenario.WarmupSeconds}` sec");
        builder.AppendLine();

        builder.AppendLine("| Target | Status | Requests | OK | Failed | RPS | Mean, ms | P50, ms | P95, ms | P99, ms | Max, ms |");
        builder.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");

        foreach (var target in targets)
        {
            builder.AppendLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"| {target.TargetName} | {(target.Passed ? "PASS" : "FAIL")} | {target.TotalRequests} | {target.OkRequests} | {target.FailedRequests} | {target.RequestsPerSecond:F2} | {target.MeanMs:F2} | {target.P50Ms:F2} | {target.P95Ms:F2} | {target.P99Ms:F2} | {target.MaxMs:F2} |"));
        }

        var thresholdFailedTargets = targets.Where(target => target.FailureReasons.Count > 0).ToArray();
        if (thresholdFailedTargets.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("### Failed Thresholds");
            builder.AppendLine();

            foreach (var target in thresholdFailedTargets)
            {
                builder.AppendLine($"#### {target.TargetName}");
                builder.AppendLine();

                foreach (var reason in target.FailureReasons)
                {
                    builder.AppendLine($"- {reason}");
                }

                builder.AppendLine();
            }
        }

        var targetsWithValidation = targets
            .Where(target => target.ValidationResults.Count > 0)
            .ToArray();

        if (targetsWithValidation.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("### Validation Results");
            builder.AppendLine();
            builder.AppendLine("| Target | Validation | Status | Issues |");
            builder.AppendLine("| --- | --- | --- | ---: |");

            foreach (var target in targetsWithValidation)
            {
                foreach (var validation in target.ValidationResults)
                {
                    builder.AppendLine(
                        $"| {target.TargetName} | {validation.Name} | {validation.Status} | {validation.Issues.Count} |");
                }
            }

            var validationsWithIssues = targetsWithValidation
                .SelectMany(target => target.ValidationResults.Select(validation => new { target.TargetName, Validation = validation }))
                .Where(item => item.Validation.Issues.Count > 0)
                .ToArray();

            if (validationsWithIssues.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine("#### Validation Issues");
                builder.AppendLine();

                foreach (var item in validationsWithIssues)
                {
                    builder.AppendLine($"##### {item.TargetName} / {item.Validation.Name}");
                    builder.AppendLine();

                    foreach (var issue in item.Validation.Issues)
                    {
                        var code = string.IsNullOrWhiteSpace(issue.Code) ? string.Empty : $" `{issue.Code}`";
                        var issuePath = string.IsNullOrWhiteSpace(issue.Path) ? string.Empty : $" `{issue.Path}`";
                        builder.AppendLine($"- {issue.Severity}{code}{issuePath}: {issue.Message}");
                    }

                    builder.AppendLine();
                }
            }
        }

        var baseline = targets.FirstOrDefault();
        if (baseline is not null && targets.Count > 1)
        {
            builder.AppendLine();
            builder.AppendLine($"### Delta vs `{baseline.TargetName}`");
            builder.AppendLine();
            builder.AppendLine("| Target | RPS delta | P95 delta | P99 delta | Failed delta |");
            builder.AppendLine("| --- | ---: | ---: | ---: | ---: |");

            foreach (var target in targets.Skip(1))
            {
                builder.AppendLine(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"| {target.TargetName} | {PercentDelta(target.RequestsPerSecond, baseline.RequestsPerSecond)} | {PercentDelta(target.P95Ms, baseline.P95Ms)} | {PercentDelta(target.P99Ms, baseline.P99Ms)} | {target.FailedRequests - baseline.FailedRequests:+#;-#;0} |"));
            }
        }

        builder.AppendLine();
    }

    private static void AppendMetadata(StringBuilder builder, RunMetadataConfig metadata)
    {
        AppendOptional(builder, "Environment", metadata.Environment);
        AppendOptional(builder, "Branch", metadata.Branch);
        AppendOptional(builder, "Commit", metadata.Commit);
        AppendOptional(builder, "Version", metadata.Version);
        AppendOptional(builder, "Build", metadata.Build);
        AppendOptional(builder, "Seed", metadata.Seed);
    }

    private static void AppendOptional(StringBuilder builder, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.AppendLine($"- {name}: `{value}`");
        }
    }

    private static string PercentDelta(double value, double baseline)
    {
        if (Math.Abs(baseline) < double.Epsilon)
        {
            return "n/a";
        }

        var delta = (value - baseline) / baseline;
        return delta.ToString("+#0.##%;-#0.##%;0%", CultureInfo.InvariantCulture);
    }
}
