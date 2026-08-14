using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using Zeeq.Tmpl;

var builder = WebApplication.CreateBuilder(args);

// Add the settings into the DI container
builder
    .Services.AddOptions<AppSettings>()
    .Bind(builder.Configuration.GetSection(nameof(AppSettings)))
    .ValidateOnStart();

// Allow the Vite dev server to call this API cross-origin; its actual port varies per run behind the Aspire proxy.
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod())
);

// Setup logging
var attributes = new Dictionary<string, object> { ["service"] = "zeeq" };

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj} ({Here}){NewLine}{Exception}"
    )
    .WriteTo.OpenTelemetry(options =>
    {
        options.ResourceAttributes = attributes;
    })
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .CreateLogger();

builder.Services.AddSerilog();

// Wire up endpoints into the DI container
builder
    .Services.AddOpenApi()
    .Scan(scan =>
        scan.FromApplicationDependencies()
            .AddClasses(classes => classes.AssignableTo<IEndpoint>())
            .AsImplementedInterfaces()
            .WithTransientLifetime()
            .AddClasses(classes => classes.AssignableTo<IEndpointHandler>())
            .AsSelf()
            .WithTransientLifetime()
    );

// Add the channels; keyed for inbound and outbound.
builder
    .Services.AddKeyedSingleton("inbound", (sp, key) => Channel.CreateUnbounded<string>())
    .AddKeyedSingleton("outbound", (sp, key) => Channel.CreateUnbounded<string>())
    .AddHostedService<AgentServiceWorker>();

// 👇 This will be injected by Aspire when we connect the resources
var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__zeeq");

// Set up the database context
builder.Services.AddDbContext<ZeeqContext>(options =>
    options
        .UseNpgsql(connectionString)
        .EnableDetailedErrors(true)
        .EnableSensitiveDataLogging(true)
        .UseSnakeCaseNamingConvention()
);

// Setup telemetry
builder
    .Services.AddOpenTelemetry()
    .ConfigureResource(res => res.AddService("zeeq").AddAttributes(attributes))
    .WithTracing(builder =>
    {
        builder
            .AddSource(ZeeqTelemetry.ActivitySourceName)
            .AddAspNetCoreInstrumentation(config =>
            {
                config.RecordException = true;
            })
            .AddHttpClientInstrumentation()
            .AddEntityFrameworkCoreInstrumentation();
    })
    .WithMetrics(builder =>
        builder
            .AddMeter("*")
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddNpgsqlInstrumentation()
    )
    .WithLogging()
    .UseOtlpExporter();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
app.UseSerilogRequestLogging();

// Connect the endpoints
var endpoints = app.Services.GetRequiredService<IEnumerable<IEndpoint>>();

foreach (var endpoint in endpoints)
{
    endpoint.MapEndpoints(app);
}

app.Run();
