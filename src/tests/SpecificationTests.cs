using System.Threading.Channels;
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

    [Test]
    public async Task SpecificationListHandler_ReturnsSavedSpecifications_OrderedByMostRecentlyUpdated()
    {
        _context.Set<Specification>().Add(new() { Name = "Older", Content = "a" });
        await _context.SaveChangesAsync();

        _context.Set<Specification>().Add(new() { Name = "Newer", Content = "b", UpdatedAtUtc = DateTime.UtcNow });
        await _context.SaveChangesAsync();

        _context.ChangeTracker.Clear();

        var handler = new SpecificationListHandler(_context);

        var result = await handler.HandleAsync();

        await Assert.That(result).Count().IsEqualTo(2);
        await Assert.That(result[0].Name).IsEqualTo("Newer");
        await Assert.That(result[1].Name).IsEqualTo("Older");
    }

    [Test]
    public async Task SpecificationSaveHandler_WithoutId_CreatesNewSpecification()
    {
        var handler = new SpecificationSaveHandler(_context);

        var result = await handler.HandleAsync(new SaveSpecificationRequest(null, "New Spec", "one two three"));

        await Assert.That(result.Id).IsNotEqualTo(Guid.Empty);
        await Assert.That(result.Name).IsEqualTo("New Spec");
        await Assert.That(result.TokenCount).IsEqualTo(3);
        await Assert.That(result.UpdatedAtUtc).IsNull();

        _context.ChangeTracker.Clear();

        var stored = await _context.Set<Specification>().FirstOrDefaultAsync(s => s.Id == result.Id);

        await Assert.That(stored).IsNotNull();
        await Assert.That(stored!.Content).IsEqualTo("one two three");
    }

    [Test]
    public async Task SpecificationSaveHandler_WithExistingId_UpdatesSpecificationInPlace()
    {
        var existing = new Specification { Name = "Original", Content = "hello world" };
        _context.Set<Specification>().Add(existing);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var handler = new SpecificationSaveHandler(_context);

        var result = await handler.HandleAsync(
            new SaveSpecificationRequest(existing.Id, "Updated", "hello there world")
        );

        await Assert.That(result.Id).IsEqualTo(existing.Id);
        await Assert.That(result.Name).IsEqualTo("Updated");
        await Assert.That(result.TokenCount).IsEqualTo(3);
        await Assert.That(result.UpdatedAtUtc).IsNotNull();

        _context.ChangeTracker.Clear();

        var count = await _context.Set<Specification>().CountAsync();
        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task SpecificationDiffHandler_Handle_WritesDiffToInboundChannel()
    {
        var channel = Channel.CreateUnbounded<string>();
        var handler = new SpecificationDiffHandler(channel);
        var id = Guid.NewGuid();

        var written = handler.Handle(id, "- old line\n+ new line");

        await Assert.That(written).IsTrue();
        await Assert.That(channel.Reader.TryRead(out var message)).IsTrue();
        await Assert.That(message).Contains(id.ToString());
        await Assert.That(message).Contains("- old line\n+ new line");
    }
}
