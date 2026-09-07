using Application.AnnualLeaves.Commands;
using Application.AnnualLeaves.DTOs;
using Application.AnnualLeaves.Queries;
using API.Hubs;
using Application.Files;
using Application.Files.Commands;
using Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Persistence;
using System.Security.Claims;
using Asp.Versioning;

namespace API.Controllers;

[ApiVersion("1.0")]

public class AnnualLeavesController : BaseApiController
{
    private readonly IHubContext<NotificationsHub> _notificationsHub;
    private readonly AppDbContext _context;

    public AnnualLeavesController(
        IHubContext<NotificationsHub> notificationsHub,
        AppDbContext context)
    {
        _notificationsHub = notificationsHub;
        _context = context;
    }

    // Audience for a leave event = the employee whose leave it is +
    // managers of the leave's department + all admins. Admins are not
    // department-scoped in this app, so they're notified for every event;
    // managers receive only events for departments they own.
    private async Task NotifyLeaveAudienceAsync(string employeeUserId, int? departmentId, CancellationToken cancellationToken = default)
    {
        var dispatch = new List<Task>
        {
            _notificationsHub.Clients.User(employeeUserId).SendAsync("notificationsUpdated", cancellationToken),
            _notificationsHub.Clients.Group(NotificationsHub.AdminGroup).SendAsync("notificationsUpdated", cancellationToken),
        };

        if (departmentId.HasValue)
        {
            dispatch.Add(_notificationsHub.Clients
                .Group(NotificationsHub.DepartmentManagerGroup(departmentId.Value))
                .SendAsync("notificationsUpdated", cancellationToken));
        }

        await Task.WhenAll(dispatch);
    }

    private async Task NotifyForLeaveAsync(string leaveId, CancellationToken cancellationToken = default)
    {
        var audience = await _context.AnnualLeaves
            .AsNoTracking()
            .Where(l => l.Id == leaveId)
            .Select(l => new { l.EmployeeId, l.DepartmentId })
            .FirstOrDefaultAsync(cancellationToken);

        if (audience is null)
        {
            return;
        }

        await NotifyLeaveAudienceAsync(audience.EmployeeId, audience.DepartmentId, cancellationToken);
    }

    // Visibility is role-scoped: Admin all, Manager by assigned departments, Employee own requests.
    [HttpGet]
    [Authorize(Policy = "AnnualLeaveRead")]
    public async Task<ActionResult<List<AnnualLeaveDto>>> GetAnnualLeaves([FromQuery] int? page = null, [FromQuery] int? pageSize = null)
    {
        var result = await Mediator.Send(new GetAnnualLeaveList.Query
        {
            RequestingUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            IsAdmin = User.IsInRole(AppRoles.Admin),
            IsManager = User.IsInRole(AppRoles.Manager),
            IsEmployee = User.IsInRole(AppRoles.Employee),
            Page = page,
            PageSize = pageSize,
        });
        return Paged(result);
    }

    [HttpGet("team-away-this-week/count")]
    [Authorize(Policy = "AnnualLeaveRead")]
    public async Task<ActionResult<int>> GetTeamAwayThisWeekCount()
    {
        return await Mediator.Send(new GetTeamAwayThisWeekCount.Query
        {
            RequestingUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            IsAdmin = User.IsInRole(AppRoles.Admin),
            IsManager = User.IsInRole(AppRoles.Manager),
            IsEmployee = User.IsInRole(AppRoles.Employee)
        });
    }

    // Visibility is role-scoped: Admin all, Manager by assigned departments, Employee own requests.
    [HttpGet("{id}")]
    [Authorize(Policy = "AnnualLeaveRead")]
    public async Task<ActionResult<AnnualLeaveDto>> GetAnnualLeaveDetails(string id)
    {
        var result = await Mediator.Send(new GetAnnualLeaveDetails.Query
        {
            Id = id,
            RequestingUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            IsAdmin = User.IsInRole(AppRoles.Admin),
            IsManager = User.IsInRole(AppRoles.Manager),
            IsEmployee = User.IsInRole(AppRoles.Employee)
        });
        return HandleResult(result);
    }

    // All roles can create leaves; status is determined by the selected leave type's approval settings.
    // Admin can supply a target EmployeeId to create on behalf of another user.
    [HttpPost]
    [Authorize(Policy = "AnnualLeaveCreate")]
    public async Task<ActionResult<string>> CreateAnnualLeave(CreateAnnualLeaveRequest request)
    {
        var isAdmin = User.IsInRole(AppRoles.Admin);
        // Non-admins always create for themselves; admins may supply a target user id.
        if (!isAdmin || string.IsNullOrWhiteSpace(request.EmployeeId))
            request.EmployeeId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        var result = await Mediator.Send(new CreateAnnualLeave.Command { AnnualLeave = request });
        if (result.IsSuccess && result.Value is not null)
        {
            await NotifyForLeaveAsync(result.Value);
        }
        return HandleResult(result);
    }

    [HttpPost("evidence-upload")]
    [Authorize(Policy = "AnnualLeaveCreate")]
    [RequestSizeLimit(10_000_000)]
    public async Task<ActionResult> UploadEvidence([FromForm] IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "Please select an evidence file." });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { message = "User is not authenticated." });
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);

        // Signature, size and extension checks live in the StoreFile handler.
        var stored = await Mediator.Send(
            new StoreFile.Command
            {
                Content = buffer.ToArray(),
                FileName = file.FileName,
                DeclaredContentType = file.ContentType,
                Purpose = StoredFilePurpose.LeaveEvidence,
                UploadedById = userId,
            },
            cancellationToken);

        if (!stored.IsSuccess || stored.Value is null)
        {
            return HandleResult(stored);
        }

        // The caller attaches this path to the leave it is creating or editing.
        // Until it does, only the uploader (and an Admin) can read the file back.
        return Ok(new
        {
            evidenceUrl = StoredFilePath.For(stored.Value),
            fileName = file.FileName,
        });
    }

    // Admin can edit all leaves; Employee can edit own leaves; Manager can edit own and managed-department leaves.
    [HttpPut]
    [Authorize(Policy = "AnnualLeaveUpdate")]
    public async Task<ActionResult> EditAnnualLeave(EditAnnualLeaveRequest request)
    {
        var result = await Mediator.Send(new EditAnnualLeave.Command
        {
            AnnualLeave = request,
            ChangedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            IsAdmin = User.IsInRole(AppRoles.Admin),
            IsManager = User.IsInRole(AppRoles.Manager)
        });
        if (result.IsSuccess)
        {
            await NotifyForLeaveAsync(request.Id);
        }
        return HandleResult(result);
    }

    // Admin and Managers can approve/reject leaves via status-only update.
    [HttpPatch("{id}/status")]
    [Authorize(Policy = "AnnualLeaveUpdate")]
    public async Task<ActionResult> UpdateLeaveStatus(string id, UpdateLeaveStatusRequest request)
    {
        var result = await Mediator.Send(new UpdateLeaveStatus.Command
        {
            LeaveId = id,
            Request = request,
            ChangedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            IsAdmin = User.IsInRole(AppRoles.Admin),
            IsManager = User.IsInRole(AppRoles.Manager),
        });
        if (result.IsSuccess)
        {
            await NotifyForLeaveAsync(id);
        }
        return HandleResult(result);
    }

    // Admin can delete all leaves; Employee can delete own leaves; Manager can delete own and managed-department leaves.
    [HttpDelete("{id}")]
    [Authorize(Policy = "AnnualLeaveDelete")]
    public async Task<ActionResult> DeleteAnnualLeave(string id)
    {
        // Resolve audience before the delete — once the row is gone we lose
        // the employee/department fix-up the notifier needs.
        var audience = await _context.AnnualLeaves
            .AsNoTracking()
            .Where(l => l.Id == id)
            .Select(l => new { l.EmployeeId, l.DepartmentId })
            .FirstOrDefaultAsync();

        var result = await Mediator.Send(new DeleteAnnualLeave.Command
        {
            Id = id,
            RequestingUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            IsAdmin = User.IsInRole(AppRoles.Admin),
            IsManager = User.IsInRole(AppRoles.Manager)
        });

        if (result.IsSuccess && audience is not null)
        {
            await NotifyLeaveAudienceAsync(audience.EmployeeId, audience.DepartmentId);
        }

        return HandleResult(result);
    }
}
