using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Persistence;

namespace WorkTrack.Tests;

/// <summary>
/// An <see cref="AppDbContext"/> over SQLite in memory, for tests that need real
/// transaction semantics.
///
/// <see cref="TestDb"/> cannot serve these: the EF in-memory provider ignores
/// BeginTransaction outright (which is why TestDb has to silence the warning), so
/// a rollback assertion written against it passes whether or not the code under
/// test opens a transaction at all. SQLite commits and rolls back for real.
///
/// Two consequences worth knowing before writing a test against this:
/// SQLite enforces foreign keys, so every parent row has to be seeded; and rows
/// live only as long as the connection, which the returned context owns.
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

        var context = new SqliteAppDbContext(options, connection);
        await context.Database.EnsureCreatedAsync();
        return context;
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
    private sealed class SqliteAppDbContext(DbContextOptions<AppDbContext> options, SqliteConnection connection)
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
        // context has to close it — EF will not, having been handed it already open.
        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await connection.DisposeAsync();
        }

        public override void Dispose()
        {
            base.Dispose();
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
