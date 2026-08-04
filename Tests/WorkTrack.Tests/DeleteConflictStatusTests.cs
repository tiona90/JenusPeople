using API.Controllers;
using Application.Core;
using Application.Departments.Commands;
using Application.LeaveTypes.Commands;
using Application.Projects.Commands;
using Domain;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Deleting something that is still referenced is a conflict with its current
/// state, not a missing resource. It used to answer 404, which is
/// indistinguishable from a bad route — an admin who could delete a department
/// locally but not on the deployed site had every reason to suspect the
/// deployment rather than read the response body.
///
/// These tests pin both halves: the handlers report a Conflict, and the API
/// layer turns that into 409 while leaving genuine "not found" on 404.
/// </summary>
public class DeleteConflictStatusTests
{
    /// <summary>Runs a result through the real HandleResult mapping.</summary>
    private static ActionResult Map<T>(Result<T> result)
    {
        var controller = new BaseApiController
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        // HandleResult is protected; the controller under test is the real one.
        var method = typeof(BaseApiController).GetMethod(
            "HandleResult",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        return (ActionResult)method.MakeGenericMethod(typeof(T)).Invoke(controller, [result])!;
    }

    private static int StatusOf(ActionResult result) => result switch
    {
        ConflictObjectResult c => c.StatusCode ?? 0,
        NotFoundObjectResult n => n.StatusCode ?? 0,
        ObjectResult o => o.StatusCode ?? 0,
        _ => 0,
    };

    [Fact]
    public async Task Department_with_employees_is_a_conflict_not_a_missing_resource()
    {
        using var context = TestDb.Create();

        context.Departments.Add(new Department { Id = 1, Name = "Engineering", Code = "ENG", IsActive = true });
        context.EmployeeProfiles.Add(new EmployeeProfile { Id = "p1", UserId = "u1", DepartmentId = 1 });
        await context.SaveChangesAsync();

        var handler = new DeleteDepartment.Handler(context);
        var result = await handler.Handle(new DeleteDepartment.Command { Id = 1 }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.Conflict, result.ErrorKind);
        Assert.Equal(409, StatusOf(Map(result)));

        // The message has to name the blocker, not just refuse.
        Assert.Contains("Engineering", result.Error);
        Assert.Contains("1 employee", result.Error);

        // And the department is still there.
        Assert.Single(context.Departments);
    }

    [Fact]
    public async Task Department_with_no_references_deletes()
    {
        using var context = TestDb.Create();

        context.Departments.Add(new Department { Id = 5, Name = "Operations", Code = "OPS", IsActive = true });
        await context.SaveChangesAsync();

        var handler = new DeleteDepartment.Handler(context);
        var result = await handler.Handle(new DeleteDepartment.Command { Id = 5 }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(context.Departments);
    }

    /// A department that genuinely is not there must still be a 404, so the new
    /// conflict path does not swallow real "missing resource" answers.
    [Fact]
    public async Task Missing_department_is_still_not_found()
    {
        using var context = TestDb.Create();

        var handler = new DeleteDepartment.Handler(context);
        var result = await handler.Handle(new DeleteDepartment.Command { Id = 404 }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.NotFound, result.ErrorKind);
        Assert.Equal(404, StatusOf(Map(result)));
    }

    [Fact]
    public async Task Project_with_timesheet_entries_is_a_conflict()
    {
        using var context = TestDb.Create();

        context.Projects.Add(new Project { Id = 1, Name = "Apollo", Code = "APL", IsActive = true });
        context.TimesheetEntries.Add(new TimesheetEntry
        {
            Id = "e1",
            TimesheetId = "t1",
            ProjectId = 1,
            Date = DateTime.UtcNow,
            HoursWorked = 4m,
        });
        await context.SaveChangesAsync();

        var handler = new DeleteProject.Handler(context);
        var result = await handler.Handle(new DeleteProject.Command { Id = 1 }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.Conflict, result.ErrorKind);
        Assert.Equal(409, StatusOf(Map(result)));
    }

    [Fact]
    public async Task Leave_type_in_use_is_a_conflict()
    {
        using var context = TestDb.Create();

        context.LeaveTypes.Add(new LeaveType { Id = 1, Name = "Annual", IsActive = true });
        context.AnnualLeaves.Add(new AnnualLeave
        {
            Id = "l1",
            EmployeeId = "p1",
            LeaveTypeId = 1,
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddDays(1),
        });
        await context.SaveChangesAsync();

        var handler = new DeleteLeaveType.Handler(context);
        var result = await handler.Handle(new DeleteLeaveType.Command { Id = 1 }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.Conflict, result.ErrorKind);
        Assert.Equal(409, StatusOf(Map(result)));
    }

    /// A plain Failure keeps its historical 404 mapping, so the handlers that
    /// were not touched behave exactly as before.
    [Fact]
    public void Plain_failure_still_maps_to_404()
    {
        Assert.Equal(404, StatusOf(Map(Result<Unit>.Failure("Cannot find the annual leave."))));
    }
}
