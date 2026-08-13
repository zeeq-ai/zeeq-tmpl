using Microsoft.EntityFrameworkCore;

namespace Zeeq.Tmpl;

public class ZeeqContext(DbContextOptions<ZeeqContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 👇 Applying the entity configurations only from this assembly; modify as needed
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ZeeqContext).Assembly);
    }
}
