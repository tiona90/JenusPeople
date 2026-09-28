using System.Reflection;
using API.Controllers;
using Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace WorkTrack.Tests.WorkTasks;

/// <summary>Tasks are the Manager's and the HR Administrator's — every action, nobody else.</summary>
public class WorkTaskSurfaceTests
{
    [Fact]
    public void The_controller_is_gated_on_the_leave_and_time_decision_roles()
    {
        var gate = typeof(WorkTasksController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(gate);
        Assert.Equal(AppRoles.LeaveAndTimeDecisionRoles, gate!.Roles);
    }

    [Fact]
    public void No_action_widens_the_gate()
    {
        var actions = typeof(WorkTasksController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.NotEmpty(actions);
        foreach (var action in actions)
        {
            Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
            Assert.All(action.GetCustomAttributes<AuthorizeAttribute>(), a => Assert.Equal(AppRoles.LeaveAndTimeDecisionRoles, a.Roles));
        }
    }
}
