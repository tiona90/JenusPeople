using System.Text.RegularExpressions;
using Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Resend;

namespace Infrastructure.Services;

public class EmailService(
    IConfiguration configuration,
    IResend resend,
    ILogger<EmailService> logger) : IEmailService
{
    public async Task<bool> SendEmailAsync(
        string toEmail,
        string subject,
        string htmlBody,
        string? textBody = null,
        CancellationToken cancellationToken = default)
    {
        var fromEmail = configuration["Resend:FromEmail"];
        var fromName = configuration["Resend:FromName"];

        if (string.IsNullOrWhiteSpace(toEmail) || string.IsNullOrWhiteSpace(fromEmail))
        {
            logger.LogWarning(
                "Resend email skipped because configuration is incomplete. HasFromAddress: {HasFromAddress}, HasRecipient: {HasRecipient}",
                !string.IsNullOrWhiteSpace(fromEmail),
                !string.IsNullOrWhiteSpace(toEmail));
            return false;
        }

        var emailMessage = new EmailMessage
        {
            From = string.IsNullOrWhiteSpace(fromName)
                ? fromEmail
                : $"{fromName} <{fromEmail}>",
            Subject = subject,
            HtmlBody = htmlBody,
            TextBody = string.IsNullOrWhiteSpace(textBody)
                ? StripHtml(htmlBody)
                : textBody
        };

        emailMessage.To.Add(toEmail);

        try
        {
            await resend.EmailSendAsync(emailMessage);
            logger.LogInformation("Resend email sent to {Recipient} with subject {Subject}.", toEmail, subject);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send Resend email to {Recipient}.", toEmail);
            return false;
        }
    }

    private static string StripHtml(string htmlBody)
    {
        var withoutTags = Regex.Replace(htmlBody, "<.*?>", string.Empty);
        return System.Net.WebUtility.HtmlDecode(withoutTags);
    }
}
