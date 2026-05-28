using ScenarioBench.Abstractions;

namespace ScenarioBench.Cli;

internal static class ScenarioLister
{
    public static void Write(BenchmarkConfig config, LoadedScenarioPack? scenarioPack)
    {
        Console.WriteLine($"Suite: {config.RunName}");
        Console.WriteLine();

        Console.WriteLine("Configured scenarios:");
        foreach (var scenario in config.GetScenarios())
        {
            var workflow = scenarioPack is null
                ? null
                : ScenarioPackLoader.SelectWorkflow(scenarioPack, config.ScenarioPack!, scenario);

            Console.WriteLine(
                $"  - {scenario.Name} | driver={scenario.Driver} | step={scenario.GetStepName()} | workflow={workflow?.Name ?? "-"}");
        }

        Console.WriteLine();
        Console.WriteLine("Configured targets:");
        foreach (var target in config.Targets)
        {
            Console.WriteLine($"  - {target.Name} | {target.BaseUrl}");
        }

        Console.WriteLine();
        if (scenarioPack is null)
        {
            Console.WriteLine("Scenario pack: -");
            return;
        }

        Console.WriteLine($"Scenario pack: {scenarioPack.Pack.Name}");
        Console.WriteLine("Pack workflows:");
        foreach (var workflow in scenarioPack.Pack.Workflows)
        {
            var load = workflow is IScenarioLoadWorkflow ? "load" : "validate";
            Console.WriteLine($"  - {workflow.Name} | {load}");
        }
    }
}
