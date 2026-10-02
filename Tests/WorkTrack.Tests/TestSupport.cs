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
    public static AppDbContext Create() => Create(Guid.NewGuid().ToString());

    /// <summary>
    /// A context over a named in-memory database, so a test can open a second
    /// context over the same data — the way a restarted API sees the rows the
    /// previous process wrote.
    /// </summary>
    public static AppDbContext Create(string databaseName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName)
            // The in-memory provider can't honour the [Timestamp] RowVersion concurrency
            // token; silence that transaction/warning so seeding stays quiet.
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new AppDbContext(options);
    }
}

/// <summary>One message a <see cref="FakeEmailService"/> was asked to send.</summary>
internal sealed record SentEmail(
    string Recipient,
    string Subject,
    string HtmlBody,
    string? TextBody,
    IReadOnlyList<EmailFileAttachment> Attachments)
{
    public SentEmail(string Recipient, string Subject, string HtmlBody, string? TextBody)
        : this(Recipient, Subject, HtmlBody, TextBody, []) { }
}

/// <summary>
/// No-op email sender — reports success without sending anything, and keeps every
/// message so tests can assert on what each recipient would have received.
///
/// The Last* properties are one message's worth of that: enough while every
/// handler sent a single email, useless for a fan-out such as the coverage
/// announcement, which is why <see cref="Sent"/> sits alongside them.
/// </summary>
internal sealed class FakeEmailService : IEmailService
{
    public int SentCount { get; private set; }

    /// <summary>Every message sent, in the order it was sent.</summary>
    public List<SentEmail> Sent { get; } = [];

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
        CancellationToken cancellationToken = default) =>
        SendEmailAsync(toEmail, subject, htmlBody, textBody, [], cancellationToken);

    public Task<bool> SendEmailAsync(
        string toEmail,
        string subject,
        string htmlBody,
        string? textBody,
        IReadOnlyList<EmailFileAttachment> attachments,
        CancellationToken cancellationToken = default)
    {
        SentCount++;
        Sent.Add(new SentEmail(toEmail, subject, htmlBody, textBody, attachments));
        LastRecipient = toEmail;
        LastSubject = subject;
        LastHtmlBody = htmlBody;
        LastTextBody = textBody;
        return Task.FromResult(SendResult);
    }
}
