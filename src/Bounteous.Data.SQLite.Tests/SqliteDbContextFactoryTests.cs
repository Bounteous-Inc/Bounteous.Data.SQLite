using Bounteous.Data.Domain.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;

namespace Bounteous.Data.SQLite.Tests;

public sealed class SqliteDbContextFactoryTests : IDisposable
{
    public class TestEntity : IAuditable, ISoftDelete
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedOn { get; set; }
        public Guid CreatedBy { get; set; }
        public DateTime ModifiedOn { get; set; }
        public DateTime SynchronizedOn { get; set; }
        public Guid ModifiedBy { get; set; }
        public int Version { get; set; }
        public bool IsDeleted { get; set; }
    }

    public class TestDbContext(
        DbContextOptions options,
        IDbContextObserver observer,
        IIdentityProvider<Guid> identityProvider)
        : DbContextBase<Guid>(options, observer, identityProvider)
    {
        public DbSet<TestEntity> Entities => Set<TestEntity>();

        protected override void RegisterModels(ModelBuilder modelBuilder)
            => modelBuilder.Entity<TestEntity>().HasKey(e => e.Id);
    }

    private class TestSqliteDbContextFactory(
        IConnectionBuilder connectionBuilder,
        IDbContextObserver observer,
        IIdentityProvider<Guid> identityProvider)
        : SqliteDbContextFactory<TestDbContext, Guid>(connectionBuilder, observer, identityProvider)
    {
        public DbContextOptions ExposeApplyOptions(bool sensitiveDataLoggingEnabled = false)
            => ApplyOptions(sensitiveDataLoggingEnabled);

        protected override TestDbContext Create(DbContextOptions options, IDbContextObserver observer,
            IIdentityProvider<Guid> identityProvider)
            => new(options, observer, identityProvider);
    }

    // A named, shared-cache in-memory database lives as long as at least one connection
    // to it is open. The keep-alive connection lets separate contexts see the same data.
    private readonly string connectionString =
        $"Data Source=bounteous-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

    private readonly SqliteConnection keepAlive;
    private readonly Mock<IConnectionBuilder> connectionBuilder = new();
    private readonly Mock<IDbContextObserver> observer = new();
    private readonly Mock<IIdentityProvider<Guid>> identityProvider = new();
    private readonly TestSqliteDbContextFactory factory;

    public SqliteDbContextFactoryTests()
    {
        keepAlive = new SqliteConnection(connectionString);
        keepAlive.Open();
        connectionBuilder.Setup(x => x.AdminConnectionString).Returns(connectionString);
        factory = new TestSqliteDbContextFactory(connectionBuilder.Object, observer.Object, identityProvider.Object);
    }

    public void Dispose() => keepAlive.Dispose();

    [Fact]
    public void FactoryIsADbContextFactory()
        => Assert.IsAssignableFrom<DbContextFactory<TestDbContext, Guid>>(factory);

    [Fact]
    public void ApplyOptionsReturnsTypedOptions()
        => Assert.IsType<DbContextOptions<DbContextBase<Guid>>>(factory.ExposeApplyOptions());

    [Fact]
    public void ApplyOptionsReturnsNewInstanceEachCall()
        => Assert.NotSame(factory.ExposeApplyOptions(), factory.ExposeApplyOptions());

    [Fact]
    public void CreateUsesSqliteProvider()
    {
        using var context = factory.Create();

        Assert.True(context.Database.IsSqlite());
        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", context.Database.ProviderName);
    }

    [Fact]
    public void CreateUsesConnectionStringFromConnectionBuilder()
    {
        using var context = factory.Create();

        Assert.Equal(connectionString, context.Database.GetConnectionString());
        connectionBuilder.Verify(x => x.AdminConnectionString, Times.AtLeastOnce);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ApplyOptionsHonoursSensitiveDataLoggingFlag(bool enabled)
    {
        var options = factory.ExposeApplyOptions(enabled);

        var core = options.FindExtension<CoreOptionsExtension>();
        Assert.NotNull(core);
        Assert.Equal(enabled, core.IsSensitiveDataLoggingEnabled);
        Assert.True(core.DetailedErrorsEnabled);
    }

    [Fact]
    public async Task SaveChangesPersistsAndAuditsAgainstRealSqlite()
    {
        var userId = Guid.NewGuid();
        identityProvider.Setup(x => x.GetCurrentUserId()).Returns(userId);
        var id = Guid.NewGuid();

        await using (var context = factory.Create())
        {
            await context.Database.EnsureCreatedAsync();
            context.Entities.Add(new TestEntity { Id = id, Name = "widget" });
            await context.SaveChangesAsync();
        }

        await using (var context = factory.Create())
        {
            var saved = await context.Entities.SingleAsync(e => e.Id == id);
            Assert.Equal("widget", saved.Name);
            Assert.Equal(userId, saved.CreatedBy);
            Assert.Equal(userId, saved.ModifiedBy);
            Assert.NotEqual(default, saved.CreatedOn);
        }

        observer.Verify(o => o.OnSaved(), Times.Once);
    }
}
