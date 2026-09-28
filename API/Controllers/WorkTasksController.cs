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
/// Tasks Managers and HR Administrators hand each other. The role gate says who
/// may reach the endpoint; the handlers scope every task to the caller's
/// departments and decide who may change what.
/// </summary>
[ApiVersion("1.0")]
[Authorize(Roles = AppRoles.LeaveAndTimeDecisionRoles)]
public class WorkTasksController(IHubContext<NotificationsHub> notificationsHub) : BaseApiController
{
    private string CallerUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    [HttpGet]
    public async Task<ActionResult<List<WorkTaskDto>>> GetWorkTasks() =>
        HandleResult(await Mediator.Send(new GetWorkTaskList.Query { CallerUserId = CallerUserId }));

    [HttpGet("departments")]
    public async Task<ActionResult<List<WorkTaskDepartmentDto>>> GetDepartments() =>
        HandleResult(await Mediator.Send(new GetWorkTaskDepartments.Query { CallerUserId = CallerUserId }));

    [HttpGet("assignees")]
    public async Task<ActionResult<List<WorkTaskAssigneeDto>>> GetAssignees([FromQuery] int departmentId) =>
        HandleResult(await Mediator.Send(new GetWorkTaskAssignees.Query { CallerUserId = CallerUserId, DepartmentId = departmentId }));

    [HttpPost]
    public async Task<ActionResult<WorkTaskDto>> CreateWorkTask(UpsertWorkTaskRequest request, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new CreateWorkTask.Command { CallerUserId = CallerUserId, Task = request }, cancellationToken);
        if (result.IsSuccess) await NotifyAsync(result.Value!.DepartmentId, cancellationToken);
        return HandleResult(result);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<WorkTaskDto>> UpdateWorkTask(int id, UpsertWorkTaskRequest request, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new UpdateWorkTask.Command { Id = id, CallerUserId = CallerUserId, Task = request }, cancellationToken);
        if (result.IsSuccess) await NotifyAsync(result.Value!.DepartmentId, cancellationToken);
        return HandleResult(result);
    }

    [HttpPatch("{id:int}/status")]
    public async Task<ActionResult<WorkTaskDto>> UpdateWorkTaskStatus(int id, UpdateWorkTaskStatusRequest request, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new UpdateWorkTaskStatus.Command { Id = id, CallerUserId = CallerUserId, Status = request.Status }, cancellationToken);
        if (result.IsSuccess) await NotifyAsync(result.Value!.DepartmentId, cancellationToken);
        return HandleResult(result);
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> DeleteWorkTask(int id, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new DeleteWorkTask.Command { Id = id, CallerUserId = CallerUserId }, cancellationToken);
        if (!result.IsSuccess) return HandleResult(result);
        await NotifyAsync(result.Value, cancellationToken);
        return NoContent();
    }

    // Managers and HR Administrators join their departments' groups on connect, so
    // this reaches exactly the people who can see the task.
    private Task NotifyAsync(int departmentId, CancellationToken cancellationToken) =>
        notificationsHub.Clients.Group(NotificationsHub.DepartmentManagerGroup(departmentId))
            .SendAsync("notificationsUpdated", cancellationToken);
}
