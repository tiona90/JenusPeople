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
/// Leave &amp; Time roles, and the assignee picker is gated again to Managers and
/// HR Administrators. An Employee (<c>AssignedOnly</c>) sees the tasks they are on
/// or created, and may create their own — always assigned to themselves — and
/// edit or delete only those. The handlers scope every task to the caller's
/// departments and decide who may change what.
/// </summary>
[ApiVersion("1.0")]
[Authorize(Roles = AppRoles.LeaveAndTimeRoles)]
public class WorkTasksController(IHubContext<NotificationsHub> notificationsHub) : BaseApiController
{
    private string CallerUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    /// <summary>An Employee works the tasks they are on and runs the ones they created; a Manager or HR Administrator runs the department's.</summary>
    private bool AssignedOnly => !User.IsDepartmentScoped();

    [HttpGet]
    public async Task<ActionResult<List<WorkTaskDto>>> GetWorkTasks() =>
        HandleResult(await Mediator.Send(new GetWorkTaskList.Query { CallerUserId = CallerUserId, AssignedOnly = AssignedOnly }));

    /// <summary>
    /// The Task picker on a timesheet row: the sheet owner's open tasks. Without a
    /// timesheet id the owner is the caller (a week not yet saved).
    /// </summary>
    [HttpGet("timesheet-options")]
    public async Task<ActionResult<List<TimesheetTaskOptionDto>>> GetTimesheetOptions([FromQuery] string? timesheetId) =>
        HandleResult(await Mediator.Send(new GetTimesheetTaskOptions.Query
        {
            CallerUserId = CallerUserId,
            TimesheetId = timesheetId,
            IsAdmin = User.IsSystemAdministrator(),
            IsManager = User.IsDepartmentScoped(),
            IsHrAdministrator = User.IsHrAdministrator(),
        }));

    [HttpGet("departments")]
    public async Task<ActionResult<List<WorkTaskDepartmentDto>>> GetDepartments() =>
        HandleResult(await Mediator.Send(new GetWorkTaskDepartments.Query { CallerUserId = CallerUserId }));

    [HttpGet("assignees")]
    [Authorize(Roles = AppRoles.LeaveAndTimeDecisionRoles)]
    public async Task<ActionResult<List<WorkTaskAssigneeDto>>> GetAssignees([FromQuery] int departmentId) =>
        HandleResult(await Mediator.Send(new GetWorkTaskAssignees.Query { CallerUserId = CallerUserId, DepartmentId = departmentId }));

    [HttpGet("projects")]
    public async Task<ActionResult<List<WorkTaskProjectDto>>> GetProjects([FromQuery] int departmentId) =>
        HandleResult(await Mediator.Send(new GetWorkTaskProjects.Query { CallerUserId = CallerUserId, DepartmentId = departmentId }));

    [HttpPost]
    public async Task<ActionResult<WorkTaskDto>> CreateWorkTask(UpsertWorkTaskRequest request, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new CreateWorkTask.Command { CallerUserId = CallerUserId, Task = request, AssignedOnly = AssignedOnly }, cancellationToken);
        if (result.IsSuccess) await NotifyAsync(result.Value!, cancellationToken);
        return HandleResult(result);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<WorkTaskDto>> UpdateWorkTask(int id, UpsertWorkTaskRequest request, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new UpdateWorkTask.Command { Id = id, CallerUserId = CallerUserId, Task = request, AssignedOnly = AssignedOnly }, cancellationToken);
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

    /// <summary>Attaches one file. Anyone who can see the task may; see <see cref="AddWorkTaskAttachment"/>.</summary>
    [HttpPost("{id:int}/attachments")]
    [RequestSizeLimit(11_000_000)]
    public async Task<ActionResult<WorkTaskDto>> AddAttachment(int id, [FromForm] IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "Please select a file to attach." });

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);

        var result = await Mediator.Send(new AddWorkTaskAttachment.Command
        {
            Id = id,
            CallerUserId = CallerUserId,
            Content = buffer.ToArray(),
            FileName = file.FileName,
            DeclaredContentType = file.ContentType,
            AssignedOnly = AssignedOnly,
        }, cancellationToken);
        if (result.IsSuccess) await NotifyAsync(result.Value!, cancellationToken);
        return HandleResult(result);
    }

    [HttpDelete("{id:int}/attachments/{attachmentId:int}")]
    public async Task<ActionResult<WorkTaskDto>> RemoveAttachment(int id, int attachmentId, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new RemoveWorkTaskAttachment.Command
        {
            Id = id, AttachmentId = attachmentId, CallerUserId = CallerUserId, AssignedOnly = AssignedOnly,
        }, cancellationToken);
        if (result.IsSuccess) await NotifyAsync(result.Value!, cancellationToken);
        return HandleResult(result);
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> DeleteWorkTask(int id, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new DeleteWorkTask.Command { Id = id, CallerUserId = CallerUserId, AssignedOnly = AssignedOnly }, cancellationToken);
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
