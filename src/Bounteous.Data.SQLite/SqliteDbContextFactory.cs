using Microsoft.EntityFrameworkCore;

namespace Bounteous.Data.SQLite;

/// <summary>
/// SQLite flavour of <see cref="DbContextFactory{T,TUserId}"/>.
/// Unlike the server-based providers, SQLite has no retrying execution strategy:
/// it is an in-process engine, so there are no transient network faults to retry.
/// </summary>
public abstract class SqliteDbContextFactory<T, TUserId>(
    IConnectionBuilder connectionBuilder,
    IDbContextObserver observer,
    IIdentityProvider<TUserId> identityProvider)
    : DbContextFactory<T, TUserId>(connectionBuilder, observer, identityProvider)
    where T : IDbContext<TUserId>
    where TUserId : struct
{
    protected override DbContextOptions ApplyOptions(bool sensitiveDataLoggingEnabled = false)
        => new DbContextOptionsBuilder<DbContextBase<TUserId>>()
            .UseSqlite(ConnectionBuilder.AdminConnectionString)
            .EnableSensitiveDataLogging(sensitiveDataLoggingEnabled: sensitiveDataLoggingEnabled)
            .EnableDetailedErrors()
            .Options;
}
