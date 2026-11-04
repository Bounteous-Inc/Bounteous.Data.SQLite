# Bounteous.Data.SQLite

SQLite provider wrapper for [Bounteous.Data](https://github.com/Bounteous-Inc/Bounteous.Data). It supplies
`SqliteDbContextFactory<T, TUserId>`, which configures EF Core's SQLite provider and inherits auditing,
soft delete, and observer support from `Bounteous.Data`.

Sibling packages: `Bounteous.Data.SqlServer`, `Bounteous.Data.PostgreSQL`, `Bounteous.Data.MySQL`.

## Installation

```bash
dotnet add package Bounteous.Data.SQLite
```

## Usage

### 1. Define the context

```csharp
public class AppDbContext(
    DbContextOptions options,
    IDbContextObserver observer,
    IIdentityProvider<Guid> identityProvider)
    : DbContextBase<Guid>(options, observer, identityProvider)
{
    public DbSet<Customer> Customers => Set<Customer>();

    protected override void RegisterModels(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Customer>().HasKey(c => c.Id);
}
```

### 2. Derive the factory

```csharp
using Bounteous.Data.SQLite;

public class AppDbContextFactory(
    IConnectionBuilder connectionBuilder,
    IDbContextObserver observer,
    IIdentityProvider<Guid> identityProvider)
    : SqliteDbContextFactory<AppDbContext, Guid>(connectionBuilder, observer, identityProvider)
{
    protected override AppDbContext Create(DbContextOptions options, IDbContextObserver observer,
        IIdentityProvider<Guid> identityProvider)
        => new(options, observer, identityProvider);
}
```

### 3. Use it

```csharp
await using var context = factory.Create();
context.Customers.Add(new Customer { Id = Guid.NewGuid(), Name = "Acme" });
await context.SaveChangesAsync(); // CreatedBy/ModifiedBy/CreatedOn set by Bounteous.Data
```

## What the factory configures

```csharp
new DbContextOptionsBuilder<DbContextBase<TUserId>>()
    .UseSqlite(ConnectionBuilder.AdminConnectionString)
    .EnableSensitiveDataLogging(sensitiveDataLoggingEnabled)
    .EnableDetailedErrors()
    .Options;
```

There is no `EnableRetryOnFailure()`, unlike the SQL Server, PostgreSQL, and MySQL wrappers. SQLite runs
in-process, so there are no transient network faults to retry, and the EF Core SQLite provider has no
retrying execution strategy.

## Connection strings

| Scenario | Connection string |
|---|---|
| File database | `Data Source=app.db` |
| Read-only file | `Data Source=app.db;Mode=ReadOnly` |
| Private in-memory | `Data Source=:memory:` |
| Shared in-memory | `Data Source=mydb;Mode=Memory;Cache=Shared` |

### In-memory databases

An in-memory SQLite database lives only while a connection to it is open. Each `factory.Create()` opens
its own connection, so with `Data Source=:memory:` every context sees a new, empty database.

To share one in-memory database across contexts (typical for tests), use a named shared-cache database
and hold one connection open for its lifetime:

```csharp
const string cs = "Data Source=tests;Mode=Memory;Cache=Shared";
using var keepAlive = new SqliteConnection(cs);
keepAlive.Open();
// contexts created by a factory returning `cs` now share the same data
```

## SQLite limits to know

- No `decimal` ordering or aggregation on the server side; EF Core stores `decimal` as TEXT.
  Use `double` or convert on the client if you need `ORDER BY` / `SUM`.
- `DateTimeOffset` is stored as TEXT; ordering and comparison are limited.
- Schema changes are restricted (no `ALTER COLUMN`); EF Core migrations rebuild tables where needed.
- One writer at a time. Fine for tests, local tools, and edge/desktop apps; not a substitute for a
  server database under concurrent write load.

## Dependencies

- `Bounteous.Data` 0.0.31
- `Microsoft.EntityFrameworkCore.Sqlite` 10.0.9
- `SQLitePCLRaw.bundle_e_sqlite3` 2.1.14 (pinned above the transitive 2.1.11, which has advisory
  GHSA-2m69-gcr7-jv3q)

## Build and test

```bash
dotnet build -c Release
dotnet test -c Release
```

Tests run against real SQLite in memory. No Docker or external database needed.

## License

MIT. See [LICENSE](LICENSE).
