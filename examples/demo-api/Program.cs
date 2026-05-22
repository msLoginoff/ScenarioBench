var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/health", () => Results.Text("ok", "text/plain"));

app.MapGet("/work", async (int? delayMs, int? bytes, CancellationToken cancellationToken) =>
{
    var boundedDelay = Math.Clamp(delayMs ?? 25, 0, 5_000);
    var boundedBytes = Math.Clamp(bytes ?? 128, 0, 64 * 1024);

    if (boundedDelay > 0)
    {
        await Task.Delay(boundedDelay, cancellationToken);
    }

    return Results.Text(new string('x', boundedBytes), "text/plain");
});

app.Run();
