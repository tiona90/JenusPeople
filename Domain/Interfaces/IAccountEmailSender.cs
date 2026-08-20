namespace Domain.Interfaces;

/// <summary>
/// Builds and sends the account-lifecycle emails — welcome invite, password
/// reset, email-change confirmation — along with the client links inside them.
/// </summary>
/// <remarks>
/// The implementation lives in the API layer (AccountEmailSender), which owns the
/// HTML templates and the client URL configuration. The interface sits here beside
/// <see cref="IEmailService"/> so Application handlers can send an invite without
/// the Application layer depending on API — creating a user is a command, and its
/// welcome email is part of creating it.
/// </remarks>
public interface IAccountEmailSender
{
    /// <summary>
    /// Builds a link into the SPA, e.g. <c>{base}/reset-password?email=…&amp;token=…</c>.
    /// </summary>
    string BuildClientUrl(string route, IDictionary<string, string?>? query = null);

    /// <summary>
    /// Invites a newly created account to choose its first password. Returns
    /// false if the mail provider rejected the send.
    /// </summary>
    Task<bool> SendWelcomeInviteAsync(User user, CancellationToken cancellationToken = default);

    Task<bool> SendPasswordResetAsync(User user, CancellationToken cancellationToken = default);

    /// <param name="apiBaseUrlFallback">
    /// Used when <c>AppUrls:ApiBaseUrl</c> is not configured — callers handling a
    /// request pass the current scheme + host.
    /// </param>
    Task<bool> SendEmailChangeConfirmationAsync(
        User user,
        string newEmail,
        string apiBaseUrlFallback,
        CancellationToken cancellationToken = default);
}
