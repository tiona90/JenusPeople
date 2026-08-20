using System.Net;
using System.Net.Sockets;
using System.Text;
using Infrastructure.Configuration;
using Infrastructure.Services.Email.Models;
using Infrastructure.Services.Email.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Both send paths, exercised for real: the SMTP provider against a socket that
/// speaks SMTP back, and the Brevo provider against a handler that answers like
/// the API does.
///
/// They exist because MailKit and MimeKit were upgraded (4.13.0 → 4.17.0, two
/// moderate advisories) and "the solution still compiles" says nothing about
/// whether a message still leaves the process. MimeKit builds the MIME body and
/// MailKit runs the SMTP conversation, so a break there is a break in every
/// notification this application sends, visible only in production. The Brevo path
/// touches neither library — it is HttpClient and JSON — and this pins that, so the
/// next upgrade does not have to re-establish it.
/// </summary>
public class EmailProviderSendTests
{
    private static EmailMessage Message() => new()
    {
        From = new EmailContact { Name = "Jenus People", Email = "noreply@test.local" },
        To = [new EmailContact { Name = "Manager", Email = "manager@test.local" }],
        Subject = "New leave request",
        HtmlContent = "<p>Hello Manager,</p><p>A request is waiting.</p>",
        TextContent = "Hello Manager,\nA request is waiting.",
    };

    /* ── SMTP, over a real socket ───────────────────────────────────────────── */

    [Fact]
    public async Task The_smtp_provider_delivers_a_message_a_server_can_read()
    {
        using var server = new FakeSmtpServer();

        var provider = new SmtpEmailProvider(
            Options.Create(new MailSettings
            {
                Host = "127.0.0.1",
                Port = server.Port,
                FromAddress = "noreply@test.local",
                DisplayName = "Jenus People",
                // Left blank on purpose: the provider only authenticates when it has
                // both a login and a password, and this server offers no AUTH.
                Mail = string.Empty,
                Password = string.Empty,
            }),
            NullLogger<SmtpEmailProvider>.Instance);

        var result = await provider.SendAsync(Message(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.False(string.IsNullOrWhiteSpace(result.MessageId));

        var received = await server.WaitForMessageAsync();

        // The envelope: whom the server was told to deliver to. StartsWith, because
        // MAIL FROM carries a SIZE parameter when the server advertises SIZE.
        Assert.Contains(
            server.Commands,
            command => command.StartsWith("MAIL FROM:<noreply@test.local>", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            server.Commands,
            command => command.StartsWith("RCPT TO:<manager@test.local>", StringComparison.OrdinalIgnoreCase));

        // The message: MimeKit's BodyBuilder still produces both alternatives, and
        // the headers still carry the display names.
        Assert.Contains("Subject: New leave request", received, StringComparison.Ordinal);
        Assert.Contains("Jenus People", received, StringComparison.Ordinal);
        Assert.Contains("multipart/alternative", received, StringComparison.Ordinal);
        Assert.Contains("A request is waiting.", received, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refused_smtp_connection_is_reported_as_a_failed_send()
    {
        var provider = new SmtpEmailProvider(
            Options.Create(new MailSettings { Host = "127.0.0.1", Port = 1 }),
            NullLogger<SmtpEmailProvider>.Instance);

        var result = await provider.SendAsync(Message(), CancellationToken.None);

        // The provider catches transport failures and reports them rather than
        // throwing into whichever handler happened to be sending a notification.
        Assert.False(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    /* ── Brevo, over a stubbed transport ────────────────────────────────────── */

    [Fact]
    public async Task The_brevo_provider_posts_the_message_to_the_transactional_api()
    {
        var transport = new RecordingHandler(
            HttpStatusCode.Created,
            """{"messageId":"<202608201200.1234@brevo>"}""");

        var provider = new BrevoEmailProvider(
            new HttpClient(transport),
            Options.Create(new BrevoOptions
            {
                BaseUrl = "https://api.brevo.com/v3",
                ApiKey = "test-key",
            }),
            NullLogger<BrevoEmailProvider>.Instance);

        var result = await provider.SendAsync(Message(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("<202608201200.1234@brevo>", result.MessageId);

        Assert.Equal("https://api.brevo.com/v3/smtp/email", transport.RequestUri?.ToString());
        Assert.Equal("test-key", transport.ApiKey);
        Assert.Contains("\"email\":\"manager@test.local\"", transport.Body, StringComparison.Ordinal);
        Assert.Contains("\"subject\":\"New leave request\"", transport.Body, StringComparison.Ordinal);
        Assert.Contains("htmlContent", transport.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_brevo_rejection_is_reported_as_a_failed_send()
    {
        var transport = new RecordingHandler(
            HttpStatusCode.Unauthorized,
            """{"code":"unauthorized","message":"unrecognised IP address"}""");

        var provider = new BrevoEmailProvider(
            new HttpClient(transport),
            Options.Create(new BrevoOptions { BaseUrl = "https://api.brevo.com/v3", ApiKey = "test-key" }),
            NullLogger<BrevoEmailProvider>.Instance);

        var result = await provider.SendAsync(Message(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(401, result.StatusCode);
    }

    /// <summary>Answers one canned response and keeps what it was asked to send.</summary>
    private sealed class RecordingHandler(HttpStatusCode status, string responseBody) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string Body { get; private set; } = string.Empty;
        public string? ApiKey { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            ApiKey = request.Headers.TryGetValues("api-key", out var values)
                ? string.Join(",", values)
                : null;
            Body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}

/// <summary>
/// The smallest SMTP server that MailKit will talk to: a greeting, an EHLO reply
/// with no extensions (so no STARTTLS and no AUTH are negotiated over loopback),
/// and enough of MAIL/RCPT/DATA to accept one message. It records the commands it
/// was sent and the message body it received.
/// </summary>
internal sealed class FakeSmtpServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stopping = new();
    private readonly TaskCompletionSource<string> _message =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _session;
    private readonly List<string> _commands = [];

    public FakeSmtpServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _session = Task.Run(RunAsync);
    }

    public int Port { get; }

    /// <summary>Commands the client sent, in order, one per line as received.</summary>
    public IReadOnlyList<string> Commands
    {
        get { lock (_commands) { return [.. _commands]; } }
    }

    /// <summary>The message body between DATA and the terminating dot.</summary>
    public async Task<string> WaitForMessageAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return await _message.Task.WaitAsync(timeout.Token);
    }

    private async Task RunAsync()
    {
        try
        {
            using var client = await _listener.AcceptTcpClientAsync(_stopping.Token);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            await using var writer = new StreamWriter(stream, Encoding.ASCII)
            {
                NewLine = "\r\n",
                AutoFlush = true,
            };

            await writer.WriteLineAsync("220 localhost ESMTP fake");

            while (await reader.ReadLineAsync(_stopping.Token) is { } line)
            {
                lock (_commands) { _commands.Add(line); }

                if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("HELO", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("250-localhost");
                    await writer.WriteLineAsync("250 SIZE 10240000");
                }
                else if (line.StartsWith("DATA", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");

                    var body = new StringBuilder();
                    while (await reader.ReadLineAsync(_stopping.Token) is { } dataLine
                        && dataLine != ".")
                    {
                        body.AppendLine(dataLine);
                    }

                    _message.TrySetResult(body.ToString());
                    await writer.WriteLineAsync("250 2.0.0 Ok: queued as FAKE1");
                }
                else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("221 2.0.0 Bye");
                    break;
                }
                else
                {
                    await writer.WriteLineAsync("250 2.0.0 Ok");
                }
            }
        }
        catch (Exception ex)
        {
            // Nothing else can observe this thread, so hand the failure to whoever
            // is waiting for the message rather than losing it.
            _message.TrySetException(ex);
        }
    }

    public void Dispose()
    {
        _stopping.Cancel();
        _listener.Stop();

        try
        {
            _session.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Cancellation on the way out.
        }

        _stopping.Dispose();
    }
}
