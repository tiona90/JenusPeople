using Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Persistence;

namespace WorkTrack.Tests;

/// <summary>
/// Builds an isolated EF Core in-memory <see cref="AppDbContext"/> per test
/// (unique database name) so seeded data never leaks across tests.
/// </summary>
internal static class TestDb
{
    public static AppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            // The in-memory provider can't honour the [Timestamp] RowVersion concurrency
            // token; silence that transaction/warning so seeding stays quiet.
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new AppDbContext(options);
    }
}

/// <summary>No-op email sender — reports success without sending anything.</summary>
internal sealed class FakeEmailService : IEmailService
{
    public int SentCount { get; private set; }

    public Task<bool> SendEmailAsync(
        string toEmail,
        string subject,
        string htmlBody,
        string? textBody = null,
        CancellationToken cancellationToken = default)
    {
        SentCount++;
        return Task.FromResult(true);
    }
}

/// <summary>No-op chat notifier.</summary>
internal sealed class FakeChatNotificationService : IChatNotificationService
{
    public Task SendMessageAsync(string text, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
