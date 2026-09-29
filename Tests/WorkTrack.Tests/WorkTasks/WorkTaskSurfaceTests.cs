using System.Reflection;
using API.Controllers;
using Domain;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace WorkTrack.Tests.WorkTasks;

/// <summary>
/// Tasks are for the three Leave &amp; Time roles. Every action is gated on the
/// class to Manager, HR Administrator and Employee; only the assignee picker is
/// gated again, on the action, to Manager and HR Administrator — the two gates AND
/// together. An Employee creates, edits and deletes their own tasks (always
/// assigned to themselves), so they never pick anybody.
/// </summary>
public class WorkTaskSurfaceTests
{
    private static readonly string[] ManagingActions = [nameof(WorkTasksController.GetAssignees)];

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
    public void Picking_assignees_is_for_managers_and_hr_only()
    {
        foreach (var name in ManagingActions)
        {
            var action = typeof(WorkTasksController).GetMethod(name)!;
            Assert.Contains(action.GetCustomAttributes<AuthorizeAttribute>(), a => a.Roles == AppRoles.LeaveAndTimeDecisionRoles);
        }
    }

    [Fact]
    public void An_employee_can_reach_every_other_action()
    {
        var employee = Actions().Where(a => !ManagingActions.Contains(a.Name)).ToList();
        Assert.Contains(employee, a => a.Name == nameof(WorkTasksController.CreateWorkTask));
        foreach (var action in employee)
            Assert.Empty(action.GetCustomAttributes<AuthorizeAttribute>());
    }

    [Fact]
    public void No_action_is_anonymous()
    {
        Assert.All(Actions(), a => Assert.Null(a.GetCustomAttribute<AllowAnonymousAttribute>()));
    }
}
