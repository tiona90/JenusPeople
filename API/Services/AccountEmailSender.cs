using Domain;
using Domain.Interfaces;
using Infrastructure.Configuration;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;

namespace API.Services;

public class AccountEmailSender(
    UserManager<User> userManager,
    IEmailService emailService,
    IOptions<AppUrlOptions> appUrlOptions,
    ILogger<AccountEmailSender> logger) : IAccountEmailSender
{
    /// <summary>
    /// Identity's default reset-token lifespan. Stated in both password emails so
    /// a recipient who opens one late knows why the link failed, and knows the
    /// self-service way out.
    /// </summary>
    private const string LinkValidityNote =
        "For security this link is valid for 24 hours. If it expires, use “Forgot password?” on the sign-in page to send yourself a new one.";

    /// <remarks>
    /// The client routes on the path (react-router), so the route must live in the
    /// path. An earlier hash form — <c>{base}/?query#route</c> — lands on
    /// <c>/</c>, which sends an unauthenticated visitor to <c>/login</c> and
    /// discards the query string, so the emailed token was thrown away.
    /// </remarks>
    public string BuildClientUrl(string route, IDictionary<string, string?>? query = null)
    {
        var clientBaseUrl = appUrlOptions.Value.ClientBaseUrl;
        if (string.IsNullOrWhiteSpace(clientBaseUrl))
        {
            clientBaseUrl = new AppUrlOptions().ClientBaseUrl;
        }

        clientBaseUrl = clientBaseUrl.TrimEnd('/');

        // Tolerate a leading '#' or '/' so existing callers stay correct.
        var path = route.TrimStart('#').Trim('/');
        var url = $"{clientBaseUrl}/{path}";

        if (query is { Count: > 0 })
        {
            url = QueryHelpers.AddQueryString(url, query);
        }

        return url;
    }

    public async Task<bool> SendWelcomeInviteAsync(User user, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(user.Email))
        {
            logger.LogWarning("Welcome invite was skipped because the user email is missing for user {UserId}.", user.Id);
            return false;
        }

        // The same token "forgot password" issues: the account has no password
        // yet, so the invite *is* a choose-a-password link.
        var setPasswordUrl = BuildClientUrl("reset-password", new Dictionary<string, string?>
        {
            ["email"] = user.Email,
            ["token"] = await GenerateEncodedResetTokenAsync(user),
            // Lets the reset screen greet a new joiner rather than talk about
            // resetting a password they have never had.
            ["welcome"] = "1"
        });

        var displayName = ResolveRecipientName(user);
        const string subject = "Your account is ready — set your password";
        const string body = "An administrator has created an account for you. Choose your password using the secure button below, then sign in with this email address.";

        var htmlBody = BuildEmailBody(
            subject,
            "Set your password",
            displayName,
            body,
            "Set your password",
            setPasswordUrl,
            LinkValidityNote);

        var textBody = $"Hello {displayName},\n\n{body}\n{setPasswordUrl}\n\n{LinkValidityNote}";

        return await emailService.SendEmailAsync(user.Email, subject, htmlBody, textBody, cancellationToken);
    }

    public async Task<bool> SendPasswordResetAsync(User user, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(user.Email))
        {
            logger.LogWarning("Password reset email was skipped because the user email is missing for user {UserId}.", user.Id);
            return false;
        }

        var resetUrl = BuildClientUrl("reset-password", new Dictionary<string, string?>
        {
            ["email"] = user.Email,
            ["token"] = await GenerateEncodedResetTokenAsync(user)
        });

        var displayName = ResolveRecipientName(user);

        var htmlBody = BuildEmailBody(
            "Reset your Annual Leave password",
            "Password reset request",
            displayName,
            "We received a request to reset your Annual Leave password. Use the secure button below to choose a new password.",
            "Reset your password",
            resetUrl,
            $"If you did not request a password reset, no further action is required and you can safely ignore this message. {LinkValidityNote}");

        var textBody = $"Hello {displayName},\n\nWe received a request to reset your Annual Leave password. Use the secure link below to choose a new password:\n{resetUrl}\n\nIf you did not request a password reset, you can safely ignore this email.";

        return await emailService.SendEmailAsync(
            user.Email,
            "Reset your Annual Leave password",
            htmlBody,
            textBody,
            cancellationToken);
    }

    public async Task<bool> SendEmailChangeConfirmationAsync(
        User user,
        string newEmail,
        string apiBaseUrlFallback,
        CancellationToken cancellationToken = default)
    {
        var token = await userManager.GenerateChangeEmailTokenAsync(user, newEmail);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

        var apiBaseUrl = appUrlOptions.Value.ApiBaseUrl;
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            apiBaseUrl = apiBaseUrlFallback;
        }
        apiBaseUrl = apiBaseUrl.TrimEnd('/');

        var confirmationUrl = $"{apiBaseUrl}/api/account/confirm-email-change"
            + $"?userId={Uri.EscapeDataString(user.Id)}"
            + $"&newEmail={Uri.EscapeDataString(newEmail)}"
            + $"&token={Uri.EscapeDataString(encodedToken)}";

        var displayName = string.IsNullOrWhiteSpace(user.DisplayName) ? newEmail : user.DisplayName;

        var htmlBody = BuildEmailBody(
            "Confirm your new Annual Leave email",
            "Confirm your new email",
            displayName,
            $"We received a request to change the email on your Annual Leave account to {newEmail}. Click the secure button below to confirm the switch.",
            "Confirm new email",
            confirmationUrl,
            "If you did not request this change, ignore this email and your current address will stay in place.");

        var textBody = $"Hello {displayName},\n\nConfirm your new Annual Leave email address ({newEmail}) using the link below:\n{confirmationUrl}\n\nIf you did not request this change, ignore this email and your current address will stay in place.";

        return await emailService.SendEmailAsync(
            newEmail,
            "Confirm your new Annual Leave email",
            htmlBody,
            textBody,
            cancellationToken);
    }

    /// <summary>
    /// Base64url-encodes the reset token so it survives a trip through the query
    /// string; the reset endpoint decodes it back before handing it to Identity.
    /// </summary>
    private async Task<string> GenerateEncodedResetTokenAsync(User user)
    {
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
    }

    private static string ResolveRecipientName(User user) =>
        string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email ?? string.Empty : user.DisplayName;

    private static string BuildEmailBody(
        string previewText,
        string heading,
        string recipientName,
        string bodyText,
        string actionText,
        string actionUrl,
        string footerText)
    {
        var safePreviewText = WebUtility.HtmlEncode(previewText);
        var safeHeading = WebUtility.HtmlEncode(heading);
        var safeRecipientName = WebUtility.HtmlEncode(recipientName);
        var safeBodyText = WebUtility.HtmlEncode(bodyText);
        var safeActionText = WebUtility.HtmlEncode(actionText);
        var safeActionUrl = WebUtility.HtmlEncode(actionUrl);
        var safeFooterText = WebUtility.HtmlEncode(footerText);

        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>{{safeHeading}}</title>
</head>
<body style="margin:0;padding:0;background-color:#eef3f8;font-family:'Segoe UI',Arial,sans-serif;color:#0f172a;">
    <div style="display:none;max-height:0;overflow:hidden;opacity:0;">{{safePreviewText}}</div>
    <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:linear-gradient(180deg,#eef3f8 0%,#f8fafc 100%);">
        <tr>
            <td align="center" style="padding:40px 16px;">
                <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:680px;background-color:#ffffff;border:1px solid #d9e3f0;border-radius:20px;overflow:hidden;box-shadow:0 18px 40px rgba(15,23,42,0.08);">
                    <tr>
                        <td style="padding:0;background-color:#0b1f3a;">
                            <table role="presentation" width="100%" cellspacing="0" cellpadding="0">
                                <tr>
                                    <td style="padding:28px 32px;background:linear-gradient(135deg,#0f766e 0%,#0b1f3a 100%);color:#ffffff;">
                                        <div style="display:inline-block;padding:8px 12px;border-radius:999px;background-color:rgba(255,255,255,0.14);font-size:12px;font-weight:700;letter-spacing:0.12em;text-transform:uppercase;">Annual Leave</div>
                                        <div style="margin-top:14px;font-size:30px;line-height:1.25;font-weight:700;">{{safeHeading}}</div>
                                        <div style="margin-top:8px;font-size:14px;line-height:1.6;color:rgba(255,255,255,0.84);">Secure account communication</div>
                                    </td>
                                </tr>
                            </table>
                        </td>
                    </tr>
                    <tr>
                        <td style="padding:34px 32px;">
                            <p style="margin:0 0 16px;font-size:16px;line-height:1.7;">Hello {{safeRecipientName}},</p>
                            <p style="margin:0 0 24px;font-size:16px;line-height:1.75;color:#334155;">{{safeBodyText}}</p>

                            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="margin:0 0 24px;border:1px solid #dbe7f3;border-radius:14px;background-color:#f8fbff;">
                                <tr>
                                    <td style="padding:18px 20px;">
                                        <div style="font-size:13px;font-weight:700;color:#0f766e;text-transform:uppercase;letter-spacing:0.08em;margin-bottom:8px;">Next step</div>
                                        <div style="font-size:14px;line-height:1.7;color:#475569;">Use the button below to continue securely.</div>
                                    </td>
                                </tr>
                            </table>

                            <table role="presentation" cellspacing="0" cellpadding="0" border="0" style="margin:0 0 24px;">
                                <tr>
                                    <td align="center" bgcolor="#0f766e" style="border-radius:12px;background-color:#0f766e;mso-padding-alt:14px 26px;">
                                        <a href="{{safeActionUrl}}" target="_blank" style="display:inline-block;padding:14px 26px;font-family:'Segoe UI',Arial,sans-serif;font-size:15px;line-height:1.2;font-weight:700;color:#ffffff !important;text-decoration:none;background-color:#0f766e;border:1px solid #0f766e;border-radius:12px;">
                                            <span style="color:#ffffff;">{{safeActionText}}</span>
                                        </a>
                                    </td>
                                </tr>
                            </table>

                            <p style="margin:0 0 10px;font-size:14px;line-height:1.7;color:#475569;">If the button above does not open, copy and paste this secure link into your browser:</p>
                            <p style="margin:0 0 24px;padding:12px 14px;font-size:13px;line-height:1.8;word-break:break-all;background-color:#f8fafc;border:1px solid #e2e8f0;border-radius:10px;">
                                <a href="{{safeActionUrl}}" style="color:#0f766e;text-decoration:underline;">{{safeActionUrl}}</a>
                            </p>

                            <hr style="border:none;border-top:1px solid #e2e8f0;margin:0 0 18px;" />
                            <p style="margin:0 0 10px;font-size:14px;line-height:1.75;color:#334155;">{{safeFooterText}}</p>
                            <p style="margin:0;font-size:12px;line-height:1.7;color:#64748b;">This is an automated message from Annual Leave account services. Please do not reply directly to this email.</p>
                        </td>
                    </tr>
                </table>
            </td>
        </tr>
    </table>
</body>
</html>
""";
    }
}
