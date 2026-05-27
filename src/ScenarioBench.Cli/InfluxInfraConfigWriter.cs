using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScenarioBench.Cli;

internal static class InfluxInfraConfigWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static async Task<string> WriteTargetConfigAsync(
        string sourcePath,
        string outputPath,
        IReadOnlyDictionary<string, string> tags)
    {
        var root = JsonNode.Parse(await File.ReadAllTextAsync(sourcePath))?.AsObject()
            ?? throw new InvalidOperationException($"Infrastructure config is empty or invalid: {sourcePath}");

        var sink = GetOrCreateObject(root, "InfluxDBSink");
        var customTags = GetOrCreateArray(sink, "CustomTags");

        foreach (var (key, value) in tags)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            customTags.Add(new JsonObject
            {
                ["Key"] = key,
                ["Value"] = value
            });
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, root.ToJsonString(JsonOptions));
        return outputPath;
    }

    private static JsonObject GetOrCreateObject(JsonObject root, string name)
    {
        if (root[name] is JsonObject existing)
        {
            return existing;
        }

        var created = new JsonObject();
        root[name] = created;
        return created;
    }

    private static JsonArray GetOrCreateArray(JsonObject root, string name)
    {
        if (root[name] is JsonArray existing)
        {
            return existing;
        }

        var created = new JsonArray();
        root[name] = created;
        return created;
    }
}
