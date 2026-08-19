using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Persistence;

namespace WorkTrack.Tests;

/// <summary>
/// An <see cref="AppDbContext"/> over SQLite in memory, for tests that need a
/// database that behaves like one: real transactions, enforced unique indexes,
/// enforced foreign keys.
///
/// <see cref="TestDb"/> cannot serve those: the EF in-memory provider ignores
/// BeginTransaction outright (which is why TestDb has to silence the warning) and
/// enforces no constraints at all, so an assertion about either passes whether or
/// not the code under test does anything.
///
/// Two consequences worth knowing before writing a test against this: every parent
/// row has to be seeded, and rows live only as long as the connection, which the
/// context returned by <see cref="CreateAsync"/> owns.
/// </summary>
internal static class TransactionalTestDb
{
    public static async Task<AppDbContext> CreateAsync(params IInterceptor[] interceptors)
    {
        var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptors)
            .Options;

        var context = new SqliteAppDbContext(options, connection, ownsConnection: true);
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    /// <summary>
    /// A second context over the same database, which is what a second concurrent
    /// request gets: its own change tracker, the same store. Neither can see what
    /// the other has not yet saved.
    ///
    /// It shares the connection deliberately. Two SQLite connections writing at
    /// once collide on database locking, which would make a race test fail for
    /// reasons that have nothing to do with the code; sharing serialises the writes
    /// so the test turns on the constraint instead of on timing.
    /// </summary>
    public static AppDbContext Attach(AppDbContext existing)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite((SqliteConnection)existing.Database.GetDbConnection())
            .Options;

        return new SqliteAppDbContext(
            options,
            (SqliteConnection)existing.Database.GetDbConnection(),
            ownsConnection: false);
    }

    /// <summary>
    /// The production model targets SQL Server in two places SQLite cannot honour,
    /// both incidental to what these tests exercise:
    /// <c>SYSUTCDATETIME()</c> column defaults, and the <c>[Timestamp]</c>
    /// rowversion columns, which only SQL Server can generate. Both are neutralised
    /// here rather than in the real model.
    ///
    /// Dropping the rowversion means this context cannot be used to test optimistic
    /// concurrency — use <see cref="TestDb"/> for that.
    /// </summary>
    private sealed class SqliteAppDbContext(
        DbContextOptions<AppDbContext> options,
        SqliteConnection connection,
        bool ownsConnection)
        : AppDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            foreach (var property in builder.Model.GetEntityTypes().SelectMany(t => t.GetProperties()))
            {
                if (property.GetDefaultValueSql() is not null)
                    property.SetDefaultValueSql(null);

                if (property.IsConcurrencyToken && property.ClrType == typeof(byte[]))
                {
                    property.IsConcurrencyToken = false;
                    property.ValueGenerated = ValueGenerated.Never;
                }
            }
        }

        // The database exists only for as long as this connection is open, so the
        // owning context has to close it — EF will not, having been handed it
        // already open. An attached context must leave it alone.
        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            if (ownsConnection)
                await connection.DisposeAsync();
        }

        public override void Dispose()
        {
            base.Dispose();
            if (ownsConnection)
                connection.Dispose();
        }
    }
}

/// <summary>
/// Fails the nth SaveChanges on a context. Lets a test stage "the second write of
/// the pair blew up" without having to corrupt data to provoke it, and without
/// depending on which particular database error would do it in production.
/// </summary>
internal sealed class FailOnNthSaveInterceptor(int failOnSave) : SaveChangesInterceptor
{
    public const string FailureMessage = "Simulated failure on the balance write.";

    private bool _armed;

    /// <summary>Saves counted since <see cref="Arm"/>.</summary>
    public int SaveCount { get; private set; }

    /// <summary>
    /// Start counting. Seeding a test world takes saves of its own, and counting
    /// those would land the failure on the handler's first write instead of its
    /// second — which still rolls back, so the test would pass without proving
    /// anything about the pair.
    /// </summary>
    public void Arm() => _armed = true;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (!_armed)
            return base.SavingChangesAsync(eventData, result, cancellationToken);

        SaveCount++;
        if (SaveCount == failOnSave)
            throw new InvalidOperationException(FailureMessage);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
