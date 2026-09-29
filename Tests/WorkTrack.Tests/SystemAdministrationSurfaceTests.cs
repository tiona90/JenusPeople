using System.Reflection;
using API.Controllers;
using Domain;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// An HR Administrator has the administrator's <b>reach</b> — every department's
/// leave, attendance and timesheets — and none of the <b>system administration</b>:
/// Users, Departments, Projects and their catalogues, Leave Types, Organization,
/// Notification Settings, Data Maintenance. The two are told apart by which gate an
/// action carries: <c>AppRoles.AdministratorRoles</c> for the reach,
/// <c>AppRoles.SystemAdministrator</c> by name for the configuration.
///
/// These pin that split action by action, because it is the kind of thing a
/// refactor undoes silently: a configuration write that drifts to
/// <c>AdministratorRoles</c> hands HR the settings with no test failing, and a reach
/// endpoint that drifts to <c>SystemAdministrator</c> locks HR out of their own pages.
/// </summary>
public class SystemAdministrationSurfaceTests
{
    private static IEnumerable<AuthorizeAttribute> GatesOn(Type controller, string action)
    {
        var method = controller.GetMethod(action, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new Xunit.Sdk.XunitException($"{controller.Name}.{action} not found");

        return controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Concat(method.GetCustomAttributes<AuthorizeAttribute>(inherit: true));
    }

    /// <summary>The configuration writes: exactly one gate names System Administrator alone.</summary>
    [Theory]
    [InlineData(typeof(AdminUsersController), nameof(AdminUsersController.CreateUser))]
    [InlineData(typeof(AdminUsersController), nameof(AdminUsersController.UpdateUser))]
    [InlineData(typeof(AdminUsersController), nameof(AdminUsersController.SetUserRoles))]
    [InlineData(typeof(AdminUsersController), nameof(AdminUsersController.SetUserDepartments))]
    [InlineData(typeof(AdminUsersController), nameof(AdminUsersController.SetUserActive))]
    [InlineData(typeof(AdminUsersController), nameof(AdminUsersController.ConfirmUserEmail))]
    [InlineData(typeof(AdminUsersController), nameof(AdminUsersController.DeleteUser))]
    [InlineData(typeof(DepartmentsController), nameof(DepartmentsController.CreateDepartment))]
    [InlineData(typeof(DepartmentsController), nameof(DepartmentsController.UpdateDepartment))]
    [InlineData(typeof(DepartmentsController), nameof(DepartmentsController.DeleteDepartment))]
    [InlineData(typeof(LeaveTypesController), nameof(LeaveTypesController.CreateLeaveType))]
    [InlineData(typeof(LeaveTypesController), nameof(LeaveTypesController.UpdateLeaveType))]
    [InlineData(typeof(LeaveTypesController), nameof(LeaveTypesController.DeleteLeaveType))]
    [InlineData(typeof(ProjectsController), nameof(ProjectsController.CreateProject))]
    [InlineData(typeof(ProjectsController), nameof(ProjectsController.UpdateProject))]
    [InlineData(typeof(ProjectsController), nameof(ProjectsController.DeleteProject))]
    [InlineData(typeof(SettingsController), nameof(SettingsController.UpdateSettings))]
    [InlineData(typeof(SettingsController), nameof(SettingsController.ResetReminders))]
    [InlineData(typeof(SettingsController), nameof(SettingsController.ClearApprovalHistory))]
    [InlineData(typeof(SettingsController), nameof(SettingsController.RunReminder))]
    [InlineData(typeof(WorkTaskSettingsController), nameof(WorkTaskSettingsController.UpdateSettings))]
    [InlineData(typeof(HolidaysController), nameof(HolidaysController.GetCountries))]
    // A read, despite the theory's name — the same exception HolidaysController.GetCountries
    // above already is. The rows say which departments each HR Administrator runs, so
    // the table is a map of who reaches whose leave and timesheets.
    [InlineData(typeof(UserDepartmentsController), nameof(UserDepartmentsController.GetUserDepartments))]
    // Also a read: the faults the system hit, which the System Administrator's bell
    // lists. Keeping the system running is theirs alone.
    [InlineData(typeof(SystemErrorsController), nameof(SystemErrorsController.GetSystemErrors))]
    public void Configuration_writes_are_System_Administrator_only(Type controller, string action)
    {
        var gates = GatesOn(controller, action).ToList();

        Assert.Contains(gates, g => g.Roles == AppRoles.SystemAdministrator);
    }

    /// <summary>
    /// The three project catalogues carry the same shape: every non-GET action gated
    /// on System Administrator alone. Enumerated by attribute rather than by name so
    /// a new write cannot be added ungated.
    /// </summary>
    [Theory]
    [InlineData(typeof(ProjectActivityTypesController))]
    [InlineData(typeof(ProjectComponentsController))]
    [InlineData(typeof(ProjectTypesController))]
    [InlineData(typeof(DepartmentsController))]
    [InlineData(typeof(LeaveTypesController))]
    [InlineData(typeof(ProjectsController))]
    public void Every_write_on_a_catalogue_controller_is_System_Administrator_only(Type controller)
    {
        var writes = controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes(inherit: true).Any(a =>
                a is Microsoft.AspNetCore.Mvc.HttpPostAttribute
                || a is Microsoft.AspNetCore.Mvc.HttpPutAttribute
                || a is Microsoft.AspNetCore.Mvc.HttpDeleteAttribute
                || a is Microsoft.AspNetCore.Mvc.HttpPatchAttribute))
            .ToList();

        Assert.NotEmpty(writes);
        Assert.All(writes, m =>
            Assert.Contains(GatesOn(controller, m.Name), g => g.Roles == AppRoles.SystemAdministrator));
    }

    /// <summary>
    /// Leave and time decisions — approving or rejecting somebody's timesheet, the
    /// team attendance board — are the HR Administrator's and a Manager's. The System
    /// Administrator configures the workspace and is deliberately not on the list.
    /// </summary>
    [Theory]
    [InlineData(typeof(TimesheetsController), nameof(TimesheetsController.ApproveTimesheet))]
    [InlineData(typeof(TimesheetsController), nameof(TimesheetsController.RejectTimesheet))]
    [InlineData(typeof(AttendanceController), nameof(AttendanceController.GetTeam))]
    [InlineData(typeof(AttendanceController), nameof(AttendanceController.GetTeamHistory))]
    public void Leave_and_time_decisions_are_for_the_HR_Administrator_and_Managers(Type controller, string action)
    {
        var gates = GatesOn(controller, action).ToList();

        Assert.Contains(gates, g => g.Roles == AppRoles.LeaveAndTimeDecisionRoles);
        Assert.Equal(new[] { AppRoles.HrAdministrator, AppRoles.Manager }, AppRoles.LeaveAndTimeDecisionRoles.Split(','));
    }

    [Theory]
    [InlineData(AppRoles.HrAdministrator, true)]
    [InlineData(AppRoles.SystemAdministrator, false)]
    [InlineData(AppRoles.Manager, false)]
    public void Only_the_HR_Administrator_acts_on_leave_as_an_administrator(string role, bool expected)
    {
        var principal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, role)], "test"));

        Assert.Equal(expected, principal.IsHrAdministrator());
    }

    /// <summary>
    /// The reads both administrators keep: the Users panel needs presence and the
    /// Departments panel the company attendance, and the leave form reads the user
    /// list to file on somebody's behalf. Nothing on these names System Administrator
    /// alone.
    /// </summary>
    [Theory]
    [InlineData(typeof(AdminUsersController), nameof(AdminUsersController.GetUsers))]
    [InlineData(typeof(AdminUsersController), nameof(AdminUsersController.GetUser))]
    [InlineData(typeof(AttendanceController), nameof(AttendanceController.GetCompany))]
    [InlineData(typeof(AttendanceController), nameof(AttendanceController.GetPresence))]
    [InlineData(typeof(SettingsController), nameof(SettingsController.GetSettings))]
    [InlineData(typeof(HolidaysController), nameof(HolidaysController.GetHolidays))]
    public void The_reach_is_open_to_both_administrator_roles(Type controller, string action)
    {
        var gates = GatesOn(controller, action).ToList();

        Assert.NotEmpty(gates);
        Assert.DoesNotContain(gates, g => g.Roles == AppRoles.SystemAdministrator);
        Assert.All(gates, g => Assert.True(
            g.Roles is null || g.Roles.Split(',').Contains(AppRoles.HrAdministrator),
            $"{controller.Name}.{action} names roles '{g.Roles}' without the HR Administrator"));
    }

    /// <summary>The task dialog of every role reads the rules; only the System Administrator writes them.</summary>
    [Fact]
    public void Task_settings_are_read_by_anyone_signed_in()
    {
        var gates = GatesOn(typeof(WorkTaskSettingsController), nameof(WorkTaskSettingsController.GetSettings)).ToList();

        Assert.NotEmpty(gates);
        Assert.All(gates, g => Assert.Null(g.Roles));
    }
}
