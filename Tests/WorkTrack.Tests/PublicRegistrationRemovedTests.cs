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
/// authenticated administrator. These tests lock that in at the controller
/// surface, so re-introducing an anonymous account-creation route fails the build
/// rather than silently shipping.
///
/// Authorization here is enforced by attributes that the MVC pipeline applies
/// before an action runs, so asserting on the attributes is what actually
/// determines whether anonymous or non-admin callers are rejected.
/// </summary>
public class PublicRegistrationRemovedTests
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
}
