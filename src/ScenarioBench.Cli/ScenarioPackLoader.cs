using System.Reflection;
using System.Runtime.Loader;
using ScenarioBench.Abstractions;

namespace ScenarioBench.Cli;

internal sealed record LoadedScenarioPack(
    IScenarioPack Pack,
    IScenarioWorkflow Workflow,
    IReadOnlyDictionary<string, string> Properties);

internal static class ScenarioPackLoader
{
    public static LoadedScenarioPack? Load(
        ScenarioPackConfig? config,
        string configPath,
        string scenarioName)
    {
        if (config is null)
        {
            return null;
        }

        var assemblyPath = ResolvePath(config.AssemblyPath, Path.GetDirectoryName(Path.GetFullPath(configPath))!);
        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException($"Scenario pack assembly was not found: {assemblyPath}");
        }

        var loadContext = new ScenarioPackLoadContext(assemblyPath);
        var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
        var pack = CreatePack(assembly, config.TypeName);
        var workflow = SelectWorkflow(pack, config.Workflow, scenarioName);

        return new LoadedScenarioPack(pack, workflow, config.Properties);
    }

    private static string ResolvePath(string path, string configDirectory)
    {
        return Path.GetFullPath(Path.IsPathRooted(path)
            ? path
            : Path.Combine(configDirectory, path));
    }

    private static IScenarioPack CreatePack(Assembly assembly, string? typeName)
    {
        var packType = typeName is null
            ? FindPackType(assembly)
            : assembly.GetType(typeName, throwOnError: true)!;

        if (!typeof(IScenarioPack).IsAssignableFrom(packType))
        {
            throw new InvalidOperationException(
                $"Scenario pack type '{packType.FullName}' must implement {nameof(IScenarioPack)}.");
        }

        if (Activator.CreateInstance(packType) is not IScenarioPack pack)
        {
            throw new InvalidOperationException(
                $"Scenario pack type '{packType.FullName}' must have a public parameterless constructor.");
        }

        if (string.IsNullOrWhiteSpace(pack.Name))
        {
            throw new InvalidOperationException($"Scenario pack type '{packType.FullName}' returned an empty name.");
        }

        if (pack.Workflows.Count == 0)
        {
            throw new InvalidOperationException($"Scenario pack '{pack.Name}' does not expose any workflows.");
        }

        return pack;
    }

    private static Type FindPackType(Assembly assembly)
    {
        var candidates = assembly
            .GetTypes()
            .Where(type =>
                !type.IsAbstract &&
                type.GetConstructor(Type.EmptyTypes) is not null &&
                typeof(IScenarioPack).IsAssignableFrom(type))
            .ToArray();

        return candidates.Length switch
        {
            1 => candidates[0],
            0 => throw new InvalidOperationException(
                $"Assembly '{assembly.Location}' does not contain a public scenario pack type."),
            _ => throw new InvalidOperationException(
                $"Assembly '{assembly.Location}' contains multiple scenario pack types. Configure scenarioPack.typeName.")
        };
    }

    private static IScenarioWorkflow SelectWorkflow(IScenarioPack pack, string? configuredWorkflow, string scenarioName)
    {
        if (!string.IsNullOrWhiteSpace(configuredWorkflow))
        {
            return FindWorkflow(pack, configuredWorkflow);
        }

        var scenarioNameMatch = pack.Workflows
            .SingleOrDefault(workflow => string.Equals(workflow.Name, scenarioName, StringComparison.OrdinalIgnoreCase));

        if (scenarioNameMatch is not null)
        {
            return scenarioNameMatch;
        }

        if (pack.Workflows.Count == 1)
        {
            return pack.Workflows[0];
        }

        throw new InvalidOperationException(
            $"Scenario pack '{pack.Name}' exposes multiple workflows. Configure scenarioPack.workflow.");
    }

    private static IScenarioWorkflow FindWorkflow(IScenarioPack pack, string workflowName)
    {
        var workflow = pack.Workflows
            .SingleOrDefault(workflow => string.Equals(workflow.Name, workflowName, StringComparison.OrdinalIgnoreCase));

        return workflow ?? throw new InvalidOperationException(
            $"Scenario pack '{pack.Name}' does not contain workflow '{workflowName}'.");
    }
}

internal sealed class ScenarioPackLoadContext(string mainAssemblyPath) : AssemblyLoadContext
{
    private static readonly string AbstractionsAssemblyName = typeof(IScenarioPack).Assembly.GetName().Name!;

    private readonly AssemblyDependencyResolver _resolver = new(mainAssemblyPath);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name == AbstractionsAssemblyName)
        {
            return null;
        }

        var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        return assemblyPath is null ? null : LoadFromAssemblyPath(assemblyPath);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return libraryPath is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(libraryPath);
    }
}
