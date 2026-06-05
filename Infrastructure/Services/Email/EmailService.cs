using System.Text.RegularExpressions;
using Domain.Interfaces;
using Infrastructure.Configuration;
using Infrastructure.Services.Email.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services.Email;

/// <summary>
/// Provider-agnostic email service. The concrete transport (SMTP or Brevo) is
/// chosen at startup from the registered <see cref="IEmailProvider"/>s based on
/// the "Email:Provider" configuration value. Handlers build their own HTML
/// bodies and call <see cref="SendEmailAsync"/>; this class wraps that into a
/// transport-neutral <see cref="EmailMessage"/> and delegates to the provider.
/// Failures are logged and surfaced as <c>false</c> — never thrown — so a slow
/// or unavailable mail provider never breaks a user request.
/// </summary>
public class EmailService : IEmailService
{
    private readonly IEmailProvider _provider;
    private readonly MailSettings _mailSettings;
    private readonly EmailProviderType _providerType;
    private readonly ILogger<EmailService> _logger;

    public EmailService(
        IEnumerable<IEmailProvider> providers,
        IOptions<EmailOptions> emailOptions,
        IOptions<MailSettings> mailSettings,
        ILogger<EmailService> logger)
    {
        _logger = logger;
        _mailSettings = mailSettings.Value;
        _providerType = emailOptions.Value.Provider;

        _provider = providers.FirstOrDefault(p => p.ProviderType == _providerType)
            ?? throw new InvalidOperationException($"No email provider registered for type: {_providerType}");

        _logger.LogInformation("EmailService initialized with provider: {Provider}", _providerType);
    }

    public async Task<bool> SendEmailAsync(
        string toEmail,
        string subject,
        string htmlBody,
        string? textBody = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(toEmail) || string.IsNullOrWhiteSpace(_mailSettings.EffectiveFromAddress))
        {
            _logger.LogWarning(
                "Email skipped because configuration is incomplete. HasFrom: {HasFrom}, HasRecipient: {HasRecipient}",
                !string.IsNullOrWhiteSpace(_mailSettings.EffectiveFromAddress),
                !string.IsNullOrWhiteSpace(toEmail));
            return false;
        }

        var message = new EmailMessage
        {
            From = new EmailContact
            {
                Name = _mailSettings.DisplayName,
                Email = _mailSettings.EffectiveFromAddress
            },
            To = [new EmailContact { Email = toEmail }],
            Subject = subject,
            HtmlContent = htmlBody,
            TextContent = string.IsNullOrWhiteSpace(textBody) ? StripHtml(htmlBody) : textBody
        };

        var result = await _provider.SendAsync(message, cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Email sent to {Recipient} via {Provider} (subject: {Subject}).",
                toEmail, _providerType, subject);
        }
        else
        {
            _logger.LogError("Failed to send email to {Recipient} via {Provider}. Status: {Status}. Error: {Error}",
                toEmail, _providerType, result.StatusCode, result.ErrorMessage);
        }

        return result.IsSuccess;
    }

    private static string StripHtml(string htmlBody)
    {
        var withoutTags = Regex.Replace(htmlBody, "<.*?>", string.Empty);
        return System.Net.WebUtility.HtmlDecode(withoutTags);
    }
}
