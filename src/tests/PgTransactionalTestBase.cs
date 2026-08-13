using Microsoft.EntityFrameworkCore.Storage;
using Zeeq.Tmpl;

/// <summary>
/// A base class that provides access to creating a context and transaction scoping
/// around the operation.
/// </summary>
public abstract class PgTransactionalTestBase(PgDatabaseFixture pg)
{
    private IDbContextTransaction? _transaction;

    protected ZeeqContext _context = pg.CreateContext();

    [Before(Test)] // 👈 Start a transaction before the test
    public async Task Before()
    {
        _transaction = await _context.Database.BeginTransactionAsync();
    }

    [After(Test)] // 👈 Roll it back so nothing is committed to state
    public async Task Cleanup()
    {
        if (_transaction != null)
        {
            await _transaction.RollbackAsync();
            await _transaction.DisposeAsync();
        }

        await _context.DisposeAsync();
    }
}
