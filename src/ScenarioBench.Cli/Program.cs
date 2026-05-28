using ScenarioBench.Cli;

var options = CliOptions.Parse(args);

if (options.ShowHelp)
{
    Console.WriteLine(CliOptions.HelpText);
    return 0;
}

if (options.Error is not null)
{
    Console.Error.WriteLine(options.Error);
    Console.Error.WriteLine();
    Console.Error.WriteLine(CliOptions.HelpText);
    return 2;
}

try
{
    var config = await BenchmarkConfig.LoadAsync(options.ConfigPath);
    if (options.ListScenarios)
    {
        var scenarioPack = ScenarioPackLoader.Load(config.ScenarioPack, options.ConfigPath);
        ScenarioLister.Write(config, scenarioPack);
        return 0;
    }

    var runner = new BenchmarkRunner(config, options.ConfigPath, options.InfraConfigPath, options.Selection);
    var runResult = await runner.RunAsync();

    Console.WriteLine();
    Console.WriteLine($"ScenarioBench run completed: {runResult.RunId}");
    Console.WriteLine($"Artifacts: {runResult.ArtifactDirectory}");
    Console.WriteLine($"Comparison: {runResult.ComparisonReportPath}");
    Console.WriteLine($"Manifest: {runResult.ManifestPath}");

    return runResult.Targets.Any(target => !target.Passed) ? 1 : 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
