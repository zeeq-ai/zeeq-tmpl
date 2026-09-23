using Microsoft.EntityFrameworkCore;

namespace Zeeq.Tmpl;

public class HealthEndpoint : IEndpoint
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", (HealthHandler handler) => handler.HandleAsync());
    }
}

public class HealthHandler(ZeeqContext dbContext) : IEndpointHandler
{
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<HealthHandler>();

    public async Task<string> HandleAsync()
    {
        // 👇 Start a trace here
        using var activity = ZeeqTelemetry.Trace(
            tags: [("endpoint", "health")],
            traceName: "HealthCheck"
        );

        // 👇 This log will be linked to the span
        Log.Here().Information("Health check requested!");

        // 👇 Database access will produce a span with the query!
        await dbContext.Database.ExecuteSqlRawAsync("SELECT 1");

        return $"Healthy @ {DateTime.UtcNow}";
    }
}
