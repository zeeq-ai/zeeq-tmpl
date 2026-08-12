namespace Zeeq.Tmpl;

public class HealthEndpoint : IEndpoint
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", (HealthHandler handler) => handler.Handle());
    }
}

public class HealthHandler : IEndpointHandler
{
    public string Handle() => $"Healthy @ {DateTime.UtcNow}";
}
