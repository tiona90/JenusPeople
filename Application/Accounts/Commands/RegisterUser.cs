using System.Net;
using System.Text;
using Application.Accounts.DTOs;
using Application.Core;
using Domain;
using Domain.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Accounts.Commands;

public class RegisterUser
{
    public class Command : IRequest<Result<RegistrationResponseDto>>
    {
        public required RegisterDto Request { get; set; }
        public required string ApiBaseUrl { get; set; }
    }

    public class RegistrationResponseDto
    {
        public string Message { get; init; } = string.Empty;
        public string Role { get; init; } = string.Empty;
        public string EmployeeProfileId { get; init; } = string.Empty;
        public bool EmailVerificationRequired { get; init; }
        public bool VerificationEmailSent { get; init; }
    }

    public class Handler(
        UserManager<User> userManager,
        AppDbContext context,
        IEmailService emailService)
        : IRequestHandler<Command, Result<RegistrationResponseDto>>
    {
        public async Task<Result<RegistrationResponseDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            var data = request.Request;

            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            var user = new User
            {
                UserName = data.Email,
                Email = data.Email,
                DisplayName = data.DisplayName,
                EmailConfirmed = false
            };

            var createResult = await userManager.CreateAsync(user, data.Password);
            if (!createResult.Succeeded)
            {
                return IdentityValidationFailure(createResult.Errors, "Registration failed.");
            }

            var addToRoleResult = await userManager.AddToRoleAsync(user, AppRoles.Employee);
            if (!addToRoleResult.Succeeded)
            {
                return IdentityValidationFailure(addToRoleResult.Errors, "Failed to assign role.");
            }

            var employeeProfile = new EmployeeProfile
            {
                UserId = user.Id,
                DepartmentId = data.DepartmentId,
                JobTitle = "Employee",
                AnnualLeaveEntitlement = 20,
                LeaveBalance = 20,
                CreatedAt = DateTime.UtcNow
            };

            context.EmployeeProfiles.Add(employeeProfile);
            await context.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            await SendVerificationEmailAsync(user, request.ApiBaseUrl, cancellationToken);

            return Result<RegistrationResponseDto>.Success(new RegistrationResponseDto
            {
                Message = "User registered successfully. Please check your email to verify your account.",
                Role = AppRoles.Employee,
                EmployeeProfileId = employeeProfile.Id,
                EmailVerificationRequired = true,
                VerificationEmailSent = true
            });
        }

        private static Result<RegistrationResponseDto> IdentityValidationFailure(
            IEnumerable<IdentityError> errors,
            string summary)
        {
            var grouped = errors
                .GroupBy(e => string.IsNullOrWhiteSpace(e.Code) ? "Identity" : e.Code)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());

            return Result<RegistrationResponseDto>.ValidationFailure(grouped, summary);
        }

        private async Task SendVerificationEmailAsync(User user, string apiBaseUrl, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(user.Email))
            {
                return;
            }

            var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
            var encodedToken = Base64UrlEncode(Encoding.UTF8.GetBytes(token));

            var baseUrl = string.IsNullOrWhiteSpace(apiBaseUrl)
                ? string.Empty
                : apiBaseUrl.TrimEnd('/');

            var verificationUrl = $"{baseUrl}/api/account/verify-email?userId={Uri.EscapeDataString(user.Id)}&token={Uri.EscapeDataString(encodedToken)}";
            var displayName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email : user.DisplayName;

            var textBody = $"Hello {displayName},\n\nWelcome to Annual Leave. Please confirm your email address using the secure link below:\n{verificationUrl}\n\nIf you did not create this account, you can safely ignore this email.";

            var htmlBody = BuildEmailBody(
                "Verify your Annual Leave account",
                "Confirm your email address",
                displayName,
                "Welcome to Annual Leave. Please confirm your email address to activate your account and complete sign-in access.",
                "Verify email address",
                verificationUrl,
                "If you did not create this account, you can safely ignore this email.");

            await emailService.SendEmailAsync(
                user.Email,
                "Verify your Annual Leave account",
                htmlBody,
                textBody,
                cancellationToken);
        }

        private static string Base64UrlEncode(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

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
}
