using API.Controllers;
using API.Security;
using Application.AdminUsers.Commands;
using Application.Accounts.DTOs;
using Application.Core;
using Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// An administrator can switch an account off without deleting it, which is the
/// only safe answer for someone who has left: deleting them rewrites history
/// (DeleteAdminUser nulls out every approval they ever gave), while leaving the
/// account enabled leaves a working set of credentials behind.
///
/// These drive the real AccountController.Login over a real SignInManager, so
/// they prove the block happens inside Identity rather than in a controller
/// branch that a future sign-in path could bypass.
/// </summary>
public class UserDeactivationTests
{
    private const string Email = "leaver@test.local";
    private const string Password = "Correct-horse-9!";

    /// <summary>
    /// The Identity stack as Program.cs builds it, including the
    /// ActiveUserSignInManager that enforces IsActive.
    ///
    /// Unlike LoginLockoutTests, these tests need logins that *succeed* — a
    /// deactivated account is only interesting next to a working baseline — so
    /// real cookie handlers are registered for the Identity schemes.
    /// PasswordSignInAsync calls HttpContext.SignInAsync on the way through, and
    /// without them it throws "no sign-in authentication handlers are registered"
    /// instead of returning a result.
    /// </summary>
    private static ServiceProvider BuildIdentity(AppDbContext db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddCookie(IdentityConstants.ApplicationScheme)
            .AddCookie(IdentityConstants.ExternalScheme)
            .AddCookie(IdentityConstants.TwoFactorUserIdScheme)
            .AddCookie(IdentityConstants.TwoFactorRememberMeScheme);
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
            .AddSignInManager<ActiveUserSignInManager>();

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext =
            new DefaultHttpContext { RequestServices = provider };
        return provider;
    }

    private static async Task<User> CreateConfirmedUser(UserManager<User> userManager, string id = "leaver-u")
    {
        var user = new User
        {
            Id = id,
            UserName = Email,
            Email = Email,
            EmailConfirmed = true,
            DisplayName = "Leaver",
        };

        var created = await userManager.CreateAsync(user, Password);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
        return user;
    }

    private static AccountController LoginController(ServiceProvider provider, AppDbContext db) =>
        new(
            provider.GetRequiredService<UserManager<User>>(),
            provider.GetRequiredService<SignInManager<User>>(),
            db,
            null!)
        {
            // RequestServices matters: issuing the sign-in cookie resolves
            // IAuthenticationService off the request.
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = provider },
            },
        };

    private static async Task<int> AttemptLogin(AccountController controller)
    {
        var result = await controller.Login(new LoginDto { Email = Email, Password = Password });
        return result switch
        {
            ObjectResult objectResult => objectResult.StatusCode ?? StatusCodes.Status200OK,
            StatusCodeResult statusCodeResult => statusCodeResult.StatusCode,
            _ => 0,
        };
    }

    private static SetAdminUserActive.Handler Handler(ServiceProvider provider) =>
        new(provider.GetRequiredService<UserManager<User>>());

    [Fact]
    public async Task A_new_account_is_active()
    {
        using var db = TestDb.Create();
        await using var provider = BuildIdentity(db);
        var user = await CreateConfirmedUser(provider.GetRequiredService<UserManager<User>>());

        // Every account that predates this feature must keep working, which the
        // migration's default and this default have to agree on.
        Assert.True(user.IsActive);
    }

    [Fact]
    public async Task A_deactivated_account_refuses_the_right_password()
    {
        using var db = TestDb.Create();
        await using var provider = BuildIdentity(db);
        var userManager = provider.GetRequiredService<UserManager<User>>();
        var signInManager = provider.GetRequiredService<SignInManager<User>>();
        var user = await CreateConfirmedUser(userManager);
        var controller = LoginController(provider, db);

        // Baseline: the credentials work, so a rejection below means
        // "deactivated" rather than "this harness never lets anyone in".
        Assert.Equal(StatusCodes.Status200OK, await AttemptLogin(controller));

        var result = await Handler(provider).Handle(
            new SetAdminUserActive.Command { Id = user.Id, IsActive = false, RequestingUserId = "some-admin" },
            CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);

        Assert.False(await signInManager.CanSignInAsync(user));
        Assert.Equal(StatusCodes.Status403Forbidden, await AttemptLogin(controller));
    }

    [Fact]
    public async Task An_unconfirmed_account_is_still_reported_as_unverified_rather_than_deactivated()
    {
        using var db = TestDb.Create();
        await using var provider = BuildIdentity(db);
        var userManager = provider.GetRequiredService<UserManager<User>>();
        var user = await CreateConfirmedUser(userManager);
        var controller = LoginController(provider, db);

        user.EmailConfirmed = false;
        await userManager.UpdateAsync(user);

        // Both states surface as IsNotAllowed, and telling an unverified user
        // their account was deactivated sends them to an administrator instead
        // of to their inbox.
        Assert.Equal(StatusCodes.Status401Unauthorized, await AttemptLogin(controller));
    }

    [Fact]
    public async Task Reactivating_an_account_lets_it_sign_in_again()
    {
        using var db = TestDb.Create();
        await using var provider = BuildIdentity(db);
        var userManager = provider.GetRequiredService<UserManager<User>>();
        var user = await CreateConfirmedUser(userManager);
        var controller = LoginController(provider, db);
        var handler = Handler(provider);

        await handler.Handle(
            new SetAdminUserActive.Command { Id = user.Id, IsActive = false, RequestingUserId = "some-admin" },
            CancellationToken.None);
        await handler.Handle(
            new SetAdminUserActive.Command { Id = user.Id, IsActive = true, RequestingUserId = "some-admin" },
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, await AttemptLogin(controller));
    }

    [Fact]
    public async Task Deactivating_an_account_rotates_its_security_stamp()
    {
        using var db = TestDb.Create();
        await using var provider = BuildIdentity(db);
        var userManager = provider.GetRequiredService<UserManager<User>>();
        var user = await CreateConfirmedUser(userManager);
        var stampBefore = await userManager.GetSecurityStampAsync(user);

        await Handler(provider).Handle(
            new SetAdminUserActive.Command { Id = user.Id, IsActive = false, RequestingUserId = "some-admin" },
            CancellationToken.None);

        // Rotating the stamp is what ends the sessions the user already holds:
        // blocking the login path alone would leave a deactivated employee
        // working from the cookie they signed in with this morning.
        Assert.NotEqual(stampBefore, await userManager.GetSecurityStampAsync(user));
    }

    [Fact]
    public async Task Reactivating_an_already_active_account_changes_nothing_and_still_answers_with_the_user()
    {
        using var db = TestDb.Create();
        await using var provider = BuildIdentity(db);
        var userManager = provider.GetRequiredService<UserManager<User>>();
        var user = await CreateConfirmedUser(userManager);
        var stampBefore = await userManager.GetSecurityStampAsync(user);

        var result = await Handler(provider).Handle(
            new SetAdminUserActive.Command { Id = user.Id, IsActive = true, RequestingUserId = "some-admin" },
            CancellationToken.None);

        // Idempotent, like ConfirmAdminUserEmail: the panel refreshes from the
        // response either way, and a no-op must not invalidate live sessions.
        Assert.True(result.IsSuccess, result.Error);
        Assert.NotNull(result.Value);
        Assert.True(result.Value!.IsActive);
        Assert.Equal(stampBefore, await userManager.GetSecurityStampAsync(user));
    }

    [Fact]
    public async Task An_administrator_cannot_deactivate_their_own_account()
    {
        using var db = TestDb.Create();
        await using var provider = BuildIdentity(db);
        var userManager = provider.GetRequiredService<UserManager<User>>();
        var user = await CreateConfirmedUser(userManager);

        // Same guard and same reason as DeleteAdminUser: the last administrator
        // switching themselves off locks everyone out of user administration.
        var result = await Handler(provider).Handle(
            new SetAdminUserActive.Command { Id = user.Id, IsActive = false, RequestingUserId = user.Id },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.Conflict, result.ErrorKind);
        Assert.True((await userManager.FindByIdAsync(user.Id))!.IsActive);
    }

    [Fact]
    public async Task Deactivating_an_unknown_user_fails_rather_than_throwing()
    {
        using var db = TestDb.Create();
        await using var provider = BuildIdentity(db);

        var result = await Handler(provider).Handle(
            new SetAdminUserActive.Command { Id = "nobody", IsActive = false, RequestingUserId = "some-admin" },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.NotFound, result.ErrorKind);
    }
}

/// <summary>
/// The tests above build their own Identity stack, so they would all still pass
/// if Program.cs never installed any of this — the feature would simply do
/// nothing in production. These assert against the running application's own
/// container and route table instead.
/// </summary>
[Collection(ApiRouteTableCollection.Name)]
public class UserDeactivationWiringTests(ApiRouteTableFixture app)
{
    [Fact]
    public void The_application_uses_the_sign_in_manager_that_enforces_IsActive()
    {
        using var scope = app.Services.CreateScope();

        Assert.IsType<ActiveUserSignInManager>(scope.ServiceProvider.GetRequiredService<SignInManager<User>>());
    }

    [Fact]
    public void A_deactivated_users_existing_session_is_revalidated_promptly()
    {
        var options = app.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<SecurityStampValidatorOptions>>()
            .Value;

        // Deactivating rotates the security stamp, but the cookie the user
        // already holds is only re-checked against it this often — Identity's
        // default is 30 minutes, which is half a working morning of access after
        // an administrator has switched the account off. Zero would re-check on
        // every single request, so this is a floor as well as a ceiling.
        Assert.InRange(options.ValidationInterval, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void Route_table_maps_the_activation_endpoint_behind_admin_authorization()
    {
        var endpoints = app.Routes
            .Where(r => r.UnversionedPattern.Equals("api/AdminUsers/{id}/active", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(endpoints);
        Assert.All(endpoints, r =>
        {
            Assert.Contains("PUT", r.HttpMethods);
            Assert.False(r.AllowsAnonymous, $"{r} is reachable anonymously.");
            Assert.Contains(AppRoles.Admin, r.Roles ?? string.Empty);
        });
    }
}
