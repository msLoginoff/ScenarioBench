using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScenarioBench.Cli;

internal static class InfluxMetricsCleaner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task ClearAsync(string infraConfigPath, MetricsCleanupOptions options)
    {
        if (!File.Exists(infraConfigPath))
        {
            throw new FileNotFoundException($"Infrastructure config file was not found: {infraConfigPath}");
        }

        var config = await LoadConfigAsync(infraConfigPath);
        var query = options.ClearAll
            ? $"DROP MEASUREMENT {QuoteIdentifier(config.Measurement)}"
            : $"DELETE FROM {QuoteIdentifier(config.Measurement)} WHERE {QuoteIdentifier("suite_id")} = {QuoteString(options.SuiteId!)}";

        using var httpClient = new HttpClient
        {
            BaseAddress = config.Url,
            Timeout = TimeSpan.FromSeconds(15)
        };

        using var response = await httpClient.PostAsync(CreateQueryPath(config, query), content: null);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"InfluxDB cleanup failed with HTTP {(int)response.StatusCode}: {responseBody}");
        }

        EnsureInfluxSuccess(responseBody);

        Console.WriteLine(options.ClearAll
            ? $"Deleted all metrics from InfluxDB measurement '{config.Measurement}'."
            : $"Deleted metrics for suite_id '{options.SuiteId}' from InfluxDB measurement '{config.Measurement}'.");
    }

    private static async Task<InfluxCleanupConfig> LoadConfigAsync(string path)
    {
        var json = await File.ReadAllTextAsync(path);
        var root = JsonSerializer.Deserialize<InfraRoot>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Infrastructure config is empty or invalid: {path}");

        var sink = root.InfluxDBSink
            ?? throw new InvalidOperationException($"Infrastructure config does not contain InfluxDBSink: {path}");

        if (sink.Url is null)
        {
            throw new InvalidOperationException("InfluxDBSink.Url is required for metrics cleanup.");
        }

        if (string.IsNullOrWhiteSpace(sink.Database))
        {
            throw new InvalidOperationException("InfluxDBSink.Database is required for metrics cleanup.");
        }

        return new InfluxCleanupConfig(
            Url: sink.Url,
            Database: sink.Database,
            UserName: sink.UserName,
            Password: sink.Password,
            Measurement: string.IsNullOrWhiteSpace(sink.Measurement) ? "nbomber" : sink.Measurement);
    }

    private static string CreateQueryPath(InfluxCleanupConfig config, string query)
    {
        var values = new List<string>
        {
            CreateQueryParameter("db", config.Database),
            CreateQueryParameter("q", query)
        };

        if (!string.IsNullOrWhiteSpace(config.UserName))
        {
            values.Add(CreateQueryParameter("u", config.UserName));
        }

        if (!string.IsNullOrWhiteSpace(config.Password))
        {
            values.Add(CreateQueryParameter("p", config.Password));
        }

        return $"query?{string.Join("&", values)}";
    }

    private static string CreateQueryParameter(string name, string value)
    {
        return $"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}";
    }

    private static void EnsureInfluxSuccess(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);

        if (document.RootElement.TryGetProperty("error", out var rootError))
        {
            throw new InvalidOperationException($"InfluxDB cleanup failed: {rootError.GetString()}");
        }

        if (!document.RootElement.TryGetProperty("results", out var results))
        {
            return;
        }

        foreach (var result in results.EnumerateArray())
        {
            if (result.TryGetProperty("error", out var error))
            {
                throw new InvalidOperationException($"InfluxDB cleanup failed: {error.GetString()}");
            }
        }
    }

    private static string QuoteIdentifier(string value)
    {
        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static string QuoteString(string value)
    {
        return $"'{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal)}'";
    }

    private sealed record InfraRoot(InfluxSinkConfig? InfluxDBSink);

    private sealed record InfluxSinkConfig(
        Uri? Url,
        string? Database,
        string? UserName,
        string? Password,
        string? Measurement);

    private sealed record InfluxCleanupConfig(
        Uri Url,
        string Database,
        string? UserName,
        string? Password,
        string Measurement);
}
