namespace Domain.Interfaces;

public interface IEmailService
{
    Task<bool> SendEmailAsync(
        string toEmail,
        string subject,
        string htmlBody,
        string? textBody = null,
        CancellationToken cancellationToken = default);
}
