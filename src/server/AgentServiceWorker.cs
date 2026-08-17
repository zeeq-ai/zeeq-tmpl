using System.Threading.Channels;
using GitHub.Copilot;
using Microsoft.Extensions.Options;

namespace Zeeq.Tmpl;

public class AgentServiceWorker(
    IOptions<AppSettings> options,
    [FromKeyedServices("inbound")] Channel<string> inboundChannel,
    [FromKeyedServices("outbound")] Channel<string> outboundChannel
) : BackgroundService
{
    // 👇 Change this to match some location on your disk
    private const string WorkingDirectory = "/Users/cchen/code/zeeq/zeeq-app";
    private CopilotClient? _client;
    private CopilotSession? _session;

    /// <summary>
    /// Set up the Copilot client and session with BYOK
    /// </summary>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // See: https://github.com/github/copilot-sdk/blob/main/docs/auth/byok.md
        // See: https://github.com/github/awesome-copilot/blob/main/cookbook/copilot-sdk/dotnet/recipe/managing-local-files.cs
        _client = new CopilotClient(new() { WorkingDirectory = WorkingDirectory });
        _session = await _client.CreateSessionAsync(
            new()
            {
                Model = "gpt-5.6-luna",
                OnPermissionRequest = PermissionHandler.ApproveAll,
                Provider = new()
                {
                    Type = "azure",
                    BaseUrl = "https://zeeq-open-ai.openai.azure.com", // 👈 This is an Azure OpenAI endpoint
                    WireApi = "responses",
                    ApiKey = options.Value.LlmApiKey,
                },
            },
            cancellationToken
        );

        await base.StartAsync(cancellationToken);
    }

    /// <summary>
    /// Execute the incoming prompt and write the response to the outbound chnanle.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_session is null)
        {
            throw new InvalidOperationException("Copilot session is not initialized.");
        }

        if (_client is null)
        {
            throw new InvalidOperationException("Copilot client is not initialized.");
        }

        // Start the streaming output that writes to the outbound channel
        _session.On<SessionEvent>(evt =>
        {
            if (evt is AssistantMessageEvent messageEvent)
            {
                outboundChannel.Writer.TryWrite(messageEvent.Data.Content);
            }
        });

        // Start the message loop here
        while (await inboundChannel.Reader.WaitToReadAsync(stoppingToken))
        {
            while (inboundChannel.Reader.TryRead(out var message))
            {
                await _session.SendAsync(message, cancellationToken: stoppingToken);
            }
        }
    }

    /// <summary>
    /// Clean up resources by disposing on stop.
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        if (_session is not null)
        {
            await _session.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }
}
