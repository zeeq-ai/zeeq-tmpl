using Zeeq.Tmpl;

var builder = WebApplication.CreateBuilder(args);

// Add the settings into the DI container
builder
    .Services.AddOptions<AppSettings>()
    .Bind(builder.Configuration.GetSection(nameof(AppSettings)))
    .ValidateOnStart();

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

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Connect the endpoints
var endpoints = app.Services.GetRequiredService<IEnumerable<IEndpoint>>();

foreach (var endpoint in endpoints)
{
    endpoint.MapEndpoints(app);
}

app.Run();
