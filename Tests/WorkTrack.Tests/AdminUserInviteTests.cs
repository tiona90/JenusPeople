using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using API.Services;
using Application.AdminUsers.DTOs;
using Domain;
using Infrastructure.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// An administrator creating a user does not choose a password. The account is
/// created without one and the new user sets their own from the link in the
/// welcome email — the same mechanism "forgot password" uses.
///
/// These tests pin the two halves of that: the create payload no longer carries
/// a password (and does insist on a display name), and the emailed invite really
/// does let a passwordless account set its first password.
/// </summary>
public class AdminUserInviteTests : IDisposable
{
    private readonly ServiceProvider _services;

    public AdminUserInviteTests()
    {
        var collection = new ServiceCollection();

        collection.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        collection.AddDataProtection();
        collection.AddDbContext<AppDbContext>(options => options
            .UseInMemoryDatabase($"invite-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

        collection.AddIdentityCore<User>(options =>
            {
                options.Password.RequiredLength = 6;
                options.Password.RequireNonAlphanumeric = false;
                options.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        _services = collection.BuildServiceProvider();
    }

    public void Dispose() => _services.Dispose();

    private UserManager<User> UserManager => _services.GetRequiredService<UserManager<User>>();

    private const string ClientBaseUrl = "https://people.example.test";

    private AccountEmailSender CreateSender(UserManager<User> userManager, FakeEmailService mail) =>
        new(
            userManager,
            mail,
            Options.Create(new AppUrlOptions { ClientBaseUrl = ClientBaseUrl }),
            NullLogger<AccountEmailSender>.Instance);

    /// <summary>Mirrors what AdminUsersController.CreateUser does: no password.</summary>
    private static async Task<User> CreateAdminInvitedUserAsync(UserManager<User> userManager)
    {
        var user = new User
        {
            UserName = "newjoiner@example.test",
            Email = "newjoiner@example.test",
            DisplayName = "New Joiner",
            EmailConfirmed = true,
        };

        var created = await userManager.CreateAsync(user);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));

        return user;
    }

    /* ── The create payload ─────────────────────────────────────────────── */

    [Fact]
    public void The_create_user_payload_carries_no_password()
    {
        Assert.DoesNotContain(
            typeof(AdminCreateUserDto).GetProperties(),
            p => p.Name.Contains("Password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Display_name_is_required_on_the_create_user_payload()
    {
        var property = typeof(AdminCreateUserDto).GetProperty(nameof(AdminCreateUserDto.DisplayName));

        Assert.NotNull(property);
        Assert.NotNull(property!.GetCustomAttribute<RequiredAttribute>());
    }

    /// <summary>
    /// [Required] rejects whitespace-only strings, so " " can't slip through as a
    /// display name the way it could when the field was optional.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_display_name_fails_validation(string displayName)
    {
        var dto = new AdminCreateUserDto
        {
            Email = "newjoiner@example.test",
            DisplayName = displayName,
            DepartmentId = 1,
        };

        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(AdminCreateUserDto.DisplayName)));
    }

    [Fact]
    public void A_payload_with_no_password_is_valid()
    {
        var dto = new AdminCreateUserDto
        {
            Email = "newjoiner@example.test",
            DisplayName = "New Joiner",
            DepartmentId = 1,
            Roles = [AppRoles.Employee],
        };

        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);

        Assert.True(isValid, string.Join("; ", results.Select(r => r.ErrorMessage)));
    }

    /* ── The invite itself ──────────────────────────────────────────────── */

    [Fact]
    public async Task An_admin_created_account_starts_with_no_password()
    {
        var userManager = UserManager;
        var user = await CreateAdminInvitedUserAsync(userManager);

        Assert.False(await userManager.HasPasswordAsync(user));

        // And so it cannot be signed into until the invite is used — no
        // admin-chosen interim password is left lying around.
        Assert.False(await userManager.CheckPasswordAsync(user, "Pa$w0rd"));
    }

    /// <summary>
    /// The whole point of the feature, end to end: create without a password,
    /// take the link out of the email that was actually sent, and use it to set
    /// the first password.
    /// </summary>
    [Fact]
    public async Task The_welcome_email_link_lets_a_new_user_set_their_first_password()
    {
        var userManager = UserManager;
        var user = await CreateAdminInvitedUserAsync(userManager);
        var mail = new FakeEmailService();

        var sent = await CreateSender(userManager, mail).SendWelcomeInviteAsync(user);

        Assert.True(sent);
        Assert.Equal(1, mail.SentCount);
        Assert.Equal(user.Email, mail.LastRecipient);

        var link = ExtractInviteLink(mail.LastTextBody);
        var query = QueryHelpers.ParseQuery(link.Query);

        Assert.Equal("/reset-password", link.AbsolutePath);
        Assert.Equal(user.Email, query["email"].ToString());
        // Tells the reset screen to greet a new joiner rather than talk about
        // resetting a password they have never had.
        Assert.Equal("1", query["welcome"].ToString());

        var decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(query["token"].ToString()));
        var result = await userManager.ResetPasswordAsync(user, decodedToken, "ChosenByTheUser1!");

        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        Assert.True(await userManager.CheckPasswordAsync(user, "ChosenByTheUser1!"));
    }

    /// <summary>
    /// The HTML body is what most recipients actually click, so the link has to
    /// be in there too — not only in the plain-text fallback.
    /// </summary>
    [Fact]
    public async Task The_welcome_email_puts_the_link_in_the_html_body_as_well()
    {
        var userManager = UserManager;
        var user = await CreateAdminInvitedUserAsync(userManager);
        var mail = new FakeEmailService();

        await CreateSender(userManager, mail).SendWelcomeInviteAsync(user);

        Assert.NotNull(mail.LastHtmlBody);
        // HtmlEncode turns the query separators into &amp;, so match on the path.
        Assert.Contains($"{ClientBaseUrl}/reset-password?email=", mail.LastHtmlBody);
        Assert.Contains("Set your password", mail.LastHtmlBody);
        Assert.Contains("set your password", mail.LastSubject!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A failed send must be reported rather than swallowed: the controller uses
    /// this to tell the admin the new user needs "Forgot password?" instead.
    /// </summary>
    [Fact]
    public async Task A_rejected_send_is_reported_as_a_failure()
    {
        var userManager = UserManager;
        var user = await CreateAdminInvitedUserAsync(userManager);
        var mail = new FakeEmailService { SendResult = false };

        Assert.False(await CreateSender(userManager, mail).SendWelcomeInviteAsync(user));
    }

    private static Uri ExtractInviteLink(string? textBody)
    {
        Assert.NotNull(textBody);

        var match = Regex.Match(textBody!, @"https://\S+/reset-password\?\S+");
        Assert.True(match.Success, $"No invite link found in:\n{textBody}");

        return new Uri(match.Value);
    }
}
