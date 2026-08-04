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

/// <summary>
/// No-op email sender — reports success without sending anything, and keeps the
/// last message so tests can assert on what a recipient would have received.
/// </summary>
internal sealed class FakeEmailService : IEmailService
{
    public int SentCount { get; private set; }

    public string? LastRecipient { get; private set; }
    public string? LastSubject { get; private set; }
    public string? LastHtmlBody { get; private set; }
    public string? LastTextBody { get; private set; }

    /// <summary>Set to false to simulate a provider rejecting the send.</summary>
    public bool SendResult { get; set; } = true;

    public Task<bool> SendEmailAsync(
        string toEmail,
        string subject,
        string htmlBody,
        string? textBody = null,
        CancellationToken cancellationToken = default)
    {
        SentCount++;
        LastRecipient = toEmail;
        LastSubject = subject;
        LastHtmlBody = htmlBody;
        LastTextBody = textBody;
        return Task.FromResult(SendResult);
    }
}

/// <summary>No-op chat notifier.</summary>
internal sealed class FakeChatNotificationService : IChatNotificationService
{
    public Task SendMessageAsync(string text, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
