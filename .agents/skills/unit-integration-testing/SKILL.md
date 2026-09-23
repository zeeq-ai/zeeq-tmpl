---
name: unit-integration-testing
description: Write and run .NET unit/integration tests in src/tests using TUnit (not xUnit) and Microsoft Testing Platform.
---

# Unit & Integration Testing (TUnit)

`src/tests` uses **TUnit** on **Microsoft Testing Platform** (`dotnet run`, not `dotnet test` semantics — it's a self-executing console app). Integration tests spin up a real Postgres via Testcontainers (`PgDatabaseFixture`) and roll back per-test transactions (`PgTransactionalTestBase`) — prefer this over mocking `DbContext`.

Docs: [test-filters](https://tunit.dev/docs/execution/test-filters) · [assertions](https://tunit.dev/docs/assertions/getting-started) · [xunit migration](https://tunit.dev/docs/migration/xunit) · [command-line flags](https://tunit.dev/docs/reference/command-line-flags)

## Best practices

- **High signal, low noise.** Assert on what the test is actually about; don't pad a test with unrelated checks or re-verify things another test already covers. A failing test's name and assertions should tell you exactly what broke, with nothing to wade through.
- **Focus on invariants.** Test behavior/contracts that must hold (e.g., "a saved entity round-trips with the same data"), not implementation details that change with refactors. Avoid asserting internal call counts or private state unless that's the actual contract under test.
- **Class-level setup for shared resources.** TUnit creates a new class instance per test, so constructor/field state doesn't leak between tests — use `[Before(Test)]`/constructor injection (e.g., `PgTransactionalTestBase(pg)`) for per-test setup, and a shared `[ClassDataSource<T>]` fixture for expensive resources (containers, connections) that should live across the run.
- **Name tests `EntityOrScopeName_StateUnderTest_ExpectedBehavior`**, e.g. `Specification_Basic_WriteRead`, `Widget_WhenNameIsEmpty_ThrowsValidationException`. The name alone should tell you what's being tested and what "pass" means, without opening the method body.

## Running tests

| Command | Purpose |
|---|---|
| `dotnet run --project src/tests` | Run the full suite. |
| `dotnet run --project src/tests --treenode-filter "/*/*/SpecificationTests/*"` | Run one class. |
| `dotnet run --project src/tests --treenode-filter "/*/*/*/Specification_Basic_WriteRead"` | Run one test method. |
| `dotnet run --project src/tests --treenode-filter "/*/*/*/*[Category=Smoke]"` | Run by `[Property]`/category. |
| `dotnet run --project src/tests --list-tests` | List discovered tests without running. |
| `dotnet run --project src/tests --fail-fast` | Stop on first failure. |
| `dotnet run --project src/tests --report-trx` | Emit a TRX report. |
| `dotnet run --project src/tests -- --help` | Full flag reference. |

## Writing a test

```csharp
public class MyTests
{
    [Test]
    public async Task Adds_Numbers()
    {
        var result = 2 + 2;

        await Assert.That(result).IsEqualTo(4);
    }
}
```

- `[Test]` covers both xUnit's `[Fact]` and `[Theory]`; parameterize with `[Arguments(...)]`.
- Every `Assert.That(...)` call must be `await`ed — an unawaited assertion silently no-ops.

## Assertion patterns

Docs: [equality & comparison](https://tunit.dev/docs/assertions/equality-and-comparison) · [combining assertions](https://tunit.dev/docs/assertions/combining-assertions) · [exceptions](https://tunit.dev/docs/assertions/exceptions)

### Equality & comparison

```csharp
await Assert.That(result).IsEqualTo(10);
await Assert.That(actual).IsNotEqualTo(0);
await Assert.That(score).IsGreaterThan(70);
await Assert.That(percentage).IsBetween(0, 100);
await Assert.That(actual).IsEqualTo(expected).Within(0.001); // double/float/decimal/long tolerance
await Assert.That(instance).IsSameReferenceAs(other);         // reference, not value, equality

await Assert.That(people1)
    .IsEquivalentTo(people2)
    .Using((p1, p2) => string.Equals(p1.Name, p2.Name, StringComparison.OrdinalIgnoreCase));
```

`IsEqualTo` uses the type's `Equals()`/`==`; records and structs get value equality for free. Use `IsEquivalentTo` for collection/object comparisons, with `.Using(...)` for a custom comparer or predicate.

### Combining assertions

```csharp
// .And — all must pass
await Assert.That(result).IsNotNull().And.IsPositive().And.IsEqualTo(3);

// .Or — at least one must pass
await Assert.That(result).IsEqualTo(2).Or.IsEqualTo(3).Or.IsEqualTo(4);

// Assert.Multiple — collect every failure instead of stopping at the first
using (Assert.Multiple())
{
    await Assert.That(result).IsPositive();
    await Assert.That(result).IsEqualTo(3);
}
```

`.And` and `.Or` cannot be mixed in the same chain (throws `MixedAndOrAssertionsException`) — split into separate `Assert.That()` calls instead.

### Exceptions

```csharp
await Assert.That(() => int.Parse("not a number")).Throws<FormatException>();          // exact type or subclass
await Assert.That(() => throw new ArgumentNullException()).ThrowsExactly<ArgumentNullException>();
await Assert.That(() => int.Parse("42")).ThrowsNothing();

await Assert.That(() => ValidateUser(null!))
    .Throws<ArgumentNullException>()
    .WithParameterName("user");

await Assert.That(() => throw new ArgumentException("The parameter 'userId' is invalid"))
    .Throws<ArgumentException>()
    .WithMessageContaining("userId");

// async delegates work the same way
await Assert.That(async () => await FailingOperationAsync()).Throws<HttpRequestException>();
```

Other message matchers: `WithMessage` (exact), `WithMessageNotContaining`, `WithMessageMatching` (regex), `.IgnoringCase()`; `WithInnerException` chains onto the inner exception.

## Integration tests against Postgres

Reuse the existing fixture/base rather than standing up your own container — see `PgDatabaseFixture.cs` and `PgTransactionalTestBase.cs`.

```csharp
[ClassDataSource<PgDatabaseFixture>(Shared = SharedType.PerTestSession)]
public class WidgetTests(PgDatabaseFixture pg) : PgTransactionalTestBase(pg)
{
    [Test]
    public async Task Widget_Basic_WriteRead()
    {
        _context.Set<Widget>().Add(new() { Name = "Test" });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var widget = await _context.Set<Widget>().FirstOrDefaultAsync();

        await Assert.That(widget).IsNotNull();
        await Assert.That(widget!.Name).IsEqualTo("Test");
    }
}
```

`[ClassDataSource<PgDatabaseFixture>(Shared = SharedType.PerTestSession)]` boots one Postgres container for the whole run; `PgTransactionalTestBase` wraps each `[Test]` in a transaction rolled back in `[After(Test)]`, so tests don't need their own cleanup.

### Scaffolding notes

- `PgDatabaseFixture` implements `IAsyncInitializer`/`IAsyncDisposable`: `InitializeAsync()` starts one `postgres:18` Testcontainer with a random host port (`WithPortBinding(5432, true)`) and waits for both the "ready to accept connections" log line and the internal TCP port; `DisposeAsync()` tears the container down (`WithAutoRemove(true)`).
- Schema setup uses `context.Database.EnsureCreatedAsync()` once, not EF Core migrations — the fixture builds the schema directly from the current model on container start.
- `DbContextOptions` are cached (`_options ??= ...`) and built with `UseSnakeCaseNamingConvention()`, `EnableDetailedErrors(true)`, `EnableSensitiveDataLogging(true)` — every context created via `CreateContext()` shares these options and points at the same container.
- `Shared = SharedType.PerTestSession` on `[ClassDataSource<PgDatabaseFixture>]` is what makes the container session-wide: it's created once and reused across every test class that requests it, not per-class or per-test. Any test class that needs the database declares the same attribute and takes `PgDatabaseFixture` as a constructor parameter.
- `PgTransactionalTestBase(pg)` calls `pg.CreateContext()` once per test-class instance and exposes it as the protected `_context` field — write your test bodies against `_context` directly rather than creating a new context.

### Test isolation

- Isolation is per-test, not per-container: `[Before(Test)]` opens a new transaction on `_context`, `[After(Test)]` rolls it back and disposes `_context`. Nothing a test writes is ever committed.
- Because rollback is transaction-scoped, tests in the same class/session can run against the same physical database and schema without cleaning up after themselves or colliding on shared rows — as long as all writes go through `_context` inside the transaction.
- Anything that bypasses `_context`'s transaction (a second connection, raw SQL on a different context, background work not awaited before the test ends) escapes rollback and can leak state into later tests.
- The schema itself is not reset between tests, only the data written inside the transaction — `EnsureCreatedAsync()` runs once per container lifetime (i.e., once per test session under `SharedType.PerTestSession`), so schema changes require a fresh container, not a fresh test.

## xUnit → TUnit cheat sheet

| xUnit | TUnit |
|---|---|
| `[Fact]` / `[Theory]` | `[Test]` |
| `[InlineData(...)]` | `[Arguments(...)]` |
| `[MemberData(nameof(...))]` | `[MethodDataSource(nameof(...))]` |
| `[Trait("key","value")]` | `[Property("key","value")]` |
| Constructor / `IDisposable` | `[Before(Test)]` / `[After(Test)]` |
| `IAsyncLifetime` | `[Before(Test)]` / `[After(Test)]` (async-native) |
| `IClassFixture<T>` | `[ClassDataSource<T>(Shared = SharedType.PerClass)]` |
| `Assert.Equal(expected, actual)` | `await Assert.That(actual).IsEqualTo(expected)` |
| `ITestOutputHelper` ctor param | `TestContext` method param → `.Output` |
