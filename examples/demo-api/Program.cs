var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var defaultWorkDelayMs = ReadInt("SCENARIOBENCH_DEFAULT_WORK_DELAY_MS", 25);
var defaultWorkBytes = ReadInt("SCENARIOBENCH_DEFAULT_WORK_BYTES", 128);

app.MapGet("/health", () => Results.Text("ok", "text/plain"));

app.MapGet("/work", async (int? delayMs, int? bytes, CancellationToken cancellationToken) =>
{
    var boundedDelay = Math.Clamp(delayMs ?? defaultWorkDelayMs, 0, 5_000);
    var boundedBytes = Math.Clamp(bytes ?? defaultWorkBytes, 0, 64 * 1024);

    if (boundedDelay > 0)
    {
        await Task.Delay(boundedDelay, cancellationToken);
    }

    return Results.Text(new string('x', boundedBytes), "text/plain");
});

app.Run();

static int ReadInt(string name, int fallback)
{
    return int.TryParse(Environment.GetEnvironmentVariable(name), out var value)
        ? value
        : fallback;
}
