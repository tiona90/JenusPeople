using System.Reflection;
using API.Controllers;
using API.Security;
using Application.Accounts.DTOs;
using Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Login used to call PasswordSignInAsync with lockoutOnFailure: false, so a
/// password could be guessed without limit — the only brake was the per-IP rate
/// limiter, which a distributed attacker sidesteps and which does nothing to
/// protect one targeted account.
///
/// These drive the real AccountController.Login over a real SignInManager, so
/// they fail if that flag goes back to false, and they read the attempt cap from
/// the same LockoutPolicy Program.cs applies rather than hardcoding a number
/// that could drift away from production.
/// </summary>
public class LoginLockoutTests
{
    private const string Email = "victim@test.local";
    private const string GoodPassword = "Correct-horse-9!";
    private const string WrongPassword = "not-the-password-1!";

    /// <summary>
    /// A minimal Identity stack over the in-memory store, configured exactly as
    /// Program.cs configures it. Only the failure paths are exercised, so no
    /// authentication handler is needed — a successful PasswordSignInAsync would
    /// need a real sign-in scheme, and the password is verified here with
    /// CheckPasswordSignInAsync instead.
    /// </summary>
    private static ServiceProvider BuildIdentity(AppDbContext db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddAuthentication();
        services.AddIdentityCore<User>(opt =>
            {
                opt.User.RequireUniqueEmail = true;
                opt.SignIn.RequireConfirmedEmail = true;
                opt.SignIn.RequireConfirmedAccount = true;
                opt.Lockout.MaxFailedAccessAttempts = LockoutPolicy.MaxFailedAccessAttempts;
                opt.Lockout.DefaultLockoutTimeSpan = LockoutPolicy.LockoutDuration;
                opt.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<Role>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager();

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext =
            new DefaultHttpContext { RequestServices = provider };
        return provider;
    }

    /// <summary>
    /// Login is looked up by user name, and the controller passes the submitted
    /// email as the user name — so the two must match, as they do for accounts
    /// created through POST /api/AdminUsers. EmailConfirmed matters too:
    /// RequireConfirmedEmail short-circuits an unverified account to NotAllowed
    /// before any password is checked, so failures would never be counted.
    /// </summary>
    private static async Task<User> CreateConfirmedUser(UserManager<User> userManager)
    {
        var user = new User
        {
            Id = "victim-u",
            UserName = Email,
            Email = Email,
            EmailConfirmed = true,
            DisplayName = "Victim",
        };

        var created = await userManager.CreateAsync(user, GoodPassword);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
        return user;
    }

    private static AccountController LoginController(ServiceProvider provider, AppDbContext db) =>
        // Login touches only the SignInManager; the email sender and upload service
        // are left null so this fails loudly rather than quietly if that changes.
        new(
            provider.GetRequiredService<UserManager<User>>(),
            provider.GetRequiredService<SignInManager<User>>(),
            db,
            null!,
            null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private static async Task<int> AttemptLogin(AccountController controller, string password)
    {
        var result = await controller.Login(new LoginDto { Email = Email, Password = password });
        return result switch
        {
            ObjectResult objectResult => objectResult.StatusCode ?? StatusCodes.Status200OK,
            StatusCodeResult statusCodeResult => statusCodeResult.StatusCode,
            _ => 0,
        };
    }

    [Fact]
    public async Task The_configured_number_of_failed_logins_locks_the_account()
    {
        using var db = TestDb.Create();
        await using var provider = BuildIdentity(db);
        var userManager = provider.GetRequiredService<UserManager<User>>();
        var signInManager = provider.GetRequiredService<SignInManager<User>>();
        var user = await CreateConfirmedUser(userManager);
        var controller = LoginController(provider, db);

        // Baseline: the account is usable, so a rejection later means "locked",
        // not "this harness never lets anyone in".
        Assert.True((await signInManager.CheckPasswordSignInAsync(user, GoodPassword, lockoutOnFailure: false)).Succeeded);

        // One short of the cap: rejected, but not yet locked.
        for (var attempt = 1; attempt < LockoutPolicy.MaxFailedAccessAttempts; attempt++)
        {
            Assert.Equal(StatusCodes.Status401Unauthorized, await AttemptLogin(controller, WrongPassword));
            Assert.False(await userManager.IsLockedOutAsync(user), $"locked after only {attempt} failed attempts");
        }

        // The attempt that hits the cap locks the account.
        Assert.Equal(StatusCodes.Status423Locked, await AttemptLogin(controller, WrongPassword));
        Assert.True(await userManager.IsLockedOutAsync(user));
    }

    [Fact]
    public async Task A_locked_account_refuses_even_the_right_password()
    {
        using var db = TestDb.Create();
        await using var provider = BuildIdentity(db);
        var userManager = provider.GetRequiredService<UserManager<User>>();
        var user = await CreateConfirmedUser(userManager);
        var controller = LoginController(provider, db);

        for (var attempt = 0; attempt < LockoutPolicy.MaxFailedAccessAttempts; attempt++)
        {
            await AttemptLogin(controller, WrongPassword);
        }

        Assert.True(await userManager.IsLockedOutAsync(user));

        // The whole point of the lockout: guessing correctly after the cap is no
        // longer good enough.
        Assert.Equal(StatusCodes.Status423Locked, await AttemptLogin(controller, GoodPassword));
    }

    [Fact]
    public void The_lockout_policy_is_a_real_brute_force_defence()
    {
        // Guards against "explicitly configured" turning into a limit so loose it
        // stops being a limit.
        Assert.InRange(LockoutPolicy.MaxFailedAccessAttempts, 1, 10);
        Assert.True(
            LockoutPolicy.LockoutDuration >= TimeSpan.FromMinutes(5),
            $"a {LockoutPolicy.LockoutDuration.TotalMinutes:0}-minute lockout barely slows an attacker down");
    }

    [Fact]
    public void Every_unauthenticated_credential_endpoint_sits_behind_the_strict_rate_limiter()
    {
        // forgot-password and reset-password are as guessable as login itself:
        // one enumerates accounts, the other grinds at reset tokens.
        string[] actions = ["Login", "ForgotPassword", "ResetPassword"];

        foreach (var action in actions)
        {
            var method = typeof(AccountController).GetMethod(action);
            Assert.NotNull(method);

            var limiter = method.GetCustomAttribute<EnableRateLimitingAttribute>();
            Assert.NotNull(limiter);
            Assert.Equal("auth-strict", limiter.PolicyName);
        }
    }
}
