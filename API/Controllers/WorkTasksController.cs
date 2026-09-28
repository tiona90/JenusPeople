using API.Hubs;
using Application.WorkTasks.Commands;
using Application.WorkTasks.DTOs;
using Application.WorkTasks.Queries;
using Asp.Versioning;
using Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace API.Controllers;

/// <summary>
/// Tasks Managers and HR Administrators hand out — to each other and to the
/// Employees in their departments. Two gates, ANDed: the class admits the three
/// Leave &amp; Time roles, and every action but reading your tasks and moving their
/// status is gated again to Managers and HR Administrators. An Employee sees only
/// the tasks they are on (<c>AssignedOnly</c>). The handlers scope every task to
/// the caller's departments and decide who may change what.
/// </summary>
[ApiVersion("1.0")]
[Authorize(Roles = AppRoles.LeaveAndTimeRoles)]
public class WorkTasksController(IHubContext<NotificationsHub> notificationsHub) : BaseApiController
{
    private string CallerUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    /// <summary>An Employee works their own tasks; a Manager or HR Administrator runs the department's.</summary>
    private bool AssignedOnly => !User.IsDepartmentScoped();

    [HttpGet]
    public async Task<ActionResult<List<WorkTaskDto>>> GetWorkTasks() =>
        HandleResult(await Mediator.Send(new GetWorkTaskList.Query { CallerUserId = CallerUserId, AssignedOnly = AssignedOnly }));

    [HttpGet("departments")]
    [Authorize(Roles = AppRoles.LeaveAndTimeDecisionRoles)]
    public async Task<ActionResult<List<WorkTaskDepartmentDto>>> GetDepartments() =>
        HandleResult(await Mediator.Send(new GetWorkTaskDepartments.Query { CallerUserId = CallerUserId }));

    [HttpGet("assignees")]
    [Authorize(Roles = AppRoles.LeaveAndTimeDecisionRoles)]
    public async Task<ActionResult<List<WorkTaskAssigneeDto>>> GetAssignees([FromQuery] int departmentId) =>
        HandleResult(await Mediator.Send(new GetWorkTaskAssignees.Query { CallerUserId = CallerUserId, DepartmentId = departmentId }));

    [HttpGet("projects")]
    [Authorize(Roles = AppRoles.LeaveAndTimeDecisionRoles)]
    public async Task<ActionResult<List<WorkTaskProjectDto>>> GetProjects([FromQuery] int departmentId) =>
        HandleResult(await Mediator.Send(new GetWorkTaskProjects.Query { CallerUserId = CallerUserId, DepartmentId = departmentId }));

    [HttpPost]
    [Authorize(Roles = AppRoles.LeaveAndTimeDecisionRoles)]
    public async Task<ActionResult<WorkTaskDto>> CreateWorkTask(UpsertWorkTaskRequest request, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new CreateWorkTask.Command { CallerUserId = CallerUserId, Task = request }, cancellationToken);
        if (result.IsSuccess) await NotifyAsync(result.Value!, cancellationToken);
        return HandleResult(result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = AppRoles.LeaveAndTimeDecisionRoles)]
    public async Task<ActionResult<WorkTaskDto>> UpdateWorkTask(int id, UpsertWorkTaskRequest request, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new UpdateWorkTask.Command { Id = id, CallerUserId = CallerUserId, Task = request }, cancellationToken);
        if (result.IsSuccess) await NotifyAsync(result.Value!, cancellationToken);
        return HandleResult(result);
    }

    [HttpPatch("{id:int}/status")]
    public async Task<ActionResult<WorkTaskDto>> UpdateWorkTaskStatus(int id, UpdateWorkTaskStatusRequest request, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new UpdateWorkTaskStatus.Command
        {
            Id = id, CallerUserId = CallerUserId, Status = request.Status, AssignedOnly = AssignedOnly,
        }, cancellationToken);
        if (result.IsSuccess) await NotifyAsync(result.Value!, cancellationToken);
        return HandleResult(result);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = AppRoles.LeaveAndTimeDecisionRoles)]
    public async Task<ActionResult> DeleteWorkTask(int id, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new DeleteWorkTask.Command { Id = id, CallerUserId = CallerUserId }, cancellationToken);
        if (!result.IsSuccess) return HandleResult(result);
        await NotifyDepartmentAsync(result.Value, cancellationToken);
        return NoContent();
    }

    // Managers and HR Administrators join their departments' groups on connect;
    // Employees join none, so the task's assignees are told by user as well.
    private async Task NotifyAsync(WorkTaskDto task, CancellationToken cancellationToken)
    {
        await NotifyDepartmentAsync(task.DepartmentId, cancellationToken);
        var assignees = task.Assignees.Select(a => a.UserId).ToList();
        if (assignees.Count > 0)
            await notificationsHub.Clients.Users(assignees).SendAsync("notificationsUpdated", cancellationToken);
    }

    private Task NotifyDepartmentAsync(int departmentId, CancellationToken cancellationToken) =>
        notificationsHub.Clients.Group(NotificationsHub.DepartmentManagerGroup(departmentId))
            .SendAsync("notificationsUpdated", cancellationToken);
}
