using Microsoft.EntityFrameworkCore;
using Zeeq.Tmpl;

[ClassDataSource<PgDatabaseFixture>(Shared = SharedType.PerTestSession)]
public class SpecificationTests(PgDatabaseFixture pg) : PgTransactionalTestBase(pg)
{
    [Test]
    public async Task Specification_Basic_WriteRead()
    {
        _context.Set<Specification>().Add(new() { Name = "Test Spec" });

        await _context.SaveChangesAsync();

        _context.ChangeTracker.Clear();

        var spec = await _context.Set<Specification>().FirstOrDefaultAsync();

        await Assert.That(spec).IsNotNull();
        await Assert.That(spec!.Name).IsEqualTo("Test Spec");
        await Assert.That(spec.Id).IsNotEqualTo(Guid.Empty);
    }
}
