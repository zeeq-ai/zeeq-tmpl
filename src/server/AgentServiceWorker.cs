using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using GitHub.Copilot;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ILogger = Serilog.ILogger;

namespace Zeeq.Tmpl;

public class AgentServiceWorker(
    IOptions<AppSettings> options,
    [FromKeyedServices("inbound")] Channel<string> inboundChannel,
    [FromKeyedServices("outbound")] Channel<string> outboundChannel
) : BackgroundService
{
    private static readonly ILogger Log = Serilog.Log.ForContext<AgentServiceWorker>();

    private static readonly JsonSerializerOptions EventJson = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private CopilotClient? _client;
    private CopilotSession? _session;

    private static string Serialize(object value) => JsonSerializer.Serialize(value, EventJson);

    /// <summary>
    /// Set up the Copilot client and session with BYOK
    /// </summary>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        var currentAppDirectory = Environment.CurrentDirectory;
        var rootDirectory = Directory.GetParent(currentAppDirectory)?.Parent?.FullName ?? currentAppDirectory;

        Log.Here().Information("Using working directory: {RootDirectory}", rootDirectory);        

        // See: https://github.com/github/copilot-sdk/blob/main/docs/auth/byok.md
        // See: https://github.com/github/awesome-copilot/blob/main/cookbook/copilot-sdk/dotnet/recipe/managing-local-files.cs
        // See: https://github.com/github/copilot-sdk/blob/main/docs/observability/opentelemetry.md
        // Verbose Copilot SDK diagnostics, surfaced live via stdout (captured by `aspire logs
        // app-backend`) instead of the CLI's own log file, which only flushes on shutdown.
        var copilotDiagnosticsLogger = LoggerFactory
            .Create(builder => builder.SetMinimumLevel(LogLevel.Debug).AddConsole())
            .CreateLogger("CopilotSDK");

        _client = new CopilotClient(
            new()
            {
                WorkingDirectory = rootDirectory,
                LogLevel = CopilotLogLevel.Debug,
                Logger = copilotDiagnosticsLogger,
                Telemetry = new()
                {
                    // The app's own telemetry (Program.cs's UseOtlpExporter()) exports to the
                    // Aspire dashboard's gRPC OTLP endpoint (OTEL_EXPORTER_OTLP_ENDPOINT), but
                    // the Copilot CLI's exporter only speaks OTLP/HTTP, so it needs the
                    // dashboard's separate HTTP listener instead (see .config/mise.toml).
                    OtlpEndpoint = Environment.GetEnvironmentVariable(
                        "DOTNET_DASHBOARD_OTLP_HTTP_ENDPOINT_URL"
                    ),
                    OtlpProtocol = "http/protobuf",
                },
            }
        );
        _session = await _client.CreateSessionAsync(
            new()
            {
                Model = "gpt-5.6-luna",
                Streaming = true,
                OnPermissionRequest = PermissionHandler.ApproveAll,
                Provider = new()
                {
                    Type = "azure",
                    BaseUrl = options.Value.LlmBaseUrl, // 👈 This is an Azure OpenAI endpoint
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

        // Start the streaming output that writes to the outbound channel.
        // Each outbound message is a JSON envelope discriminated by `type`.
        // Tracks message ids that already streamed via delta events, so the final
        // AssistantMessageEvent (which repeats the full content) isn't re-sent and
        // double-appended on the frontend.
        var messageIdsWithDeltas = new HashSet<string>();
        string TrackDelta(string messageId, object payload)
        {
            messageIdsWithDeltas.Add(messageId);
            return Serialize(payload);
        }
        _session.On<SessionEvent>(evt =>
        {
            var envelope = evt switch
            {
                AssistantTurnStartEvent e => Serialize(
                    new
                    {
                        type = "turn_start",
                        turnId = e.Data.TurnId,
                        model = e.Data.Model,
                    }
                ),
                AssistantIntentEvent e => Serialize(new { type = "intent", text = e.Data.Intent }),
                AssistantReasoningDeltaEvent e => Serialize(
                    new
                    {
                        type = "thinking_delta",
                        id = e.Data.ReasoningId,
                        text = e.Data.DeltaContent,
                    }
                ),
                AssistantReasoningEvent e => Serialize(
                    new
                    {
                        type = "thinking_done",
                        id = e.Data.ReasoningId,
                        text = e.Data.Content,
                    }
                ),
                ToolExecutionStartEvent e => Serialize(
                    new
                    {
                        type = "tool_call_start",
                        id = e.Data.ToolCallId,
                        toolCallId = e.Data.ToolCallId,
                        name = e.Data.ToolName,
                        description = e.Data.ToolDescription?.Description,
                        args = e.Data.Arguments?.GetRawText(),
                    }
                ),
                ToolExecutionProgressEvent e => Serialize(
                    new
                    {
                        type = "tool_call_progress",
                        id = e.Data.ToolCallId,
                        toolCallId = e.Data.ToolCallId,
                        message = e.Data.ProgressMessage,
                    }
                ),
                ToolExecutionCompleteEvent e => Serialize(
                    new
                    {
                        type = "tool_call_result",
                        id = e.Data.ToolCallId,
                        toolCallId = e.Data.ToolCallId,
                        success = e.Data.Success,
                        result = e.Data.Result?.DetailedContent ?? e.Data.Result?.Content,
                        error = e.Data.Error?.Message,
                    }
                ),
                AssistantMessageDeltaEvent e => TrackDelta(
                    e.Data.MessageId,
                    new
                    {
                        type = "text_delta",
                        id = e.Data.MessageId,
                        text = e.Data.DeltaContent,
                    }
                ),
                AssistantMessageEvent e
                    when !string.IsNullOrEmpty(e.Data.Content)
                        && !messageIdsWithDeltas.Contains(e.Data.MessageId) => Serialize(
                    new
                    {
                        type = "text_delta",
                        id = e.Data.MessageId,
                        text = e.Data.Content,
                    }
                ),
                SessionIdleEvent e => Serialize(new { type = "done", aborted = e.Data.Aborted }),
                SessionErrorEvent e => Serialize(new { type = "error", message = e.Data.Message }),
                _ => null,
            };

            if (envelope is not null)
            {
                outboundChannel.Writer.TryWrite(envelope);
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
