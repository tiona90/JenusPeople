using System.Net;
using System.Net.Mail;
using System.Text.RegularExpressions;
using Domain.Interfaces;
using Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public class EmailService(
    IOptions<MailSettings> mailSettings,
    ILogger<EmailService> logger) : IEmailService
{
    private readonly MailSettings _settings = mailSettings.Value;

    public async Task<bool> SendEmailAsync(
        string toEmail,
        string subject,
        string htmlBody,
        string? textBody = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(toEmail)
            || string.IsNullOrWhiteSpace(_settings.Mail)
            || string.IsNullOrWhiteSpace(_settings.Host)
            || string.IsNullOrWhiteSpace(_settings.Password))
        {
            logger.LogWarning(
                "SMTP email skipped because configuration is incomplete. HasSender: {HasSender}, HasRecipient: {HasRecipient}, HasHost: {HasHost}, HasPassword: {HasPassword}",
                !string.IsNullOrWhiteSpace(_settings.Mail),
                !string.IsNullOrWhiteSpace(toEmail),
                !string.IsNullOrWhiteSpace(_settings.Host),
                !string.IsNullOrWhiteSpace(_settings.Password));
            return false;
        }

        var plainText = string.IsNullOrWhiteSpace(textBody) ? StripHtml(htmlBody) : textBody;

        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(_settings.Mail, _settings.DisplayName),
                Subject = subject,
            };
            message.To.Add(new MailAddress(toEmail));
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(plainText, null, "text/plain"));
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(htmlBody, null, "text/html"));

            using var smtp = new SmtpClient(_settings.Host, _settings.Port)
            {
                Credentials = new NetworkCredential(_settings.Mail, _settings.Password),
                EnableSsl = true,
            };

            await smtp.SendMailAsync(message, cancellationToken);
            logger.LogInformation("SMTP email sent to {Recipient} with subject {Subject}.", toEmail, subject);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send SMTP email to {Recipient}.", toEmail);
            return false;
        }
    }

    private static string StripHtml(string htmlBody)
    {
        var withoutTags = Regex.Replace(htmlBody, "<.*?>", string.Empty);
        return System.Net.WebUtility.HtmlDecode(withoutTags);
    }
}
