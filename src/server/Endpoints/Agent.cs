using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Zeeq.Tmpl;

public class AgentEndpoint : IEndpoint
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
            "/send-prompt",
            (AgentHandler handler, string prompt) => handler.Handle(prompt)
        );
    }
}

public class AgentHandler([FromKeyedServices("inbound")] Channel<string> inboundChannel)
    : IEndpointHandler
{
    public bool Handle(string prompt) => inboundChannel.Writer.TryWrite(prompt);
}

public class AgentResponseEndpoint : IEndpoint
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/read-response",
            async (AgentResponseHandler handler, CancellationToken cancellation) =>
                TypedResults.ServerSentEvents(handler.HandleAsync(cancellation))
        );
    }
}

public class AgentResponseHandler([FromKeyedServices("outbound")] Channel<string> outboundChannel)
    : IEndpointHandler
{
    public async IAsyncEnumerable<SseItem<string>> HandleAsync(
        [EnumeratorCancellation] CancellationToken cancellation
    )
    {
        while (await outboundChannel.Reader.WaitToReadAsync(cancellation))
        {
            while (outboundChannel.Reader.TryRead(out var response))
            {
                yield return new SseItem<string>(response);
            }
        }
    }
}
