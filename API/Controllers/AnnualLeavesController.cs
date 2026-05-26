using Application.AnnualLeaves.Commands;
using Application.AnnualLeaves.DTOs;
using Application.AnnualLeaves.Queries;
using API.Hubs;
using Domain;
using Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace API.Controllers;

public class AnnualLeavesController : BaseApiController
{
    private readonly IHubContext<NotificationsHub> _notificationsHub;
    private readonly IFileUploadService _fileUploadService;

    public AnnualLeavesController(IHubContext<NotificationsHub> notificationsHub, IFileUploadService fileUploadService)
    {
        _notificationsHub = notificationsHub;
        _fileUploadService = fileUploadService;
    }

    // Visibility is role-scoped: Admin all, Manager by assigned departments, Employee own requests.
    [HttpGet]
    [Authorize(Policy = "AnnualLeaveRead")]
    public async Task<ActionResult<List<AnnualLeaveDto>>> GetAnnualLeaves()
    {
        return await Mediator.Send(new GetAnnualLeaveList.Query
        {
            RequestingUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            IsAdmin = User.IsInRole(AppRoles.Admin),
            IsManager = User.IsInRole(AppRoles.Manager),
            IsEmployee = User.IsInRole(AppRoles.Employee)
        });
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

        var createdId = await Mediator.Send(new CreateAnnualLeave.Command { AnnualLeave = request });
        await _notificationsHub.Clients.All.SendAsync("notificationsUpdated");
        return createdId;
    }

    [HttpPost("evidence-upload")]
    [Authorize(Policy = "AnnualLeaveCreate")]
    [RequestSizeLimit(10_000_000)]
    public async Task<ActionResult> UploadEvidence([FromForm] IFormFile file)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "Please select an evidence file." });
        }

        var allowedExtensions = new[] { ".pdf", ".jpg", ".jpeg", ".png", ".doc", ".docx" };
        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension) || !allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "Supported evidence files are PDF, JPG, PNG, DOC, and DOCX." });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";

        await using var stream = file.OpenReadStream();
        var uploadResult = await _fileUploadService.UploadEvidenceAsync(userId, stream, file.FileName, file.ContentType);

        if (!uploadResult.IsSuccess)
        {
            return BadRequest(new { message = uploadResult.ErrorMessage ?? "Failed to upload evidence." });
        }

        return Ok(new { evidenceUrl = uploadResult.Url, fileName = uploadResult.FileName });
    }

    // Admin can edit all leaves; Employee can edit own leaves; Manager can edit own and managed-department leaves.
    [HttpPut]
    [Authorize(Policy = "AnnualLeaveUpdate")]
    public async Task<ActionResult> EditAnnualLeave(EditAnnualLeaveRequest request)
    {
        await Mediator.Send(new EditAnnualLeave.Command
        {
            AnnualLeave = request,
            ChangedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            IsAdmin = User.IsInRole(AppRoles.Admin),
            IsManager = User.IsInRole(AppRoles.Manager)
        });
        await _notificationsHub.Clients.All.SendAsync("notificationsUpdated");
        return NoContent();
    }

    // Admin and Managers can approve/reject leaves via status-only update.
    [HttpPatch("{id}/status")]
    [Authorize(Policy = "AnnualLeaveUpdate")]
    public async Task<ActionResult> UpdateLeaveStatus(string id, UpdateLeaveStatusRequest request)
    {
        await Mediator.Send(new UpdateLeaveStatus.Command
        {
            LeaveId = id,
            Request = request,
            ChangedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            IsAdmin = User.IsInRole(AppRoles.Admin),
            IsManager = User.IsInRole(AppRoles.Manager),
        });
        await _notificationsHub.Clients.All.SendAsync("notificationsUpdated");
        return NoContent();
    }

    // Admin can delete all leaves; Employee can delete own leaves; Manager can delete own and managed-department leaves.
    [HttpDelete("{id}")]
    [Authorize(Policy = "AnnualLeaveDelete")]
    public async Task<ActionResult> DeleteAnnualLeave(string id)
    {
        await Mediator.Send(new DeleteAnnualLeave.Command
        {
            Id = id,
            RequestingUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            IsAdmin = User.IsInRole(AppRoles.Admin),
            IsManager = User.IsInRole(AppRoles.Manager)
        });
        await _notificationsHub.Clients.All.SendAsync("notificationsUpdated");
        return Ok();
    }
}
