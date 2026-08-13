using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
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

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();

// Connect the endpoints
var endpoints = app.Services.GetRequiredService<IEnumerable<IEndpoint>>();

foreach (var endpoint in endpoints)
{
    endpoint.MapEndpoints(app);
}

app.Run();
