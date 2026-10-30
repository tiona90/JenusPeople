using System.Security.Claims;
using API.Controllers;
using Application.Accounts.DTOs;
using Domain;
using Domain.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Changing the sign-in email is a takeover step: whoever controls the address
/// can reset the password. PUT /api/account/profile accepted a new email on the
/// strength of the session alone — the confirmation link goes to the new address,
/// which an attacker holding a stolen session controls — so a session was enough
/// to own the account for good. The current password is now asked for whenever
/// the email changes, and nothing else on the profile needs it.
/// </summary>
public class EmailChangeNeedsPasswordTests
{
    private const string Password = "Correct-horse-9!";

    private sealed class RecordingSender : IAccountEmailSender
    {
        public List<string> EmailChangesSent { get; } = [];
        public string BuildClientUrl(string route, IDictionary<string, string?>? query = null) => route;
        public Task<bool> SendWelcomeInviteAsync(User user, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> SendPasswordResetAsync(User user, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> SendEmailChangeConfirmationAsync(User user, string newEmail, string apiBaseUrlFallback, CancellationToken cancellationToken = default)
        {
            EmailChangesSent.Add(newEmail);
            return Task.FromResult(true);
        }
    }

    private static async Task<(AccountController Controller, RecordingSender Sender, ServiceProvider Provider)> ArrangeAsync(AppDbContext db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddAuthentication();
        services.AddIdentityCore<User>(opt => opt.User.RequireUniqueEmail = true)
            .AddRoles<Role>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager();
        var provider = services.BuildServiceProvider();

        var userManager = provider.GetRequiredService<UserManager<User>>();
        var user = new User { Id = "u1", UserName = "ana@t.local", Email = "ana@t.local", EmailConfirmed = true, DisplayName = "Ana" };
        Assert.True((await userManager.CreateAsync(user, Password)).Succeeded);
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "p1", UserId = "u1", DepartmentId = null });
        await db.SaveChangesAsync();

        var sender = new RecordingSender();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = provider,
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u1")], "Test")),
        };
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("worktrack.test");
        var controller = new AccountController(userManager, provider.GetRequiredService<SignInManager<User>>(), db, sender)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
        return (controller, sender, provider);
    }

    private static UpdateProfileDto Profile(string email, string? currentPassword = null) => new()
    {
        DisplayName = "Ana", Email = email, CurrentPassword = currentPassword,
    };

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-password-1!")]
    public async Task A_new_email_without_the_right_password_is_refused(string? currentPassword)
    {
        using var db = TestDb.Create();
        var (controller, sender, provider) = await ArrangeAsync(db);
        await using var _ = provider;

        var result = await controller.UpdateProfile(Profile("attacker@evil.test", currentPassword));

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(AccountController.EmailChangeNeedsPasswordMessage, bad.Value?.GetType().GetProperty("message")?.GetValue(bad.Value));
        Assert.Empty(sender.EmailChangesSent);
    }

    [Fact]
    public async Task A_new_email_with_the_right_password_is_sent_for_confirmation()
    {
        using var db = TestDb.Create();
        var (controller, sender, provider) = await ArrangeAsync(db);
        await using var _ = provider;

        var result = await controller.UpdateProfile(Profile("ana.new@t.local", Password));

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(["ana.new@t.local"], sender.EmailChangesSent);
    }

    [Fact]
    public async Task Other_profile_changes_need_no_password()
    {
        using var db = TestDb.Create();
        var (controller, _, provider) = await ArrangeAsync(db);
        await using var __ = provider;

        var result = await controller.UpdateProfile(Profile("ana@t.local"));

        Assert.IsType<OkObjectResult>(result);
    }
}
