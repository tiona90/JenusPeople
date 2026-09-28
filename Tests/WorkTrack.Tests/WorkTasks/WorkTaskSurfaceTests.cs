using System.Reflection;
using API.Controllers;
using Domain;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace WorkTrack.Tests.WorkTasks;

/// <summary>
/// Tasks are for the three Leave &amp; Time roles, but only two of them run them.
/// Every action is gated on the class to Manager, HR Administrator and Employee;
/// everything but reading your tasks and moving their status is gated again, on
/// the action, to Manager and HR Administrator — the two gates AND together.
/// </summary>
public class WorkTaskSurfaceTests
{
    private static readonly string[] EmployeeActions =
        [nameof(WorkTasksController.GetWorkTasks), nameof(WorkTasksController.UpdateWorkTaskStatus),
            // The timesheet Task picker: every timesheet writer logs against their own tasks.
            nameof(WorkTasksController.GetTimesheetOptions)];

    private static IEnumerable<MethodInfo> Actions() =>
        typeof(WorkTasksController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

    [Fact]
    public void The_controller_admits_the_leave_and_time_roles()
    {
        var gate = typeof(WorkTasksController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(gate);
        Assert.Equal(AppRoles.LeaveAndTimeRoles, gate!.Roles);
    }

    [Fact]
    public void Running_tasks_is_for_managers_and_hr_only()
    {
        var managing = Actions().Where(a => !EmployeeActions.Contains(a.Name)).ToList();
        Assert.NotEmpty(managing);
        foreach (var action in managing)
            Assert.Contains(action.GetCustomAttributes<AuthorizeAttribute>(), a => a.Roles == AppRoles.LeaveAndTimeDecisionRoles);
    }

    [Fact]
    public void An_employee_can_read_their_tasks_and_move_their_status()
    {
        foreach (var name in EmployeeActions)
        {
            var action = typeof(WorkTasksController).GetMethod(name)!;
            Assert.Empty(action.GetCustomAttributes<AuthorizeAttribute>());
        }
    }

    [Fact]
    public void No_action_is_anonymous()
    {
        Assert.All(Actions(), a => Assert.Null(a.GetCustomAttribute<AllowAnonymousAttribute>()));
    }
}
