using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;

namespace Zeeq.Tmpl;

public record SpecificationDto(
    Guid Id,
    string Name,
    string Content,
    long TokenCount,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc
)
{
    public static SpecificationDto FromModel(Specification spec) =>
        new(
            spec.Id,
            spec.Name,
            spec.Content,
            spec.TokenCount,
            spec.CreatedAtUtc,
            spec.UpdatedAtUtc
        );
}

/// <summary>
/// Body for saving a specification. Omit <see cref="Id"/> to create a new one;
/// pass an existing <see cref="Id"/> to update it in place.
/// </summary>
public record SaveSpecificationRequest(Guid? Id, string Name, string Content);

public class SpecificationEndpoint : IEndpoint
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/specifications",
            (SpecificationListHandler handler) => handler.HandleAsync()
        );

        endpoints.MapPost(
            "/specifications",
            (SpecificationSaveHandler handler, SaveSpecificationRequest request) =>
                handler.HandleAsync(request)
        );

        endpoints.MapPost(
            "/specifications/{id:guid}/diff",
            (SpecificationDiffHandler handler, Guid id, string diff) => handler.Handle(id, diff)
        );
    }
}

public class SpecificationListHandler(ZeeqContext dbContext) : IEndpointHandler
{
    private static readonly Serilog.ILogger Log =
        Serilog.Log.ForContext<SpecificationListHandler>();

    public async Task<List<SpecificationDto>> HandleAsync()
    {
        using var activity = ZeeqTelemetry.Trace(
            tags: [("endpoint", "specifications.list")],
            traceName: "SpecificationList"
        );

        Log.Here().Information("Listing specifications");

        var specs = await dbContext
            .Set<Specification>()
            .OrderByDescending(s => s.UpdatedAtUtc ?? s.CreatedAtUtc)
            .ToListAsync();

        return specs.Select(SpecificationDto.FromModel).ToList();
    }
}

public class SpecificationSaveHandler(ZeeqContext dbContext) : IEndpointHandler
{
    private static readonly Serilog.ILogger Log =
        Serilog.Log.ForContext<SpecificationSaveHandler>();

    public async Task<SpecificationDto> HandleAsync(SaveSpecificationRequest request)
    {
        using var activity = ZeeqTelemetry.Trace(
            tags: [("endpoint", "specifications.save")],
            traceName: "SpecificationSave"
        );

        var tokenCount = request
            .Content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Length;

        var spec = request.Id is { } id
            ? await dbContext.Set<Specification>().FirstOrDefaultAsync(s => s.Id == id)
            : null;

        if (spec is null)
        {
            spec = new Specification
            {
                Name = request.Name,
                Content = request.Content,
                TokenCount = tokenCount,
            };

            // 👇 Preserve a client-supplied id (e.g. optimistic creation) if one was given
            if (request.Id is { } newId)
            {
                spec.Id = newId;
            }

            dbContext.Set<Specification>().Add(spec);

            Log.Here().Information("Creating specification {Name}", request.Name);
        }
        else
        {
            spec.Name = request.Name;
            spec.Content = request.Content;
            spec.TokenCount = tokenCount;
            spec.UpdatedAtUtc = DateTime.UtcNow;

            Log.Here().Information("Updating specification {Id}", spec.Id);
        }

        await dbContext.SaveChangesAsync();

        return SpecificationDto.FromModel(spec);
    }
}

/// <summary>
/// Forwards a specification diff onto the agent's inbound channel, matching the
/// pattern used by <see cref="AgentHandler"/> for chat prompts.
/// </summary>
public class SpecificationDiffHandler([FromKeyedServices("inbound")] Channel<string> inboundChannel)
    : IEndpointHandler
{
    private static readonly Serilog.ILogger Log =
        Serilog.Log.ForContext<SpecificationDiffHandler>();

    public bool Handle(Guid id, string diff)
    {
        using var activity = ZeeqTelemetry.Trace(
            tags: [("endpoint", "specifications.diff"), ("specification.id", id)],
            traceName: "SpecificationDiff"
        );

        Log.Here().Information("Forwarding diff for specification {Id}", id);

        var instructionMessage = $"""
            The following is a diff for specification {id}.

            Your objective is to review the change in the requirements and specification and perform the research required to understand the technical change

            - Identify the key files that will be affected by this specification change
            - Ask any clarifying questions you need to understand the change
            - Summarize the change in the technical specification you are building for this requirements specification based on this change.

            <specification_diff>
            {diff}
            </specification_diff>
            """;

        return inboundChannel.Writer.TryWrite(instructionMessage);
    }
}
