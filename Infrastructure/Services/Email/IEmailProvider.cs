using Infrastructure.Services.Email.Models;

namespace Infrastructure.Services.Email;

/// <summary>
/// A transport that delivers an <see cref="EmailMessage"/>. Multiple providers
/// are registered; <c>EmailService</c> picks one by <see cref="ProviderType"/>
/// based on the "Email:Provider" configuration value.
/// </summary>
public interface IEmailProvider
{
    EmailProviderType ProviderType { get; }

    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);

    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
}
