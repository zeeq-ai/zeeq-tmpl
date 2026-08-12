namespace Zeeq.Tmpl;

/// <summary>
/// Interface for wiring up HTTP endpoints
/// </summary>
public interface IEndpoint
{
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
