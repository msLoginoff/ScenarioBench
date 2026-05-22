using System.Globalization;
using System.Text;

namespace ScenarioBench.Cli;

internal static class ReportWriter
{
    public static async Task WriteComparisonAsync(
        BenchmarkConfig config,
        string runId,
        IReadOnlyList<TargetRunResult> targets,
        string path)
    {
        var builder = new StringBuilder();

        builder.AppendLine($"# ScenarioBench Comparison: {config.RunName}");
        builder.AppendLine();
        builder.AppendLine($"- Run ID: `{runId}`");
        builder.AppendLine($"- Scenario: `{config.Scenario.Name}`");
        builder.AppendLine($"- Method: `{config.Scenario.Method}`");
        builder.AppendLine($"- Path: `{config.Scenario.Path}`");
        builder.AppendLine($"- Rate: `{config.Scenario.RatePerSecond}` requests/sec");
        builder.AppendLine($"- Duration: `{config.Scenario.DurationSeconds}` sec");
        builder.AppendLine();

        builder.AppendLine("| Target | Requests | OK | Failed | RPS | Mean, ms | P50, ms | P95, ms | P99, ms | Max, ms |");
        builder.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");

        foreach (var target in targets)
        {
            builder.AppendLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"| {target.TargetName} | {target.TotalRequests} | {target.OkRequests} | {target.FailedRequests} | {target.RequestsPerSecond:F2} | {target.MeanMs:F2} | {target.P50Ms:F2} | {target.P95Ms:F2} | {target.P99Ms:F2} | {target.MaxMs:F2} |"));
        }

        var baseline = targets.FirstOrDefault();
        if (baseline is not null && targets.Count > 1)
        {
            builder.AppendLine();
            builder.AppendLine($"## Delta vs `{baseline.TargetName}`");
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

        await File.WriteAllTextAsync(path, builder.ToString());
    }

    private static string PercentDelta(double value, double baseline)
    {
        if (Math.Abs(baseline) < double.Epsilon)
        {
            return "n/a";
        }

        var delta = (value - baseline) / baseline * 100;
        return delta.ToString("+#0.##%;-#0.##%;0%", CultureInfo.InvariantCulture);
    }
}
