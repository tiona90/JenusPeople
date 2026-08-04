using API.DTOs;
using API.Services;
using Application.Accounts.DTOs;
using AccountCommands = Application.Accounts.Commands;
using Domain;
using Domain.Interfaces;
using Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Persistence;
using System.Net;
using System.Security.Claims;
using System.Text;
using Asp.Versioning;

namespace API.Controllers;

[ApiVersion("1.0")]

public class AccountController(
    UserManager<User> userManager,
    SignInManager<User> signInManager,
    AppDbContext context,
    IAccountEmailSender accountEmailSender,
    IFileUploadService fileUploadService) : BaseApiController
{
    // There is deliberately no public registration endpoint. Accounts are
    // created only by an administrator via POST /api/AdminUsers, which is
    // gated by [Authorize(Roles = AppRoles.Admin)].

    [AllowAnonymous]
    [HttpGet("verify-email")]
    public async Task<ActionResult> VerifyEmail([FromQuery] string userId, [FromQuery] string token)
    {
        var result = await Mediator.Send(new AccountCommands.VerifyEmail.Command
        {
            UserId = userId,
            Token = token
        });

        var outcome = result.Value!;
        return RenderVerificationPage(outcome.Title, outcome.Message, outcome.IsConfirmed);
    }


    [AllowAnonymous]
    [HttpPost("forgot-password")]
    public async Task<ActionResult> ForgotPassword(ForgotPasswordDto request)
    {
        const string responseMessage = "If an account with that email exists and has been verified, a password reset link has been sent.";

        var email = request.Email.Trim();
        var user = await userManager.FindByEmailAsync(email);
        if (user is null || !user.EmailConfirmed)
        {
            return Ok(new { message = responseMessage });
        }

        await accountEmailSender.SendPasswordResetAsync(user, HttpContext.RequestAborted);
        return Ok(new { message = responseMessage });
    }

    [AllowAnonymous]
    [HttpPost("reset-password")]
    public async Task<ActionResult> ResetPassword(ResetPasswordDto request)
    {
        var email = request.Email.Trim();
        var user = await userManager.FindByEmailAsync(email);
        if (user is null || !user.EmailConfirmed)
        {
            return BadRequest(new { message = "The password reset link is invalid or has expired." });
        }

        string decodedToken;
        try
        {
            decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.Token));
        }
        catch (Exception)
        {
            return BadRequest(new { message = "The password reset token is invalid." });
        }

        var result = await userManager.ResetPasswordAsync(user, decodedToken, request.NewPassword);
        if (!result.Succeeded)
        {
            // A rejected token is by far the common failure here, and Identity's
            // stock wording ("Invalid token.") tells the user nothing they can act
            // on. Reset tokens last 24 hours and are invalidated early by any
            // password change, so say that and point at the recovery action.
            // Returning no `errors` array matters: the client prefers those over
            // `message`, so the raw Identity text would otherwise win.
            if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken)))
            {
                return BadRequest(new
                {
                    message = "This reset link has expired or has already been used. Links are valid for 24 hours — please request a new one."
                });
            }

            // Password-policy failures are already actionable; pass them through.
            return BadRequest(new
            {
                message = "Unable to reset the password.",
                errors = result.Errors.Select(e => e.Description)
            });
        }

        return Ok(new { message = "Password reset successfully. You can now sign in with your new password." });
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth-strict")]
    [HttpPost("login")]
    public async Task<ActionResult> Login(LoginDto request)
    {
        var result = await signInManager.PasswordSignInAsync(request.Email, request.Password, request.RememberMe, lockoutOnFailure: false);
        if (result.IsNotAllowed)
        {
            return Unauthorized(new
            {
                message = "Your account has not been verified yet. Please check your email and click the confirmation link before signing in."
            });
        }

        if (!result.Succeeded)
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        return Ok(new { message = "Logged in successfully." });
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<ActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return Ok(new { message = "Logged out successfully." });
    }

    [Authorize]
    [HttpGet("user-info")]
    public async Task<ActionResult> GetUserInfo()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized(new { message = "User is not authenticated." });
        }

        var roles = await userManager.GetRolesAsync(user);
        var employeeProfile = await context.EmployeeProfiles
            .AsNoTracking()
            .Include(profile => profile.Department)
            .FirstOrDefaultAsync(profile => profile.UserId == user.Id);

        return Ok(new
        {
            user.Id,
            user.UserName,
            user.Email,
            user.DisplayName,
            user.ImageUrl,
            user.PhoneNumber,
            user.DateOfBirth,
            DepartmentId = employeeProfile?.DepartmentId,
            DepartmentName = employeeProfile?.Department?.Name,
            Roles = roles
        });
    }

    [Authorize]
    [HttpPut("profile")]
    public async Task<ActionResult> UpdateProfile(UpdateProfileDto request)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized(new { message = "User is not authenticated." });
        }

        var displayName = request.DisplayName.Trim();
        var requestedEmail = request.Email.Trim();
        var normalizedRequestedEmail = userManager.NormalizeEmail(requestedEmail);
        var emailChanged = !string.Equals(user.NormalizedEmail, normalizedRequestedEmail, StringComparison.Ordinal);

        if (emailChanged)
        {
            var emailInUse = await userManager.Users
                .AnyAsync(existing => existing.Id != user.Id && existing.NormalizedEmail == normalizedRequestedEmail);

            if (emailInUse)
            {
                return BadRequest(new { message = "Email is already registered." });
            }
        }

        var employeeProfile = await context.EmployeeProfiles
            .FirstOrDefaultAsync(profile => profile.UserId == user.Id);

        if (employeeProfile is null)
        {
            return BadRequest(new { message = "Employee profile could not be found." });
        }

        var department = await context.Departments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.DepartmentId && d.IsActive);

        if (department is null)
        {
            return BadRequest(new { message = "The selected department is invalid or inactive." });
        }

        user.DisplayName = displayName;
        user.PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();
        user.DateOfBirth = request.DateOfBirth;
        employeeProfile.DepartmentId = department.Id;

        var result = await userManager.UpdateAsync(user);

        if (!result.Succeeded)
        {
            return BadRequest(new
            {
                message = "Failed to update profile.",
                errors = result.Errors.Select(e => e.Description)
            });
        }

        await context.SaveChangesAsync();

        var emailChangePending = false;
        if (emailChanged)
        {
            await accountEmailSender.SendEmailChangeConfirmationAsync(
                user,
                requestedEmail,
                $"{Request.Scheme}://{Request.Host.Value}",
                HttpContext.RequestAborted);
            emailChangePending = true;
        }

        return Ok(new
        {
            message = emailChangePending
                ? $"Profile updated. Check {requestedEmail} for a confirmation link — the email change takes effect only after you click it."
                : "Profile updated successfully.",
            displayName = user.DisplayName,
            email = user.Email,
            phoneNumber = user.PhoneNumber,
            dateOfBirth = user.DateOfBirth,
            departmentId = department.Id,
            departmentName = department.Name,
            emailChangePending,
            pendingEmail = emailChangePending ? requestedEmail : null
        });
    }

    [AllowAnonymous]
    [HttpGet("confirm-email-change")]
    public async Task<IActionResult> ConfirmEmailChange(
        [FromQuery] string userId,
        [FromQuery] string newEmail,
        [FromQuery] string token)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(newEmail) || string.IsNullOrWhiteSpace(token))
        {
            return RenderVerificationPage(
                "Confirmation link invalid",
                "This confirmation link is incomplete. Request a new email change from your profile.",
                false);
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return RenderVerificationPage(
                "Confirmation failed",
                "We could not find the account for this confirmation link.",
                false);
        }

        var normalizedNewEmail = userManager.NormalizeEmail(newEmail);
        var emailTaken = await userManager.Users
            .AnyAsync(existing => existing.Id != user.Id && existing.NormalizedEmail == normalizedNewEmail);

        if (emailTaken)
        {
            return RenderVerificationPage(
                "Email unavailable",
                "Another account is already using this email address. Choose a different one from your profile.",
                false);
        }

        string decodedToken;
        try
        {
            decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
        }
        catch
        {
            return RenderVerificationPage(
                "Confirmation token invalid",
                "The confirmation token could not be read. Request a new email change from your profile.",
                false);
        }

        var changeResult = await userManager.ChangeEmailAsync(user, newEmail, decodedToken);
        if (!changeResult.Succeeded)
        {
            var message = changeResult.Errors.Select(e => e.Description).FirstOrDefault()
                ?? "Email change failed.";
            return RenderVerificationPage("Confirmation failed", message, false);
        }

        // The app uses email as username, so keep them aligned.
        var setUserNameResult = await userManager.SetUserNameAsync(user, newEmail);
        if (!setUserNameResult.Succeeded)
        {
            var message = setUserNameResult.Errors.Select(e => e.Description).FirstOrDefault()
                ?? "Username could not be updated.";
            return RenderVerificationPage("Confirmation failed", message, false);
        }

        return RenderVerificationPage(
            "Email updated",
            "Your account email has been updated. Sign in with the new address from now on.",
            true);
    }

    [Authorize]
    [HttpPost("profile-image")]
    [RequestSizeLimit(5_000_000)]
    public async Task<ActionResult> UploadProfileImage([FromForm] UploadProfileImageDto dto)
    {
        var file = dto.File;
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "Please select an image file." });
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized(new { message = "User is not authenticated." });
        }

        await using var stream = file.OpenReadStream();

        var allowed = new[] { FileSignatureValidator.FileKind.Jpeg, FileSignatureValidator.FileKind.Png };
        var detected = await FileSignatureValidator.DetectAsync(stream, allowed);
        if (detected is null)
        {
            return BadRequest(new { message = "Only real JPG or PNG images are accepted." });
        }

        var uploadResult = await fileUploadService.UploadProfileImageAsync(user.Id, stream, file.FileName);

        if (!uploadResult.IsSuccess)
        {
            return BadRequest(new { message = uploadResult.ErrorMessage ?? "Failed to upload image." });
        }

        user.ImageUrl = uploadResult.Url;
        await userManager.UpdateAsync(user);

        return Ok(new { imageUrl = user.ImageUrl });
    }

    private IActionResult RedirectToAuthPage(string status, string message, string route = "login")
    {
        return Redirect(accountEmailSender.BuildClientUrl(route, new Dictionary<string, string?>
        {
            ["authStatus"] = status,
            ["authMessage"] = message
        }));
    }

    private ContentResult RenderVerificationPage(string title, string message, bool isSuccess)
    {
        var loginUrl = accountEmailSender.BuildClientUrl("login", new Dictionary<string, string?>
        {
            ["authStatus"] = isSuccess ? "success" : "error",
            ["authMessage"] = message
        });

        var safeTitle = WebUtility.HtmlEncode(title);
        var safeMessage = WebUtility.HtmlEncode(message);
        var safeClientBaseUrl = WebUtility.HtmlEncode(loginUrl);
        var badgeText = isSuccess ? "Email confirmed" : "Verification issue";
        var badgeClass = isSuccess ? "badge success" : "badge error";
        var buttonText = isSuccess ? "Go to login" : "Open application";

        var html = $$"""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>{{safeTitle}}</title>
    <style>
        :root {
            color-scheme: light;
            --bg: #f4f7fb;
            --card: #ffffff;
            --text: #0f172a;
            --muted: #475569;
            --success: #0f766e;
            --success-soft: #ccfbf1;
            --error: #b91c1c;
            --error-soft: #fee2e2;
            --shadow: 0 20px 45px rgba(15, 23, 42, 0.12);
        }

        * { box-sizing: border-box; }

        body {
            margin: 0;
            min-height: 100vh;
            display: grid;
            place-items: center;
            padding: 24px;
            font-family: Inter, "Segoe UI", Arial, sans-serif;
            background: linear-gradient(135deg, #eff6ff 0%, #f8fafc 50%, #ecfeff 100%);
            color: var(--text);
        }

        .card {
            width: min(100%, 560px);
            background: var(--card);
            border-radius: 20px;
            padding: 32px;
            box-shadow: var(--shadow);
            border: 1px solid rgba(148, 163, 184, 0.18);
        }

        .badge {
            display: inline-flex;
            align-items: center;
            padding: 6px 12px;
            border-radius: 999px;
            font-size: 13px;
            font-weight: 700;
            margin-bottom: 18px;
        }

        .badge.success {
            color: var(--success);
            background: var(--success-soft);
        }

        .badge.error {
            color: var(--error);
            background: var(--error-soft);
        }

        h1 {
            margin: 0 0 12px;
            font-size: 30px;
            line-height: 1.2;
        }

        p {
            margin: 0 0 24px;
            font-size: 16px;
            line-height: 1.6;
            color: var(--muted);
        }

        .actions {
            display: flex;
            gap: 12px;
            flex-wrap: wrap;
        }

        .button {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            padding: 12px 18px;
            border-radius: 10px;
            text-decoration: none;
            font-weight: 700;
            background: #111827;
            color: #ffffff;
        }

        .subtle {
            font-size: 14px;
            margin-top: 18px;
            color: #64748b;
        }
    </style>
</head>
<body>
    <main class="card">
        <div class="{{badgeClass}}">{{badgeText}}</div>
        <h1>{{safeTitle}}</h1>
        <p>{{safeMessage}}</p>
        <div class="actions">
            <a class="button" href="{{safeClientBaseUrl}}">{{buttonText}}</a>
        </div>
        <p class="subtle">Annual Leave account services</p>
    </main>
</body>
</html>
""";

        return Content(html, "text/html");
    }

}
