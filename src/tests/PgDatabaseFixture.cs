using DotNet.Testcontainers.Builders;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using TUnit.Core.Interfaces;
using Zeeq.Tmpl;

/// <summary>
/// TUnit fixture for setting up the Postgres Testcontainer database
/// </summary>
public class PgDatabaseFixture : IAsyncInitializer, IAsyncDisposable
{
    /// <summary>
    /// The container instance for this fixture.
    /// </summary>
    private PostgreSqlContainer? _container;
    private DbContextOptions<ZeeqContext> _options = null!;

    private DbContextOptions<ZeeqContext> EnsureOptions()
    {
        if (_container is null)
        {
            throw new InvalidOperationException("Postgres container is not initialized.");
        }

        _options ??= new DbContextOptionsBuilder<ZeeqContext>()
            .UseNpgsql(_container.GetConnectionString())
            .EnableDetailedErrors(true)
            .EnableSensitiveDataLogging(true)
            .UseSnakeCaseNamingConvention()
            .Options;

        return _options;
    }

    public ZeeqContext CreateContext() => new(EnsureOptions());

    public async Task InitializeAsync()
    {
        // Initialize the Postgres container
        _container = new PostgreSqlBuilder("postgres:18")
            .WithPortBinding(5432, true)
            .WithPassword("password")
            .WithUsername("username")
            .WithDatabase("zeeq")
            .WithWaitStrategy(
                Wait.ForUnixContainer()
                    .UntilMessageIsLogged("database system is ready to accept connections")
                    .UntilInternalTcpPortIsAvailable(5432)
            )
            .WithAutoRemove(true)
            .Build();

        await _container.StartAsync();

        // Run the migrations to set up the database schema
        using var context = new ZeeqContext(EnsureOptions());

        await context.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }

        GC.SuppressFinalize(this);

        await ValueTask.CompletedTask;
    }
}
