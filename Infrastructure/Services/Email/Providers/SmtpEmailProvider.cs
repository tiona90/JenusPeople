using Infrastructure.Configuration;
using Infrastructure.Services.Email.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Infrastructure.Services.Email.Providers;

/// <summary>
/// Sends mail over SMTP via MailKit. Works with Gmail, Office 365, the Brevo
/// SMTP relay, or any SMTP server. Unlike the Brevo HTTP API, SMTP is not
/// subject to Brevo's "Authorised IPs" API restriction, so this is the
/// reliable choice on a host with a dynamic/rotating outbound IP.
/// </summary>
public class SmtpEmailProvider : IEmailProvider
{
    private readonly ILogger<SmtpEmailProvider> _logger;
    private readonly MailSettings _mailSettings;

    public SmtpEmailProvider(
        IOptions<MailSettings> mailSettings,
        ILogger<SmtpEmailProvider> logger)
    {
        _logger = logger;
        _mailSettings = mailSettings.Value;
    }

    public EmailProviderType ProviderType => EmailProviderType.Smtp;

    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Sending email via SMTP ({Host}:{Port}) to: {Recipients}",
                _mailSettings.Host, _mailSettings.Port,
                string.Join(", ", message.To.Select(t => t.Email)));

            var mimeMessage = BuildMimeMessage(message);

            using var client = new SmtpClient();
            await client.ConnectAsync(_mailSettings.Host, _mailSettings.Port, GetSecureSocketOptions(), cancellationToken);

            if (!string.IsNullOrEmpty(_mailSettings.Mail) && !string.IsNullOrEmpty(_mailSettings.Password))
            {
                await client.AuthenticateAsync(_mailSettings.Mail, _mailSettings.Password, cancellationToken);
            }

            var response = await client.SendAsync(mimeMessage, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            _logger.LogInformation("SMTP email sent successfully. Response: {Response}", response);
            return EmailSendResult.Success(mimeMessage.MessageId);
        }
        catch (AuthenticationException ex)
        {
            _logger.LogError(ex, "SMTP authentication failed.");
            return EmailSendResult.Failure($"Authentication failed: {ex.Message}", 401);
        }
        catch (SmtpCommandException ex)
        {
            _logger.LogError(ex, "SMTP command error: {StatusCode}", ex.StatusCode);
            return EmailSendResult.Failure($"SMTP error: {ex.Message}", (int)ex.StatusCode);
        }
        catch (SmtpProtocolException ex)
        {
            _logger.LogError(ex, "SMTP protocol error.");
            return EmailSendResult.Failure($"SMTP protocol error: {ex.Message}", 500);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("SMTP email send was cancelled.");
            return EmailSendResult.Failure("Request was cancelled.", 499);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while sending email via SMTP.");
            return EmailSendResult.Failure(ex.Message, 500);
        }
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(_mailSettings.Host, _mailSettings.Port, GetSecureSocketOptions(), cancellationToken);

            if (!string.IsNullOrEmpty(_mailSettings.Mail) && !string.IsNullOrEmpty(_mailSettings.Password))
            {
                await client.AuthenticateAsync(_mailSettings.Mail, _mailSettings.Password, cancellationToken);
            }

            await client.DisconnectAsync(true, cancellationToken);
            _logger.LogInformation("SMTP connection test successful.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMTP connection test failed.");
            return false;
        }
    }

    // Port 465 uses implicit SSL; everything else negotiates STARTTLS when available.
    private SecureSocketOptions GetSecureSocketOptions() =>
        _mailSettings.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;

    private static MimeMessage BuildMimeMessage(EmailMessage message)
    {
        var mimeMessage = new MimeMessage();
        mimeMessage.From.Add(new MailboxAddress(message.From.Name ?? string.Empty, message.From.Email));

        foreach (var to in message.To)
        {
            mimeMessage.To.Add(new MailboxAddress(to.Name ?? string.Empty, to.Email));
        }

        if (message.Cc != null)
        {
            foreach (var cc in message.Cc)
            {
                mimeMessage.Cc.Add(new MailboxAddress(cc.Name ?? string.Empty, cc.Email));
            }
        }

        if (message.Bcc != null)
        {
            foreach (var bcc in message.Bcc)
            {
                mimeMessage.Bcc.Add(new MailboxAddress(bcc.Name ?? string.Empty, bcc.Email));
            }
        }

        mimeMessage.Subject = message.Subject;

        var bodyBuilder = new BodyBuilder();
        if (!string.IsNullOrEmpty(message.HtmlContent))
        {
            bodyBuilder.HtmlBody = message.HtmlContent;
        }

        if (!string.IsNullOrEmpty(message.TextContent))
        {
            bodyBuilder.TextBody = message.TextContent;
        }
        else if (!string.IsNullOrEmpty(message.HtmlContent))
        {
            bodyBuilder.TextBody = HtmlToPlainText(message.HtmlContent);
        }

        if (message.Attachments != null)
        {
            foreach (var attachment in message.Attachments)
            {
                bodyBuilder.Attachments.Add(attachment.Name, attachment.Content, ContentType.Parse(attachment.ContentType));
            }
        }

        mimeMessage.Body = bodyBuilder.ToMessageBody();
        return mimeMessage;
    }

    private static string HtmlToPlainText(string html)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", string.Empty);
        text = System.Net.WebUtility.HtmlDecode(text);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        return text;
    }
}
