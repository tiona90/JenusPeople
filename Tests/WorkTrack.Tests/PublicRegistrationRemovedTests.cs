using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using API.Controllers;
using Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Public self-registration has been removed: accounts may only be created by an
/// authenticated administrator. These tests lock that in so re-introducing an
/// anonymous account-creation route fails the build rather than silently shipping.
///
/// Two complementary layers:
///
///   • Attribute assertions, because authorization on a controller action is
///     enforced by attributes the MVC pipeline applies before the action runs —
///     the attributes are what actually reject anonymous or non-admin callers.
///
///   • Route-table assertions over the running application's
///     <see cref="Microsoft.AspNetCore.Routing.EndpointDataSource"/>, because
///     reflection over controllers is blind to minimal-API routes. That blind
///     spot is not hypothetical: <c>app.MapGroup("api").MapIdentityApi&lt;User&gt;()</c>
///     published an anonymous <c>POST /api/register</c> that every attribute test
///     in this class passed straight over.
/// </summary>
public class PublicRegistrationRemovedTests(ApiRouteTableFixture routeTable)
    : IClassFixture<ApiRouteTableFixture>
{
    private static IEnumerable<MethodInfo> ActionsOf<TController>() =>
        typeof(TController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName);

    private static IEnumerable<string> RouteTemplatesOf(MethodInfo action) =>
        action.GetCustomAttributes<HttpMethodAttribute>()
            .Select(a => a.Template ?? string.Empty);

    // ── The public registration endpoint is gone ────────────────────────────────

    [Fact]
    public void AccountController_has_no_register_action()
    {
        Assert.Null(typeof(AccountController).GetMethod("Register"));
    }

    [Fact]
    public void AccountController_exposes_no_route_that_registers_an_account()
    {
        var offending = ActionsOf<AccountController>()
            .SelectMany(a => RouteTemplatesOf(a).Select(t => new { a.Name, Template = t }))
            .Where(x => x.Template.Contains("register", StringComparison.OrdinalIgnoreCase))
            .Select(x => $"{x.Name} -> \"{x.Template}\"")
            .ToList();

        Assert.Empty(offending);
    }

    /// <summary>
    /// Social sign-in was removed with registration: its callback provisioned a
    /// brand-new account (plus Employee role and profile) for any unrecognised
    /// email, which is public self-registration through a different door.
    /// </summary>
    [Fact]
    public void AccountController_has_no_external_login_actions()
    {
        Assert.Null(typeof(AccountController).GetMethod("ExternalLogin"));
        Assert.Null(typeof(AccountController).GetMethod("ExternalLoginCallback"));

        var offending = ActionsOf<AccountController>()
            .SelectMany(a => RouteTemplatesOf(a))
            .Where(t => t.Contains("external-login", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(offending);
    }

    /// <summary>
    /// Pins the anonymous surface of AccountController. Every entry here either
    /// authenticates an existing account or acts on a emailed single-use token —
    /// none of them creates an account. A new [AllowAnonymous] action fails this
    /// test until it is reviewed and added deliberately.
    /// </summary>
    [Fact]
    public void AccountController_anonymous_actions_are_limited_to_the_reviewed_set()
    {
        var expected = new[]
        {
            "ConfirmEmailChange",
            "ForgotPassword",
            "Login",
            "ResetPassword",
            "VerifyEmail",
        };

        var actual = ActionsOf<AccountController>()
            .Where(a => a.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
            .Select(a => a.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    // ── Admin-only user creation is preserved ───────────────────────────────────

    [Fact]
    public void AdminUsersController_still_exposes_a_create_user_action()
    {
        var create = typeof(AdminUsersController).GetMethod("CreateUser");

        Assert.NotNull(create);
        Assert.NotNull(create!.GetCustomAttribute<HttpPostAttribute>());
    }

    /// <summary>
    /// Covers both "an unauthenticated request cannot create a user" and "a
    /// non-admin user cannot create a user": [Authorize(Roles = "Admin")] on the
    /// controller rejects anonymous callers with 401 and non-admins with 403
    /// before CreateUser is ever invoked.
    /// </summary>
    [Fact]
    public void AdminUsersController_requires_an_authenticated_admin()
    {
        var authorize = typeof(AdminUsersController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal(AppRoles.Admin, authorize!.Roles);
    }

    [Fact]
    public void AdminUsersController_does_not_opt_any_action_out_of_authorization()
    {
        var anonymous = ActionsOf<AdminUsersController>()
            .Where(a => a.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
            .Select(a => a.Name)
            .ToList();

        Assert.Empty(anonymous);
    }

    // ── Supporting data is no longer readable anonymously ───────────────────────

    /// <summary>
    /// The department list was anonymous only so the public registration form
    /// could populate its dropdown. Every remaining caller is authenticated.
    /// </summary>
    [Fact]
    public void DepartmentsController_no_longer_lists_departments_anonymously()
    {
        var getDepartments = typeof(DepartmentsController).GetMethod("GetDepartments");

        Assert.NotNull(getDepartments);
        Assert.Null(getDepartments!.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.NotNull(getDepartments.GetCustomAttribute<AuthorizeAttribute>());
    }

    // ── The real route table, not just the controller attributes ────────────────

    /// <summary>
    /// The templates MapIdentityApi mounts under its group. /register and
    /// /resendConfirmationEmail are the account-creation pair; /refresh and
    /// /manage/* round out the surface it would have published anonymously or on a
    /// bearer token this app never issues. None of them may exist.
    /// </summary>
    private static readonly string[] IdentityApiRoutes =
    [
        "api/confirmEmail",
        "api/forgotPassword",
        "api/login",
        "api/manage/2fa",
        "api/manage/info",
        "api/refresh",
        "api/register",
        "api/resendConfirmationEmail",
        "api/resetPassword",
    ];

    [Fact]
    public void Route_table_does_not_map_the_identity_api_endpoints()
    {
        var offending = routeTable.Routes
            .Where(r => IdentityApiRoutes.Contains(r.Pattern, StringComparer.OrdinalIgnoreCase))
            .Select(r => r.Describe())
            .ToList();

        Assert.Empty(offending);
    }

    [Fact]
    public void Route_table_has_no_endpoint_whose_path_mentions_registration()
    {
        var offending = routeTable.Routes
            .Where(r => r.Pattern.Contains("register", StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Describe())
            .ToList();

        Assert.Empty(offending);
    }

    /// <summary>
    /// Pins the whole anonymous surface of the application, not one controller's
    /// worth of it. Each entry either authenticates an existing account or acts on
    /// an emailed single-use token; none creates one. The SPA fallback is anonymous
    /// by necessity — it serves index.html, and Program.cs makes it 404 for any
    /// /api or /hubs path so an unmatched API route cannot answer with HTML.
    ///
    /// Adding an anonymous endpoint anywhere — controller action, minimal API or
    /// mapped group — fails this test until it is reviewed and listed here.
    /// </summary>
    [Fact]
    public void Route_table_anonymous_surface_is_limited_to_the_reviewed_set()
    {
        var expected = new[]
        {
            "* /{*path:nonfile}",
            "GET /api/Account/confirm-email-change",
            "GET /api/Account/verify-email",
            "POST /api/Account/forgot-password",
            "POST /api/Account/login",
            "POST /api/Account/reset-password",
        };

        var actual = routeTable.Routes
            .Where(r => r.AllowsAnonymous)
            .Select(r => r.HttpMethods.IsEmpty
                ? $"* /{r.UnversionedPattern}"
                : $"{string.Join(",", r.HttpMethods)} /{r.UnversionedPattern}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Guards the tests above against passing vacuously. Every assertion here is
    /// "the route table does not contain X", which an empty or half-built table
    /// would satisfy — so prove the table really is the application's, by finding
    /// the admin-only creation route in it and confirming it is not anonymous.
    /// </summary>
    [Fact]
    public void Route_table_still_maps_admin_user_creation_behind_authorization()
    {
        var createUser = routeTable.Routes
            .Where(r => r.UnversionedPattern.Equals("api/AdminUsers", StringComparison.OrdinalIgnoreCase)
                && r.HttpMethods.Contains("POST"))
            .ToList();

        Assert.NotEmpty(createUser);
        Assert.All(createUser, r => Assert.False(r.AllowsAnonymous, $"{r} is reachable anonymously."));
    }

    /// <summary>
    /// End-to-end confirmation that the routes are gone from the running server and
    /// not merely renamed: an unauthenticated POST to the old registration path is
    /// answered 404 by the SPA fallback, not 400/401 by a handler.
    /// </summary>
    [Theory]
    [InlineData("/api/register")]
    [InlineData("/api/resendConfirmationEmail")]
    [InlineData("/api/manage/info")]
    public async Task Identity_api_paths_are_not_served(string path)
    {
        var client = routeTable.Client;

        using var post = await client.PostAsync(path, JsonContent.Create(new
        {
            email = "intruder@example.com",
            password = "Pa$$w0rd!"
        }));
        var postBody = await post.Content.ReadAsStringAsync();
        Assert.True(post.StatusCode == HttpStatusCode.NotFound, $"POST -> {(int)post.StatusCode}: {postBody}");

        using var get = await client.GetAsync(path);
        var getBody = await get.Content.ReadAsStringAsync();
        Assert.True(get.StatusCode == HttpStatusCode.NotFound, $"GET -> {(int)get.StatusCode}: {getBody}");
    }

    /// <summary>
    /// The counterpart to the 404s above: the sign-in route the client does call is
    /// still served, so a 404 there would mean a broken app rather than a hardened
    /// one. 401 rather than any-status-but-404 is the assertion because it is proof
    /// the request reached AccountController.Login and got a verdict — the user
    /// store is empty, so the credentials are rejected. A 500 would also be
    /// "not 404" while telling us nothing about routing.
    /// </summary>
    [Fact]
    public async Task The_account_login_path_is_served()
    {
        var client = routeTable.Client;

        using var response = await client.PostAsync("/api/account/login", JsonContent.Create(new
        {
            email = "nobody@example.com",
            password = "wrong-password",
            rememberMe = false
        }));
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(
            response.StatusCode == HttpStatusCode.Unauthorized,
            $"POST /api/account/login -> {(int)response.StatusCode}: {body}");
    }
}
